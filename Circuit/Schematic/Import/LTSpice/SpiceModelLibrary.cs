using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ComputerAlgebra;

namespace Circuit.LTSpiceImport
{
    /// <summary>
    /// Parses SPICE <c>.model</c> directives embedded in an LTSpice .asc text directive
    /// and exposes them as an <see cref="IPartLookup"/>. The synthesized templates carry
    /// the parameters the schematic actually declared, instead of the LiveSPICE defaults
    /// the JFET/BJT/diode components ship with.
    ///
    /// Syntax handled (case-insensitive, whitespace-tolerant):
    ///     .MODEL  &lt;name&gt;  &lt;TYPE&gt;  ( PARAM1=val PARAM2=val ... )
    /// where TYPE is one of NJF, PJF, NPN, PNP, D. Parens are optional and the parameter
    /// list may span lines once we've joined them into a single string.
    /// </summary>
    public sealed class SpiceModelLibrary : IPartLookup
    {
        private readonly Dictionary<string, Component> templates =
            new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);

        public Component TryGetByPartNumber(string partNumber)
        {
            if (string.IsNullOrWhiteSpace(partNumber)) return null;
            return templates.TryGetValue(partNumber.Trim(), out Component c) ? c : null;
        }

        /// <summary>
        /// Parse every <c>.model</c> directive in the supplied text records. Records that
        /// don't start with <c>.model</c> (case-insensitive) are ignored. Unsupported
        /// device types are reported as Info and skipped — they're harmless on their own.
        /// </summary>
        public static SpiceModelLibrary Build(IEnumerable<LTText> texts, ImportReport report)
        {
            SpiceModelLibrary lib = new SpiceModelLibrary();
            if (texts == null) return lib;

            foreach (LTText t in texts)
            {
                if (!t.IsSpiceDirective) continue;
                string content = t.Content?.Trim();
                if (string.IsNullOrEmpty(content)) continue;
                if (!content.StartsWith(".model", StringComparison.OrdinalIgnoreCase)) continue;

                if (!TryParseModelLine(content, out string name, out string type, out Dictionary<string, double> parameters, out string error))
                {
                    report.Warning(t.LineNumber, "Could not parse .model directive: " + error);
                    continue;
                }

                Component template = BuildTemplate(type, name, parameters, out string typeError);
                if (template == null)
                {
                    report.Info(t.LineNumber, "Ignoring .model '" + name + "' " + type + ": " + typeError);
                    continue;
                }
                lib.templates[name] = template;
                report.Info(t.LineNumber, "Loaded SPICE model '" + name + "' (" + type + ").");
            }
            return lib;
        }

        // === Parsing ===

        // Splits ".model NAME TYPE(p1=v1 p2=v2 ...)" into its three pieces. The argument
        // list parentheses are optional; "BETA=...uF" style suffixed values are handled
        // by ParseNumericSpiceValue.
        internal static bool TryParseModelLine(string line, out string name, out string type, out Dictionary<string, double> parameters, out string error)
        {
            name = null;
            type = null;
            parameters = null;
            error = null;

            // Drop the leading ".model" keyword.
            int firstSpace = line.IndexOfAny(new[] { ' ', '\t' });
            if (firstSpace < 0)
            {
                error = "missing name after .model";
                return false;
            }
            string rest = line.Substring(firstSpace).Trim();

            // Pull the model name.
            int afterName = rest.IndexOfAny(new[] { ' ', '\t' });
            if (afterName < 0)
            {
                error = "missing type after model name";
                return false;
            }
            name = rest.Substring(0, afterName).Trim();
            rest = rest.Substring(afterName).Trim();

            // Pull the device type, which may be glued to '(' with no whitespace.
            int parenIndex = rest.IndexOf('(');
            int typeEnd = parenIndex >= 0
                ? Math.Min(parenIndex, IndexOfWhitespaceOrEnd(rest, parenIndex))
                : IndexOfWhitespaceOrEnd(rest, rest.Length);
            // If a space comes before '(' use that, else use '(' itself.
            int spaceBeforeParen = IndexOfWhitespaceOrEnd(rest, parenIndex >= 0 ? parenIndex : rest.Length);
            typeEnd = Math.Min(typeEnd, spaceBeforeParen);
            type = rest.Substring(0, typeEnd).Trim();

            // Whatever remains is the parameter list. Strip any wrapping parens.
            string paramList = rest.Substring(typeEnd).Trim();
            if (paramList.StartsWith("(")) paramList = paramList.Substring(1);
            if (paramList.EndsWith(")")) paramList = paramList.Substring(0, paramList.Length - 1);

            parameters = ParseParamList(paramList);
            return true;
        }

        private static int IndexOfWhitespaceOrEnd(string s, int limit)
        {
            for (int i = 0; i < Math.Min(limit, s.Length); i++)
                if (char.IsWhiteSpace(s[i])) return i;
            return Math.Min(limit, s.Length);
        }

        // Tokenize "BETA=0.75m VTO=-2 IDSS=3mA LAMBDA=0.02" into a dictionary.
        // Values keep their SPICE suffix (k/m/u/n/p/f/Meg/G) which ParseNumericSpiceValue
        // handles at decode time.
        private static Dictionary<string, double> ParseParamList(string paramList)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(paramList)) return result;

            // Replace commas with spaces (SPICE allows both as separators).
            paramList = paramList.Replace(',', ' ');

            foreach (string token in paramList.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = token.IndexOf('=');
                if (eq <= 0 || eq == token.Length - 1) continue;
                string key = token.Substring(0, eq).Trim();
                string value = token.Substring(eq + 1).Trim();
                if (TryParseNumericSpiceValue(value, out double n))
                    result[key] = n;
            }
            return result;
        }

        // Decode SPICE numeric literals: "3mA" -> 3e-3, "1Meg" -> 1e6, "10k" -> 10e3,
        // "1u" or "1uF" -> 1e-6, "10n" -> 10e-9. SPICE units are unit-less suffixes;
        // any non-digit tail after the numeric part is treated as a unit and ignored.
        internal static bool TryParseNumericSpiceValue(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();

            // Walk forward to the end of the numeric prefix.
            int i = 0;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            string numericPart = s.Substring(0, i);
            string suffix = s.Substring(i);

            if (!double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double mantissa))
                return false;

            double scale = SpiceSuffixScale(suffix);
            value = mantissa * scale;
            return true;
        }

        private static double SpiceSuffixScale(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return 1.0;
            // Longest-match-first for "Meg" vs "m".
            string lower = suffix.ToLowerInvariant();
            if (lower.StartsWith("meg")) return 1e6;
            switch (lower[0])
            {
                case 'f': return 1e-15;
                case 'p': return 1e-12;
                case 'n': return 1e-9;
                case 'u': return 1e-6;
                case 'm': return 1e-3;
                case 'k': return 1e3;
                case 'g': return 1e9;
                case 't': return 1e12;
                default: return 1.0; // unit only (no scale)
            }
        }

        // === Template synthesis ===

        private static Component BuildTemplate(string type, string name, Dictionary<string, double> p, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(type)) { error = "no device type"; return null; }
            string t = type.Trim().ToUpperInvariant();
            switch (t)
            {
                case "NJF": return BuildJfet(name, JfetType.N, p);
                case "PJF": return BuildJfet(name, JfetType.P, p);
                default:
                    error = "device type not yet supported by SPICE model parser";
                    return null;
            }
        }

        // Map SPICE NJF/PJF parameters onto JunctionFieldEffectTransistor:
        //   VTO    -> Vt0          (V)
        //   BETA   -> Beta         (A/V^2)  -- if absent, derived from IDSS / VTO^2
        //   IDSS   -> used only to compute Beta when BETA not given
        //   LAMBDA -> Lambda       (1/V)
        //   IS     -> IS           (A)      -- gate-junction saturation current
        //   N      -> n            (gate emission coefficient)
        // Any unrecognized parameters are silently ignored (SPICE models often carry
        // RD/RS/CGS/CGD/etc. that this model doesn't represent).
        private static JunctionFieldEffectTransistor BuildJfet(string name, JfetType type, Dictionary<string, double> p)
        {
            JunctionFieldEffectTransistor j = new JunctionFieldEffectTransistor
            {
                Type = type,
                PartNumber = name,
            };

            if (p.TryGetValue("VTO", out double vto)) j.Vt0 = new Quantity((decimal)vto, Units.V);
            else if (p.TryGetValue("VT0", out vto)) j.Vt0 = new Quantity((decimal)vto, Units.V);

            bool haveBeta = p.TryGetValue("BETA", out double beta);
            if (!haveBeta && p.TryGetValue("IDSS", out double idss))
            {
                // Beta = IDSS / VTO^2 for the standard square-law form.
                double vt = (double)j.Vt0;
                if (Math.Abs(vt) > 1e-12)
                {
                    beta = idss / (vt * vt);
                    haveBeta = true;
                }
            }
            if (haveBeta) j.Beta = new Quantity((decimal)beta, Units.None);

            if (p.TryGetValue("LAMBDA", out double lambda))
                j.Lambda = new Quantity((decimal)lambda, Units.None);

            if (p.TryGetValue("IS", out double @is))
                j.IS = new Quantity((decimal)@is, Units.A);

            if (p.TryGetValue("N", out double n))
                j.n = new Quantity((decimal)n, Units.None);

            return j;
        }
    }

    /// <summary>
    /// Chains two <see cref="IPartLookup"/>s: the first wins, the second is the fallback.
    /// Used to layer schematic-embedded <c>.model</c> directives on top of a caller-supplied
    /// vendor part library.
    /// </summary>
    public sealed class ChainedPartLookup : IPartLookup
    {
        private readonly IPartLookup first;
        private readonly IPartLookup second;

        public ChainedPartLookup(IPartLookup first, IPartLookup second)
        {
            this.first = first;
            this.second = second;
        }

        public Component TryGetByPartNumber(string partNumber)
        {
            return first?.TryGetByPartNumber(partNumber) ?? second?.TryGetByPartNumber(partNumber);
        }
    }
}

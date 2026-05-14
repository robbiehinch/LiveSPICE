using System;
using System.Collections.Generic;
using ComputerAlgebra;

namespace Circuit.LTSpiceImport
{
    /// <summary>
    /// Per-pin description: the pin offset from the LTSpice symbol anchor (in R0, LTSpice units),
    /// and a function that picks the matching LiveSPICE Terminal off the constructed component.
    /// </summary>
    public class PinSpec
    {
        public Coord LTOffsetR0;        // Pin offset in LTSpice coords for R0 orientation.
        public Func<Component, Terminal> Selector;

        public PinSpec(Coord ltOffsetR0, Func<Component, Terminal> selector)
        {
            LTOffsetR0 = ltOffsetR0;
            Selector = selector;
        }
    }

    /// <summary>
    /// Entry in the LTSpice symbol whitelist. Each entry knows how to:
    /// - Build the LiveSPICE Component from an LTSymbol (using the part lookup for SPICE models).
    /// - List its pin offsets in LTSpice R0 coordinates (so the importer can locate net connections).
    /// </summary>
    public class WhitelistEntry
    {
        public string LTSymbolName;     // matched case-insensitively
        public Func<LTSymbol, IPartLookup, ImportReport, Component> Factory;
        public List<PinSpec> Pins;

        public WhitelistEntry(string ltName, Func<LTSymbol, IPartLookup, ImportReport, Component> factory, List<PinSpec> pins)
        {
            LTSymbolName = ltName;
            Factory = factory;
            Pins = pins;
        }
    }

    public static class Whitelist
    {
        // Stock LTSpice pin offsets (R0) from the .asy library.

        // res/res2/ind/ind2: tall body, pins 96 apart at x=16.
        private static readonly Coord TallTopPin = new Coord(16, 0);
        private static readonly Coord TallBottomPin = new Coord(16, 96);

        // cap/polcap/diode/schottky/zener/LED/LED2: short body, pins 64 apart at x=16.
        private static readonly Coord ShortTopPin = new Coord(16, 0);
        private static readonly Coord ShortBottomPin = new Coord(16, 64);

        // voltage/current source: pins at x=0, 96 apart (no x offset).
        private static readonly Coord SourceTopPin = new Coord(0, 0);
        private static readonly Coord SourceBottomPin = new Coord(0, 96);

        // BJT npn (collector top, base left, emitter bottom). pnp has the same pin
        // positions but the top/bottom roles are swapped at the symbol level.
        private static readonly Coord BjtTop = new Coord(16, 0);
        private static readonly Coord BjtBase = new Coord(-16, 48);
        private static readonly Coord BjtBottom = new Coord(16, 96);

        // JFET njf (drain top, gate left, source bottom). pjf swaps top/bottom roles.
        private static readonly Coord JfetTop = new Coord(16, 0);
        private static readonly Coord JfetGate = new Coord(-32, 48);
        private static readonly Coord JfetBottom = new Coord(16, 96);

        // Opamp pins (LTSpice "Opamps\opamp"): +input top-left, -input bottom-left,
        // output right, V+ top, V- bottom.
        private static readonly Coord OpampPlus = new Coord(-32, 32);
        private static readonly Coord OpampMinus = new Coord(-32, 64);
        private static readonly Coord OpampOut = new Coord(32, 48);
        private static readonly Coord OpampVcc = new Coord(0, 32);
        private static readonly Coord OpampVee = new Coord(0, 64);

        private static readonly Dictionary<string, WhitelistEntry> Entries = BuildEntries();

        public static bool TryGet(string ltSymbolName, out WhitelistEntry entry)
        {
            // Case-insensitive match; LTSpice symbol names may use either '\' or '/' as the
            // separator for subdirectory-style names.
            string key = Normalize(ltSymbolName);
            return Entries.TryGetValue(key, out entry);
        }

        private static string Normalize(string name)
        {
            if (name == null) return "";
            return name.Replace('/', '\\').ToUpperInvariant();
        }

        private static Dictionary<string, WhitelistEntry> BuildEntries()
        {
            var d = new Dictionary<string, WhitelistEntry>();

            // Resistors: res, res2 — tall body.
            var resistorPins = new List<PinSpec> {
                new PinSpec(TallTopPin, c => ((Resistor)c).Anode),
                new PinSpec(TallBottomPin, c => ((Resistor)c).Cathode),
            };
            d[Normalize("res")] = new WhitelistEntry("res", MakeResistor, resistorPins);
            d[Normalize("res2")] = new WhitelistEntry("res2", MakeResistor, resistorPins);

            // Capacitors: cap, polcap — short body.
            var capPins = new List<PinSpec> {
                new PinSpec(ShortTopPin, c => ((Capacitor)c).Anode),
                new PinSpec(ShortBottomPin, c => ((Capacitor)c).Cathode),
            };
            d[Normalize("cap")] = new WhitelistEntry("cap", MakeCapacitor, capPins);
            d[Normalize("polcap")] = new WhitelistEntry("polcap", MakeCapacitor, capPins);

            // Inductors: ind, ind2 — tall body.
            var indPins = new List<PinSpec> {
                new PinSpec(TallTopPin, c => ((Inductor)c).Anode),
                new PinSpec(TallBottomPin, c => ((Inductor)c).Cathode),
            };
            d[Normalize("ind")] = new WhitelistEntry("ind", MakeInductor, indPins);
            d[Normalize("ind2")] = new WhitelistEntry("ind2", MakeInductor, indPins);

            // Diodes: diode, schottky, zener, LED, LED2 — short body.
            var diodePins = new List<PinSpec> {
                new PinSpec(ShortTopPin, c => ((Diode)c).Anode),
                new PinSpec(ShortBottomPin, c => ((Diode)c).Cathode),
            };
            d[Normalize("diode")] = new WhitelistEntry("diode", (s, p, r) => MakeDiode(s, p, r, DiodeType.Diode), diodePins);
            d[Normalize("schottky")] = new WhitelistEntry("schottky", (s, p, r) => MakeDiode(s, p, r, DiodeType.Diode), diodePins);
            d[Normalize("zener")] = new WhitelistEntry("zener", (s, p, r) => MakeDiode(s, p, r, DiodeType.Zener), diodePins);
            d[Normalize("LED")] = new WhitelistEntry("LED", (s, p, r) => MakeDiode(s, p, r, DiodeType.LED), diodePins);
            d[Normalize("LED2")] = new WhitelistEntry("LED2", (s, p, r) => MakeDiode(s, p, r, DiodeType.LED), diodePins);

            // Voltage source — may build a Rail (DC) or a VoltageSource (function-style).
            var vsourcePins = new List<PinSpec> {
                new PinSpec(SourceTopPin, c => c is Rail r ? r.Terminal : c is VoltageSource v ? v.Anode : null),
                new PinSpec(SourceBottomPin, c => c is Rail ? null : c is VoltageSource v ? v.Cathode : null),
            };
            d[Normalize("voltage")] = new WhitelistEntry("voltage", MakeVoltageSource, vsourcePins);

            // Current source
            var iPins = new List<PinSpec> {
                new PinSpec(SourceTopPin, c => ((CurrentSource)c).Anode),
                new PinSpec(SourceBottomPin, c => ((CurrentSource)c).Cathode),
            };
            d[Normalize("current")] = new WhitelistEntry("current", MakeCurrentSource, iPins);

            // NPN BJT: top pin = collector, bottom = emitter.
            var npnPins = new List<PinSpec> {
                new PinSpec(BjtTop, c => ((BipolarJunctionTransistor)c).Collector),
                new PinSpec(BjtBase, c => ((BipolarJunctionTransistor)c).Base),
                new PinSpec(BjtBottom, c => ((BipolarJunctionTransistor)c).Emitter),
            };
            // PNP BJT: same pin positions, but in LTSpice's pnp.asy the top pin is the emitter
            // and the bottom is the collector.
            var pnpPins = new List<PinSpec> {
                new PinSpec(BjtTop, c => ((BipolarJunctionTransistor)c).Emitter),
                new PinSpec(BjtBase, c => ((BipolarJunctionTransistor)c).Base),
                new PinSpec(BjtBottom, c => ((BipolarJunctionTransistor)c).Collector),
            };
            d[Normalize("npn")] = new WhitelistEntry("npn", (s, p, r) => MakeBjt(s, p, r, BjtType.NPN), npnPins);
            d[Normalize("pnp")] = new WhitelistEntry("pnp", (s, p, r) => MakeBjt(s, p, r, BjtType.PNP), pnpPins);

            // N-JFET: top = drain, bottom = source.
            var njfPins = new List<PinSpec> {
                new PinSpec(JfetTop, c => ((JunctionFieldEffectTransistor)c).Drain),
                new PinSpec(JfetGate, c => ((JunctionFieldEffectTransistor)c).Gate),
                new PinSpec(JfetBottom, c => ((JunctionFieldEffectTransistor)c).Source),
            };
            // P-JFET: pin positions same, but pjf.asy labels them with drain/source swapped.
            var pjfPins = new List<PinSpec> {
                new PinSpec(JfetTop, c => ((JunctionFieldEffectTransistor)c).Source),
                new PinSpec(JfetGate, c => ((JunctionFieldEffectTransistor)c).Gate),
                new PinSpec(JfetBottom, c => ((JunctionFieldEffectTransistor)c).Drain),
            };
            d[Normalize("njf")] = new WhitelistEntry("njf", (s, p, r) => MakeJfet(s, p, r, JfetType.N), njfPins);
            d[Normalize("pjf")] = new WhitelistEntry("pjf", (s, p, r) => MakeJfet(s, p, r, JfetType.P), pjfPins);

            // Op-amps
            var opampPins = new List<PinSpec> {
                new PinSpec(OpampPlus, c => ((IdealOpAmp)c).Positive),
                new PinSpec(OpampMinus, c => ((IdealOpAmp)c).Negative),
                new PinSpec(OpampOut, c => ((IdealOpAmp)c).Out),
                new PinSpec(OpampVcc, c => c is OpAmp oa ? GetTerminalByName(oa, "Vcc+") : null),
                new PinSpec(OpampVee, c => c is OpAmp oa ? GetTerminalByName(oa, "Vcc-") : null),
            };
            d[Normalize("Opamps\\opamp")] = new WhitelistEntry("Opamps\\opamp", MakeOpAmp, opampPins);
            d[Normalize("Opamps\\opamp2")] = new WhitelistEntry("Opamps\\opamp2", MakeOpAmp, opampPins);
            d[Normalize("Opamps\\UniversalOpamp2")] = new WhitelistEntry("Opamps\\UniversalOpamp2", MakeOpAmp, opampPins);

            return d;
        }

        private static Terminal GetTerminalByName(Component c, string name)
        {
            foreach (Terminal t in c.Terminals)
                if (t.Name == name) return t;
            return null;
        }

        // === Factories ===

        private static Component MakeResistor(LTSymbol s, IPartLookup parts, ImportReport report)
        {
            Resistor r = new Resistor();
            string val;
            if (s.Attributes.TryGetValue("Value", out val) && !string.IsNullOrWhiteSpace(val))
            {
                if (TryParseSpiceQuantity(val, Units.Ohm, out Quantity q))
                    r.Resistance = q;
                else
                    report.Warning(s.LineNumber, "Could not parse resistor value '" + val + "', using default.");
            }
            ApplyName(r, s);
            return r;
        }

        private static Component MakeCapacitor(LTSymbol s, IPartLookup parts, ImportReport report)
        {
            Capacitor c = new Capacitor();
            string val;
            if (s.Attributes.TryGetValue("Value", out val) && !string.IsNullOrWhiteSpace(val))
            {
                if (TryParseSpiceQuantity(val, Units.F, out Quantity q))
                    c.Capacitance = q;
                else
                    report.Warning(s.LineNumber, "Could not parse capacitor value '" + val + "', using default.");
            }
            ApplyName(c, s);
            return c;
        }

        private static Component MakeInductor(LTSymbol s, IPartLookup parts, ImportReport report)
        {
            Inductor l = new Inductor();
            string val;
            if (s.Attributes.TryGetValue("Value", out val) && !string.IsNullOrWhiteSpace(val))
            {
                if (TryParseSpiceQuantity(val, Units.H, out Quantity q))
                    l.Inductance = q;
                else
                    report.Warning(s.LineNumber, "Could not parse inductor value '" + val + "', using default.");
            }
            ApplyName(l, s);
            return l;
        }

        private static Component MakeDiode(LTSymbol s, IPartLookup parts, ImportReport report, DiodeType type)
        {
            // Try part lookup first.
            string model = null;
            s.Attributes.TryGetValue("SpiceModel", out model);
            if (string.IsNullOrWhiteSpace(model))
                s.Attributes.TryGetValue("Value", out model);

            Diode template = null;
            if (parts != null && !string.IsNullOrWhiteSpace(model))
                template = parts.TryGetByPartNumber(model) as Diode;

            Diode d;
            if (template != null)
            {
                d = (Diode)Component.Deserialize(template.Serialize());
                d.PartNumber = template.PartNumber;
            }
            else
            {
                d = new Diode();
                if (!string.IsNullOrWhiteSpace(model))
                {
                    d.PartNumber = model;
                    report.Info(s.LineNumber, "Diode model '" + model + "' not found in library; using default Shockley parameters.");
                }
            }
            d.Type = type;
            ApplyName(d, s);
            return d;
        }

        private static Component MakeVoltageSource(LTSymbol s, IPartLookup parts, ImportReport report)
        {
            string val;
            s.Attributes.TryGetValue("Value", out val);
            val = (val ?? "").Trim();

            // DC numeric: become a Rail.
            if (IsDcNumber(val, out double dc))
            {
                Rail rail = new Rail();
                rail.Voltage = new Quantity(dc, Units.V);
                ApplyName(rail, s);
                return rail;
            }

            // Function-call SPICE source: VoltageSource with best-effort expression.
            VoltageSource vs = new VoltageSource();
            if (string.IsNullOrWhiteSpace(val))
            {
                ApplyName(vs, s);
                return vs;
            }

            Expression expr;
            if (TryParseSineExpression(val, out expr))
            {
                vs.Voltage = new Quantity(expr, Units.V);
            }
            else
            {
                // Preserve the raw text in the Voltage description; warn the user.
                report.Warning(s.LineNumber, "Voltage source expression '" + val + "' could not be fully translated; review after import.");
            }
            ApplyName(vs, s);
            return vs;
        }

        private static Component MakeCurrentSource(LTSymbol s, IPartLookup parts, ImportReport report)
        {
            CurrentSource cs = new CurrentSource();
            string val;
            if (s.Attributes.TryGetValue("Value", out val) && !string.IsNullOrWhiteSpace(val))
            {
                if (TryParseSpiceQuantity(val, Units.A, out Quantity q))
                    cs.Current = q;
                else
                    report.Warning(s.LineNumber, "Could not parse current source value '" + val + "', using default.");
            }
            ApplyName(cs, s);
            return cs;
        }

        private static Component MakeBjt(LTSymbol s, IPartLookup parts, ImportReport report, BjtType type)
        {
            string model = null;
            s.Attributes.TryGetValue("SpiceModel", out model);
            if (string.IsNullOrWhiteSpace(model))
                s.Attributes.TryGetValue("Value", out model);

            BipolarJunctionTransistor template = null;
            if (parts != null && !string.IsNullOrWhiteSpace(model))
                template = parts.TryGetByPartNumber(model) as BipolarJunctionTransistor;

            BipolarJunctionTransistor q;
            if (template != null)
            {
                q = (BipolarJunctionTransistor)Component.Deserialize(template.Serialize());
                q.PartNumber = template.PartNumber;
            }
            else
            {
                q = new BipolarJunctionTransistor();
                if (!string.IsNullOrWhiteSpace(model))
                {
                    q.PartNumber = model;
                    report.Info(s.LineNumber, "BJT model '" + model + "' not found in library; using default Ebers-Moll parameters.");
                }
            }
            q.Type = type;
            ApplyName(q, s);
            return q;
        }

        private static Component MakeJfet(LTSymbol s, IPartLookup parts, ImportReport report, JfetType type)
        {
            string model = null;
            s.Attributes.TryGetValue("SpiceModel", out model);
            if (string.IsNullOrWhiteSpace(model))
                s.Attributes.TryGetValue("Value", out model);

            JunctionFieldEffectTransistor template = null;
            if (parts != null && !string.IsNullOrWhiteSpace(model))
                template = parts.TryGetByPartNumber(model) as JunctionFieldEffectTransistor;

            JunctionFieldEffectTransistor j;
            if (template != null)
            {
                j = (JunctionFieldEffectTransistor)Component.Deserialize(template.Serialize());
                j.PartNumber = template.PartNumber;
            }
            else
            {
                j = new JunctionFieldEffectTransistor();
                if (!string.IsNullOrWhiteSpace(model))
                {
                    j.PartNumber = model;
                    report.Info(s.LineNumber, "JFET model '" + model + "' not found in library; using default parameters.");
                }
            }
            j.Type = type;
            ApplyName(j, s);
            return j;
        }

        private static Component MakeOpAmp(LTSymbol s, IPartLookup parts, ImportReport report)
        {
            string model = null;
            s.Attributes.TryGetValue("SpiceModel", out model);
            if (string.IsNullOrWhiteSpace(model))
                s.Attributes.TryGetValue("Value", out model);

            OpAmp template = null;
            if (parts != null && !string.IsNullOrWhiteSpace(model))
                template = parts.TryGetByPartNumber(model) as OpAmp;

            OpAmp op;
            if (template != null)
            {
                op = (OpAmp)Component.Deserialize(template.Serialize());
                op.PartNumber = template.PartNumber;
            }
            else
            {
                op = new OpAmp();
                if (!string.IsNullOrWhiteSpace(model))
                {
                    op.PartNumber = model;
                    report.Info(s.LineNumber, "Op-amp model '" + model + "' not found in library; using default parameters.");
                }
            }
            ApplyName(op, s);
            return op;
        }

        private static void ApplyName(Component c, LTSymbol s)
        {
            if (s.Attributes.TryGetValue("InstName", out string name) && !string.IsNullOrWhiteSpace(name))
                c.Name = name;
        }

        // === SPICE value parsing ===

        /// <summary>
        /// Try to parse a SPICE value like "1k", "100n", "1.5u", "10Meg" into a Quantity.
        /// SPICE prefix conventions (case-insensitive): T=1e12 G=1e9 MEG=1e6 K=1e3 (none)=1 M=1e-3 U=1e-6 N=1e-9 P=1e-12 F=1e-15.
        /// MIL=25.4e-6 is not supported (rare in LTSpice).
        /// </summary>
        public static bool TryParseSpiceQuantity(string s, Units units, out Quantity q)
        {
            q = null;
            if (string.IsNullOrWhiteSpace(s)) return false;

            // Strip the unit-symbol suffix if present (e.g. "1kOhm", "100nF", "10mH").
            string trimmed = s.Trim();
            string unitSuffix = units.ToString();
            if (!string.IsNullOrEmpty(unitSuffix) && trimmed.Length > unitSuffix.Length &&
                trimmed.EndsWith(unitSuffix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - unitSuffix.Length);
            }
            // Common single-letter unit suffixes for SI base units (V, A, H, F).
            // We've already stripped the proper unit name, so this catches "10A" â†’ "10".

            // Find the boundary between the numeric prefix and the SPICE suffix.
            int i = 0;
            // Optional sign.
            if (i < trimmed.Length && (trimmed[i] == '+' || trimmed[i] == '-')) i++;
            while (i < trimmed.Length && (char.IsDigit(trimmed[i]) || trimmed[i] == '.')) i++;
            // Optional exponent.
            if (i < trimmed.Length && (trimmed[i] == 'e' || trimmed[i] == 'E'))
            {
                i++;
                if (i < trimmed.Length && (trimmed[i] == '+' || trimmed[i] == '-')) i++;
                while (i < trimmed.Length && char.IsDigit(trimmed[i])) i++;
            }

            string numberPart = trimmed.Substring(0, i);
            string suffixPart = trimmed.Substring(i).Trim();

            if (numberPart.Length == 0) return false;

            if (!double.TryParse(numberPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
                return false;

            double scale = SpicePrefixScale(suffixPart);
            value *= scale;

            q = new Quantity(value, units);
            return true;
        }

        private static double SpicePrefixScale(string suffix)
        {
            if (string.IsNullOrEmpty(suffix)) return 1.0;
            string up = suffix.ToUpperInvariant();
            // Strip a trailing unit letter (V, A, F, H, S, Î©, etc.) that LTSpice users sometimes leave on.
            // Only do this if the result is a known prefix to avoid eating legitimate prefixes.
            if (up.Length > 1)
            {
                // Try peeling a single trailing letter and see if what's left is a prefix.
                string head = up.Substring(0, up.Length - 1);
                if (head == "T" || head == "G" || head == "MEG" || head == "K" || head == "M" ||
                    head == "U" || head == "N" || head == "P" || head == "F")
                    up = head;
            }
            switch (up)
            {
                case "T": return 1e12;
                case "G": return 1e9;
                case "MEG": return 1e6;
                case "K": return 1e3;
                case "M": return 1e-3;
                case "U": return 1e-6;
                case "N": return 1e-9;
                case "P": return 1e-12;
                case "F": return 1e-15;
                default: return 1.0;
            }
        }

        public static bool IsDcNumber(string s, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string trimmed = s.Trim();
            // Reject anything that looks like a function call.
            if (trimmed.Contains("(") || trimmed.Contains(")")) return false;
            // Try as a SPICE quantity (handles things like "5V", "-9", "1.5k").
            if (TryParseSpiceQuantity(trimmed, Units.V, out Quantity q))
            {
                try { value = (double)q; return true; }
                catch { return false; }
            }
            return false;
        }

        /// <summary>
        /// Translate SINE(offset amplitude freq ...) to a symbolic expression: offset + amplitude*sin(2Ï€*freq*t).
        /// Returns false for any other function-style spec.
        /// </summary>
        public static bool TryParseSineExpression(string s, out Expression expr)
        {
            expr = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string trimmed = s.Trim();
            // LTSpice accepts both SINE(...) and SIN(...) for the sine source.
            int open = trimmed.IndexOf('(');
            if (open <= 0) return false;
            string head = trimmed.Substring(0, open).Trim();
            if (!head.Equals("SINE", StringComparison.OrdinalIgnoreCase) &&
                !head.Equals("SIN", StringComparison.OrdinalIgnoreCase))
                return false;
            int close = trimmed.LastIndexOf(')');
            if (close <= open) return false;
            string body = trimmed.Substring(open + 1, close - open - 1).Trim();
            string[] parts = body.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;

            if (!TryParseSpiceQuantity(parts[0], Units.None, out Quantity offset)) return false;
            if (!TryParseSpiceQuantity(parts[1], Units.None, out Quantity amp)) return false;
            if (!TryParseSpiceQuantity(parts[2], Units.None, out Quantity freq)) return false;

            double off = (double)offset;
            double a = (double)amp;
            double f = (double)freq;

            Expression t = Component.t;
            expr = off + a * Call.Sin(2 * Math.PI * f * t);
            return true;
        }
    }
}

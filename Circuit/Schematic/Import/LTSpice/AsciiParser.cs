using System;
using System.IO;

namespace Circuit.LTSpiceImport
{
    /// <summary>
    /// Forward-pass parser for the LTSpice .asc text format (Version 4).
    /// Tolerant of unknown record types â€” unknowns are reported but parsing continues.
    /// </summary>
    public static class AsciiParser
    {
        public static LTSpiceSheet Parse(TextReader reader, ImportReport report)
        {
            LTSpiceSheet sheet = new LTSpiceSheet();
            LTSymbol currentSymbol = null;
            int lineNumber = 0;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("#")) continue;

                string[] parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                string keyword = parts[0].ToUpperInvariant();

                try
                {
                    switch (keyword)
                    {
                        case "VERSION":
                            if (parts.Length >= 2 && int.TryParse(parts[1], out int v))
                                sheet.Version = v;
                            break;

                        case "SHEET":
                            // SHEET <n> <w> <h> â€” ignored
                            break;

                        case "WIRE":
                            if (parts.Length >= 5)
                            {
                                LTWire w = new LTWire
                                {
                                    LineNumber = lineNumber,
                                    X1 = ParseInt(parts[1]),
                                    Y1 = ParseInt(parts[2]),
                                    X2 = ParseInt(parts[3]),
                                    Y2 = ParseInt(parts[4]),
                                };
                                sheet.Wires.Add(w);
                            }
                            else
                            {
                                report.Warning(lineNumber, "Malformed WIRE record.");
                            }
                            // Closing a SYMBOL context.
                            currentSymbol = null;
                            break;

                        case "FLAG":
                            if (parts.Length >= 4)
                            {
                                LTFlag f = new LTFlag
                                {
                                    LineNumber = lineNumber,
                                    X = ParseInt(parts[1]),
                                    Y = ParseInt(parts[2]),
                                    Name = string.Join(" ", parts, 3, parts.Length - 3),
                                };
                                sheet.Flags.Add(f);
                            }
                            else
                            {
                                report.Warning(lineNumber, "Malformed FLAG record.");
                            }
                            currentSymbol = null;
                            break;

                        case "SYMBOL":
                            if (parts.Length >= 5)
                            {
                                LTSymbol s = new LTSymbol
                                {
                                    LineNumber = lineNumber,
                                    SymbolName = parts[1],
                                    X = ParseInt(parts[2]),
                                    Y = ParseInt(parts[3]),
                                    Orientation = ParseOrientation(parts[4], lineNumber, report),
                                };
                                sheet.Symbols.Add(s);
                                currentSymbol = s;
                            }
                            else
                            {
                                report.Warning(lineNumber, "Malformed SYMBOL record.");
                                currentSymbol = null;
                            }
                            break;

                        case "SYMATTR":
                            if (currentSymbol == null)
                            {
                                report.Warning(lineNumber, "SYMATTR without preceding SYMBOL.");
                                break;
                            }
                            if (parts.Length >= 2)
                            {
                                string attrName = parts[1];
                                // Value runs to end-of-line after the attribute name.
                                int idx = IndexOfWord(trimmed, parts[1]);
                                string attrValue = "";
                                if (idx >= 0)
                                {
                                    int after = idx + parts[1].Length;
                                    attrValue = trimmed.Substring(after).Trim();
                                }
                                currentSymbol.Attributes[attrName] = attrValue;
                            }
                            break;

                        case "WINDOW":
                            // WINDOW <id> <x> <y> <just> <size> â€” purely cosmetic; ignored
                            break;

                        case "TEXT":
                            // TEXT <x> <y> <just> <size> <content...>
                            if (parts.Length >= 6)
                            {
                                int tx = ParseInt(parts[1]);
                                int ty = ParseInt(parts[2]);
                                // Find the content (the 5th whitespace-separated token onwards).
                                int contentIdx = SkipNTokens(trimmed, 5);
                                string content = contentIdx >= 0 ? trimmed.Substring(contentIdx).Trim() : "";
                                bool directive = content.StartsWith("!");
                                if (directive) content = content.Substring(1).Trim();
                                sheet.Texts.Add(new LTText
                                {
                                    LineNumber = lineNumber,
                                    X = tx,
                                    Y = ty,
                                    Content = content,
                                    IsSpiceDirective = directive,
                                });
                            }
                            currentSymbol = null;
                            break;

                        case "IOPIN":
                        case "BUSTAP":
                        case "DATAFLAG":
                        case "LINE":
                        case "RECTANGLE":
                        case "CIRCLE":
                        case "ARC":
                            // Decorations / I/O pins we don't yet handle.
                            report.Warning(lineNumber, "Unsupported LTSpice record '" + parts[0] + "' â€” skipped.");
                            break;

                        default:
                            report.Warning(lineNumber, "Unknown LTSpice record '" + parts[0] + "' â€” skipped.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    report.Error(lineNumber, "Parse error: " + ex.Message);
                }
            }

            return sheet;
        }

        public static LTSpiceSheet Parse(string text, ImportReport report)
        {
            using (StringReader sr = new StringReader(text))
                return Parse(sr, report);
        }

        private static int ParseInt(string s)
        {
            return int.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static LTOrientation ParseOrientation(string s, int lineNumber, ImportReport report)
        {
            switch (s.ToUpperInvariant())
            {
                case "R0": return LTOrientation.R0;
                case "R90": return LTOrientation.R90;
                case "R180": return LTOrientation.R180;
                case "R270": return LTOrientation.R270;
                case "M0": return LTOrientation.M0;
                case "M90": return LTOrientation.M90;
                case "M180": return LTOrientation.M180;
                case "M270": return LTOrientation.M270;
                default:
                    report.Warning(lineNumber, "Unknown orientation '" + s + "', defaulting to R0.");
                    return LTOrientation.R0;
            }
        }

        // Locate `word` in `line` as a whitespace-delimited token. Returns the start index, or -1.
        private static int IndexOfWord(string line, string word)
        {
            int start = 0;
            while (start < line.Length)
            {
                int found = line.IndexOf(word, start, StringComparison.Ordinal);
                if (found < 0) return -1;
                bool leftOK = found == 0 || char.IsWhiteSpace(line[found - 1]);
                int end = found + word.Length;
                bool rightOK = end == line.Length || char.IsWhiteSpace(line[end]);
                if (leftOK && rightOK) return found;
                start = found + 1;
            }
            return -1;
        }

        // Return the index in `line` just after the N-th whitespace-separated token (1-based).
        // Returns -1 if line has fewer than N tokens.
        private static int SkipNTokens(string line, int n)
        {
            int i = 0;
            int tokens = 0;
            while (i < line.Length)
            {
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                if (i >= line.Length) return -1;
                while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
                tokens++;
                if (tokens == n) return i;
            }
            return -1;
        }
    }
}

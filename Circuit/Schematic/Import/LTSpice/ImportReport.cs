using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Circuit.LTSpiceImport
{
    public enum ImportSeverity
    {
        Info,
        Warning,
        Error,
    }

    public class ImportReportEntry
    {
        public ImportSeverity Severity { get; }
        public int LineNumber { get; }
        public string Message { get; }

        public ImportReportEntry(ImportSeverity severity, int lineNumber, string message)
        {
            Severity = severity;
            LineNumber = lineNumber;
            Message = message;
        }

        public override string ToString()
        {
            return LineNumber > 0
                ? string.Format("[{0}] line {1}: {2}", Severity, LineNumber, Message)
                : string.Format("[{0}]: {1}", Severity, Message);
        }
    }

    public class ImportReport
    {
        private readonly List<ImportReportEntry> entries = new List<ImportReportEntry>();
        public IReadOnlyList<ImportReportEntry> Entries { get { return entries; } }

        public bool HasErrors { get { return entries.Any(e => e.Severity == ImportSeverity.Error); } }
        public bool HasWarnings { get { return entries.Any(e => e.Severity == ImportSeverity.Warning); } }

        public void Add(ImportSeverity severity, int lineNumber, string message)
        {
            entries.Add(new ImportReportEntry(severity, lineNumber, message));
        }

        public void Info(int lineNumber, string message) { Add(ImportSeverity.Info, lineNumber, message); }
        public void Warning(int lineNumber, string message) { Add(ImportSeverity.Warning, lineNumber, message); }
        public void Error(int lineNumber, string message) { Add(ImportSeverity.Error, lineNumber, message); }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            foreach (ImportReportEntry e in entries)
                sb.AppendLine(e.ToString());
            return sb.ToString();
        }
    }
}

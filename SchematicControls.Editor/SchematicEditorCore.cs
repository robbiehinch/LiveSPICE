using System;
using System.IO;
using SchematicControls.Editor.Edits;
using Util;

namespace SchematicControls.Editor
{
    /// <summary>
    /// Per-schematic editor session: holds a file path + dirty state and offers Save / Load
    /// operations. The actual visual control (WPF SchematicEditor or Avalonia SchematicCanvas)
    /// owns one of these alongside the <see cref="Circuit.Schematic"/> it edits.
    /// File pickers are host-supplied because the dialogue API differs between heads.
    /// </summary>
    public class SchematicEditorCore
    {
        public const string FileExtension = "schx";

        private readonly ISchematicHost host;
        private string filepath;
        private DateTime accessed = DateTime.Now;

        public SchematicEditorCore(ISchematicHost host) { this.host = host; }

        public string FilePath => filepath;
        public string Title => filepath == null ? "<Untitled>" : Path.GetFileNameWithoutExtension(filepath);
        public bool Dirty => host.Edits.Dirty;

        public event EventHandler FilePathChanged;
        public event EventHandler DirtyChanged;

        public void SetFilePath(string path)
        {
            if (filepath == path) return;
            filepath = path;
            accessed = DateTime.Now;
            FilePathChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool Save(ILog log = null)
        {
            if (filepath == null) return false;
            return SaveTo(filepath, log);
        }

        public bool SaveTo(string path, ILog log = null)
        {
            try
            {
                host.Schematic.Save(path);
                SetFilePath(path);
                host.Edits.Dirty = false;
                DirtyChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                log?.WriteLine(MessageType.Error, "Save failed: " + ex.Message);
                return false;
            }
        }

        public bool CheckForExternalModifications()
        {
            if (filepath == null || !File.Exists(filepath)) return false;
            if (File.GetLastWriteTime(filepath) <= accessed) return false;
            accessed = DateTime.Now;
            host.Edits.Dirty = true;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void Touch() => accessed = DateTime.Now;

        public void MarkClean()
        {
            host.Edits.Dirty = false;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

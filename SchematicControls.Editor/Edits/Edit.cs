using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SchematicControls.Editor.Edits
{
    /// <summary>An undoable editor action. Ported from <c>LiveSPICE/Utils/Edit.cs</c> with no behavioural changes.</summary>
    public abstract class Edit
    {
        public abstract void Do();
        public abstract void Undo();
        public override string ToString() { return ""; }
    }

    /// <summary>A group of edits performed (and undone) as a single atomic unit.</summary>
    public class EditList : Edit
    {
        private readonly IEnumerable<Edit> edits;

        private EditList(IEnumerable<Edit> edits) { this.edits = edits; }

        public static Edit New(IEnumerable<Edit> edits)
        {
            if (edits.Count() > 1) return new EditList(edits);
            return edits.First();
        }
        public static Edit New(params Edit[] edits) => New(edits.AsEnumerable());

        public override void Do() { foreach (Edit i in edits) i.Do(); }
        public override void Undo() { foreach (Edit i in edits.Reverse()) i.Undo(); }
        public override string ToString() => edits.FirstOrDefault(i => i.ToString() != "")?.ToString() ?? "";
    }

    /// <summary>Edit that sets a property; remembers the prior value for undo.</summary>
    public class PropertyEdit : Edit
    {
        private readonly object target;
        private readonly PropertyInfo property;
        private readonly object set;
        private readonly object unset;

        public PropertyEdit(object target, PropertyInfo property, object value)
            : this(target, property, value, property.GetValue(target, null)) { }

        public PropertyEdit(object target, PropertyInfo property, object set, object unset)
        {
            this.target = target;
            this.property = property;
            this.set = set;
            this.unset = unset;
        }

        public override void Do() { property.SetValue(target, set, null); }
        public override void Undo() { property.SetValue(target, unset, null); }
        public override string ToString() => "Set " + property.Name;
    }

    /// <summary>Editor undo/redo stack. Supports nested edit groups (BeginEditGroup/EndEditGroup).</summary>
    public class EditStack
    {
        private readonly List<Edit> undo = new List<Edit>();
        private readonly List<Edit> redo = new List<Edit>();
        private readonly Stack<List<Edit>> tentative = new Stack<List<Edit>>();
        private int clean = 0;

        public bool Dirty
        {
            get => undo.Count != clean;
            set => clean = value ? -1 : undo.Count;
        }

        public void Do(params Edit[] edits)
        {
            Edit edit = EditList.New(edits);
            edit.Do();
            Record(edit);
        }

        public void Did(params Edit[] edits) => Record(EditList.New(edits));

        private void Record(Edit edit)
        {
            if (tentative.Count > 0) tentative.Peek().Add(edit);
            else { undo.Add(edit); redo.Clear(); }
        }

        public void BeginEditGroup() => tentative.Push(new List<Edit>());

        public void EndEditGroup()
        {
            List<Edit> edits = tentative.Pop();
            if (edits.Count == 0) return;
            Record(EditList.New(edits));
        }

        public void CancelEditGroup()
        {
            List<Edit> edits = tentative.Pop();
            for (int i = edits.Count - 1; i >= 0; i--) edits[i].Undo();
        }

        public void Undo()
        {
            if (undo.Count == 0) return;
            int last = undo.Count - 1;
            undo[last].Undo();
            redo.Add(undo[last]);
            undo.RemoveAt(last);
        }

        public void Redo()
        {
            if (redo.Count == 0) return;
            int last = redo.Count - 1;
            redo[last].Do();
            undo.Add(redo[last]);
            redo.RemoveAt(last);
        }

        public bool CanUndo() => undo.Count > 0;
        public bool CanRedo() => redo.Count > 0;
        public string UndoDescription => undo.Count > 0 ? undo[undo.Count - 1].ToString() : "";
        public string RedoDescription => redo.Count > 0 ? redo[redo.Count - 1].ToString() : "";
    }
}

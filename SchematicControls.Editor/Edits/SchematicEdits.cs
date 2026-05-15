using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SchematicControls.Editor.Edits
{
    public class MoveElements : Edit
    {
        private readonly List<Circuit.Element> elements;
        private readonly Circuit.Coord dx;

        public MoveElements(IEnumerable<Circuit.Element> elements, Circuit.Coord dx)
        {
            this.elements = elements.ToList();
            Debug.Assert(this.elements.Count > 0);
            this.dx = dx;
        }

        public override void Do() { foreach (Circuit.Element e in elements) e.Move(dx); }
        public override void Undo() { foreach (Circuit.Element e in elements) e.Move(-dx); }
        public override string ToString() => "Move";
    }

    public class RotateElements : Edit
    {
        private readonly List<Circuit.Element> elements;
        private readonly int delta;
        private readonly Circuit.Point around;

        public RotateElements(IEnumerable<Circuit.Element> elements, int delta, Circuit.Point around)
        {
            this.elements = elements.ToList();
            Debug.Assert(this.elements.Count > 0);
            this.delta = delta;
            this.around = around;
        }

        public override void Do() { foreach (Circuit.Element e in elements) e.RotateAround(delta, around); }
        public override void Undo() { foreach (Circuit.Element e in elements) e.RotateAround(-delta, around); }
        public override string ToString() => "Rotate";
    }

    public class FlipElements : Edit
    {
        private readonly List<Circuit.Element> elements;
        private readonly double at;

        public FlipElements(IEnumerable<Circuit.Element> elements, double at)
        {
            this.elements = elements.ToList();
            Debug.Assert(this.elements.Count > 0);
            this.at = at;
        }

        public override void Do() { foreach (Circuit.Element e in elements) e.FlipOver(at); }
        public override void Undo() { foreach (Circuit.Element e in elements) e.FlipOver(at); }
        public override string ToString() => "Flip";
    }

    public class AddElements : Edit
    {
        private readonly Circuit.Schematic target;
        private readonly List<Circuit.Element> elements;

        public AddElements(Circuit.Schematic target, IEnumerable<Circuit.Element> elements)
        {
            this.target = target;
            this.elements = elements.ToList();
            Debug.Assert(this.elements.Count > 0);
        }

        public override void Do() { target.Add(elements); }
        public override void Undo() { target.Remove(elements); }

        public override string ToString() =>
            elements.Count > 1 ? "Add " + elements.Count + " Elements" : "Add " + elements[0].ToString();
    }

    public class RemoveElements : Edit
    {
        private readonly Circuit.Schematic target;
        private readonly List<Circuit.Element> elements;

        public RemoveElements(Circuit.Schematic target, IEnumerable<Circuit.Element> elements)
        {
            this.target = target;
            this.elements = elements.ToList();
            Debug.Assert(this.elements.Count > 0);
        }

        public override void Do() { target.Remove(elements); }
        public override void Undo() { target.Add(elements); }

        public override string ToString() =>
            elements.Count > 1 ? "Remove " + elements.Count + " Elements" : "Remove " + elements[0].ToString();
    }
}

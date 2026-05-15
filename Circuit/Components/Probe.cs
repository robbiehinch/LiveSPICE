namespace Circuit
{
    /// <summary>
    /// Single-terminal probe — marks a circuit node for voltage measurement during live
    /// simulation. Carries no electrical effect (<see cref="Analyze"/> is a no-op); the
    /// simulation host pulls samples from the node by adding <c>this.V</c> to its
    /// <see cref="Simulation.Output"/> list.
    ///
    /// Originally an internal LiveSPICE WPF type; lifted here so the Avalonia head can
    /// drop probes too without depending on the WPF assembly.
    /// </summary>
    public class Probe : OneTerminal
    {
        private EdgeType color = EdgeType.Magenta;
        [Serialize, System.ComponentModel.Description("Display color of the probe and its scope trace.")]
        public EdgeType Color { get { return color; } set { color = value; NotifyChanged(nameof(Color)); } }

        public Probe() { }
        public Probe(EdgeType color) { this.color = color; }

        public override void Analyze(Analysis Mna) { }

        protected internal override void LayoutSymbol(SymbolLayout Sym)
        {
            Coord w = new Coord(0, 0);
            Sym.AddTerminal(Terminal, w);

            Coord dw = new Coord(1, 1);
            Coord pw = new Coord(dw.y, -dw.x);

            w += dw * 10;
            Sym.AddWire(Terminal, w);

            Sym.AddLine(color, w - pw * 4, w + pw * 4);
            Sym.AddLoop(color,
                w + pw * 2,
                w + pw * 2 + dw * 10,
                w + dw * 12,
                w - pw * 2 + dw * 10,
                w - pw * 2);

            if (ConnectedTo != null)
                Sym.DrawText(() => V.ToString(), new Point(0, 6), Alignment.Far, Alignment.Near);
        }
    }
}

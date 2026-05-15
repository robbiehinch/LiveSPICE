using System;
using System.IO;
using Circuit;
using Circuit.LTSpiceImport;
using System.Collections.Generic;
using Util;
using System.Linq;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace Tests
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand().WithCommand("test", "Run tests", c => c
                                                    .WithArgument<string>("pattern", "Glob pattern for files to test")
                                                    .WithOption<bool>(new[] { "--plot" }, "Plot results")
                                                    .WithOption<bool>(new[] { "--stats" }, "Write statistics")
                                                    .WithOption(new[] { "--samples" }, () => 4800, "Samples")
                                                    .WithHandler(CommandHandler.Create<string, bool, bool, int, int, int, int>(Test)))
                                               .WithCommand("benchmark", "Run benchmarks", c => c
                                                    .WithArgument<string>("pattern", "Glob pattern for files to benchmark")
                                                    .WithHandler(CommandHandler.Create<string, int, int, int>(Benchmark)))
                                               .WithCommand("import-ltspice", "Import LTSpice .asc files and verify they build", c => c
                                                    .WithArgument<string>("pattern", "Glob pattern for .asc files to import")
                                                    .WithOption<bool>(new[] { "--simulate" }, "Simulate the imported circuit and write stats")
                                                    .WithHandler(CommandHandler.Create<string, bool, int, int, int>(ImportLTSpice)))
                                               .WithGlobalOption(new Option<int>("--sampleRate", () => 48000, "Sample Rate"))
                                               .WithGlobalOption(new Option<int>("--oversample", () => 8, "Oversample"))
                                               .WithGlobalOption(new Option<int>("--iterations", () => 8, "Iterations"));

            return await rootCommand.InvokeAsync(args);
        }

        public static void Test(string pattern, bool plot, bool stats, int sampleRate, int samples, int oversample, int iterations)
        {
            var log = new ConsoleLog() { Verbosity = MessageType.Info };
            var tester = new Test();

            foreach (var circuit in GetCircuits(pattern, log))
            {
                var outputs = tester.Run(circuit, t => Harmonics(t, 0.5, 82, 2), sampleRate, samples, oversample, iterations);
                if (plot)
                {
                    tester.PlotAll(circuit.Name, outputs);
                }
                if (stats)
                {
                    tester.WriteStatistics(circuit.Name, outputs);
                }
            }
        }

        public static void Benchmark(string pattern, int sampleRate, int oversample, int iterations)
        {
            var log = new ConsoleLog() { Verbosity = MessageType.Error };
            var tester = new Test();
            string fmt = "{0,-40}{1,12:G4}{2,12:G4}{3,12:G4}{4,12:G4}";
            System.Console.WriteLine(fmt, "Circuit", "Analysis (ms)", "Solve (ms)", "Sim (kHz)", "Realtime x");
            foreach (var circuit in GetCircuits(pattern, log))
            {
                double[] result = tester.Benchmark(circuit, t => Harmonics(t, 0.5, 82, 2), sampleRate, oversample, iterations, log: log);
                double analyzeTime = result[0];
                double solveTime = result[1];
                double simRate = result[2];
                string name = circuit.Name;
                if (name.Length > 39)
                    name = name.Substring(0, 39);
                System.Console.WriteLine(fmt, name, analyzeTime * 1000, solveTime * 1000, simRate / 1000, simRate / sampleRate);
            }
        }

        public static void ImportLTSpice(string pattern, bool simulate, int sampleRate, int oversample, int iterations)
        {
            var log = new ConsoleLog() { Verbosity = MessageType.Info };
            var tester = simulate ? new Test() : null;
            int failed = 0;
            int passed = 0;
            foreach (var filename in Globber.Glob(pattern))
            {
                log.WriteLine(MessageType.Info, "Importing {0}", filename);
                try
                {
                    var (schematic, report) = LTSpiceImporter.Import(filename);
                    foreach (var entry in report.Entries)
                        log.WriteLine(
                            entry.Severity == ImportSeverity.Error ? MessageType.Error :
                            entry.Severity == ImportSeverity.Warning ? MessageType.Warning :
                            MessageType.Info,
                            "  {0}", entry.ToString());

                    if (report.HasErrors)
                    {
                        log.WriteLine(MessageType.Error, "  Import failed: report contained errors");
                        failed++;
                        continue;
                    }

                    // Round-trip via .schx serialization.
                    string tmp = Path.Combine(Path.GetTempPath(),
                        "ltspice_" + Path.GetFileNameWithoutExtension(filename) + "_" + Guid.NewGuid().ToString("N") + ".schx");
                    schematic.Save(tmp);
                    var reloaded = Schematic.Load(tmp, log);
                    File.Delete(tmp);

                    // Build the reloaded schematic — this exercises the same path Tests uses.
                    Circuit.Circuit circuit = reloaded.Build(log);

                    log.WriteLine(MessageType.Info, "  OK ({0} symbols, {1} wires)",
                        schematic.Symbols.Count(), schematic.Wires.Count());

                    if (simulate)
                    {
                        circuit.Name = Path.GetFileNameWithoutExtension(filename);
                        var outputs = tester.Run(circuit, t => Harmonics(t, 0.5, 82, 2), sampleRate, 4800, oversample, iterations);
                        tester.WriteStatistics(circuit.Name, outputs);
                        log.WriteLine(MessageType.Info, "  Simulated and wrote Stats/{0}.csv ({1} unknowns)", circuit.Name, outputs.Count);
                    }
                    passed++;
                }
                catch (Exception ex)
                {
                    log.WriteLine(MessageType.Error, "  Exception: {0}", ex.Message);
                    failed++;
                }
            }
            log.WriteLine(MessageType.Info, "import-ltspice: {0} passed, {1} failed", passed, failed);
            if (failed > 0) Environment.ExitCode = 1;
        }

        private static IEnumerable<Circuit.Circuit> GetCircuits(string glob, ILog log) => Globber.Glob(glob).Select(filename =>
        {
            log.WriteLine(MessageType.Info, filename);
            var circuit = Schematic.Load(filename, log).Build();
            circuit.Name = Path.GetFileNameWithoutExtension(filename);
            return circuit;
        });

        // Generate a function with the first N harmonics of f0.
        private static double Harmonics(double t, double A, double f0, int N)
        {
            double s = 0;
            for (int i = 1; i <= N; ++i)
                s += Math.Sin(t * f0 * 2 * 3.1415 * i) / N;
            return A * s;
        }
    }
}

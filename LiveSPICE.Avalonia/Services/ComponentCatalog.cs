using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Util;

namespace LiveSPICE.Avalonia.Services
{
    /// <summary>
    /// Head-agnostic component-library catalog. Mirrors the model classes from the WPF
    /// <c>LiveSPICE.Library</c>/<c>Category</c> port, but without the
    /// <c>System.Windows.Input.KeyGesture</c> deps. Discovery sources: reflection over
    /// loaded assemblies for built-in components, plus XML libraries shipped under
    /// <c>Circuit/Components/*.xml</c>.
    /// </summary>
    public class ComponentEntry : INotifyPropertyChanged
    {
        public string Name { get; }
        public string Description { get; }
        public Circuit.Component Instance { get; }
        public Circuit.SymbolLayout Layout => Instance.LayoutSymbol();

        private bool isVisible = true;
        public bool IsVisible
        {
            get => isVisible;
            set { if (isVisible == value) return; isVisible = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible))); }
        }

        public ComponentEntry(Circuit.Component instance, string name, string description)
        {
            Instance = instance;
            Name = name;
            Description = description;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class CatalogCategory : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public ObservableCollection<CatalogCategory> Children { get; } = new ObservableCollection<CatalogCategory>();
        public ObservableCollection<ComponentEntry> Components { get; } = new ObservableCollection<ComponentEntry>();

        private bool isExpanded = false;
        public bool IsExpanded
        {
            get => isExpanded;
            set { if (isExpanded == value) return; isExpanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); }
        }

        public IEnumerable<ComponentEntry> Flatten =>
            Children.SelectMany(c => c.Flatten).Concat(Components);

        public void Clear() { Children.Clear(); Components.Clear(); }

        public CatalogCategory FindChild(string name)
        {
            CatalogCategory existing = Children.FirstOrDefault(c => c.Name == name);
            if (existing != null) return existing;
            CatalogCategory created = new CatalogCategory { Name = name };
            Children.Add(created);
            return created;
        }

        public void AddComponent(Circuit.Component c, string name, string description)
        {
            Components.Add(new ComponentEntry(c, name, description));
        }

        public void AddComponent(Circuit.Component c)
        {
            string n = string.IsNullOrEmpty(c.PartNumber) ? c.TypeName : c.PartNumber;
            AddComponent(c, n, c.Description);
        }

        public void AddComponent(Type t)
        {
            try
            {
                AddComponent((Circuit.Component)Activator.CreateInstance(t));
            }
            catch
            {
                // Components without a default constructor or with constructor failures are skipped.
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public static class ComponentCatalog
    {
        private static readonly Type[] CommonOrdered = new[]
        {
            typeof(Circuit.Conductor),
            typeof(Circuit.Ground),
            typeof(Circuit.Rail),
            typeof(Circuit.Resistor),
            typeof(Circuit.Capacitor),
            typeof(Circuit.Inductor),
            typeof(Circuit.VoltageSource),
            typeof(Circuit.CurrentSource),
            typeof(Circuit.NamedWire),
            typeof(Circuit.Label),
        };

        public static CatalogCategory BuildRoot()
        {
            CatalogCategory root = new CatalogCategory { Name = "All" };

            CatalogCategory common = root.FindChild("Common");
            common.IsExpanded = true;
            foreach (Type t in CommonOrdered)
                common.AddComponent(t);

            CatalogCategory generic = root.FindChild("Generic");
            Type baseType = typeof(Circuit.Component);
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                foreach (Type t in types)
                {
                    if (!t.IsPublic || t.IsAbstract) continue;
                    if (!baseType.IsAssignableFrom(t)) continue;
                    if (t.GetCustomAttribute<ObsoleteAttribute>() != null) continue;
                    if (CommonOrdered.Contains(t)) continue;
                    generic.AddComponent(t);
                }
            }

            foreach (string dir in CandidateComponentDirs())
            {
                if (Directory.Exists(dir))
                    LoadLibrariesRecursive(root, dir);
            }

            foreach (ComponentEntry e in root.Children.SelectMany(c => c.Flatten))
                root.Components.Add(e);

            return root;
        }

        private static IEnumerable<string> CandidateComponentDirs()
        {
            string app = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location ?? typeof(ComponentCatalog).Assembly.Location);
            if (app != null)
            {
                yield return Path.Combine(app, "Components");
                // Dev tree fallback: jump up to the repo root and into Circuit/Components.
                yield return Path.Combine(app, "..", "..", "..", "..", "Circuit", "Components");
            }
        }

        private static void LoadLibrariesRecursive(CatalogCategory root, string dir)
        {
            foreach (string sub in Directory.GetDirectories(dir))
            {
                CatalogCategory child = root.FindChild(Path.GetFileName(sub));
                LoadLibrariesRecursive(child, sub);
            }
            foreach (string file in Directory.GetFiles(dir))
                LoadOne(root, file);
        }

        private static void LoadOne(CatalogCategory root, string file)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            try
            {
                XDocument doc = XDocument.Load(file);
                XElement libEl = doc.Element("Library");
                if (libEl != null)
                {
                    XAttribute cat = libEl.Attribute("Category");
                    CatalogCategory child = root.FindChild(cat != null ? cat.Value : name);
                    foreach (XElement comp in libEl.Elements("Component"))
                    {
                        try
                        {
                            Circuit.Component c = Circuit.Component.Deserialize(comp);
                            child.AddComponent(c);
                        }
                        catch (Exception ex)
                        {
                            Util.Log.Global.WriteLine(MessageType.Warning, "Skipped component in '{0}': {1}", file, ex.Message);
                        }
                    }
                }
                else if (doc.Element("Schematic") != null)
                {
                    Circuit.Schematic s = Circuit.Schematic.Deserialize(doc.Element("Schematic"));
                    Circuit.Circuit c = s.Build();
                    root.AddComponent(c, name, c.Description);
                }
            }
            catch (System.Xml.XmlException)
            {
                LoadSpiceLib(root, file, name);
            }
            catch (Exception ex)
            {
                Util.Log.Global.WriteLine(MessageType.Warning, "Library '{0}' failed: {1}", file, ex.Message);
            }
        }

        private static void LoadSpiceLib(CatalogCategory root, string file, string name)
        {
            try
            {
                Circuit.Spice.Statements stmts = new Circuit.Spice.Statements { Log = Util.Log.Global };
                stmts.Parse(file);
                var models = stmts.OfType<Circuit.Spice.Model>().Where(m => m.Component != null).ToList();
                if (models.Count == 0) return;
                CatalogCategory child = root.FindChild(name);
                foreach (Circuit.Spice.Model m in models)
                    child.AddComponent(m.Component, m.Component.PartNumber, m.Description);
            }
            catch (Exception ex)
            {
                Util.Log.Global.WriteLine(MessageType.Warning, "SPICE library '{0}' failed: {1}", file, ex.Message);
            }
        }
    }
}

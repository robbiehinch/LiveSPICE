using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;

namespace LiveSPICE.Avalonia.Mcp
{
    /// <summary>Serializers turning circuit/schematic objects into JSON for MCP tool responses.</summary>
    internal static class SchematicSnapshot
    {
        public static JsonObject Element(Circuit.Element e, McpHost host)
        {
            JsonObject obj = new JsonObject
            {
                ["id"] = host.IdFor(e),
                ["kind"] = e.GetType().Name,
                ["lower_bound"] = Coord(e.LowerBound),
                ["upper_bound"] = Coord(e.UpperBound),
            };
            if (e is Circuit.Symbol sym)
            {
                obj["position"] = Coord(sym.Position);
                obj["rotation"] = sym.Rotation;
                obj["flip"] = sym.Flip;
                obj["component"] = Component(sym.Component);
            }
            else if (e is Circuit.Wire wire)
            {
                obj["a"] = Coord(wire.A);
                obj["b"] = Coord(wire.B);
                obj["node"] = wire.Node?.Name;
            }
            return obj;
        }

        public static JsonObject Component(Circuit.Component c)
        {
            JsonObject obj = new JsonObject
            {
                ["type_name"] = c.GetType().FullName,
                ["short_name"] = c.GetType().Name,
                ["name"] = c.Name,
            };
            if (!string.IsNullOrEmpty(c.PartNumber)) obj["part_number"] = c.PartNumber;

            JsonObject props = new JsonObject();
            foreach (PropertyInfo p in c.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(j => j.GetCustomAttribute<Circuit.Serialize>() != null &&
                            (j.GetCustomAttribute<BrowsableAttribute>() == null ||
                             j.GetCustomAttribute<BrowsableAttribute>().Browsable)))
            {
                object value;
                try { value = p.GetValue(c, null); } catch { continue; }
                TypeConverter conv = TypeDescriptor.GetConverter(p.PropertyType);
                string text = null;
                try { text = conv.CanConvertTo(typeof(string)) ? conv.ConvertToString(value) : value?.ToString(); } catch { }
                props[p.Name] = text;
            }
            obj["properties"] = props;
            return obj;
        }

        public static JsonObject Coord(Circuit.Coord c) =>
            new JsonObject { ["x"] = c.x, ["y"] = c.y };

        public static JsonArray Elements(IEnumerable<Circuit.Element> elements, McpHost host)
        {
            JsonArray arr = new JsonArray();
            foreach (Circuit.Element e in elements) arr.Add(Element(e, host));
            return arr;
        }
    }
}

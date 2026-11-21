using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

/// <summary>
/// P2CI (14/11/2026, user decision 3; ChatGPT tasks 56 and 78): the palette contract of BridGHe 0.4.0, compared exactly with a frozen table:
/// the 228 component and stand-alone parameter GUIDs (type, name, nickname, description, category, subcategory, exposure, obsolete) and their
/// 1,882 ports (direction, index, name, nickname, description, parameter type, access, optional and the explicit defaults), as Grasshopper
/// makes them when the plug-in registers them (a new instance, before any solution: the Units menu of Custom Material renames its ports
/// at solve time, outside this table).
/// <para>The table is <c>Fixtures/palette/components.json</c> and <c>ports.json</c>, written from the static extraction of task 56 (59e9def),
/// checked on the runtime of the branch before it was frozen and shown to the user. <b>The check never writes it</b>, and it is never generated
/// again from a run: a difference fails with the GUID, the port and the field. Update procedure: the failing run names the rows; each changed
/// row is explained and the user gives the OK; only those rows are edited by hand, the two SHA-256 below are updated in the same commit, whose
/// message names the decision. Without the files (a branch where the table is not yet approved) the check fails and says so.</para>
/// </summary>
internal static class PaletteContractChecks
{
    private sealed record ComponentRow(string Guid, string Type, string Name, string NickName, string Description,
        string Category, string SubCategory, string Exposure, bool Obsolete, string Source);
    private sealed record DefaultRow(string Type, JsonElement Value);
    private sealed record PortRow(string Guid, string Direction, int Index, string Name, string NickName, string Description,
        string Type, string Access, bool Optional, DefaultRow[] Defaults, string DefaultSource, string Source);

    /// <summary>The counts of the table: 228 GUIDs (220 components, 8 stand-alone parameters) and 1,880 ports (task 78 gave 1,876; P2CI: 4 truss
    /// ports of Bridge Solid Diaphragms removed and the 4 switches of Moving Point Force and of Moving Distributed Force added, all read on the
    /// source). 0.4.3 (19/11/2026): 1,882, with the optional inputs 5 Options and 6 ULS Combinations of Super Element Check (Brecciarola study,
    /// item 16, approved by the user with «Sì, correzioni S»). 0.5.0 (20/11/2026): 229 GUIDs and 1,890 ports, with Concrete Slab Section given
    /// by pitch and diameters (new GUID, 7 inputs and 1 output) and the former one with row areas hidden and obsolete (Brecciarola study, item 20,
    /// the user's request «per la soletta gli input devono essere base, altezza, passo e diametro armature sopra e sotto»). 0.6.0 (20/11/2026):
    /// 231 GUIDs and 1,904 ports: Super Element Check with 3 inputs (its optional inputs moved to the new Super Element Check Options, 6 inputs
    /// and 1 output) and the new Bridge Notable Stations (6 inputs, 5 outputs); the 13 rows approved by the user with «Sì, approvo». 0.7.0
    /// (21/11/2026): 232 GUIDs and 1,908 ports: Super Element Check without «Tau b» (user: «5 va bene. toglie.»), Tension Forces its second
    /// output; the new Bridge Moving Envelope (3 inputs, 2 outputs; user: «posizioni / inviluppo / combo concomitanti»). 0.7.2 (21/11/2026):
    /// 1,909 ports: the new input Auto Read of Bridge Read Section Excel (proposals approved by the user «per tutti i punti»).</summary>
    private const int Components = 232, Ports = 1909;

    /// <summary>The SHA-256 of the two frozen files (UTF-8, line ends normalised to LF): an edit of the table without the procedure above fails.
    /// P2CI (14/11/2026): the proposal shown to the user (INT\rapporti\P2CI-palette-proposta-20261114). 0.4.3 (19/11/2026): ports.json with the
    /// two rows of Super Element Check above, no other row changed. 0.5.0 (20/11/2026): the row of the new Concrete Slab Section and its 8 ports
    /// added; the row of the former one with its new name, description, exposure (hidden) and obsolete flag; no other row changed. 0.6.0
    /// (20/11/2026): the ports 2-6 of Super Element Check replaced by Options, the rows of Super Element Check Options and Bridge Notable
    /// Stations with their ports; no other row changed. 0.7.0 (21/11/2026): the output Tau b of Super Element Check removed and Tension Forces
    /// at index 1, the row of Bridge Moving Envelope and its 5 ports; no other row changed. 0.7.2 (21/11/2026, proposals 2, 4 and 8, texts in
    /// English): the row of Auto Read (Bridge Read Section Excel, input 2) added; the descriptions of Labels (Bridge Grillage Mesh) and of Family
    /// (Bridge Section Schedule, Bridge Family Property) changed; no other row changed.</summary>
    private const string ComponentsSha = "1C6ED0E73D17F25AEE6914F9CED7ECBBF4477EB87E5FE38168D8C65AB18AB92F",
        PortsSha = "1F0EEEE443FE8A4EFB6484CF78066E1479DBF8B4B06CFB2A0B9867E80E799ABD";

    internal static string Folder => Path.Combine(Path.GetDirectoryName(ExamplePaths.Root), "tests", "BridGHe.Smoke", "Fixtures", "palette");

    private static string Sha(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n"))));

    internal static void Regression()
    {
        string components = Path.Combine(Folder, "components.json"), portsFile = Path.Combine(Folder, "ports.json");
        if (!File.Exists(components) || !File.Exists(portsFile))
            throw new Exception("PALETTE FAIL: the frozen palette table is not in " + Folder + " (components.json, ports.json): it is frozen only with the user's OK.");
        var failures = new List<string>();
        if (Sha(components) != ComponentsSha) failures.Add("PALETTE the frozen components.json changed (SHA-256 " + Sha(components) + ", frozen " + ComponentsSha + "): edit it only by the procedure of the check");
        if (Sha(portsFile) != PortsSha) failures.Add("PALETTE the frozen ports.json changed (SHA-256 " + Sha(portsFile) + ", frozen " + PortsSha + "): edit it only by the procedure of the check");
        var expected = JsonSerializer.Deserialize<ComponentRow[]>(File.ReadAllText(components, Encoding.UTF8));
        var ports = JsonSerializer.Deserialize<PortRow[]>(File.ReadAllText(portsFile, Encoding.UTF8));
        if (expected == null || ports == null) throw new Exception("PALETTE FAIL: the frozen table cannot be read.");
        if (expected.Length != Components || ports.Length != Ports)
            failures.Add($"PALETTE the frozen table has {expected.Length} components and {ports.Length} ports, not {Components} and {Ports}");
        void Equal(string at, string field, object want, object actual)
        {
            if (!Equals(want, actual)) failures.Add(at + " | " + field + " | frozen " + JsonSerializer.Serialize(want) + " | now " + JsonSerializer.Serialize(actual));
        }

        // Every exported, concrete component and parameter type of the plug-in with a parameterless constructor, by its ComponentGuid.
        var actual = new Dictionary<string, IGH_DocumentObject>(StringComparer.Ordinal);
        foreach (var type in R2aDesignFixtures.Plugin.GetExportedTypes().Where(t => !t.IsAbstract && typeof(IGH_DocumentObject).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null))
        {
            var obj = (IGH_DocumentObject)Activator.CreateInstance(type);
            if (!(obj is GH_Component) && !(obj is IGH_Param)) continue;
            string key = obj.ComponentGuid.ToString("D");
            if (!actual.TryAdd(key, obj)) failures.Add("PALETTE duplicate GUID " + key + " (" + type.FullName + ", " + actual[key].GetType().FullName + ")");
        }
        var expectedIds = new HashSet<string>(StringComparer.Ordinal);
        var portKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in ports)
            if (!portKeys.Add(row.Guid + "/" + row.Direction + "/" + row.Index)) failures.Add("PALETTE duplicate port of the table " + row.Guid + "/" + row.Direction + "/" + row.Index);
        int compared = 0, portsCompared = 0, defaults = 0;
        foreach (var row in expected)
        {
            string at = row.Guid + " " + row.Name;
            if (!expectedIds.Add(row.Guid)) failures.Add("PALETTE duplicate GUID of the table " + row.Guid);
            if (!actual.TryGetValue(row.Guid, out var obj)) { failures.Add(at + " | missing component or parameter"); continue; }
            compared++;
            Equal(at, "Type", row.Type, obj.GetType().FullName);
            Equal(at, "Name", row.Name, obj.Name); Equal(at, "NickName", row.NickName, obj.NickName);
            Equal(at, "Description", row.Description, obj.Description);
            Equal(at, "Category", row.Category, obj.Category); Equal(at, "SubCategory", row.SubCategory, obj.SubCategory);
            Equal(at, "Exposure", row.Exposure, obj.Exposure.ToString()); Equal(at, "Obsolete", row.Obsolete, obj.Obsolete);
            var component = obj as GH_Component;
            foreach (string direction in new[] { "in", "out" })
            {
                var wanted = ports.Where(p => p.Guid == row.Guid && p.Direction == direction).OrderBy(p => p.Index).ToArray();
                var found = component == null ? Array.Empty<IGH_Param>() : (direction == "in" ? component.Params.Input : component.Params.Output).ToArray();
                Equal(at + "/" + direction, "port count", wanted.Length, found.Length);
                for (int n = 0; n < wanted.Length; n++)
                {
                    var p = wanted[n]; string where = at + "/" + direction + "[" + p.Index + "] " + p.Name;
                    Equal(where, "contiguous index of the table", n, p.Index);
                    if (p.Index < 0 || p.Index >= found.Length) { failures.Add(where + " | missing port"); continue; }
                    var live = found[p.Index]; portsCompared++;
                    Equal(where, "Name", p.Name, live.Name); Equal(where, "NickName", p.NickName, live.NickName);
                    Equal(where, "Description", p.Description, live.Description); Equal(where, "Type", p.Type, live.GetType().Name);
                    Equal(where, "Access", p.Access, live.Access.ToString()); Equal(where, "Optional", p.Optional, live.Optional);
                    var data = live.GetType().GetProperty("PersistentData")?.GetValue(live);
                    var values = data == null ? Array.Empty<IGH_Goo>() : ((IEnumerable)data.GetType().GetMethod("AllData").Invoke(data, new object[] { true })).Cast<IGH_Goo>().ToArray();
                    Equal(where, "default count", p.Defaults.Length, values.Length);
                    for (int i = 0; i < Math.Min(p.Defaults.Length, values.Length); i++)
                    {
                        var d = p.Defaults[i]; var value = values[i]; bool ok = false; defaults++;
                        switch (d.Type)
                        {
                            case "number": ok = value is GH_Number num && num.Value.Equals(d.Value.GetDouble()); break;
                            case "int": ok = value is GH_Integer integer && integer.Value == d.Value.GetInt32(); break;
                            case "bool": ok = value is GH_Boolean boolean && boolean.Value == d.Value.GetBoolean(); break;
                            case "string": ok = value is GH_String text && text.Value == d.Value.GetString(); break;
                            case "point": ok = value is GH_Point point && new[] { point.Value.X, point.Value.Y, point.Value.Z }.SequenceEqual(d.Value.EnumerateArray().Select(x => x.GetDouble())); break;
                            case "vector": ok = value is GH_Vector vector && new[] { vector.Value.X, vector.Value.Y, vector.Value.Z }.SequenceEqual(d.Value.EnumerateArray().Select(x => x.GetDouble())); break;
                        }
                        if (!ok) failures.Add(where + " | default[" + i + "] | frozen " + d.Type + " " + d.Value.GetRawText() + " | now " + (value?.GetType().Name ?? "null") + " " + value);
                    }
                }
            }
        }
        foreach (var id in actual.Keys.Except(expectedIds).OrderBy(k => k, StringComparer.Ordinal)) failures.Add("PALETTE new component or parameter " + id + " " + actual[id].Name + " (" + actual[id].GetType().FullName + ")");
        foreach (var row in ports.Where(p => !expectedIds.Contains(p.Guid) || (p.Direction != "in" && p.Direction != "out"))) failures.Add("PALETTE port of the table without its component, or of no direction: " + row.Guid + "/" + row.Direction + "/" + row.Index);
        foreach (string failure in failures) Console.WriteLine("FAIL: " + failure);
        if (failures.Count != 0) throw new Exception("PALETTE FAIL: " + failures.Count + " differences from the frozen table (named above). Review them; never write the table from a run.");
        Console.WriteLine($"PALETTE PASS: {compared} GUIDs, {portsCompared} ports and {defaults} explicit defaults equal to the frozen table ({Folder}).");
    }
}

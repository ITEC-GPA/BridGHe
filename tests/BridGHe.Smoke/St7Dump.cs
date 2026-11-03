#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using GPC.Converter.Straus7;

/// <summary>Numbering-independent dump of a Straus7 model, for the comparison of the former exporter with the GPC writer.
/// Entities are keyed by node coordinates; distributed beam actions are normalized to global intensities per actual length,
/// sampled at every breakpoint (left and right limits) of both files; principal-axis actions are rotated to global axes.</summary>
internal sealed class St7Dump
{
    internal readonly SortedDictionary<string, double[]> Values = new SortedDictionary<string, double[]>(StringComparer.Ordinal);
    internal readonly SortedDictionary<string, string> Texts = new SortedDictionary<string, string>(StringComparer.Ordinal);
    /// <summary>Beam → (case, component) → list of (start, end, v0, v1) linear pieces of global intensity per actual length.</summary>
    internal readonly Dictionary<string, List<(double S0, double S1, double V0, double V1)>> Distributed = new Dictionary<string, List<(double, double, double, double)>>(StringComparer.Ordinal);
    internal int Nodes, Beams, Plates, Links, Properties;

    // St7api.dll is loaded and released by each Straus7NativeApi session: bind per session, never with a cached DllImport.
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int IntRefCall(int file, int prop, ref int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TwoIntRefCall(int file, int a, int b, ref int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int OneIntRefCall(int file, int a, ref int value);
    private static IntPtr Function(string name) => GetProcAddress(GetModuleHandle("St7api.dll"), name);
    private static int Count(string name, int a) { int v = 0; if (Marshal.GetDelegateForFunctionPointer<OneIntRefCall>(Function(name))(1, a, ref v) != 0) throw new InvalidOperationException(name); return v; }
    private static int Count(string name, int a, int b) { int v = 0; if (Marshal.GetDelegateForFunctionPointer<TwoIntRefCall>(Function(name))(1, a, b, ref v) != 0) throw new InvalidOperationException(name); return v; }
    private static int ShearModulusMode(int property)
    {
        var call = Marshal.GetDelegateForFunctionPointer<IntRefCall>(GetProcAddress(GetModuleHandle("St7api.dll"), "St7GetBeamShearModulusMode"));
        int mode = 0; if (call(1, property, ref mode) != 0) throw new InvalidOperationException("St7GetBeamShearModulusMode"); return mode;
    }

    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string Key(double[] p) => "(" + string.Join(",", p.Select(v => Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture))) + ")";

    internal static St7Dump Read(string path)
    {
        var dump = new St7Dump();
        using var api = new Straus7NativeApi();
        api.OpenModelReadOnly(Path.GetFullPath(path), Path.GetTempPath());
        try { dump.Fill(api); }
        finally { api.CloseModel(); }
        return dump;
    }

    private void Fill(Straus7NativeApi api)
    {
        Texts["units"] = string.Join(",", api.ReadUnits());
        Nodes = api.Count(Straus7Entity.Node); Beams = api.Count(Straus7Entity.Beam); Plates = api.Count(Straus7Entity.Plate); Links = api.Count(Straus7Entity.Link);
        Values["count/nodes"] = new double[] { Nodes }; Values["count/beams"] = new double[] { Beams }; Values["count/plates"] = new double[] { Plates }; Values["count/links"] = new double[] { Links };
        var caseNames = new Dictionary<int, string>();
        for (int c = 1; c <= api.LoadCaseCount; c++)
        {
            string name = api.ReadLoadCase(c); caseNames[c] = name; var data = api.ReadLoadCaseData(c); var mass = api.ReadLoadCaseMassOption(c);
            Values["case/" + name + "/type,gravityDir,gravity"] = new double[] { data.Type, data.Type == 1 ? data.GravityDirection : 0, data.Type == 1 ? data.Gravity : 0 };
            Values["case/" + name + "/mass(structural,nonstructural)"] = new double[] { mass[0] ? 1 : 0, mass[1] ? 1 : 0 };
            Texts["case/" + name + "/number"] = c.ToString();
        }
        int freedom = api.FreedomCaseCount; Values["count/freedomCases"] = new double[] { freedom };
        var groups = new Dictionary<int, Straus7Group>();
        for (int g = 1; g <= api.GroupCount; g++) { var group = api.ReadGroup(g); groups[group.Id] = group; }
        // St7GetGroupByIndex returns the full path ("<root>"); the root group is compared separately.
        var root = groups.Values.FirstOrDefault(g => g.ParentId == -1);
        string GroupPath(int id)
        {
            if (!groups.TryGetValue(id, out var g) || g.ParentId == -1) return "";
            string name = g.Name; return root != null && name.StartsWith(root.Name + "\\", StringComparison.Ordinal) ? name.Substring(root.Name.Length + 1) : name;
        }
        Texts["group/root"] = groups.Values.Where(g => g.ParentId == -1).Select(g => g.Name).FirstOrDefault();
        foreach (var g in groups.Values.Where(g => g.ParentId != -1)) Texts["group/" + GroupPath(g.Id)] = "exists";
        var nodeKey = new Dictionary<int, string>(); var nodeXyz = new Dictionary<int, double[]>();
        for (int n = 1; n <= Nodes; n++)
        {
            var node = api.ReadNode(n); string key = Key(node.Coordinates); nodeKey[n] = key; nodeXyz[n] = node.Coordinates;
            Texts["node/" + key + "/number"] = n.ToString(); Texts["node/" + key + "/userId"] = node.UserId.ToString();
            for (int fc = 1; fc <= freedom; fc++)
                foreach (var a in api.ReadAttributes(Straus7Entity.Node, n, Straus7Attributes.Restraint).Where(a => a.Case == fc))
                {
                    var r = api.ReadRestraint(n, fc);
                    Values["node/" + key + "/restraint" + fc + "/status"] = r.Status.Select(s => (double)s).ToArray();
                    Values["node/" + key + "/restraint" + fc + "/values"] = r.Values;
                    Values["node/" + key + "/restraint" + fc + "/axes"] = Axes(api, r.Ucs);
                }
            foreach (var c in caseNames)
            {
                if (api.ReadAttributes(Straus7Entity.Node, n, Straus7Attributes.Force).Any(a => a.Case == c.Key)) Values["node/" + key + "/force/" + c.Value] = api.ReadNodeLoad(n, c.Key, false);
                if (api.ReadAttributes(Straus7Entity.Node, n, Straus7Attributes.Moment).Any(a => a.Case == c.Key)) Values["node/" + key + "/moment/" + c.Value] = api.ReadNodeLoad(n, c.Key, true);
            }
            foreach (int attribute in new[] { 4, 5, 6, 7, 8, 10 })
                if (api.ReadAttributes(Straus7Entity.Node, n, attribute).Length != 0) Texts["node/" + key + "/attribute" + attribute] = "present (not dumped)";
        }
        for (int l = 1; l <= Links; l++)
        {
            var link = api.ReadLink(l);
            var ends = link.Nodes.Select(n => nodeKey[n]).ToArray();
            Texts["link/" + string.Join("-", ends)] = "type " + link.Type + " ucs " + link.Ucs + " plane " + link.Plane;
        }
        var properties = new HashSet<int>();
        for (int b = 1; b <= Beams; b++)
        {
            var e = api.ReadElement(Straus7Entity.Beam, b);
            string key = "beam/" + nodeKey[e.Nodes[0]] + "-" + nodeKey[e.Nodes[1]];
            Texts[key + "/number"] = b.ToString(); Texts[key + "/userId"] = e.UserId.ToString();
            Texts[key + "/group"] = GroupPath(e.GroupId);
            var p = api.ReadProperty(Straus7Entity.Beam, e.Property); properties.Add(e.Property);
            int mode = ShearModulusMode(e.Property);
            var m = p.Material;
            // Effective shear modulus: mode 0 = from Poisson's ratio, 1 = explicit.
            double g = mode == 0 ? m[0] / (2 * (1 + m[2])) : m[1];
            Values[key + "/formulation"] = new double[] { p.Formulation };
            Values[key + "/material(E,Geff,nu,density,alpha,viscosity,damping,conductivity,specificHeat)"] = new[] { m[0], g, m[2], m[3], m[4], m[5], m[6], m[7], m[8] };
            var sp = p.SectionProperties;
            // Axis labels and signs of the principal axes are representation: compare A, J and the tensors
            // I = I11 a1⊗a1 + I22 a2⊗a2, SA = SA1 a1⊗a1 + SA2 a2⊗a2 in global axes, and the shear-centre offset in global axes.
            Values[key + "/section(A,J)"] = new[] { sp[0], sp[3] };
            Values[key + "/section(I tensor)"] = Tensor(e.InitialAxes, sp[1], sp[2]);
            Values[key + "/section(shear-area tensor)"] = Tensor(e.InitialAxes, sp[6], sp[7]);
            Values[key + "/section(shear-centre offset)"] = Enumerable.Range(0, 3).Select(k => sp[4] * e.InitialAxes[k] + sp[5] * e.InitialAxes[3 + k]).ToArray();
            Texts[key + "/section(XBAR,YBAR,ANGLE)"] = string.Join(" ", new[] { sp[8], sp[9], sp[10] }.Select(F));
            Texts[key + "/sectionType"] = p.SectionType + " " + (p.Geometry == null ? "" : string.Join(",", p.Geometry.Select(F)));
            Texts[key + "/propertyName"] = p.Name; Texts[key + "/materialName"] = p.MaterialName;
            Values[key + "/axis3"] = e.InitialAxes.Skip(6).Take(3).ToArray();
            var axes = e.InitialAxes; var I = nodeXyz[e.Nodes[0]]; var J = nodeXyz[e.Nodes[1]];
            var t = new[] { J[0] - I[0], J[1] - I[1], J[2] - I[2] }; double length = Math.Sqrt(t.Sum(v => v * v)); t = t.Select(v => v / length).ToArray();
            if (api.ReadAttributes(Straus7Entity.Beam, b, Straus7Attributes.BeamOffset).Length != 0) Values[key + "/offset"] = api.ReadBeamOffset(b);
            foreach (int end in new[] { 1, 2 }) foreach (bool rot in new[] { false, true })
                if (api.ReadAttributes(Straus7Entity.Beam, b, rot ? Straus7Attributes.BeamREndRelease : Straus7Attributes.BeamTEndRelease).Any(a => a.Local == end))
                { var r = api.ReadBeamRelease(b, end, rot); Texts[key + "/release" + end + (rot ? "R" : "T")] = string.Join(",", r.Status) + " " + string.Join(",", r.Values.Select(F)); }
            foreach (var a in api.ReadAttributes(Straus7Entity.Beam, b, 26))
            { double v = api.ReadBeamInitialState(b, a.Case, out int kind); Values[key + "/preload/" + caseNames[a.Case]] = new double[] { kind, v }; }
            foreach (var a in api.ReadAttributes(Straus7Entity.Beam, b, Straus7Attributes.BeamTemperatureGradient))
                Values[key + "/gradient/" + caseNames[a.Case]] = api.ReadBeamTemperatureGradient(b, a.Case);
            if (api.ReadAttributes(Straus7Entity.Beam, b, 45).Length != 0) Values[key + "/stringGroup"] = new double[] { api.ReadBeamStringGroup(b) };
            foreach (int attribute in new[] { 25, 41, 92, 94 })
                if (api.ReadAttributes(Straus7Entity.Beam, b, attribute).Length != 0) Texts[key + "/attribute" + attribute] = "present (not dumped)";
            // Distributed forces: global (28 local principal, 29 global, 44 principal moments).
            foreach (var (attribute, frame) in new[] { (Straus7Attributes.BeamDLL, Straus7LoadFrame.Principal), (Straus7Attributes.BeamDLG, Straus7LoadFrame.Global), (Straus7Attributes.BeamDML, Straus7LoadFrame.PrincipalMoment) })
                foreach (var a in api.ReadAttributes(Straus7Entity.Beam, b, attribute))
                {
                    var d = api.ReadBeamDistributedLoad(b, frame, a.Axis, a.Case, a.Id);
                    double factor = 1;
                    var dir = frame == Straus7LoadFrame.Global ? Unit(a.Axis) : new[] { axes[3 * (a.Axis - 1)], axes[3 * (a.Axis - 1) + 1], axes[3 * (a.Axis - 1) + 2] };
                    if (frame == Straus7LoadFrame.Global && d.Project != 0) { double c = dir.Zip(t, (x, y) => x * y).Sum(); factor = Math.Sqrt(Math.Max(0, 1 - c * c)); }
                    string kind = frame == Straus7LoadFrame.PrincipalMoment ? "moment" : "force";
                    foreach (var piece in Pieces(d))
                        for (int k = 0; k < 3; k++)
                        {
                            if (dir[k] == 0) continue;
                            string id = key + "/" + kind + "/" + caseNames[a.Case] + "/" + "XYZ"[k];
                            if (!Distributed.TryGetValue(id, out var list)) Distributed.Add(id, list = new List<(double, double, double, double)>());
                            list.Add((piece.S0, piece.S1, piece.V0 * dir[k] * factor, piece.V1 * dir[k] * factor));
                        }
                }
            foreach (var (attribute, moment, global) in new[] { (Straus7Attributes.BeamCFL, false, false), (Straus7Attributes.BeamCFG, false, true), (Straus7Attributes.BeamCML, true, false), (Straus7Attributes.BeamCMG, true, true) })
                foreach (var a in api.ReadAttributes(Straus7Entity.Beam, b, attribute))
                {
                    var v = api.ReadBeamPointLoad(b, a.Case, a.Id, moment, global);
                    var g3 = global ? new[] { v[0], v[1], v[2] } : Enumerable.Range(0, 3).Select(k => v[0] * axes[k] + v[1] * axes[3 + k] + v[2] * axes[6 + k]).ToArray();
                    string id = key + "/point" + (moment ? "Moment" : "Force") + "/" + caseNames[a.Case] + "/at " + F(v[3]);
                    Values[id] = Values.TryGetValue(id, out var prev) ? prev.Zip(g3, (x, y) => x + y).ToArray() : g3;
                }
        }
        Properties = properties.Count; Values["count/beamProperties"] = new double[] { Properties };
        int combinations = api.CombinationCount;
        for (int c = 1; c <= combinations; c++)
        {
            var combination = api.ReadCombination(c, caseNames.Count, freedom);
            for (int l = 0; l < caseNames.Count; l++)
                for (int f = 0; f < freedom; f++)
                {
                    double factor = combination.Factors[l * freedom + f];
                    if (factor != 0) Values["combination/" + combination.Name + "/" + caseNames[l + 1] + "/fc" + (f + 1)] = new[] { factor };
                }
        }
        int increments = api.ReadStageIncrementCount(0);
        for (int i = 1; i <= increments; i++)
        {
            string name = api.ReadStageIncrementName(0, i);
            foreach (var c in caseNames) { double factor = api.ReadStageFactor(0, i, c.Key, false); if (factor != 0) Values["nla0/" + name + "/" + c.Value] = new[] { factor }; }
            for (int f = 1; f <= freedom; f++) Values["nla0/" + name + "/freedom" + f] = new[] { api.ReadStageFactor(0, i, f, true) };
        }
        Values["count/stages"] = new double[] { api.StageCount };
        // Stages: name, the five flags, enabled groups by path, increments with load and freedom factors by case name, case states.
        var groupIds = groups.Keys.OrderBy(g => g).ToArray();
        for (int st = 1; st <= api.StageCount; st++)
        {
            var stage = api.ReadStage(st, groupIds); string sk = "stage" + st;
            Texts[sk + "/name"] = stage.Name;
            Values[sk + "/flags(morph,moveFixed,rotate,fluid,reset)"] = stage.Data.Select(d => (double)d).ToArray();
            Texts[sk + "/enabledGroups"] = string.Join(";", stage.EnabledGroups.Select(g => g == root?.Id ? "<root>" : GroupPath(g)).OrderBy(g => g, StringComparer.Ordinal));
            int stageIncrements = api.ReadStageIncrementCount(st);
            for (int i = 1; i <= stageIncrements; i++)
            {
                string name = api.ReadStageIncrementName(st, i);
                foreach (var c in caseNames)
                    if (api.ReadStageCaseEnabled(st, c.Key, false)) Values[sk + "/" + name + "/" + c.Value] = new[] { api.ReadStageFactor(st, i, c.Key, false) };
                for (int f = 1; f <= freedom; f++)
                    if (api.ReadStageCaseEnabled(st, f, true)) Values[sk + "/" + name + "/freedom" + f] = new[] { api.ReadStageFactor(st, i, f, true) };
            }
            Texts[sk + "/enabledCases"] = string.Join(";", caseNames.Where(c => api.ReadStageCaseEnabled(st, c.Key, false)).Select(c => c.Value).OrderBy(c => c, StringComparer.Ordinal));
        }
        // Moving loads: paths (case, template content, shape, surface, target, divisions, coordinates) and templates.
        int paths = api.Count((Straus7Entity)8);
        for (int path = 1; path <= paths; path++)
        {
            var coordinates = api.ReadMovingPath(path, out var flags); string pk = "path" + path;
            Values[pk + "/coordinates"] = coordinates; Texts[pk + "/case"] = caseNames.TryGetValue(flags[0], out var pc) ? pc : flags[0].ToString();
            Values[pk + "/flags(shape,surface,target,divisions,set)"] = flags.Skip(2).Select(v => (double)v).ToArray();
            int t = flags[1]; var tv = api.ReadMovingTemplate(t, out var tf);
            Values[pk + "/template/flags(colour,lanes,multilane,transition,lateral)"] = tf.Select(v => (double)v).ToArray(); Values[pk + "/template/values"] = tv;
            Values[pk + "/template/laneFactors"] = Enumerable.Range(1, tf[1]).Select(l => api.ReadMovingLaneFactor(t, l)).ToArray();
            int vehicles = Count("St7GetNumLoadPathTemplateVehicles", t);
            for (int v = 1; v <= vehicles; v++)
            {
                var vv = api.ReadMovingVehicle(t, v, out var vf); string vk = pk + "/vehicle" + v;
                Values[vk + "/flags,values"] = vf.Select(x => (double)x).Concat(vv).ToArray();
                for (int q = 1; q <= Count("St7GetNumLoadPathTemplatePointForces", t, v); q++)
                { var d = api.ReadMovingPointForce(t, v, q, out var ff); Values[vk + "/point" + q] = ff.Select(x => (double)x).Concat(d).ToArray(); }
                for (int q = 1; q <= Count("St7GetNumLoadPathTemplateDistributedForces", t, v); q++)
                { var d = api.ReadMovingDistributedForce(t, v, q, out var ff); Values[vk + "/distributed" + q] = ff.Select(x => (double)x).Concat(d).ToArray(); }
            }
        }
        // Plates: property formulation/material/thickness, pressures and offsets.
        for (int pl = 1; pl <= Plates; pl++)
        {
            var e = api.ReadElement(Straus7Entity.Plate, pl);
            string key = "plate/" + string.Join("-", e.Nodes.Select(n => nodeKey[n]));
            var p = api.ReadProperty(Straus7Entity.Plate, e.Property);
            Texts[key + "/group"] = GroupPath(e.GroupId); Values[key + "/formulation,materialType"] = new double[] { p.Formulation, p.MaterialType };
            Values[key + "/material"] = p.Material ?? new double[0];
            if (api.ReadAttributes(Straus7Entity.Plate, pl, Straus7Attributes.PlateThickness).Length != 0) Values[key + "/thickness"] = api.ReadPlateThickness(pl);
            Values[key + "/propertyGeometry"] = p.Geometry ?? new double[0];
            if (api.ReadAttributes(Straus7Entity.Plate, pl, Straus7Attributes.PlateOffset).Length != 0) Values[key + "/offset"] = api.ReadPlateOffset(pl);
            foreach (var a in api.ReadAttributes(Straus7Entity.Plate, pl, Straus7Attributes.PlateFacePressure)) Values[key + "/normalPressure/" + caseNames[a.Case]] = api.ReadPlateNormalPressure(pl, a.Case);
            foreach (var a in api.ReadAttributes(Straus7Entity.Plate, pl, Straus7Attributes.PlateGlobalPressure))
            { var v = api.ReadPlateGlobalPressure(pl, a.Local, a.Case, out int project); Values[key + "/globalPressure/" + caseNames[a.Case] + "/surface" + a.Local] = v.Concat(new double[] { project }).ToArray(); }
            foreach (int attribute in new[] { 53, 61, 70, 71 })
                if (api.ReadAttributes(Straus7Entity.Plate, pl, attribute).Length != 0) Texts[key + "/attribute" + attribute] = "present (not dumped)";
        }
    }

    private static double[] Tensor(double[] axes, double v1, double v2)
    {
        var t = new double[6]; int n = 0;
        for (int i = 0; i < 3; i++) for (int j = i; j < 3; j++) t[n++] = v1 * axes[i] * axes[j] + v2 * axes[3 + i] * axes[3 + j];
        return t;
    }
    private static double[] Unit(int axis) => axis == 1 ? new double[] { 1, 0, 0 } : axis == 2 ? new double[] { 0, 1, 0 } : new double[] { 0, 0, 1 };

    private static double[] Axes(Straus7NativeApi api, int ucs)
    {
        if (ucs == 1) return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        var u = api.ReadUcs(ucs).Data; // origin, point on X, point in XY
        double[] o = { u[0], u[1], u[2] }, x = { u[3] - u[0], u[4] - u[1], u[5] - u[2] }, y = { u[6] - u[0], u[7] - u[1], u[8] - u[2] };
        double lx = Math.Sqrt(x.Sum(v => v * v)); x = x.Select(v => v / lx).ToArray();
        double dot = x.Zip(y, (a, b) => a * b).Sum(); y = y.Zip(x, (a, b) => a - dot * b).ToArray(); double ly = Math.Sqrt(y.Sum(v => v * v)); y = y.Select(v => v / ly).ToArray();
        var z = new[] { x[1] * y[2] - x[2] * y[1], x[2] * y[0] - x[0] * y[2], x[0] * y[1] - x[1] * y[0] };
        return x.Concat(y).Concat(z).ToArray();
    }

    /// <summary>dl* law as linear pieces: PA, PB, P1, P2, a, b (see Straus7Assignments.Distribution).</summary>
    private static IEnumerable<(double S0, double S1, double V0, double V1)> Pieces(Straus7DistributedLoad d)
    {
        var v = d.Values; double pa = v[0], pb = v[1], p1 = v[2], p2 = v[3], a = v[4], end = 1 - v[5];
        (double, double)[] points = d.Type switch
        {
            0 => new[] { (a, pa), (end, pa) },
            1 => new[] { (a, pa), (end, pb) },
            2 => new[] { (0.0, p1), (a, pa), (1.0, p2) },
            3 => new[] { (0.0, p1), (a, pa), (end, pb) },
            4 => new[] { (a, pa), (end, pb), (1.0, p2) },
            5 => new[] { (0.0, p1), (a, pa), (end, pb), (1.0, p2) },
            _ => throw new NotSupportedException("dl type " + d.Type)
        };
        for (int k = 1; k < points.Length; k++) if (points[k].Item1 > points[k - 1].Item1) yield return (points[k - 1].Item1, points[k].Item1, points[k - 1].Item2, points[k].Item2);
    }

    internal static double Evaluate(List<(double S0, double S1, double V0, double V1)> pieces, double s, bool right)
    {
        double sum = 0;
        foreach (var p in pieces)
        {
            bool inside = right ? s >= p.S0 && s < p.S1 : s > p.S0 && s <= p.S1;
            if (inside) sum += p.V0 + (p.V1 - p.V0) * (s - p.S0) / (p.S1 - p.S0);
        }
        return sum;
    }

    /// <summary>Differences of b against a: absolute tolerance 1e-9 × max(1, |a|) for numbers.</summary>
    internal static List<(string Key, string A, string B)> Compare(St7Dump a, St7Dump b, double relative = 1e-9)
    {
        var differences = new List<(string, string, string)>();
        foreach (var key in a.Values.Keys.Union(b.Values.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            a.Values.TryGetValue(key, out var x); b.Values.TryGetValue(key, out var y);
            // Tolerance relative to the largest component of the array (tensors, vectors) and at least 1.
            double scale = x == null || y == null ? 1 : Math.Max(1, x.Concat(y).Select(Math.Abs).DefaultIfEmpty(0).Max());
            if (x == null || y == null || x.Length != y.Length || x.Zip(y, (p, q) => Math.Abs(p - q) > relative * scale).Any(t => t))
                differences.Add((key, x == null ? "-" : string.Join(" ", x.Select(F)), y == null ? "-" : string.Join(" ", y.Select(F))));
        }
        foreach (var key in a.Texts.Keys.Union(b.Texts.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            a.Texts.TryGetValue(key, out var x); b.Texts.TryGetValue(key, out var y);
            if (x != y) differences.Add((key, x ?? "-", y ?? "-"));
        }
        foreach (var key in a.Distributed.Keys.Union(b.Distributed.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            a.Distributed.TryGetValue(key, out var x); b.Distributed.TryGetValue(key, out var y);
            x ??= new List<(double, double, double, double)>(); y ??= new List<(double, double, double, double)>();
            var stations = x.Concat(y).SelectMany(p => new[] { p.S0, p.S1 }).Distinct().OrderBy(s => s).ToArray();
            var samples = stations.SelectMany(s => new[] { (s, false), (s, true) }).Concat(stations.Zip(stations.Skip(1), (s, t) => ((s + t) / 2, true)));
            double worst = 0, scale = 1;
            foreach (var (s, right) in samples)
            {
                double p = Evaluate(x, s, right), q = Evaluate(y, s, right);
                scale = Math.Max(scale, Math.Max(Math.Abs(p), Math.Abs(q))); worst = Math.Max(worst, Math.Abs(p - q));
            }
            if (worst > relative * scale) differences.Add((key + "/distributed", Describe(x), Describe(y)));
        }
        return differences;
    }
    private static string Describe(List<(double S0, double S1, double V0, double V1)> pieces) =>
        pieces.Count == 0 ? "-" : string.Join("; ", pieces.Select(p => $"[{F(p.S0)},{F(p.S1)}] {F(p.V0)}→{F(p.V1)}"));

    /// <summary>Differences grouped by the attribute name (last path segments without the entity key).</summary>
    internal static string Summary(List<(string Key, string A, string B)> differences)
    {
        var text = new StringBuilder();
        foreach (var group in differences.GroupBy(d => Category(d.Key)).OrderBy(g => g.Key, StringComparer.Ordinal))
            text.AppendLine($"{group.Count(),7}  {group.Key}   e.g. {group.First().Key}: {Trim(group.First().A)} | {Trim(group.First().B)}");
        return text.ToString();
    }
    private static string Trim(string s) => s.Length > 160 ? s.Substring(0, 160) + "…" : s;
    private static string Category(string key)
    {
        var parts = key.Split('/');
        if (parts[0] == "node" || parts[0] == "beam") return parts[0] + "/" + string.Join("/", parts.Skip(2).Select(p => p.StartsWith("at ") ? "at" : p));
        if (parts[0] == "group" || parts[0] == "link") return parts[0];
        return key;
    }
}
#endif

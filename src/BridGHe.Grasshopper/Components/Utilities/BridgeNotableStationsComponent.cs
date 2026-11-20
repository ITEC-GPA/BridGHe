using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Utilities;

/// <summary>The notable sections of every girder for the Detail Check (user rule of 20/11/2026): both ends and the middle of every
/// section-constant segment, the supports and the sagging maximum of each span from the Super Element Check, on the girder nodes. The points go
/// to Bridge Detail Stations (Locations), which selects those results for Bridge Detail Check; the guard warns where the shear or the stud flow
/// of the Super Element Check is higher away from the points.</summary>
public sealed class BridgeNotableStationsComponent : BridgeUtilityComponent
{
    public BridgeNotableStationsComponent() : base("Bridge Notable Stations", "NotableStations",
        "Notable sections of the girders for the Detail Check: both ends and the middle of every section-constant segment, the supports and the sagging maximum of each span, on the girder nodes. Connect Points to Bridge Detail Stations > Locations.",
        CategoryNameConstants.SUBCATEGORY_RESULTS) { }
    public override Guid ComponentGuid => new Guid("b6e1d0c4-7a3f-4c25-9e58-2d41f6a0c917");

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Model", "M", "Assembled Build BridGHe Model, N-mm: its girders (super elements) and their sections.", GH_ParamAccess.item);
        p.AddPointParameter("Supports", "S", "Support points on the girder axes (e.g. Bridge Grillage Mesh > Support Points), mm. Points away from a girder are ignored.", GH_ParamAccess.list);
        p[1].Optional = true;
        p.AddGenericParameter("Results", "R", "Optional Super Element Check results (Tension Check): sagging maximum of each span and guard on shear and stud flow.", GH_ParamAccess.tree);
        p[2].Optional = true;
        p.AddBooleanParameter("Middles", "SM", "Add the middle of every section-constant segment.", GH_ParamAccess.item, true);
        p.AddNumberParameter("Tolerance", "Tol", "Point matching tolerance [mm].", GH_ParamAccess.item, 1.0);
        p.AddPointParameter("Additional", "A", "Optional points to keep as they are (e.g. the stiffener points of other Bridge Detail Stations that read the same Detail Check), mm; a point within Tolerance of a notable point is not repeated.", GH_ParamAccess.list);
        p[5].Optional = true;
        // Support and joint points usually come one branch per girder: flattened, the component runs once and gives one list.
        p[1].DataMapping = GH_DataMapping.Flatten; p[5].DataMapping = GH_DataMapping.Flatten;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddPointParameter("Points", "P", "Notable points of all the girders, girder by girder along each girder; to Bridge Detail Stations > Locations.", GH_ParamAccess.list);
        p.AddTextParameter("Kinds", "K", "Why each point is there (segment end, segment middle, support, span sagging maximum), one per point.", GH_ParamAccess.list);
        p.AddNumberParameter("Chain mm", "C", "Distance of each point from the first node of its girder [mm], one per point.", GH_ParamAccess.list);
        p.AddTextParameter("Girders", "G", "Girder (super element) of each point.", GH_ParamAccess.list);
        p.AddTextParameter("Guard", "W", "Segments where the shear or the stud flow of the Super Element Check is higher away from the notable points.", GH_ParamAccess.list);
    }

    protected override void Solve(IGH_DataAccess da)
    {
        GH_Model model = null; var supports = new List<Point3d>(); bool middles = true; double tolerance = 1.0;
        if (!da.GetData(0, ref model)) return;
        da.GetDataList(1, supports); da.GetData(3, ref middles); da.GetData(4, ref tolerance);
        da.GetDataTree(2, out GH_Structure<IGH_Goo> input);
        var bridge = model?.GpcBridge ?? throw new ArgumentException("Model richiede il modello di ponte GPC di Build BridGHe Model.");
        if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentException("Tolerance deve essere positiva.");
        var results = (input?.AllData(false) ?? Enumerable.Empty<IGH_Goo>()).Select(g => g?.ScriptVariable()).OfType<BridgeStressResult>().ToArray();
        var samples = results.Where(r => r.Source != null).Select(r => new BridgeNotableStations.Sample(r.Source.BeamId, r.Source.StationMm, r.Point8_SteelSigma,
            r.Stage.Contributions.Sum(c => c.V), r.Stage.Studs?.Flow ?? double.NaN)).GroupBy(s => s.BeamId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var girders = bridge.Model.BeamElements.Values.Select(b => (Beam: b, Girder: bridge.Data.SuperElement(b.Guid)))
            .Where(t => !string.IsNullOrWhiteSpace(t.Girder)).GroupBy(t => t.Girder, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
        if (girders.Length == 0) throw new ArgumentException("Il modello non ha travi principali (superelementi).");
        var supportPoints = supports.Where(s => s.IsValid).Select(s => new BridgeNotableStations.P3(s.X, s.Y, s.Z)).ToArray();
        var points = new List<Point3d>(); var kinds = new List<string>(); var chainOut = new List<double>();
        var names = new List<string>(); var guard = new List<string>(); int total = 0;
        for (int g = 0; g < girders.Length; g++)
        {
            var elements = girders[g].Select(t => new BridgeNotableStations.Element(t.Beam.Source.OriginalId,
                new BridgeNotableStations.P3(t.Beam.StartPoint.X, t.Beam.StartPoint.Y, t.Beam.StartPoint.Z),
                new BridgeNotableStations.P3(t.Beam.EndPoint.X, t.Beam.EndPoint.Y, t.Beam.EndPoint.Z),
                t.Beam.BeamProperty == null ? "" : t.Beam.BeamProperty.Guid.ToString())).ToArray();
            var chain = BridgeNotableStations.Order(elements, tolerance);
            var own = elements.SelectMany(e => samples.TryGetValue(e.BeamId, out var s) ? s : Array.Empty<BridgeNotableStations.Sample>()).ToArray();
            var result = BridgeNotableStations.Points(chain, supportPoints, own, middles, tolerance);
            foreach (var pt in result.Points)
            {
                points.Add(new Point3d(pt.Point.X, pt.Point.Y, pt.Point.Z)); kinds.Add(string.Join(", ", pt.Kinds));
                chainOut.Add(pt.ChainMm); names.Add(girders[g].Key);
            }
            total += result.Points.Count;
            foreach (var note in result.Notes) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, girders[g].Key + ": " + note);
            foreach (var w in result.Guard)
                guard.Add($"{girders[g].Key}, tratto {w.Segment + 1}: {w.Quantity} massimo {w.SegmentMax:0.###} a {w.SegmentMaxChainMm / 1000:0.###} m, nei punti notevoli {w.NotableMax:0.###}");
        }
        // Additional points (the stiffener points of the other selectors): kept as they are, once, unless a notable point is already there.
        var additional = new List<Point3d>(); da.GetDataList(5, additional);
        foreach (var a in additional.Where(a => a.IsValid))
        {
            if (points.Any(q => q.DistanceTo(a) <= tolerance)) continue;
            points.Add(a); kinds.Add("punto aggiunto"); chainOut.Add(double.NaN); names.Add(""); total++;
        }
        if (guard.Count > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{guard.Count} tratti con taglio o flusso dei pioli più alti fuori dai punti notevoli: vedi Guard.");
        Message = $"{total} punti";
        da.SetDataList(0, points); da.SetDataList(1, kinds); da.SetDataList(2, chainOut); da.SetDataList(3, names); da.SetDataList(4, guard);
    }
}

using System;
using System.Linq;
using BridGHe.Common.Geometry;
using Rhino.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

internal static class BridgeAlignmentInputsTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool test, string name) { if (!test) throw new Exception("ALIGNMENT INPUTS: " + name); checks++; }
        void Reject(Action action) { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception("Expected invalid alignment inputs."); }
        using var line = new LineCurve(Point3d.Origin, new Point3d(100000, 0, 0));
        var regular = BridgeAlignmentInputs.Create(line);
        Check(regular.Spans.Length == 3 && Math.Abs(regular.Spans.Sum() - 100000) < 1e-9, "curve length determines equal spans without manual matching");
        var explicitInput = BridgeAlignmentInputs.Create(line, new[] { 58000d, 23000.4, 23000, -.2, 100000.3 }, joints: new[] { 25000d, 25000.5, 75000, 0, 100000 });
        Check(explicitInput.Supports.SequenceEqual(new[] { 0d, 23000, 58000, 100000 }), "sort, merge near duplicates and snap endpoints");
        Check(explicitInput.Joints.SequenceEqual(new[] { 25000d, 75000 }), "fabrication joints normalized independently");
        var overhangs = BridgeAlignmentInputs.Create(line, new[] { 95000d, 5000, 50000 }, endSupports: false);
        Check(overhangs.StartOverhang == 5000 && overhangs.EndOverhang == 5000 && overhangs.Spans.SequenceEqual(new[] { 45000d, 45000 }), "actual support stations derive both end overhangs");
        using var arc = new ArcCurve(new Arc(Plane.WorldXY, 220000, .47));
        using var spatial = Curve.CreateInterpolatedCurve(new[] { new Point3d(0,0,0), new Point3d(20000,3000,1000), new Point3d(65000,-5000,2400), new Point3d(100000,0,4000) }, 3);
        foreach (var curve in new Curve[] { arc, spatial })
        {
            double length = curve.GetLength(); var result = BridgeAlignmentInputs.Create(curve, new[] { length * .24, length * .68 });
            var downstream = BridgeParametricLayout.Create(1, 1, result.Spans, 3, 3500, null, 1500, 1500, result.StartOverhang, result.EndOverhang, 0, Point3d.Origin, 0, result.Alignment);
            Check(downstream.Supports.Zip(result.Supports, (a,b) => Math.Abs(a-b)).Max() < 1e-6, "exact compatibility with downstream layout on curved/3D alignment");
            Check(Math.Abs(result.Spans.Sum() - length) < 1e-6, "3D arc length, not plan projection");
            Check(result.SupportPoints.Length == result.Supports.Length && result.SupportPoints.All(p => p.IsValid), "support points available for preview");
        }
        var reversed = line.DuplicateCurve(); reversed.Reverse();
        Check(BridgeAlignmentInputs.Create(reversed, new[] { 20000d }).SupportPoints[1].DistanceTo(new Point3d(80000,0,0)) < 1e-6, "stations follow curve direction");
        Reject(() => BridgeAlignmentInputs.Create(line, new[] { -10d }));
        Reject(() => BridgeAlignmentInputs.Create(line, new[] { 100010d }));
        Reject(() => BridgeAlignmentInputs.Create(line, new[] { double.NaN }));
        Reject(() => BridgeAlignmentInputs.Create(line, new[] { 30000d }, endSupports:false));
        Reject(() => BridgeAlignmentInputs.Create(line, spanCount:0));
        Reject(() => BridgeAlignmentInputs.Create(line, tolerance:-1));
        using var closed = new Circle(1000).ToNurbsCurve(); Reject(() => BridgeAlignmentInputs.Create(closed));
        using var doc = new GH_Document();
        var preparation = BridgeSegmentExcelTests.Add(doc, "BridgeAlignmentInputsComponent");
        var segmentComponent = BridgeSegmentExcelTests.Add(doc, "BridgeSegmentsComponent");
        var layoutComponent = BridgeSegmentExcelTests.Add(doc, "BridgeParametricLayoutComponent");
        ((Param_Curve)preparation.Params.Input[0]).PersistentData.Append(new GH_Curve(line), new GH_Path(0,0));
        ((Param_Number)preparation.Params.Input[1]).PersistentData.AppendRange(new[]{new GH_Number(58000),new GH_Number(23000)}, new GH_Path(0,0));
        segmentComponent.Params.Input[0].AddSource(preparation.Params.Output[7]);segmentComponent.Params.Input[1].AddSource(preparation.Params.Output[8]);
        layoutComponent.Params.Input[13].AddSource(preparation.Params.Output[0]);layoutComponent.Params.Input[2].AddSource(preparation.Params.Output[1]);
        layoutComponent.Params.Input[8].AddSource(preparation.Params.Output[2]);layoutComponent.Params.Input[9].AddSource(preparation.Params.Output[3]);
        BridgeSegmentExcelTests.Solve(doc);
        Check(layoutComponent.Params.Output[0].VolatileDataCount==1 && segmentComponent.Params.Output[0].VolatileDataCount==1,"real GH connections accept multi-index input paths and empty automatic joints");
        ((Param_Number)preparation.Params.Input[4]).SetPersistentData(new GH_Number(70000),new GH_Number(20000));preparation.ExpireSolution(false);BridgeSegmentExcelTests.Solve(doc);
        var jointPath=preparation.Params.Output[7].VolatileData.Paths.Single();
        Check(jointPath.Length==1 && jointPath[0]==0,"joint output is explicitly branch {0}, irrespective of input paths: "+jointPath);
        Console.WriteLine($"ALIGNMENT INPUTS PASS: {checks} checks; straight, curved and 3D curves, station normalization, overhangs, curve direction and exact downstream span compatibility.");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using GPC.Model.ElementProperties;

// Task 190, tranche 1. No fixture/capture generation and no native Straus7 call.
internal static class UniqueSectionChecks
{
    private sealed record Family(string Old, string Current, string Guid, string[] Ports, int Shape);
    private static readonly Family[] families =
    {
        new("StrausCircularSolidSectionComponent", "BeamSectionSolidCircleComponent", "91092dc4-a478-5bae-b104-6d9b79acfa48", new[]{"D"}, 1),
        new("StrausCircularHollowSectionComponent", "BeamSectionHollowCircleComponent", "52dfbc6c-40ee-5ecc-8de7-a0252a1aa3ca", new[]{"D","T"}, 2),
        new("StrausRectangleSolidSectionComponent", "BeamSectionSolidRectComponent", "0d17a80e-109b-5a2b-a68c-f0cbacf6f453", new[]{"B","D"}, 3),
        new("StrausRectangleHollowSectionComponent", "BeamSectionHollowRectComponent", "5fba2a07-4ad4-54ca-9fe2-8a8350e7e912", new[]{"B","D","T1","T2"}, 4),
        new("StrausISectionComponent", "BeamSectionIComponent", "6ce96dfc-2dda-53ea-9740-b0e2535cd7ad", new[]{"B1","B2","D","T1","T2","T3"}, 7),
    };
    private static Assembly plugin;
    private static int checks, failed;
    private static void Check(bool ok, string text)
    {
        checks++; if(!ok) failed++;
        Console.WriteLine((ok ? "PASS: " : "FAIL: ")+text);
    }
    private static GH_Component Component(string type) => (GH_Component)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.Components.Sections."+type, true));
    private static void Add(GH_Document doc, IGH_DocumentObject value) { value.CreateAttributes(); doc.AddObject(value, false); }
    private static void Near(double actual, double expected, string text) => Check(Math.Abs(actual-expected) <= 1e-10*Math.Max(1,Math.Abs(expected)), text+$": {actual:R}, expected {expected:R}");

    internal static int Run(bool saveBase)
    {
        checks=failed=0; plugin=BridgeMergeTests.Initialize();
        foreach(var f in families)
        {
            var c=Component(f.Old);
            Check(c.Obsolete, f.Old+": obsolete");
            Check(c.Exposure==GH_Exposure.hidden, f.Old+": hidden");
            Check(c.ComponentGuid==Guid.Parse(f.Guid), f.Old+": original GUID");
            Check(c.Params.Input.Select(p=>p.Name).SequenceEqual(f.Ports), f.Old+": original input order");
            Check(c.Params.Input.All(p=>p is Param_Number && p.Access==GH_ParamAccess.item && !p.Optional && ((Param_Number)p).PersistentData.DataCount==0), f.Old+": original parameter types and defaults");
            Check(c.Params.Output.Count==1 && c.Params.Output[0] is Param_GenericObject && c.Params.Output[0].Name=="Section", f.Old+": original output");
            var current=Component(f.Current);
            Check(!current.Obsolete && current.Exposure!=GH_Exposure.hidden, f.Current+": replacement remains available");
        }
        // These retain domains absent from the five simple standard components: do not hide them with the duplicates.
        foreach(string type in new[]{"StrausBglRectangleSectionComponent","StrausBglISectionComponent","StrausTSectionComponent","StrausLipChannelSectionComponent","StrausAngleSectionComponent","StrausBxsSectionComponent","StrausLibrarySectionComponent"})
        {
            var c=Component(type); Check(!c.Obsolete && c.Exposure!=GH_Exposure.hidden, type+": uncovered/native domain remains available");
        }
        Archives(saveBase);
        Domains();
        Console.WriteLine($"UNIQUE SECTIONS 190: {checks-failed}/{checks} PASS, {failed} FAIL");
        return failed==0 ? 0 : 1;
    }

    private static void Archives(bool saveBase)
    {
        string folder=Path.GetFullPath(Path.Combine("artifacts","190")); Directory.CreateDirectory(folder);
        foreach(string extension in new[]{".ghx",".gh"})
        {
            string path=Path.Combine(folder,"old-five-sections"+extension);
            // D6 (0.4.2): in regression all a checkout that has not made its own base documents (--sections-190-before, on the base of task 190)
            // reads those of task 190, kept as fixtures (Fixtures/sections-190/PROVENIENZA.md).
            string fixture=Path.GetFullPath(Path.Combine("tests","BridGHe.Smoke","Fixtures","sections-190","old-five-sections"+extension));
            if(!saveBase && !File.Exists(path) && File.Exists(fixture)) path=fixture;
            if(saveBase && !File.Exists(path))
            {
                using var doc=new GH_Document(); doc.Enabled=false;
                foreach(var f in families)
                {
                    var c=Component(f.Old); Add(doc,c); c.Locked=true;
                    for(int i=0;i<c.Params.Input.Count;i++) ((Param_Number)c.Params.Input[i]).SetPersistentData(100+i);
                    var source=new Param_Number(); Add(doc,source); source.SetPersistentData(321); c.Params.Input[0].AddSource(source);
                    var sink=new Param_GenericObject(); Add(doc,sink); sink.AddSource(c.Params.Output[0]);
                }
                var written=new GH_Archive();
                Check(written.AppendObject(doc,"Definition") && written.WriteToFile(path,false,false), "base document created "+extension);
            }
            Check(File.Exists(path), "old document available "+extension);
            if(!File.Exists(path)) continue;
            using var restored=new GH_Document(); restored.Enabled=false;
            var archive=new GH_Archive(); Check(archive.ReadFromFile(path) && archive.ExtractObject(restored,"Definition"), "old document opens "+extension);
            var components=restored.Objects.OfType<GH_Component>().ToArray();
            Check(components.Length==5, "all old components loaded "+extension);
            foreach(var f in families)
            {
                var c=components.Single(x=>x.ComponentGuid==Guid.Parse(f.Guid));
                Check(c.GetType().Name==f.Old && c.Locked, f.Old+": old class and lock retained "+extension);
                Check(c.Params.Input.Select(p=>p.Name).SequenceEqual(f.Ports), f.Old+": saved ports retained "+extension);
                Check(c.Params.Input.Select((p,i)=>((Param_Number)p).PersistentData.AllData(true).OfType<GH_Number>().Single().Value==100+i).All(v=>v), f.Old+": saved numbers retained "+extension);
                Check(c.Params.Input[0].Sources.Count==1 && restored.Objects.OfType<Param_GenericObject>().Any(p=>p.Sources.Contains(c.Params.Output[0])), f.Old+": input and output wires retained "+extension);
            }
            var roundtrip=new GH_Archive(); Check(roundtrip.AppendObject(restored,"Definition"), "old components can still be saved "+extension);
            using var again=new GH_Document(); again.Enabled=false;
            Check(roundtrip.ExtractObject(again,"Definition") && again.Objects.OfType<GH_Component>().Select(c=>c.ComponentGuid).OrderBy(g=>g).SequenceEqual(components.Select(c=>c.ComponentGuid).OrderBy(g=>g)), "old components reopen again "+extension);
        }
    }

    private static LegacySectionRecipe Solve(Family family, params double[] values)
    {
        using var doc=new GH_Document(); var c=Component(family.Current); Add(doc,c);
        for(int i=0;i<values.Length;i++)
        {
            var p=(Param_Number)c.Params.Input[i]; p.PersistentData.Clear(); p.SetPersistentData(values[i]);
        }
        GH_Document.EnableSolutions=true; doc.Enabled=true; doc.NewSolution(false,GH_SolutionMode.Silent);
        Check(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0, family.Current+": solves without errors");
        var goo=c.Params.Output[0].VolatileData.AllData(true).Single(g=>g!=null && g.IsValid);
        return (LegacySectionRecipe)goo.GetType().GetProperty("Gpc").GetValue(goo);
    }

    private static void Domain(Family family, double[] inputs, double[] nativeDimensions, double area, double ixx, double iyy)
    {
        var recipe=Solve(family,inputs);
        Check(recipe.NativeKey==null, family.Current+": GPC recipe without native resolver");
        var property=GpcPropertyBuilders.Beam("190 "+family.Shape,5,recipe,GpcFixtures.Steel());
        var section=((ElasticBeamProperty)property.Property).Section;
        Near(section.Area,area,family.Current+": area [mm2]");
        Near(section.Jxx,ixx,family.Current+": Ixx [mm4]");
        Near(section.Jyy,iyy,family.Current+": Iyy [mm4]");
        Check(property.Data.Native==null,family.Current+": GPC properties without native override");
        // A beam rotation is not a rotation of the section's parameterisation. Both must preserve the native shape mapping.
        foreach(double angle in new[]{0.0,37.0})
        {
            var beam=GpcBeamBuilders.New(new GPC.Geometry.Point3d(0,0,0),new GPC.Geometry.Point3d(3000,4000,0),property,null,angle,0,0,Guid.NewGuid());
            var model=LegacyBuildModel.Build("190 section",new BridgeUnits(DefinitionUnits.Nmm),.001,
                new List<(Guid,LegacyNodeDraft)>(),new List<(Guid,LegacyBeamDraft)>{(beam.Beam.Guid,beam)},new List<(Guid,LegacyPlateDraft)>(),new List<LegacyCombinationDraft>(),new List<LegacyStageDraft>());
            var calls=Straus7Recorder.Record(model);
            string dimensionText="["+string.Join(" ",nativeDimensions.Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture)))+"]";
            var geometry=calls.Where(x=>x.StartsWith("SetBeamSectionGeometry(",StringComparison.Ordinal)).ToArray();
            Check(geometry.Length==1 && geometry[0].StartsWith("SetBeamSectionGeometry(1, "+family.Shape+", "+dimensionText+",",StringComparison.Ordinal), family.Current+$": native shape/dimensions at beam angle {angle}: "+string.Join(" | ",geometry));
            Check(!calls.Any(x=>x.StartsWith("SetBeamSectionMesh(") || x.StartsWith("SetNativeSection(")), family.Current+": no BXS/native-property override in the standard domain");
        }
        var bytes=GpcDraftArchive.Write(recipe); var read=GpcDraftArchive.ReadSection(bytes);
        Check(GpcDraftArchiveChecks.Text(recipe)==GpcDraftArchiveChecks.Text(read),family.Current+": section recipe round trip");
    }

    private static void Domains()
    {
        foreach(double d in new[]{20.0,300.0}) Domain(families[0],new[]{d},new[]{d,0,0,0,0,0},Math.PI*d*d/4,Math.PI*Math.Pow(d,4)/64,Math.PI*Math.Pow(d,4)/64);
        foreach(double t in new[]{3.0,40.0})
        {
            double d=200,inside=d-2*t;
            Domain(families[1],new[]{d,t},new[]{d,0,0,t,0,0},Math.PI*(d*d-inside*inside)/4,Math.PI*(Math.Pow(d,4)-Math.Pow(inside,4))/64,Math.PI*(Math.Pow(d,4)-Math.Pow(inside,4))/64);
        }
        foreach(var (d,b) in new[]{(500.0,300.0),(150.0,450.0)}) Domain(families[2],new[]{d,b},new[]{b,d,0,0,0,0},b*d,b*Math.Pow(d,3)/12,d*Math.Pow(b,3)/12);
        foreach(var (t1,t2) in new[]{(20.0,8.0),(7.0,15.0)})
        {
            double d=500,b=300,di=d-2*t1,bi=b-2*t2;
            Domain(families[3],new[]{d,b,t1,t2},new[]{b,d,0,t1,t2,0},b*d-bi*di,(b*Math.Pow(d,3)-bi*Math.Pow(di,3))/12,(d*Math.Pow(b,3)-di*Math.Pow(bi,3))/12);
        }
        foreach(var (b1,b2,t1,t2) in new[]{(300.0,200.0,25.0,15.0),(200.0,300.0,15.0,25.0)})
        {
            double d=600,tw=12,hw=d-t1-t2;
            var rectangles=new[]{(B:b1,H:t1,Y:t1/2),(B:tw,H:hw,Y:t1+hw/2),(B:b2,H:t2,Y:d-t2/2)};
            double a=rectangles.Sum(p=>p.B*p.H),cy=rectangles.Sum(p=>p.B*p.H*p.Y)/a;
            double ix=rectangles.Sum(p=>p.B*Math.Pow(p.H,3)/12+p.B*p.H*Math.Pow(p.Y-cy,2));
            double iy=rectangles.Sum(p=>p.H*Math.Pow(p.B,3)/12);
            Domain(families[4],new[]{b1,b2,d,t1,t2,tw,0},new[]{b1,b2,d,t1,t2,tw},a,ix,iy);
        }
        var rounded=Solve(families[4],300,200,600,25,15,12,10);
        Console.WriteLine($"LIMITATION (pre-existing): I recipe Radius={rounded.R:R}; Beam Property copies Radius={GpcPropertyBuilders.PropertySection(rounded).R:R}. BGL I remains available. This tranche does not change the radius calculation.");
    }
}

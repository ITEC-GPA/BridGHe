using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;

internal static class BridgeDefinitionTests
{
    internal static void Run()
    {
        int checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception("MODULAR BRIDGE: "+message);checks++;}
        void Reject(Action action){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Expected invalid definition to fail.");}
        var original=SimpleBridgeTests.Input();
        foreach(bool truss in new[]{false,true})
        foreach(double maximum in new[]{0d,12000,17500})
        {
            var settings=new BridgeSegments{MaximumLength=maximum};
            var plan=BridgeFabricationPlan.Create(original.Alignment.GetLength(),original.GirderOffsets.Length,settings);
            var definition=new BridgeDefinition(original.Alignment,new(original.Supports,original.SupportSkewDegrees),new(original.GirderOffsets,original.GirderLevels),
                new(original.DeckWidths,original.DeckThickness,original.CrossfallStations,original.CrossfallPercent,original.DeckBeamLevel),plan,new(original.Diaphragms,truss));
            var mesh=new BridgeMeshSettings(original.MaximumElementLength,original.ChordTolerance,original.DeckBeamSpacing,original.DeckBeamStations);
            var before=SimpleBridgeBuilder.Build(original with{Segments=settings,Truss=truss});
            var after=SimpleBridgeBuilder.Build(definition.ToInput(mesh));
            Check(before.Members.SequenceEqual(after.Members)&&before.Nodes.SequenceEqual(after.Nodes)&&before.Segments.SequenceEqual(after.Segments),"Modular assembly preserves every axis, node and physical segment");
            var fine=SimpleBridgeBuilder.Build(definition.ToInput(mesh with{MaximumLength=mesh.MaximumLength/2}));
            Check(plan.Ranges.SequenceEqual(fine.Segments)&&plan.Ranges.SequenceEqual(after.Segments),"Fabrication plan independent of mesh");
            Check(BridgeSegmentWorkbook.Template(plan).SequenceEqual(BridgeSegmentWorkbook.Template(after)),"Excel before mesh has same identities/ranges");
            var archive=ModelArchive.Clone(definition);
            Check(SimpleBridgeBuilder.Build(archive.ToInput(mesh)).Members.SequenceEqual(after.Members),"Typed configuration archive preserves geometry");
            Reject(()=> (definition with{Fabrication=plan with{Length=plan.Length+1}}).ToInput(mesh));
        }
        var sets=new[]{new BridgeJointSet(new[]{22000d,10000,10000},Array.Empty<int>()),new BridgeJointSet(new[]{5000d},new[]{2})};
        var p=BridgeFabricationPlan.Create(30000,3,BridgeFabricationPlan.Joints(sets,12000));
        Check(p.Ranges.Count(r=>r.Girder==1)==3&&p.Ranges.Count(r=>r.Girder==2)==4&&p.Ranges.Count(r=>r.Girder==3)==3,"Common and per-girder joints, sorted and deduplicated");
        Check(p.Ranges.Where(r=>r.Girder==2).Select(r=>r.Start).SequenceEqual(new[]{0d,5000,10000,22000}),"Explicit stations replace automatic subdivision");
        Reject(()=>BridgeFabricationPlan.Create(30000,3,new BridgeSegments{MaximumLength=double.NaN}));
        Reject(()=>BridgeFabricationPlan.Create(30000,3,BridgeFabricationPlan.Joints(new[]{new BridgeJointSet(new[]{40000d},new[]{1})},12000)));
        Reject(()=>BridgeFabricationPlan.Create(30000,3,BridgeFabricationPlan.Joints(new[]{new BridgeJointSet(new[]{5000d},new[]{4})},12000)));
        string path=Path.GetFullPath("artifacts/modular-bridge/sections-before-mesh.xlsx");Directory.CreateDirectory(Path.GetDirectoryName(path));
        BridgeSegmentWorkbook.Write(path,p,overwrite:true);Check(BridgeSegmentWorkbook.Read(path,p).Table.SequenceEqual(BridgeSegmentWorkbook.Template(p)),"Workbook round trip without FEM mesh");
        Console.WriteLine($"MODULAR BRIDGE PASS: {checks} checks; exact geometry, mesh-independent segments, configuration archives and Excel before mesh.");
    }
}

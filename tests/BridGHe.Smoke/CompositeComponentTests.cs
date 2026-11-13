using System;
using System.Linq;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class CompositeComponentTests
{
    internal static void Run()
    {
        Gpc();
    }

    /// <summary>
    /// R6.6 R2a: Composite Beam on a beam, composite properties and groups given by their GPC data alone (no FEM value; the GPC path of the
    /// component is the one that stays in P2): the beam B1 (10 m along X, group «Original», super element «Spine») on the steel H of the
    /// grillage example, the composite section of the example (<see cref="GpcCompositeDefinitions"/>), the groups «Additional» and «Original».
    /// The same checks as the FEM test on the GPC datum of the composite beam: the omitted end uses the start definition (a constant section),
    /// an explicit tapered end keeps its steel height (a variable section), the BridGHe ID, the groups of the beam plus the new ones (unique),
    /// the attributes of the beam (its super element), the input beam unchanged, the link to the analysed GPC beam, and the definitions equal
    /// bit for bit to the ones built in GPC from the numbers of the composite properties. Then the obsolete Composite Beam Slab Strain.
    /// </summary>
    private static void Gpc()
    {
        var assembly = R2aDesignFixtures.Plugin;
        using var document = new GH_Document();
        var component = (GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.CompositeBeamComponent", true));
        component.CreateAttributes(); document.AddObject(component, false);
        var p = new BridgeGrillageExample.Parameters(); var section = GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(p));
        var h = BridgeGrillageExample.GpcH(p);
        var steel = BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Beam("H steel", 5, BridGHe.Common.Gpc.Builders.GpcSectionBuilders.Generic(h.Area, h.Jxx, h.Jyy, h.Jt, 0, 0, 0, 0, 0, 0, 0),
            GpcCompositeDefinitions.AnalysisSteel(GpcCompositeDefinitions.Grillage(p)), "Steel");
        var draft = BridGHe.Common.Gpc.Builders.GpcBeamBuilders.New(new GPC.Geometry.Point3d(0, 0, 0), new GPC.Geometry.Point3d(10000, 0, 0), steel,
            new[] { BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Group("Original", null) }, 0, 0, 0, R2aDesignFixtures.Id("composite component:B1"), "B1");
        draft = BridGHe.Common.Gpc.Builders.GpcBeamBuilders.WithSuperElement(draft, "Spine", R2aDesignFixtures.Id("composite component:B1 super element"));
        void Set(int port, params IGH_Goo[] values)
        {
            var param = (Param_GenericObject)component.Params.Input[port]; param.PersistentData.Clear();
            foreach (var value in values) param.PersistentData.Append(value); param.ExpireSolution(false);
        }
        var analysed = GpcAnalysedBeam();
        var beamGoo = R2aPropertyFixtures.WithAnalysed(R2aDesignFixtures.Beam(draft), analysed.Beam, analysed.Model);
        Set(0, beamGoo); Set(1, R2aDesignFixtures.Composite(section));
        IGH_Goo Group(string name) => R2aDesignFixtures.Goo("GH_Group", BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Group(name, null));
        Set(3, Group("Additional"), Group("Original"));
        GH_Document.EnableSolutions = true; document.Enabled = true;
        IGH_Goo Solve()
        {
            document.NewSolution(false, GH_SolutionMode.Silent);
            if (component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0) throw new Exception(string.Join("; ", component.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
            var goo = component.Params.Output[0].VolatileData.AllData(true).Single();
            if (R2aDesignFixtures.HasFem(goo) || R2aDesignFixtures.Datum(goo) == null) throw new Exception("The composite beam of GPC-only inputs must be given by its GPC datum alone.");
            return goo;
        }
        double Height(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition d) => ((GPC.Model.Sections.SectionH)d.Section.SteelSections[0].Section.SectionShape).Height;
        GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition Start(object d) => R2aDesignFixtures.Get<GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition>(d, "Start");
        GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition End(object d) => R2aDesignFixtures.Get<GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition>(d, "End");
        var output = Solve(); object datum = R2aDesignFixtures.Datum(output);
        if (!component.Params.Input[2].Optional || !ReferenceEquals(End(datum), Start(datum)) || Height(End(datum)) != p.SteelHeight) throw new Exception("Omitted end section must use the start section.");
        if (R2aDesignFixtures.Get<string>(output, "BeamId") != "B1" || !R2aDesignFixtures.Get<System.Collections.Generic.IReadOnlyList<string>>(datum, "Groups").SequenceEqual(new[] { "Original", "Additional" })
            || !draft.Records.Groups.SequenceEqual(new[] { "Original" }) || !R2aDesignFixtures.Get<BridGHe.Common.Gpc.Legacy.LegacyBeamDraft>(datum, "Beam").Records.SuperElements.SequenceEqual(new[] { "Spine" }))
            throw new Exception("Composite assignment must retain identity/data, add unique groups and leave input unchanged.");
        int checks = Gpc(output, p, null, analysed);
        var end = GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(p with { SteelHeight = 1800 })); Set(2, R2aDesignFixtures.Composite(end)); output = Solve(); datum = R2aDesignFixtures.Datum(output);
        if (Height(Start(datum)) != 1600 || Height(End(datum)) != 1800) throw new Exception("Explicit tapered end section was not retained.");
        checks += Gpc(output, p, 1800, analysed);
        Console.WriteLine($"COMPOSITE BEAM GPC PASS: {checks} checks on the definitions of the composite beam (omitted and tapered end), equal to definitions built in GPC, and on its link to the analysed GPC beam.");
        VerifyLegacyStrainComponent(assembly, draft);
    }

    /// <summary>P2U: the analysed GPC beam of the fixture (B1, 10 m along X), built in GPC, the link that Composite Beam passes from
    /// its beam to the composite beam (Girder Results and Super Element Check read the GPC results through it).</summary>
    private static (GPC.Model.Elements.BeamElement Beam, GPC.Model.Models.Model Model) GpcAnalysedBeam()
    {
        var test=new GpcTestModel("Composite beam component");test.Case("Q");
        var beam=test.Beam("B1",test.Node("1",0,0,0),test.Node("2",10000,0,0));
        foreach(double s in new[]{0d,5000,10000})test.Forces(beam,"Q",s,s);
        return (beam,test.Analyse());
    }

    /// <summary>P2U (F6, chain E (c)): the definitions the composite beam carries (what Super Element Check, Linear Tension and the preview
    /// read): with the end omitted the end definition is the start one (a constant section for the rule of Super Element Check); with an
    /// explicit tapered end the start and end definitions keep their steel heights (a variable section). Both equal bit for bit to the
    /// definitions built in GPC from the numbers of the composite properties (GpcCompositeDefinitions), the steel H named by the property.
    /// The composite beam keeps the link of its beam to the analysed GPC beam and model.</summary>
    private static int Gpc(IGH_Goo goo,BridgeGrillageExample.Parameters p,double? endHeight,(GPC.Model.Elements.BeamElement Beam,GPC.Model.Models.Model Model) analysed)
    {
        void Require(bool ok,string message){if(!ok)throw new Exception("GPC: "+message);}
        T Get<T>(string name)=>(T)goo.GetType().GetProperty(name).GetValue(goo);
        var start=Get<GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition>("StartDefinition");
        var end=Get<GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition>("EndDefinition");
        Require(start!=null && end!=null,"the composite beam must carry its start and end definitions.");
        double Height(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition d)=>((GPC.Model.Sections.SectionH)d.Section.SteelSections[0].Section.SectionShape).Height;
        if(endHeight==null)
        {
            Require(ReferenceEquals(end,start) && Height(end)==p.SteelHeight,"Omitted end section must use the start definition.");
            Require(BridGHe.Common.Gpc.GpcCompositeSections.IsConstant(start,end),"Omitted end section must give a constant section.");
        }
        else
        {
            Require(Height(start)==p.SteelHeight && Height(end)==endHeight.Value,"Explicit tapered end definition was not retained.");
            Require(!BridGHe.Common.Gpc.GpcCompositeSections.IsConstant(start,end),"An explicit tapered end must give a variable section.");
        }
        Require(ReferenceEquals(Get<GPC.Model.Elements.Element>("AnalysedGpc"),analysed.Beam) && ReferenceEquals(Get<GPC.Model.Models.Model>("AnalysedGpcModel"),analysed.Model),
            "Composite assignment must retain the link to the analysed GPC beam.");
        // The definitions built in GPC from the numbers of the composite properties (BridgeGrillageExample.Composite).
        GpcCompositeDefinitions.RequireSame(GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(p)),start,"composite beam start");
        GpcCompositeDefinitions.RequireSame(GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(p with{SteelHeight=endHeight??p.SteelHeight})),end,"composite beam end");
        return 6;
    }

    /// <summary>R6.6 R2a: the obsolete Composite Beam Slab Strain on a beam and a load case given by their GPC data alone: obsolete status,
    /// GUID, the explaining warning, the output by its draft alone with the BridGHe ID of the beam and one more load record, the slab strain in
    /// the case «Legacy shrinkage» (the record of the load and of its case: the reader carries the load, no GPC load), archived inputs, the
    /// input beam unchanged. The strain value itself is not in the GPC datum (only in the identity of the output): the FEM check «unscaled
    /// dimensionless strain» has no GPC counterpart (listed product gap).</summary>
    private static void VerifyLegacyStrainComponent(System.Reflection.Assembly assembly, BridGHe.Common.Gpc.Legacy.LegacyBeamDraft beam)
    {
        var type = assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.CompositeBeamSlabStrainComponent", true);
        var component = (GH_Component)Activator.CreateInstance(type);
        var loadCase = BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case("Legacy shrinkage", false);
        const double strain = -.0002;
        int loads = beam.Records.Loads;
        ((Param_GenericObject)component.Params.Input[0]).PersistentData.Append(R2aDesignFixtures.Beam(beam));
        ((Param_GenericObject)component.Params.Input[1]).PersistentData.Append(R2aDesignFixtures.Case(loadCase));
        ((Param_Number)component.Params.Input[2]).PersistentData.Append(new GH_Number(strain));

        using var document = new GH_Document { Enabled = true };
        component.CreateAttributes();
        document.AddObject(component, false);
        Verify(component, document);

        var archive = new GH_IO.Serialization.GH_Archive();
        var restored = (GH_Component)Activator.CreateInstance(type);
        if (!archive.AppendObject(component, "Legacy strain") || !archive.ExtractObject(restored, "Legacy strain"))
            throw new Exception("The obsolete strain component must retain its archived inputs.");
        using var restoredDocument = new GH_Document { Enabled = true };
        restored.CreateAttributes();
        restoredDocument.AddObject(restored, false);
        Verify(restored, restoredDocument);
        if (beam.Records.Loads != loads) throw new Exception("The obsolete strain component must leave the input beam unchanged.");

        void Verify(GH_Component candidate, GH_Document owner)
        {
            if (!candidate.Obsolete || candidate.Message != "Obsolete"
                || candidate.ComponentGuid != new Guid("adf0a668-9fee-4d5d-8665-57aa769319b0"))
                throw new Exception("The retained strain component must advertise its obsolete status and preserve its GUID.");
            candidate.ExpireSolution(false);
            owner.NewSolution(false, GH_SolutionMode.Silent);
            if (candidate.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count != 0)
                throw new Exception(string.Join("; ", candidate.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
            if (!candidate.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(m => m.Contains("Bridge Shrinkage Action")))
                throw new Exception("The obsolete strain component must explain the replacement workflow.");
            var output = candidate.Params.Output[0].VolatileData.AllData(true).Single();
            var result = R2aDesignFixtures.Datum<BridGHe.Common.Gpc.Legacy.LegacyBeamDraft>(output);
            if (R2aDesignFixtures.HasFem(output) || result == null || result.Beam.Name != beam.Beam.Name || result.Records.Loads != loads + 1
                || result.Records.LoadRecords[^1].Case != loadCase.Name || result.Records.LoadCases[^1] != loadCase)
                throw new Exception("Deprecation must preserve the beam and add the slab strain in its load case.");
        }
    }

}

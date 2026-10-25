using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Rhino.Geometry;
using static BridgeGrillageExample;
using static BridgeSuiteExample;

/// <summary>Three longitudinal girders: a deeper central spine and two identical side girders.</summary>
internal static class BridgeSpineExamples
{
    internal sealed record Settings(Definition Definition,Parameters Central,Parameters Lateral);
    internal static readonly Definition[] Definitions = {
        new("16-spina-1campata","Spina centrale: campata 30 m",new[]{30000.0},0,24),
        new("17-spina-3campate","Spina centrale: 20 + 30 + 20 m",new[]{20000.0,30000,20000},0,48),
        new("18-spina-curva-2campate","Spina centrale curva: 25 + 25 m / R 120 m",new[]{25000.0,25000},120000,96)
    };

    private static Settings Default(Definition d)
    {
        var lateral=new Parameters { Span=d.Length,Width=12000,EffectiveSlabWidth=4000,ShrinkageLong=-200e-6,ShrinkageInstant=0 };
        return new(d,lateral with { SteelHeight=2000,FlangeWidth=800,FlangeThickness=50,WebThickness=20 },lateral);
    }

    private static void Validate(Settings setup)
    {
        setup.Definition.Validate(refined:true);setup.Central.Validate();setup.Lateral.Validate();
        // The generation/closed-form checks below assume a symmetric deck with equal tributary strips.
        if(setup.Central.Width!=setup.Lateral.Width || setup.Central.SlabThickness!=setup.Lateral.SlabThickness
            ||setup.Central.GirderSlabWidth!=setup.Central.Width/3 || setup.Lateral.GirderSlabWidth!=setup.Lateral.Width/3)
            throw new ArgumentException("Use the same deck/slab and effective width equal to one third of the deck for each girder.");
    }

    internal static ModelModel Create(Settings setup,int phase)
    {
        Validate(setup);if(phase<0||phase>3)throw new ArgumentOutOfRangeException(nameof(phase));
        var d=setup.Definition;var stations=Stations(d);int n=d.Panels;
        var model=new ModelModel(ModelModel.ModelUnits.Nmm) { Name=d.Title+" / "+Cases[phase] };
        var lc=Case(Cases[phase],phase==3?1:phase);model.LoadCases.Add(lc);
        double ad=new GPC.Model.Sections.SectionH(800,12,300,20,300,20,"Diaframma").Area;
        for(int j=0;j<3;j++)
        {
            var p=j==1?setup.Central:setup.Lateral;
            double y=(j-1)*p.Width/3;
            double q=phase==0?GpcH(p).Area*78.5e-6+p.GirderSlabWidth*p.SlabThickness*25e-6
                +ad*(2*p.Width/3)*(6*d.Spans.Length+1)*78.5e-6/(3*d.Length)
                :phase==1?p.PermanentPressure*p.GirderSlabWidth:phase==2?p.TrafficPressure*p.GirderSlabWidth:0;
            for(int i=0;i<=n;i++)
            {
                var node=new NodeModel((j*(n+1)+i+1).ToString(),Position(d,stations[i],y));node.Groups.Add(Main);
                node.Attributes.Add(new NodeRestraintAttributeModel(j==0&&i==0,j==0&&(i==0||i==n),i%d.PanelsPerSpan==0,false,false,false));
                double tributary=0;
                if(i>0)tributary+=node.Position.DistanceTo(Position(d,stations[i-1],y))/2;
                if(i<n)tributary+=node.Position.DistanceTo(Position(d,stations[i+1],y))/2;
                node.Loads.Add(new NodeForceLoadModel(lc,new(0,0,-q*tributary)));model.AddElements(new[]{node});
            }
        }
        for(int j=0;j<3;j++)
        {
            var p=j==1?setup.Central:setup.Lateral;var property=SuiteProperty(p,phase==3?1:phase);
            property.Name=(j==1?"Spina centrale":"Travi laterali")+" / "+Cases[phase];
            var shrinkage=Shrinkage(p);if(phase==3)property.Material.ThermalExpansion=1e-5;
            for(int i=0;i<n;i++)
            {
                var beam=new BeamModel(model.Nodes[j*(n+1)+i].Position,model.Nodes[j*(n+1)+i+1].Position,property,Main){BeamId=(j*n+i+1).ToString()};
                beam.Attributes.Add(new SuperElementAttributeModel("G"+(j+1)));
                if(phase==3)
                {
                    beam.Loads.Add(new BeamPreLoadModel(lc,-shrinkage.Epsilon,BeamPreLoadModel.LoadType.Strain));
                    beam.Loads.Add(new BeamGradientLoadModel(lc,new(){Axis2=shrinkage.Gradient/property.Material.ThermalExpansion}));
                }
                model.AddElements(new[]{beam});
            }
        }
        for(int i=0;i<=n;i++)
        {
            double strip=((i>0?stations[i]-stations[i-1]:0)+(i<n?stations[i+1]-stations[i]:0))/2;
            var property=BridgeSuiteExample.CrossProperty(setup.Central,strip,i%(d.PanelsPerSpan/6)==0,phase);
            if(property==null)continue;
            for(int j=0;j<2;j++)model.AddElements(new[]{new BeamModel(model.Nodes[j*(n+1)+i].Position,model.Nodes[(j+1)*(n+1)+i].Position,property,Cross){BeamId=(3*n+2*i+j+1).ToString()}});
        }
        return model;
    }

    private static List<SectionSample> CheckSections(Settings setup,ModelModel[] results,NativeExamples.Recorder checks)
    {
        var d=setup.Definition;var p=setup.Central;var checker=Checker(p);var samples=new List<SectionSample>();
        foreach(int index in new[]{d.PanelsPerSpan/2,Math.Min(d.PanelsPerSpan,d.Panels-1)})
        {
            int beam=d.Panels+index;
            var forces=Enumerable.Range(0,4).Select(phase=>results[phase].Beams[beam].Results.OfType<BeamForceResultModel>().OrderBy(f=>f.Station).First()).ToArray();
            var combined=ModelArchive.Clone(forces[1]);var shrinkage=forces[3];
            combined.AxialForce+=shrinkage.AxialForce;combined.Vx+=shrinkage.Vx;combined.Vy+=shrinkage.Vy;
            combined.Mx+=shrinkage.Mx;combined.My+=shrinkage.My;combined.Torque+=shrinkage.Torque;
            try
            {
                checker.GetLinearAnalysisResultByStages(out var stress,out _,p.PhiLong,0,new[]{BridgeForceConvention.FromStraus(forces[0],Reference)},
                    new[]{BridgeForceConvention.FromStraus(combined,Reference)},new[]{BridgeForceConvention.FromStraus(forces[2],Reference)},true,new[]{p.ShrinkageLong},new[]{0.0},false);
                var actual=StressValues(stress[0]);var expected=ExpectedStress(p,forces);
                checks.Near("Spina: quattro contributi",stress[0].Analysis.Stages.Count,4,0,0);
                for(int i=0;i<4;i++)checks.Near($"Spina beam {beam+1}: sigma {i} [MPa]",actual[i],expected[i],1e-6,1e-6);
                samples.Add(new(beam+1,0,actual,true,"Sezione H centrale: tensioni elastiche lorde; interazione completa."));
            }
            catch(NotSupportedException ex)
            {
                checks.Near("Spina curva: azioni fuori campo segnalate",d.Radius>0?1:0,1,0,0);
                samples.Add(new(beam+1,0,Array.Empty<double>(),false,ex.Message));
            }
        }
        return samples;
    }

    internal static int Run(bool grasshopper,string selected)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        var definitions=Definitions.Where(d=>selected=="all"||d.Id.StartsWith(selected,StringComparison.Ordinal)).ToArray();
        if(definitions.Length==0)throw new ArgumentException("Unknown spine example: "+selected);
        int total=0,failures=0;
        foreach(var definition in definitions)
        {
            string folder=Path.Combine(Root,definition.Id);Directory.CreateDirectory(folder);
            string parameters=Path.Combine(folder,"parameters.json");
            var setup=File.Exists(parameters)?JsonSerializer.Deserialize<Settings>(File.ReadAllText(parameters)):Default(definition);
            Validate(setup);if(setup.Definition.Id!=definition.Id)throw new ArgumentException("Keep example Id unchanged.");
            File.WriteAllText(parameters,JsonSerializer.Serialize(setup,Json));
            var d=setup.Definition;var checks=new NativeExamples.Recorder();
            var inputs=Enumerable.Range(0,4).Select(phase=>Create(setup,phase)).ToArray();var results=new ModelModel[4];var mesh=new List<object>();
            checks.Near("Tre gruppi longitudinali",inputs[0].Beams.Where(b=>b.HasAttribute(typeof(SuperElementAttributeModel))).Select(SuperElementAttributeModel.GetSuperElement).Distinct().Count(),3,0,0);
            checks.Near("Spina con inerzia maggiore delle laterali",GpcH(setup.Central).Jxx>GpcH(setup.Lateral).Jxx?1:0,1,0,0);
            for(int phase=0;phase<4;phase++)
            {
                Console.WriteLine("SOLVE "+d.Id+" "+Cases[phase]);results[phase]=Solve(inputs[phase],d.Id+"/"+Cases[phase]);
                Equilibrium(inputs[phase],results[phase],Cases[phase],checks);
                checks.Near(Cases[phase]+" risultati per ogni beam",results[phase].Beams.Count(b=>b.Results.OfType<BeamForceResultModel>().Count()==5),inputs[phase].Beams.Count,0,0);
                if(d.Radius==0)
                {
                    double error=Enumerable.Range(0,d.Panels+1).Max(i=>Math.Abs(Displ(results[phase].Nodes[i],Cases[phase]).Displacement.Z-Displ(results[phase].Nodes[2*(d.Panels+1)+i],Cases[phase]).Displacement.Z));
                    checks.Near(Cases[phase]+" simmetria Uz laterali [mm]",error,0,0,1e-6);
                }
                var refined=d with { PanelsPerSpan=2*d.PanelsPerSpan };
                var refinedResult=Solve(Create(setup with { Definition=refined },phase),d.Id+"/mesh-"+refined.PanelsPerSpan+"-"+Cases[phase]);
                double difference=0,scale=0;
                for(int j=0;j<3;j++)for(int i=0;i<=d.Panels;i++)
                {
                    double u=Displ(results[phase].Nodes[j*(d.Panels+1)+i],Cases[phase]).Displacement.Z;
                    double v=Displ(refinedResult.Nodes[j*(refined.Panels+1)+2*i],Cases[phase]).Displacement.Z;
                    difference=Math.Max(difference,Math.Abs(u-v));scale=Math.Max(scale,Math.Abs(v));
                }
                checks.Near(Cases[phase]+" convergenza Uz [mm]",difference,0,0,Math.Max(1e-6,.02*scale));
                mesh.Add(new { Phase=Cases[phase],Base=d.PanelsPerSpan,Refined=refined.PanelsPerSpan,MaxUzDifference=difference,ReferencePeakUz=scale,RelativeError=difference/Math.Max(scale,1e-12) });
            }
            var sections=CheckSections(setup,results,checks);WriteResults(folder,d,results);
            if(grasshopper)GrasshopperBridgeSuite.Run(d,inputs,results,sections,checks,setup.Central,new[]{"G2"});
            var report=new { Date=DateTimeOffset.Now,Definition=d,Section=setup.Central,LateralSection=setup.Lateral,Girders=3,Ec,
                Shrinkage=Shrinkage(setup.Central),LateralShrinkage=Shrinkage(setup.Lateral),
                Method="Three incremental linear stiffness states plus differential shrinkage. Primary section stress plus native secondary resultants, once each.",
                Scope="Equivalent planar grillage, gross uncracked sections, full interaction; explicit tributary effective width. Central H section only. N-Mx-Vy checks exclude T, M2 and V1 with an explicit assumption; all FEM resultants retained. No complete bridge design verification.",
                Results=results.Select((m,phase)=>new { Case=Cases[phase],MinUz=m.Nodes.Min(n=>Displ(n,Cases[phase]).Displacement.Z),MaxUz=m.Nodes.Max(n=>Displ(n,Cases[phase]).Displacement.Z),
                    MinMy=m.Beams.Take(3*d.Panels).SelectMany(b=>b.Results.OfType<BeamForceResultModel>()).Min(f=>f.My),MaxMy=m.Beams.Take(3*d.Panels).SelectMany(b=>b.Results.OfType<BeamForceResultModel>()).Max(f=>f.My),
                    MaxAbsT=m.Beams.Take(3*d.Panels).SelectMany(b=>b.Results.OfType<BeamForceResultModel>()).Max(f=>Math.Abs(f.Torque)) }),Mesh=mesh,Sections=sections,Checks=checks.Measurements };
            File.WriteAllText(Path.Combine(folder,"validation.json"),JsonSerializer.Serialize(report,Json));
            int failed=checks.Measurements.Count(c=>!c.Passed);total+=checks.Measurements.Count;failures+=failed;
            Console.WriteLine($"{d.Id}: {checks.Measurements.Count-failed} passed, {failed} failed");
        }
        Console.WriteLine($"BRIDGE SPINE: {total-failures} passed, {failures} failed");return failures==0?0:1;
    }
}

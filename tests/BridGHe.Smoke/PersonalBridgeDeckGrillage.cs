using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;

internal sealed partial class PersonalBridgeRevision
{
    private void ValidateDeckPhaseModels()
    {
        var rows = analyses.Select(a =>
        {
            var m=(ModelModel)Value(a);
            string log=Path.ChangeExtension((string)a.Params.Input[3].VolatileData.AllData(true).Single().ScriptVariable(),".LSL");
            if(!File.Exists(log)) throw new Exception("Missing solver log for "+m.Name);
            string logText=File.ReadAllText(log);
            if(logText.Contains("*WARNING[") || logText.Contains("*ERROR[")) throw new Exception("Unresolved solver warning in "+m.Name);
            File.WriteAllBytes(Path.Combine(folder,"calcoli",m.Name+"-results.bgmodel"),ModelArchive.Write(m));
            var deck=m.Beams.Where(b=>b.BeamProperty.Name.StartsWith("Soletta ")).ToArray();
            var main=m.Beams.Where(b=>b.Attributes.OfType<SuperElementAttributeModel>().Any()).ToArray();
            var forces=main.SelectMany(b=>b.Results.OfType<BeamForceResultModel>()).Where(r=>r.LoadCase.Name==m.Name).ToArray();
            int supports=m.Nodes.Count(n=>n.Attributes.OfType<NodeRestraintAttributeModel>().Any());
            double maxDisplacement=m.Nodes.SelectMany(n=>n.Results.OfType<NodeDisplResultModel>()).Max(r=>r.Displacement.Length);
            if(supports!=6 || !double.IsFinite(maxDisplacement) || maxDisplacement>100) throw new Exception("Missing supports or unstable grillage in "+m.Name);
            if(m.Beams.Count != (m.Name=="G1_GETTO"?37:76) || deck.Length!=(m.Name=="G1_GETTO"?0:39)) throw new Exception("Incorrect phase activation of transverse slab.");
            if(deck.Any(b=>Math.Abs(b.PointFrom.Z+150)>1e-6 || Math.Abs(b.PointTo.Z+150)>1e-6)) throw new Exception("Deck elevation mismatch.");
            var row=new { m.Name, beams=m.Beams.Count, nodes=m.Nodes.Count, deckBeams=deck.Length, supports, maxDisplacementMm=maxDisplacement, solverWarnings=0,
                maxVxN=forces.Max(r=>Math.Abs(r.Vx)),maxMyNmm=forces.Max(r=>Math.Abs(r.My)),maxTorqueNmm=forces.Max(r=>Math.Abs(r.Torque)) };
            Console.WriteLine("GRILLAGE PHASE: "+JsonSerializer.Serialize(row)); return row;
        }).ToArray();
        File.WriteAllText(Path.Combine(folder,"grillage-validation.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
    }
    private GH_Component Standard(string name, float x, float y)
    {
        var server = Grasshopper.Instances.ComponentServer;
        var c = server.ObjectProxies.Where(p => p.Desc.Name == name).Select(p => server.EmitObject(p.Guid))
            .OfType<GH_Component>().First(c => !c.GetType().Name.Contains("OBSOLETE"));
        c.CreateAttributes(); c.Attributes.Pivot = new PointF(x,y); doc.AddObject(c,false); return c;
    }

    private void ConfigureDeckGrillage()
    {
        bridgeGeometry.NickName = "Graticcio acciaio + soletta 12+12 m";
        Number(bridgeGeometry,13,2000);
        var rebar = Add("MaterialRebarComponent",1900,1300); Wire(C(PropertyId),5,rebar.Params.Output[0]);
        var links = Add("BridgeConnectionsComponent",2300,-2500,"Eccentricità rigide trave-soletta");
        Wire(links,1,bridgeGeometry.Params.Output[16]); links.Params.Input[1].DataMapping=GH_DataMapping.Flatten;
        // Equal station spacing in this example. Derive half/full strip widths from the
        // actual geometric output, including the reduced end tributary lengths.
        var firstWidth=Standard("List Item",2100,-2900); Wire(firstWidth,0,bridgeGeometry.Params.Output[18]);
        firstWidth.Params.Input[0].DataMapping=GH_DataMapping.Flatten;
        var fullWidth=Multiply(firstWidth.Params.Output[0],Note("Due mezze fasce","2",1800,-3100,200,60),2500,-2900);
        var stationCount=Standard("List Length",2100,-3200); Wire(stationCount,0,bridgeGeometry.Params.Output[17]);
        stationCount.Params.Input[0].DataMapping=GH_DataMapping.Flatten;
        var halfSection=Add("BeamSectionSolidRectComponent",2900,-3200,"Sezione fascia estremità");
        var fullSection=Add("BeamSectionSolidRectComponent",2900,-2900,"Sezione fascia interna");
        foreach(var section in new[]{halfSection,fullSection}) Wire(section,0,P("3d534111-49fb-4797-8f1a-b033d8cba0eb"));
        Wire(halfSection,1,firstWidth.Params.Output[0]); Wire(fullSection,1,fullWidth.Params.Output[0]);
        var steelE=Note("Es [MPa]","210000",2600,-2500,200,60);
        foreach(var (tag,nPort,modelId,y) in new[]{("G2",7,"a2b57f7a-764c-44e1-b68e-6b71ee7b8b84",-4200f),
            ("Q",6,"f57d490b-f486-4a2f-8764-2b5d9f3c9c42",-5300f),
            ("RITIRO",8,"d9961a47-9644-4892-88f8-227d97e77948",-6400f)})
        {
            var modulus=Standard("Division",3100,y); Wire(modulus,0,steelE); Wire(modulus,1,C(PropertyId).Params.Input[nPort].Sources.ToArray());
            var shear=Standard("Division",3400,y-300); Wire(shear,0,modulus.Params.Output[0]);
            Wire(shear,1,Note("2(1+nu)","2.4",3100,y-300,180,60));
            var material=Add("MaterialCustomComponent",3700,y,"Cls equivalente "+tag);
            Text(material,0,"C32/40 Eeff "+tag); Integer(material,1,(int)MaterialModel.MaterialKind.Concrete); Integer(material,2,(int)ModelModel.ModelUnits.Nmm);
            Wire(material,3,modulus.Params.Output[0]); Number(material,4,.2); Number(material,5,1e-5); Wire(material,6,shear.Params.Output[0]);
            Boolean(material,7,true); Number(material,8,2.5e-9); Number(material,9,32); Number(material,10,0);
            GH_Component Property(GH_Component section, string suffix, float dy)
            {
                var p=Add("BeamPropertyComponent",4100,y+dy,"Soletta "+tag+suffix); Text(p,0,"Soletta "+tag+suffix);
                Integer(p,1,5); Wire(p,2,section.Params.Output[0]); Wire(p,3,material.Params.Output[0]); return p;
            }
            var full=Property(fullSection," interna",0); var half=Property(halfSection," estremità",300);
            var assign=Add("BridgeMemberAssignmentComponent",5000,y,"Assegna soletta "+tag);
            Wire(assign,0,bridgeGeometry.Params.Output[19]); Wire(assign,1,C(PropertyId).Params.Output[1],full.Params.Output[0]);
            var families=(Param_Integer)assign.Params.Input[2]; families.PersistentData.Clear(); families.SetPersistentData(new[]{-1,8});
            var start=Add("BridgeSectionScheduleComponent",4500,y+300); var end=Add("BridgeSectionScheduleComponent",4500,y+600);
            foreach(var rule in new[]{start,end}) { Wire(rule,0,half.Params.Output[0]); Number(rule,1,0); Number(rule,2,24000); Integer(rule,3,8); }
            Integer(start,4,1); Wire(end,4,stationCount.Params.Output[0]); Wire(assign,3,start.Params.Output[0],end.Params.Output[0]);
            var select=Add("BeamFilterComponent",5400,y,"Solo fasce cls "+tag); Wire(select,0,assign.Params.Output[0]);
            Text(select,5,"Soletta "+tag+" interna","Soletta "+tag+" estremità");
            var model=C(modelId); Wire(model,0,model.Params.Input[0].Sources.Concat(new[]{select.Params.Output[0],links.Params.Output[0]}).ToArray());
            model.Params.Input[0].DataMapping=GH_DataMapping.Flatten;
        }
        Boolean(C(BuildId),4,true);
        // The steel reference must be the first model; phase identity still comes from the action objects.
        Note("Soletta del graticcio — REV004", "G1: carpenteria e getto fresco, senza rigidezza delle fasce cls.\nG2/Q/RITIRO: fasce trasversali a quota -h/2 e link rigidi agli assi delle travi.\nEc,eff = Es/n della stessa fase. Larghezza fascia da passo uniforme, dimezzata alle estremità.\nIl ritiro qui è longitudinale (sezioni miste + autotensioni); il ritiro trasversale della soletta non è applicato.\nNessuna shell longitudinale sovrapposta alle sezioni miste. Fasce cls non verificate dal checker delle travi.\nPer progressive soletta non uniformi occorre assegnare ogni larghezza tributaria, non la regola uniforme di questo esempio.",3300,-1900,1700,290);
        foreach(var note in doc.Objects.OfType<Grasshopper.Kernel.Special.GH_Panel>().Where(p=>p.NickName=="Schema tridimensionale"))
            note.UserText="REV004: due travi, traversi in acciaio e soletta a graticcio. Vedi il gruppo sopra per quote, fasce, link e proprietà di fase.\nL'uscita Bridge Geometry alimenta G1; Grillage Geometry aggiunge il cls solo dopo l'attivazione del misto.";
    }
}

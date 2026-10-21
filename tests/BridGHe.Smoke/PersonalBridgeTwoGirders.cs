using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using GH_IO.Serialization;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal sealed partial class PersonalBridgeRevision
{
    private GH_Component bridgeGeometry, secondComposite, secondCheck;
    private GH_Component Copy(GH_Component source, float x, float y, string nickname)
    {
        var copy=Add(source.GetType().Name,x,y,nickname);
        for(int i=0;i<source.Params.Input.Count;i++)
        {
            var param=copy.Params.Input[i];var chunk=new GH_LooseChunk("Input");source.Params.Input[i].Write(chunk);
            chunk.RemoveItem("InstanceGuid");chunk.SetGuid("InstanceGuid",param.InstanceGuid);param.Read(chunk);
            Wire(copy,i,source.Params.Input[i].Sources.ToArray());
        }
        return copy;
    }

    private void AddSecondGirderDetails()
    {
        var detail=Copy(C(DetailsId),13200,4400,"Pioli G2"); Wire(detail,0,secondCheck.Params.Output[0]);
        var model=(ModelModel)Value(C(BuildId));
        var ids=secondCheck.Params.Output[0].VolatileData.AllData(true).Select(g=>((BridgeStressResult)g.ScriptVariable()).Source.BeamId).ToHashSet();
        var ends=model.Beams.Where(b=>ids.Contains(b.BeamId)).SelectMany(b=>new[]{b.PointFrom,b.PointTo}).Distinct().ToArray();
        var supports=ends.Where(p=>Math.Abs(p.X)<1e-6||Math.Abs(p.X-12000)<1e-6||Math.Abs(p.X-24000)<1e-6).ToArray();
        var intermediates=ends.Except(supports).ToArray();
        var supportSelection=Add("BridgeDetailStationsComponent",14300,4900,"Appoggi G2");
        Wire(supportSelection,0,C(BuildId).Params.Output[0]);Wire(supportSelection,1,detail.Params.Output[0]);Wire(supportSelection,2,Points("Appoggi G2",supports,13800,5000));Wire(supportSelection,3,phases.Select(p=>p.Params.Output[0]).ToArray());
        var support=Copy(C("bc9ea8ec-36bf-4344-acd9-f50456e1b9cd"),14800,4900,"Irrigiditori appoggi G2");Wire(support,6,supportSelection.Params.Output[3]);Wire(support,9,supportSelection.Params.Output[1]);
        var supportResult=Add("BridgeDetailCheckComponent",15400,4900,"Verifiche appoggi G2");Wire(supportResult,0,supportSelection.Params.Output[0]);Wire(supportResult,3,support.Params.Output[0]);
        var selection=Add("BridgeDetailStationsComponent",14300,4400,"Intermedi G2");Wire(selection,0,C(BuildId).Params.Output[0]);Wire(selection,1,detail.Params.Output[0]);Wire(selection,2,Points("Intermedi G2",intermediates,13800,4500));
        var intermediate=Add("BridgeDetailCheckComponent",15400,4400,"Verifiche intermedi G2");Wire(intermediate,0,selection.Params.Output[0]);Wire(intermediate,2,C("17d36e61-ca92-4706-b6aa-12efb6b88ded").Params.Output[0]);
        var summary=C("d3cfa47d-ae7a-4248-907c-ce45a479572b");
        foreach(var target in new[]{report,summary}) Wire(target,0,target.Params.Input[0].Sources.Concat(new[]{detail.Params.Output[0],intermediate.Params.Output[0],supportResult.Params.Output[0]}).ToArray());
        foreach(var source in new[]{C("58c312f7-3e3d-4a53-b764-56245251f7d7"),C("f7160f75-761f-45f8-87bf-e20a1621711b")}.Concat(addedPlots.ToArray()))
        {
            float y=5400+addedPlots.Count*450;var plot=Copy(source,16000,y,source.Name+" G2");Wire(plot,1,secondComposite.Params.Output[0]);
            var result=source.GetType().Name switch { "BridgeForcePlotComponent" => secondCheck.Params.Output[2], "BridgeResultPlotComponent" => secondCheck.Params.Output[0], "BridgeStudPlotComponent" => detail.Params.Output[0], "BridgeIntermediateStiffenerPlotComponent" => intermediate.Params.Output[0], "BridgeSupportStiffenerPlotComponent" => supportResult.Params.Output[0], _ => throw new InvalidOperationException("Unexpected bridge plot: " + source.GetType().Name) };
            Wire(plot,2,result);Text(plot,plot.Params.Input.FindIndex(p=>p.Name=="Graphic Title"),source.Name+" G2");
            var preview=Add("BridgePlotPreviewComponent",16700,y);Wire(preview,0,plot.Params.Output[0]);addedPlots.Add(plot);
            Wire(report,9,report.Params.Input[9].Sources.Concat(new[]{plot.Params.Output[0]}).ToArray());
        }
    }

    private void ConfigureTwoGirders()
    {
        // The construction analysis remains a steel grillage with longitudinal property overrides.
        // The slab mesh is geometric: adding longitudinal shell stiffness would count concrete twice.
        bridgeGeometry = Add("SimpleBridgeSolidComponent", 1600, -1700, "Ponte 12+12 m due travi");
        var axis = new Param_Curve { NickName = "Asse impalcato 24 m", Hidden = true };
        axis.CreateAttributes(); axis.Attributes.Pivot = new PointF(1100, -1800);
        axis.PersistentData.Append(new GH_Curve(new LineCurve(new Point3d(0, 0, 0), new Point3d(24000, 0, 0))));
        doc.AddObject(axis, false); Wire(bridgeGeometry, 0, axis);
        Number(bridgeGeometry, 1, 0, 12000, 24000); Number(bridgeGeometry, 2, -1500, 1500);
        Wire(bridgeGeometry, 4, P("5784144e-44fd-4116-ba2b-4d7300ebdce9"));
        var geometryProperty=Add("SectionGeometricPropertyComponent",500,-2200);Wire(geometryProperty,0,C("1777690f-b3ea-4641-83df-186914c57f9f").Params.Output[0]);
        GH_Component Subtract(IGH_Param a,IGH_Param b,float x)
        {
            var server=Grasshopper.Instances.ComponentServer;
            var c=server.ObjectProxies.Where(p=>p.Desc.Name=="Subtraction").Select(p=>server.EmitObject(p.Guid)).OfType<GH_Component>().First(c=>c.Category=="Maths"&&!c.GetType().Name.Contains("OBSOLETE"));
            c.CreateAttributes();c.Attributes.Pivot=new PointF(x,-2100);doc.AddObject(c,false);Wire(c,0,a);Wire(c,1,b);return c;
        }
        var top=Subtract(geometryProperty.Params.Output[3],P("9c8132a5-d379-4927-8e78-9208080e68ae"),800);
        var level=Subtract(top.Params.Output[0],P("3d534111-49fb-4797-8f1a-b033d8cba0eb"),1150);Wire(bridgeGeometry,3,level.Params.Output[0]);
        Wire(bridgeGeometry, 5, P("3d534111-49fb-4797-8f1a-b033d8cba0eb")); Number(bridgeGeometry, 9, 2000);
        var segments = Add("BridgeSegmentsComponent", 1100, -1300); Number(segments, 1, 8000); Wire(bridgeGeometry, 7, segments.Params.Output[0]);
        var diaphragms = Add("BridgeDiaphragmsComponent", 1100, -1000); Number(diaphragms, 1, 2000); Wire(bridgeGeometry, 8, diaphragms.Params.Output[0]);
        var assign = Add("BridgeMemberAssignmentComponent", 2200, -1650); Wire(assign, 0, bridgeGeometry.Params.Output[14]);
        Wire(assign, 1, C(PropertyId).Params.Output[1]);
        var crossSection = Add("BeamSectionIComponent", 1600, -600, "Traversi H800");
        foreach (var v in new[] { (0,300d),(1,300d),(2,800d),(3,20d),(4,20d),(5,12d),(6,0d) }) Number(crossSection,v.Item1,v.Item2);
        var crossProperty = Add("BeamPropertyComponent", 2100, -650, "S355 traversi");
        Text(crossProperty, 0, "Traversi H800"); Integer(crossProperty,1,5); Wire(crossProperty, 2, crossSection.Params.Output[0]); Wire(crossProperty, 3, C("df362034-315c-4fcb-8509-f758102c83dd").Params.Output[0]);
        var rule = Add("BridgeSectionScheduleComponent", 2600, -700); Wire(rule, 0, crossProperty.Params.Output[0]);
        Number(rule, 1, 0); Number(rule, 2, 24000); Integer(rule, 3, 1); Wire(assign, 3, rule.Params.Output[0]);
        var stations = C("75b2a793-ce0b-4ce5-a410-e27a875e7b48"); Wire(stations,0,assign.Params.Output[0]);
        var bearings = Add("BridgeBearingsComponent", 2200, -1100); Wire(bearings, 0, bridgeGeometry.Params.Output[14]);
        var types = (Param_Integer)bearings.Params.Input[3]; types.PersistentData.Clear();
        // At S1: one fixed and one transverse sliding bearing. S2/S3: longitudinal guide + multidirectional.
        foreach (int type in new[] { 0, 2, 1, 2, 1, 2 }) types.PersistentData.Append(new GH_Integer(type));
        Wire(C("d082c27d-61ca-4cef-be8e-f7bbec0b4342"), 0, stations.Params.Output[0], bearings.Params.Output[0]);
        var filter = C("e3638efa-0be6-4fd7-b5d8-3cb34a3ba283"); Wire(filter, 6); Text(filter,5, "H1800 600x30 800x40 anima15 + soletta 2500x300OnlySteel");
        foreach (string id in new[] { "b4db0475-9d8e-4e1d-8d9f-3db4e4d918df", "a2b57f7a-764c-44e1-b68e-6b71ee7b8b84", "f57d490b-f486-4a2f-8764-2b5d9f3c9c42", "d9961a47-9644-4892-88f8-227d97e77948" })
        { var model = C(id); Wire(model,0,model.Params.Input[0].Sources.Concat(new[]{filter.Params.Output[1]}).ToArray()); }
        Text(C("dd29b475-2431-4a9f-9066-d968f12d3ea5"),1,"G1");
        foreach(var oldFilter in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="SuperElementFilterComponent").ToArray()) Text(oldFilter,1,"G1");
        foreach(var oldId in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="ElementIdComponent").ToArray()) doc.RemoveObject(oldId,false);
        var filter2=Add("SuperElementFilterComponent",11000,4400,"Trave G2"); Wire(filter2,0,C(BuildId).Params.Output[0]); Text(filter2,1,"G2");
        secondComposite=Add("CompositeBeamComponent",11600,4400,"Misto G2"); Wire(secondComposite,0,filter2.Params.Output[0]); Wire(secondComposite,1,C(PropertyId).Params.Output[0]);
        secondCheck=Add("SuperElementCheckComponent",12400,4400,"Verifiche G2"); Wire(secondCheck,0,C(BuildId).Params.Output[0]); Wire(secondCheck,1,secondComposite.Params.Output[0]);
        Note("Schema tridimensionale", "Due travi G1/G2, interasse 3 m, campate 12+12 m, impalcato 5 m, soletta 300 mm.\nConci 8 m e traversi pieni ogni 2 m; vincoli spaziali ai sei appoggi.\nLa soletta è rappresentata dalle proprietà omogeneizzate delle travi e dalla preview geometrica.\nNessuna shell aggiunta: rigidezza trasversale del graticcio affidata ai traversi in acciaio.", 2800,-1750,1500,230);
        // The preceding original single-girder construction is retained as a reference but disconnected.
        foreach(var c in doc.Objects.OfType<GH_Component>().Where(c=>c.Attributes.Pivot.X<3500 && c!=bridgeGeometry)) c.Hidden=true;
    }
}

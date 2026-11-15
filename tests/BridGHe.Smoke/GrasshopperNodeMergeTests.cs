using System;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class GrasshopperNodeMergeTests
{
    internal static void Run()
    {
        var plugin=BridgeMergeTests.Initialize();
        foreach(bool reverse in new[]{false,true})
        {
            // R6.6 lane R4a: the nodes are given by their GPC drafts alone (product builders, GH_Node.FromGpc, no FEM value) and Build Model takes
            // its GPC-only path. The same nodes as the FEM path: a support restrained in Tx, Ty, Tz with a force of -100 along Z in Q; a node at the
            // same position with a rigid connection to (0, 0, 1000) (the record of Bridge Rigid Links, GpcBridgeGeometryBuilders.RigidNode), the
            // same force and the group "Link"; the target node "top".
            Guid Id(string what)=>GpcIdentity.Derive(GpcIdentity.NameSpace,"r66-r4a-node-merge:"+what);
            var loadCase=GpcCaseBuilders.Case("Q",false);
            var support=GpcNodeBuilders.WithForce(GpcNodeBuilders.WithRestraint(GpcNodeBuilders.Create(0,0,0,"support",null,Id("support")),true,true,true,false,false,false,Id("support restraint")),
                loadCase,0,0,-100,Id("support force"));
            var linked=GpcNodeBuilders.Create(0,0,0,"link",new[]{new[]{"Link"}},Id("link"));
            linked.Node.Attributes.Add(new GPC.Model.Attributes.RigidConnectionAttribute(new GPC.Geometry.Point3d(0,0,1000)));
            var connection=GpcNodeBuilders.WithForce(linked,loadCase,0,0,-100,Id("link force"));
            var top=GpcNodeBuilders.Create(0,0,1000,"top",null,Id("top"));
            string Snapshot(LegacyNodeDraft d)=>$"{d.Node.Attributes.Count}/{d.Node.Assignments.Restrains.Count}/{d.Node.Loads.Count}/{d.Node.Groups.Count}/{d.Node.Position.X},{d.Node.Position.Y},{d.Node.Position.Z}";
            string supportBefore=Snapshot(support),connectionBefore=Snapshot(connection);
            using var doc=new GH_Document();
            var component=(GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t=>t.Name=="BuildModelComponent"));
            component.CreateAttributes();doc.AddObject(component,false);
            var input=(Param_GenericObject)component.Params.Input[0];
            var ghNode=plugin.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Node",true);
            int index=0;
            foreach(var node in (reverse?new[]{connection,support}:new[]{support,connection}).Concat(new[]{top}))
            {
                var goo=(IGH_Goo)ghNode.GetMethod("FromGpc").Invoke(null,new object[]{node,Id("gh "+reverse+" "+index++)});
                if(ghNode.GetProperty("Value").GetValue(goo)!=null)throw new Exception("GPC: the GH node has a FEM value");
                input.PersistentData.Append(goo);
            }
            var name=(Param_String)component.Params.Input[1];name.PersistentData.Clear();name.SetPersistentData("Coincident support and link");
            var units=(Param_Integer)component.Params.Input[2];units.PersistentData.Clear();units.SetPersistentData(2);
            GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
            if(component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0)throw new Exception(string.Join(";",component.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
            // The output: the GH model given by its GPC input alone; the merged node read on the GPC model of Build Model.
            var output=component.Params.Output[0].VolatileData.AllData(true).Single();
            if(output.GetType().GetProperty("Value").GetValue(output)!=null)throw new Exception("GPC: the Build Model output has a FEM value");
            var model=((GpcModels.Conversion)output.GetType().GetProperty("GpcInput").GetValue(output)).Model;
            bool At(GPC.Geometry.Point3d p,double z)=>p.X==0&&p.Y==0&&p.Z==z;
            var merged=model.NodesElements.Values.Single(n=>At(n.Position,0));
            var target=model.NodesElements.Values.Single(n=>At(n.Position,1000));
            bool[] Fixed(GPC.Model.Structure.Assignments.RestrainAssignment r)=>r.Restrain.Restrains.OrderBy(d=>(int)d.Dof).Select(d=>d.IsRestrained).ToArray();
            bool restrained=merged.Assignments.Restrains.Any(r=>Fixed(r).SequenceEqual(new[]{true,true,true,false,false,false}));
            var links=model.Costrains.Values.OfType<GPC.Model.Constraints.RigidLink>().ToArray();
            bool linkedOnce=links.Length==1&&links[0].StartNode.Id==merged.Id&&links[0].EndNodes.Length==1&&links[0].EndNodes[0].Id==target.Id;
            if(model.NodesElements.Count!=2 || !restrained || !linkedOnce || merged.Groups.Count!=1 || !merged.Groups.Keys.Contains("Link"))
                throw new Exception("GH node merge lost restraint/link/group; order="+reverse);
            var forces=merged.Loads.Values.OfType<GPC.Model.Loads.PointLoad>().Where(l=>((GPC.Model.LoadCases.LoadCaseBase)l.LoadCase).Name=="Q").ToArray();
            if(forces.Sum(l=>l.F3)!=-200 || forces.Sum(l=>l.F1)!=0 || forces.Sum(l=>l.F2)!=0 || Snapshot(support)!=supportBefore || Snapshot(connection)!=connectionBefore)
                throw new Exception("GH node merge lost additive actions or mutated input");
        }
        Console.WriteLine("GH NODE MERGE PASS: support plus rigid link, additive equal loads, groups and input isolation in both source orders (GPC data alone).");
    }
}

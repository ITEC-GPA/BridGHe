using System;
using System.Linq;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
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
            var loadCase=new LoadCaseModel("Q","Q");
            var support=new NodeModel("support",Point3d.Origin);
            support.Attributes.Add(new NodeRestraintAttributeModel(true,true,true,false,false,false));
            support.Loads.Add(new NodeForceLoadModel(loadCase,new Vector3d(0,0,-100)));
            var connection=new NodeModel("link",Point3d.Origin);
            connection.Attributes.Add(new RigidConnectionAttributeModel(new Point3d(0,0,1000)));
            connection.Loads.Add(new NodeForceLoadModel(loadCase,new Vector3d(0,0,-100)));
            connection.Groups.Add(new GroupModel("Link"));
            using var doc=new GH_Document();
            var component=(GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t=>t.Name=="BuildModelComponent"));
            component.CreateAttributes();doc.AddObject(component,false);
            var input=(Param_GenericObject)component.Params.Input[0];
            foreach(var node in (reverse?new[]{connection,support}:new[]{support,connection}).Concat(new[]{new NodeModel("top",new Point3d(0,0,1000))}))
            {
                var goo=(IGH_Goo)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Node"));
                goo.GetType().GetProperty("Value").SetValue(goo,node); input.PersistentData.Append(goo);
            }
            var name=(Param_String)component.Params.Input[1];name.PersistentData.Clear();name.SetPersistentData("Coincident support and link");
            var units=(Param_Integer)component.Params.Input[2];units.PersistentData.Clear();units.SetPersistentData(2);
            GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
            if(component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0)throw new Exception(string.Join(";",component.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
            var model=(ModelModel)component.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
            var merged=model.Nodes.Single(n=>n.Position==Point3d.Origin);
            if(model.Nodes.Count!=2 || !merged.Attributes.OfType<NodeRestraintAttributeModel>().Any() || !merged.Attributes.OfType<RigidConnectionAttributeModel>().Any() || merged.Groups.Count!=1)
                throw new Exception("GH node merge lost restraint/link/group; order="+reverse);
            if(NodalLoadAccumulator.Sum(merged.Loads)["Q"].Force.Z!=-200 || support.Attributes.Count!=1 || support.Loads.Count!=1)
                throw new Exception("GH node merge lost additive actions or mutated input");
            if(RigidConnectionExport.Resolve(model,1e-6).Length!=1)throw new Exception("Merged link has no target");
        }
        Console.WriteLine("GH NODE MERGE PASS: support plus rigid link, additive equal loads, groups and input isolation in both source orders.");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GPC.Converter.Straus7;
using Rhino.Geometry;
using static BridgeMovingRevisionExample;

/// <summary>Read the generated native attributes: equilibrium alone cannot prove that traffic loads the slab. F6A: read with the GPC
/// Converter API (the model is written by the Converter; the former St7API binding cannot share the process with it).</summary>
internal static class BridgeMovingSlabTargets
{
    internal static int Run(string folder)
    {
        var model=ModelArchive.Read<ModelModel>(File.ReadAllBytes(Path.Combine(folder,"moving.bgmodel")));
        var xyz=model.MovingPaths.Single().Coordinates;double level=xyz[2];
        Require(xyz[5]==level&&xyz[8]==level,"This REV003 target test expects a horizontal path.");
        var forces=model.GeneratedMovingCases.ToDictionary(k=>k.Key,k=>Vector3d.Zero);
        var assignments=new List<object>();
        string scratch=Path.Combine(Path.GetTempPath(),"bridghe-slab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
        try
        {
            using var api=new Straus7NativeApi();
            api.OpenModelReadOnly(Path.Combine(folder,"M03_N0_MOBILE.st7"),scratch);
            Point3d Node(int id){var p=api.ReadNode(id).Coordinates;return new(p[0],p[1],p[2]);}
            foreach(var entity in new[]{Straus7Entity.Node,Straus7Entity.Beam})
            {
                int total=api.Count(entity);
                for(int id=1;id<=total;id++)
                {
                    foreach(var attribute in api.ReadAttributes(entity,id,entity==Straus7Entity.Node?Straus7Attributes.Force:Straus7Attributes.BeamCFG))
                    {
                        int lc=attribute.Case;if(!forces.ContainsKey(lc))continue;
                        double[] data;Point3d p;
                        if(entity==Straus7Entity.Node){data=api.ReadNodeLoad(id,lc,false);p=Node(id);}
                        else
                        {
                            data=api.ReadBeamPointLoad(id,lc,attribute.Id,false,true);
                            var connection=api.ReadElement(Straus7Entity.Beam,id).Nodes;
                            var a=Node(connection[0]);var b=Node(connection[1]);p=a;
                            Require(Math.Abs(a.Z-level)<.01&&Math.Abs(b.Z-level)<.01,"Moving load assigned to a beam outside slab plane: "+id);
                        }
                        Require(Math.Abs(p.Z-level)<.01,"Moving load assigned below slab plane: "+id);
                        var f=new Vector3d(data[0],data[1],data[2]);forces[lc]+=f;assignments.Add(new{Entity=(int)entity,Id=id,Case=lc,Z_mm=p.Z,Fz_N=f.Z});
                    }
                    if(entity==Straus7Entity.Beam)
                        Require(!api.ReadAttributes(entity,id,Straus7Attributes.BeamCFL).Any(a=>forces.ContainsKey(a.Case)),"Unexpected local moving-force attributes.");
                }
            }
            api.CloseModel();
        }
        finally{try{Directory.Delete(scratch,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
        foreach(var lc in forces)
        {
            string name=model.GeneratedMovingCases[lc.Key];var reaction=model.Nodes.SelectMany(n=>n.Results.OfType<NodeReactResultModel>()).Where(r=>r.LoadCase.Name==name).Aggregate(Vector3d.Zero,(a,r)=>a+r.Force);
            Require(lc.Value.Length>0&&(reaction+lc.Value).Length<.1,"Native load assignments do not balance reactions: "+name);
        }
        File.WriteAllText(Path.Combine(folder,"slab-target-validation.json"),JsonSerializer.Serialize(new{Cases=forces.Count,SlabAxisElevation_mm=level,AllGeneratedForcesOnSlab=true,Assignments=assignments},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"REV003 SLAB TARGETS PASS: {forces.Count} cases, {assignments.Count} force attributes on slab axes at Z={level} mm.");return assignments.Count;
    }
}

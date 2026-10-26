using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
using Rhino.Geometry;
using St7API;
using static BridgeMovingRevisionExample;

/// <summary>Read the generated native attributes: equilibrium alone cannot prove that traffic loads the slab.</summary>
internal static class BridgeMovingSlabTargets
{
    internal static int Run(string folder)
    {
        var model=ModelArchive.Read<ModelModel>(File.ReadAllBytes(Path.Combine(folder,"moving.bgmodel")));
        var xyz=model.MovingPaths.Single().Coordinates;double level=xyz[2];
        Require(xyz[5]==level&&xyz[8]==level,"This REV003 target test expects a horizontal path.");
        var forces=model.GeneratedMovingCases.ToDictionary(k=>k.Key,k=>Vector3d.Zero);
        var assignments=new List<object>();
        lock(StrausRuntime.Gate)
        {
            int uid=StrausRuntime.AcquireModelId();bool open=false;
            try
            {
                StrausRuntime.Check(St7.St7OpenFile(uid,Path.Combine(folder,"M03_N0_MOBILE.st7"),Path.GetTempPath()),"Open generated moving loads");open=true;
                Point3d Node(int id){var p=new double[3];StrausRuntime.Check(St7.St7GetNodeXYZ(uid,id,p),"Load target coordinates");return new(p[0],p[1],p[2]);}
                int[] Attributes(int entity,int id,int attribute)
                {
                    int count=0;StrausRuntime.Check(St7.St7GetEntityAttributeSequenceCount(uid,entity,id,attribute,ref count),"Attribute count");
                    if(count==0)return Array.Empty<int>();
                    var data=new int[4*count];StrausRuntime.Check(St7.St7GetEntityAttributeSequence(uid,entity,id,attribute,count,data),"Attribute sequence");return data;
                }
                foreach(int entity in new[]{St7.tyNODE,St7.tyBEAM})
                {
                    int total=0;StrausRuntime.Check(St7.St7GetTotal(uid,entity,ref total),"Entity count");
                    for(int id=1;id<=total;id++)
                    {
                        var attrs=Attributes(entity,id,entity==St7.tyNODE?St7.aoForce:St7.aoBeamCFG);
                        for(int k=0;k<attrs.Length;k+=4)
                        {
                            int lc=attrs[k+St7.ipAttrCase];if(!forces.ContainsKey(lc))continue;
                            var data=new double[4];Point3d p;
                            if(entity==St7.tyNODE){StrausRuntime.Check(St7.St7GetNodeForce3(uid,id,lc,data),"Generated node force");p=Node(id);}
                            else
                            {
                                StrausRuntime.Check(St7.St7GetBeamPointForceGlobal4ID(uid,id,lc,attrs[k+St7.ipAttrID],data),"Generated beam force");
                                var connection=new int[St7.kMaxElementNode+1];StrausRuntime.Check(St7.St7GetElementConnection(uid,entity,id,connection),"Loaded beam connection");
                                var a=Node(connection[1]);var b=Node(connection[2]);p=a;
                                Require(Math.Abs(a.Z-level)<.01&&Math.Abs(b.Z-level)<.01,"Moving load assigned to a beam outside slab plane: "+id);
                            }
                            Require(Math.Abs(p.Z-level)<.01,"Moving load assigned below slab plane: "+id);
                            var f=new Vector3d(data[0],data[1],data[2]);forces[lc]+=f;assignments.Add(new{Entity=entity,Id=id,Case=lc,Z_mm=p.Z,Fz_N=f.Z});
                        }
                        if(entity==St7.tyBEAM)
                        {
                            var principal=Attributes(entity,id,St7.aoBeamCFL);
                            Require(!Enumerable.Range(0,principal.Length/4).Any(k=>forces.ContainsKey(principal[4*k+St7.ipAttrCase])),"Unexpected local moving-force attributes.");
                        }
                    }
                }
            }
            finally{if(open)St7.St7CloseFile(uid);StrausRuntime.ReleaseModelId(uid);}
        }
        foreach(var lc in forces)
        {
            string name=model.GeneratedMovingCases[lc.Key];var reaction=model.Nodes.SelectMany(n=>n.Results.OfType<NodeReactResultModel>()).Where(r=>r.LoadCase.Name==name).Aggregate(Vector3d.Zero,(a,r)=>a+r.Force);
            Require(lc.Value.Length>0&&(reaction+lc.Value).Length<.1,"Native load assignments do not balance reactions: "+name);
        }
        File.WriteAllText(Path.Combine(folder,"slab-target-validation.json"),JsonSerializer.Serialize(new{Cases=forces.Count,SlabAxisElevation_mm=level,AllGeneratedForcesOnSlab=true,Assignments=assignments},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"REV003 SLAB TARGETS PASS: {forces.Count} cases, {assignments.Count} force attributes on slab axes at Z={level} mm.");return assignments.Count;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BridGHe.Common.Models;
using GPC.Model.Analysis;
using GPC.Model.Core.Identity;
using GPC.Model.Elements;
using GPC.Model.LoadCases;
using GPC.Model.Loads;
using GPC.Model.Models;
using GPC.Model.Restraints;
using GPC.Model.Results;
using GPC.Model.Results.Locations;
using GPC.Model.Results.State;
using GPC.Model.Results.Storage;
using GPC.Model.Structure.Assignments;
using G = GPC.Geometry;
using R = Rhino.Geometry;

// Transitional F4 adapter. The existing Straus writer is only a backend; every
// exported field is rebuilt from GPC, with unsupported inputs rejected.
internal static class GpcPilotAdapter
{
    internal static Guid Identity(Guid owner,string key) { using var sha=SHA256.Create(); return new Guid(sha.ComputeHash(Encoding.UTF8.GetBytes(owner+"\n"+key)).Take(16).ToArray()); }
    internal static G.Point3d Point(R.Point3d p)=>new(p.X,p.Y,p.Z);
    internal static G.Vector3d Vector(R.Vector3d p)=>new(p.X,p.Y,p.Z);
    internal static R.Point3d Point(G.Point3d p)=>new(p.X,p.Y,p.Z);
    internal static R.Vector3d Vector(G.Vector3d p)=>new(p.X,p.Y,p.Z);
    internal static Model Import(ModelModel input,Guid owner)
    {
        if(input.Units!=ModelModel.ModelUnits.Nmm || input.Plates.Count!=0 || input.LoadCombinations.Count!=0 || input.StagedConstruction.Count!=0 || input.MovingPaths.Count!=0)
            throw new NotSupportedException("PilotRequiresNmmStaticBeamModel");
        var cases=input.LoadCases.Concat(input.Elements.SelectMany(e=>e.Loads).Select(l=>l.LoadCase)).GroupBy(c=>c.Name).ToDictionary(c=>c.Key,c=>new LoadCaseBase(Identity(owner,"case:"+c.Key),c.Key));
        var nodes=input.Nodes.Select((n,i)=>new NodeElement(Identity(owner,"node:"+i),Point(n.Position),name:"node:"+i)).ToArray();
        var elements=new List<Element>(nodes);
        for(int i=0;i<nodes.Length;i++)
        {
            var n=input.Nodes[i]; var target=nodes[i]; target.Source=new SourceIdentity("BridGHe.F4",owner.ToString(),EntityFamily.Node,i.ToString());
            if(n.Attributes.Any(a=>a is not NodeRestraintAttributeModel && a is not NodeCoordinateAttributeModel) || n.Attributes.OfType<NodeRestraintAttributeModel>().Count()>1 || n.Attributes.OfType<NodeCoordinateAttributeModel>().Count()>1)
                throw new NotSupportedException("PilotUnsupportedNodeAssignment");
            var r=n.Attributes.OfType<NodeRestraintAttributeModel>().SingleOrDefault()?.Value;
            if(r!=null)
            {
                if(r.TransationalDisplacement.Length!=0 || r.RotationDisplacement.Length!=0) throw new NotSupportedException("PilotPrescribedDisplacement");
                var frame=n.Attributes.OfType<NodeCoordinateAttributeModel>().SingleOrDefault()?.Value??R.Plane.WorldXY;
                var axes=new G.CoordinateSystem(Point(frame.Origin),Vector(frame.XAxis),Vector(frame.YAxis));
                var dofs=new[]{r.Tx,r.Ty,r.Tz,r.Rx,r.Ry,r.Rz}.Select((v,j)=>new DofRestrain((GeometryRestrain.DOF)j,v)).ToList();
                target.Assignments.Restrains.Add(new RestrainAssignment{Restrain=new NodeRestrain(target,axes,dofs)});
            }
            foreach(var load in n.Loads)
            {
                var f=load is NodeForceLoadModel force?Vector(force.Value):new G.Vector3d(0,0,0);
                var m=load is NodeMomentLoadModel moment?Vector(moment.Value):new G.Vector3d(0,0,0);
                if(load is not NodeForceLoadModel && load is not NodeMomentLoadModel) throw new NotSupportedException("PilotNodalLoad");
                target.Loads.Add(new PointLoad(f,m,target.Position,cases[load.LoadCase.Name]));
            }
        }
        NodeElement At(R.Point3d p)=>nodes.Single(n=>Point(n.Position)==p);
        for(int i=0;i<input.Beams.Count;i++)
        {
            var b=input.Beams[i]; var p=b.BeamProperty;
            if(b.Attributes.Count!=0 || b.Loads.Count!=0 || p?.PropertyType!=BeamPropertyType.Beam || p.Section.SectionType!=SectionModel.SectionTypes.Generic || p.AngleX1Rad!=0 || p.Material.Density!=0)
                throw new NotSupportedException("PilotGenericElasticBeamOnly");
            var s=p.Section; var material=p.Material;
            var beam=new BeamElement(Identity(owner,"beam:"+i),Point(b.PointFrom),Point(b.PointTo),null,name:"beam:"+i); beam.ConnectNodes(At(b.PointFrom),At(b.PointTo));
            beam.Source=new SourceIdentity("BridGHe.F4",owner.ToString(),EntityFamily.Beam,i.ToString()); beam.Assignments.Formulation=BeamFormulation.StraightTwoNode;
            beam.RotationAroundFirstAxis=b.AngleDeg*Math.PI/180;
            b.GetLocalAxes(true,out var v1,out var v2,out _,out _);
            beam.Assignments.SectionAxes=new G.CoordinateSystem(beam.StartPoint,Vector(v1),Vector(v2));
            beam.Assignments.OffsetAxes=beam.Assignments.SectionAxes;
            beam.Assignments.OffsetI=new G.Vector3d(b.Offset.X,b.Offset.Y,0); beam.Assignments.OffsetJ=new G.Vector3d(b.Offset.X,b.Offset.Y,0);
            double e=material.Modulus,g=material.UsePoisson ? e/(2*(1+material.PoissonRatio)) : material.ShearModulus;
            beam.Assignments.AnalysisProfile=new BeamAnalysisProfile();
            foreach(double t in new[]{0.0,1.0}) beam.Assignments.AnalysisProfile.Stations.Add(new BeamAnalysisStation {Station=t,EA=e*s.SectionArea,EI1=e*s.I11,EI2=e*s.I22,GJ=g*s.J,GA1=g*s.ShearA1,GA2=g*s.ShearA2,MassPerLength=0});
            elements.Add(beam);
        }
        var result=ModelAssembly.Assemble(elements,owner,"F4 bridge");
        foreach(var lc in cases.Values) if(!result.LoadCases.ContainsKey(lc.Name))result.LoadCases.Add(lc);
        return result;
    }
    internal static ModelModel Export(Model model,out SolverIdentityMap map)
    {
        if(model.FormFinding!=null || model.MovingLoads!=null || PhysicalAssignments.HasData(model) || model.AreaElements.Count!=0 || model.VolumeElements.Count!=0 || model.Combinations.Count!=0 || model.Stages.Count!=0 || model.Costrains.Count!=0 || model.ModelLoads.Count!=0) throw new NotSupportedException("PilotModelScope");
        var result=new ModelModel(ModelModel.ModelUnits.Nmm){Name=model.Name};
        var cases=model.LoadCases.Values.ToDictionary(c=>c.Name,c=>new LoadCaseModel(c.Name,c.Name)); result.LoadCases.AddRange(cases.Values);
        var ids=new Dictionary<Guid,int>();
        foreach(var node in model.NodesElements.Values.OrderBy(n=>int.Parse(n.Source.OriginalId)))
        {
            if(node.Attributes.Count!=0 || node.Assignments.GroundSprings.Count!=0 || node.Assignments.Links.Count!=0 || node.Assignments.Mass!=null || node.Assignments.Active!=null || node.Assignments.Dofs!=null || node.Assignments.Restrains.Count>1) throw new NotSupportedException("PilotNodeScope");
            int id=result.Nodes.Count+1; ids.Add(node.Guid,id); var n=new NodeModel(id.ToString(),Point(node.Position));
            foreach(var assignment in node.Assignments.Restrains)
            {
                var r=assignment.Restrain; if(assignment.Case!=null || assignment.Phase!=null || r.Restrains.Any(d=>d.HasStiffness || d.HasImposedDisplacement))throw new NotSupportedException("PilotRestraintScope");
                bool At(int i)=>r.Restrains.Any(d=>(int)d.Dof==i&&d.IsRestrained);
                n.Attributes.Add(new NodeRestraintAttributeModel(At(0),At(1),At(2),At(3),At(4),At(5)));
                var cs=r.CoordinateSystem; n.Attributes.Add(new NodeCoordinateAttributeModel(new R.Plane(Point(cs.Origin),Vector(cs.V1),Vector(cs.V2))));
            }
            foreach(var load in node.Loads.Values)
            {
                if(load is not PointLoad p)throw new NotSupportedException("PilotNodalLoadScope"); if(!p.Point.Equals(node.Position))throw new NotSupportedException("PilotLoadMustActAtNode"); var global=p.ToGlobal();
                n.Loads.Add(new NodeForceLoadModel(cases[p.LoadCase.Name],new R.Vector3d(global.F1,global.F2,global.F3)));
                n.Loads.Add(new NodeMomentLoadModel(cases[p.LoadCase.Name],new R.Vector3d(global.M1,global.M2,global.M3)));
            }
            result.AddElements(new[]{n});
        }
        foreach(var beam in model.BeamElements.Values.OrderBy(b=>int.Parse(b.Source.OriginalId)))
        {
            var a=beam.Assignments;
            if(beam.BeamProperty!=null || beam.Loads.Count!=0 || beam.Attributes.Count!=0 || a.Formulation!=BeamFormulation.StraightTwoNode || a.Loads.Count!=0 || a.Sections.Count!=0 || a.OtherAssignments.Count!=0 || a.RigidLengthI!=0 || a.RigidLengthJ!=0 || a.OrientationNodeId!=null || a.SectionGeometryAxes!=null || a.SectionCentroidOffset!=null || a.StationDomain!=null)
                throw new NotSupportedException("PilotBeamScope");
            var p=a.AnalysisProfile; p.Validate(); var s=p.At(0); var end=p.At(1);
            if(p.Interpolation!=ProfileInterpolation.Constant || p.Stations.Count!=2 || s.MassPerLength!=0 || s.EA!=end.EA || s.EI1!=end.EI1 || s.EI2!=end.EI2 || s.GJ!=end.GJ || s.GA1!=end.GA1 || s.GA2!=end.GA2)throw new NotSupportedException("PilotConstantRigiditiesOnly");
            // Reference modulus 1 N/mm^2, nu=0: the section is an analysis surrogate;
            // EA, EI1, EI2, GJ, GA1, GA2 are preserved, no resistance material is inferred.
            var property=new BeamPropertyModel((result.Beams.Count+1).ToString()){Name="Analysis rigidities",PropertyType=BeamPropertyType.Beam,Material=new MaterialModel("Analysis"){Modulus=1,PoissonRatio=0,UsePoisson=true},
                Section=new SectionModel{SectionType=SectionModel.SectionTypes.Generic,SectionArea=s.EA,I11=s.EI1,I22=s.EI2,J=2*s.GJ,ShearA1=2*s.GA1,ShearA2=2*s.GA2}};
            var b=new BeamModel(Point(beam.StartPoint),Point(beam.EndPoint),property,angleDeg:beam.RotationAroundFirstAxis*180/Math.PI){BeamId=(result.Beams.Count+1).ToString()};
            b.GetLocalAxes(true,out var v1,out var v2,out _,out _);
            if((Vector(a.SectionAxes.V1)-v1).Length>1e-12 || (Vector(a.SectionAxes.V2)-v2).Length>1e-12 || a.OffsetI.Z!=0 || a.OffsetJ.Z!=0 || !a.OffsetI.Equals(a.OffsetJ) || !a.OffsetAxes.V1.Equals(a.SectionAxes.V1) || !a.OffsetAxes.V2.Equals(a.SectionAxes.V2))throw new NotSupportedException("PilotInconsistentBeamAxesOrOffsets");
            b.Offset=new R.Vector2d(a.OffsetI.X,a.OffsetI.Y); ids.Add(beam.Guid,result.Beams.Count+1); result.AddElements(new[]{b});
        }
        map=model.AssemblyMap.ToSolver(model,"Straus7-R3",ids); return result;
    }
    internal static void Attach(Model model,ModelModel solved,SolverIdentityMap map)
    {
        if(model.AnalysisFingerprint()!=map.AnalysisFingerprint)throw new InvalidOperationException("StaleStrausResult");
        var ids=map.Entries.ToDictionary(e=>e.EntityGuid,e=>e.SolverId); model.CaptureAnalysis();
        ResultState State(string name)=>new(){DatasetId="linear-static",InputFingerprint=model.Analysis.InputFingerprint,Step=name,Semantics=AnalysisSemantics.LinearStatic,Components=Enumerable.Repeat(ComponentAvailability.Available,6).ToArray()};
        foreach(var node in model.NodesElements.Values)
        {
            var n=solved.Nodes[ids[node.Guid]-1];
            foreach(var d in n.Results.OfType<NodeDisplResultModel>())node.AddResult(new NodeResult(new List<INodeResultLocation>{new NodeResultDisplacement(model.LoadCases[d.LoadCase.Name],new ResultDisplacement(d.Displacement.X,d.Displacement.Y,d.Displacement.Z,d.Rotation.X,d.Rotation.Y,d.Rotation.Z)){State=State(d.LoadCase.Name)}}));
            foreach(var r in n.Results.OfType<NodeReactResultModel>())node.AddResult(new NodeResult(new List<INodeResultLocation>{new NodeResultForces(model.LoadCases[r.LoadCase.Name],new ResultBeamForces(r.Force.Z,r.Force.X,r.Force.Y,r.Moment.Z,r.Moment.X,r.Moment.Y,G.CoordinateSystem.Global)){Kind=NodalForceKind.SupportReaction,State=State(r.LoadCase.Name)}}));
        }
        foreach(var beam in model.BeamElements.Values)
        {
            var samples=solved.Beams[ids[beam.Guid]-1].Results.OfType<BeamForceResultModel>().Select(f=>new StationResultBeamForces(model.LoadCases[f.LoadCase.Name],new ResultBeamForces(f.AxialForce,f.Vx,f.Vy,f.Torque,f.My,-f.Mx,beam.Assignments.SectionAxes),f.NormalizedLength?f.Station:f.Station/beam.Length){State=State(f.LoadCase.Name)}).ToArray();
            beam.Results.Add(new BeamResult(samples));
        }
        model.CompactResults();
    }
}

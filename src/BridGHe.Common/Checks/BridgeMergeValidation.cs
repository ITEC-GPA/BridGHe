using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;

namespace BridGHe.Common.Checks
{
    public static class BridgeMergeValidation
    {
        public static double StationTolerance(double length) => Math.Max(GPC.Geometry.GeometryBase.Tolerance, length * 1e-12);
        public static double[] CanonicalStations(IEnumerable<double> samples,double length)
        {
            var stations=new List<double>();double tolerance=StationTolerance(length);
            foreach(double station in samples.OrderBy(x=>x))
            {
                if(!double.IsFinite(station) || station < -tolerance || station > length+tolerance)throw new ArgumentException("Result station outside beam bounds.");
                double bounded=Math.Clamp(station,0,length);
                if(stations.Count==0 || bounded-stations[stations.Count-1]>tolerance)stations.Add(bounded);
            }
            return stations.ToArray();
        }

        public static BeamModel[][] Align(IReadOnlyList<ModelModel> models,double tolerance, bool allowAdditionalMembers = false)
        {
            if(models.Count==0 || models.Any(m=>m==null))throw new ArgumentException("Provide analysed models in the intended origin order.");
            var first=models[0];
            if(models.Any(m=>m.Units!=first.Units))throw new ArgumentException("All phase models must use the same units.");
            if(first.Beams.Count==0)throw new ArgumentException("No beam results to merge.");
            var aligned=new BeamModel[models.Count][];
            for(int i=0;i<models.Count;i++)
            {
                var map=models[i].Beams.ToDictionary(b=>b.BeamId,StringComparer.Ordinal);
                if(!allowAdditionalMembers && map.Count!=first.Beams.Count)throw new ArgumentException($"Model {i+1}: beam count differs from model 1.");
                aligned[i]=new BeamModel[first.Beams.Count];
                for(int j=0;j<first.Beams.Count;j++)
                {
                    var source=first.Beams[j];
                    if(!map.TryGetValue(source.BeamId,out var beam))throw new ArgumentException($"Model {i+1}: missing beam {source.BeamId}.");
                    if(source.PointFrom.DistanceTo(beam.PointFrom)>tolerance || source.PointTo.DistanceTo(beam.PointTo)>tolerance || Math.Abs(source.AngleDeg-beam.AngleDeg)>1e-9 || (source.Offset-beam.Offset).Length>tolerance)
                        throw new ArgumentException($"Model {i+1}, beam {source.BeamId}: geometry, orientation or offsets differ. Reversed axes cannot be merged without transforming results.");
                    aligned[i][j]=beam;
                }
            }
            return aligned;
        }

        public static void ValidatePhases(IEnumerable<string> forceCases,IEnumerable<string> strainCases,params (IEnumerable<string> Names,bool Strain)[] phases)
        {
            var forces=forceCases.ToHashSet(StringComparer.Ordinal);var strains=strainCases.ToHashSet(StringComparer.Ordinal);
            if(forces.Overlaps(strains))throw new ArgumentException("Use distinct case names for FEM forces and slab eigenstrains.");
            var assigned=new HashSet<string>(StringComparer.Ordinal);
            foreach(var phase in phases)foreach(string name in phase.Names)
            {
                if(!phase.Strain && strains.Contains(name))
                    throw new ArgumentException($"Case '{name}' contains slab eigenstrain/autostress, not FEM forces. Connect it to Phase Ninf strain Load Cases or Phase N0 strain Load Cases. The final shrinkage FEM input requires an analysed force case.");
                if(phase.Strain && forces.Contains(name))
                    throw new ArgumentException($"Case '{name}' contains analysed FEM forces, not slab eigenstrain. Assign it to a force phase; for shrinkage use the final shrinkage FEM Load Cases input.");
                if(!(phase.Strain?strains:forces).Contains(name))throw new ArgumentException($"Unknown phase case '{name}'. Use the model-order prefix and the exact analysed case name.");
                if(!assigned.Add(name))throw new ArgumentException($"Case '{name}' belongs to more than one phase.");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BridGHe.Common.MovingLoads;
using St7API;
using static BridGHe.Common.Straus.StrausRuntime;

namespace BridGHe.Common.Straus
{
    public static class MovingLoadExporter
    {
        public static IReadOnlyDictionary<int, string> Write(int uid, IReadOnlyList<MovingPath> paths, IReadOnlyDictionary<string, int> cases)
        {
            foreach (var path in paths)
            {
                path.Validate();
                if (!cases.ContainsKey(path.LoadCaseName)) throw new ArgumentException("Caso mobile assente dal modello: " + path.LoadCaseName);
                if (path.Target == 1) throw new NotSupportedException("Target Entity Set: il nuovo modello BridGHe non contiene set nativi Straus. Usare Any, Beam o Plate finché l'esportazione dei set non è implementata.");
                if (path.Target == 4) throw new NotSupportedException("Target Brick: questa trasposizione esporta nodi, beam e plate; non contiene elementi brick.");
            }
            var templates = new Dictionary<string, int>();
            bool convert = false;
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                string key = System.Text.Json.JsonSerializer.Serialize(path.Template);
                if (!templates.TryGetValue(key, out int templateId))
                {
                    templateId = templates.Count + 1; templates.Add(key, templateId);
                    WriteTemplate(uid, templateId, path.Template);
                }
                Check(St7.St7SetLoadPath(uid, i + 1, new[] { cases[path.LoadCaseName], templateId, path.Shape, path.Surface, path.Target, path.Divisions, path.EntitySetId }, path.Coordinates), "St7SetLoadPath");
                Check(St7.St7SetEntitySelectState(uid, St7.tyLOADPATH, i + 1, 0, (byte)(path.ConvertToStaticCases ? 1 : 0)), "St7SetEntitySelectState");
                convert |= path.ConvertToStaticCases;
            }
            var generated = new Dictionary<int, string>();
            if (convert)
            {
                int before = 0, after = 0;
                Check(St7.St7GetNumLoadCase(uid, ref before), "St7GetNumLoadCase");
                Check(St7.St7ConvertLoadPathsToLoadCases(uid, 1, 1, 1), "St7ConvertLoadPathsToLoadCases");
                Check(St7.St7GetNumLoadCase(uid, ref after), "St7GetNumLoadCase");
                for (int id = before + 1; id <= after; id++)
                {
                    var name = new StringBuilder(St7.kMaxStrLen);
                    Check(St7.St7GetLoadCaseName(uid, id, name, name.Capacity), "St7GetLoadCaseName");
                    generated.Add(id, name.ToString());
                }
                if (generated.Count == 0) throw new InvalidOperationException("Conversione dei percorsi senza nuovi casi statici: controllare geometria, target e licenza Moving Load.");
            }
            return generated;
        }

        private static void WriteTemplate(int uid, int id, MovingTemplate template)
        {
            Check(St7.St7NewLoadPathTemplate(uid, id, template.Name), "St7NewLoadPathTemplate");
            if (!string.IsNullOrWhiteSpace(template.LibraryName))
            {
                int library = 0, item = 0;
                Check(St7.St7GetLibraryID(St7.lbLoadPathTemplate, template.LibraryName, ref library), "St7GetLibraryID");
                Check(St7.St7GetLibraryItemID(St7.lbLoadPathTemplate, library, template.LibraryItem, ref item), "St7GetLibraryItemID");
                Check(St7.St7AssignLibraryLoadPathTemplate(uid, id, library, item), "St7AssignLibraryLoadPathTemplate");
                return;
            }
            Check(St7.St7SetLoadPathTemplateParameters(uid, id, new[] { template.Colour, template.LaneFactors.Length, template.MultiLaneType, template.TransitionLoad ? 1 : 0, template.BeamLateralTolerance ? 1 : 0 }, new[] { template.RelativeTolerance, template.MinimumLaneWidth }), "St7SetLoadPathTemplateParameters");
            for (int lane = 0; lane < template.LaneFactors.Length; lane++) Check(St7.St7SetLoadPathTemplateLaneFactor(uid, id, lane + 1, template.LaneFactors[lane]), "St7SetLoadPathTemplateLaneFactor");
            if (template.Centrifugal is MovingCentrifugal c)
                Check(St7.St7SetLoadPathTemplateCentrifugalData(uid, id, c.K0, c.K1, new[] { c.LimitK1 ? 1 : 0, c.LengthUnit, c.ForceUnit }, new[] { c.MinimumK1, c.MaximumK1 }), "St7SetLoadPathTemplateCentrifugalData");
            for (int v = 0; v < template.Vehicles.Length; v++)
            {
                var vehicle = template.Vehicles[v]; int vi = v + 1;
                Check(St7.St7AddLoadPathTemplateVehicle(uid, id), "St7AddLoadPathTemplateVehicle");
                Check(St7.St7SetLoadPathTemplateVehicleName(uid, id, vi, vehicle.Name), "St7SetLoadPathTemplateVehicleName");
                Check(St7.St7SetLoadPathTemplateVehicleData(uid, id, vi, new[] { vehicle.Instance, vehicle.Direction }, new[] { vehicle.Velocity, vehicle.StartTime }), "St7SetLoadPathTemplateVehicleData");
                Check(St7.St7SetLoadPathTemplateVehicleSet(uid, id, vi, vehicle.VehicleSet), "St7SetLoadPathTemplateVehicleSet");
                for (int lane = 1; lane <= template.LaneFactors.Length; lane++)
                {
                    bool enabled = vehicle.Lanes.Length == 0 || vehicle.Lanes.Contains(lane);
                    Check(enabled ? St7.St7EnableLoadPathTemplateVehicleLane(uid, id, vi, lane) : St7.St7DisableLoadPathTemplateVehicleLane(uid, id, vi, lane), "Set vehicle lane");
                }
                int point = 0, distributed = 0, heat = 0;
                foreach (var load in vehicle.Loads)
                {
                    switch (load)
                    {
                        case MovingPointForce p:
                            Check(St7.St7AddLoadPathTemplatePointForce(uid, id, vi), "St7AddLoadPathTemplatePointForce");
                            Check(St7.St7SetLoadPathTemplatePointForceData(uid, id, vi, ++point, new[] { p.Mobility, p.Axis, p.Adjacent ? 1 : 0, p.Centrifugal ? 1 : 0 }, new[] { p.X, p.Y, p.Force[0], p.Force[1], p.Force[2] }), "St7SetLoadPathTemplatePointForceData"); break;
                        case MovingDistributedForce d:
                            Check(St7.St7AddLoadPathTemplateDistributedForce(uid, id, vi), "St7AddLoadPathTemplateDistributedForce");
                            Check(St7.St7SetLoadPathTemplateDistributedForceData(uid, id, vi, ++distributed, new[] { d.Mobility, d.Axis, d.Adjacent ? 1 : 0, d.Centrifugal ? 1 : 0 }, new[] { d.X1, d.X2, d.Y1, d.Y2, d.Force[0], d.Force[1], d.Force[2] }), "St7SetLoadPathTemplateDistributedForceData"); break;
                        case MovingHeatSource h:
                            Check(St7.St7AddLoadPathTemplateHeatSource(uid, id, vi), "St7AddLoadPathTemplateHeatSource");
                            Check(St7.St7SetLoadPathTemplateHeatSourceData(uid, id, vi, ++heat, new int[1], new[] { h.X, h.Y, h.SizeX, h.SizeY, h.Heat }), "St7SetLoadPathTemplateHeatSourceData"); break;
                    }
                }
            }
        }
    }
}

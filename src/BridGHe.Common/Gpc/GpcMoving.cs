using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GPC.Converter.Straus7;
using GPC.Geometry;
using GPC.Model.Actions.Moving;
using GPC.Model.LoadCases;
using GPC.Model.Models;

namespace BridGHe.Common.Gpc
{
    /// <summary>BridGHe moving loads (paths, templates, vehicles, centrifugal law) as the GPC moving-load contract, N-mm.
    /// The integer codes of BridGHe are the Straus7 R3 codes, which the GPC enumerations keep by value (mobility, axes, vehicle
    /// placement and direction, multi-lane rule, path shape and surface). The load-application controls of the template
    /// (transition load, beam lateral tolerance, relative tolerance) and the static conversion become the path options (B4);
    /// the display colour is Straus7 data preserved with the model; a centrifugal law that is not N-mm data stays as a
    /// preserved Straus7 expression (B8).</summary>
    public static class GpcMoving
    {
        /// <summary>Paths stored by the former model as System.Text.Json text (read as a document, without the former classes).</summary>
        public static MovingLoadDefinition FromLegacyJson(string json, Model model, IReadOnlyDictionary<string, LoadCaseBase> cases, BridgeUnits units, Guid owner)
        {
            using var document = JsonDocument.Parse(json);
            var definition = new MovingLoadDefinition(); int index = 0;
            var stored = new List<(Guid Id, Straus7MovingPathOptions Options)>();
            foreach (var p in document.RootElement.EnumerateArray())
            {
                string caseName = p.GetProperty("LoadCaseName").GetString();
                if (!cases.TryGetValue(caseName, out var loadCase)) throw new ArgumentException("Caso mobile assente dal modello: " + caseName);
                var t = p.GetProperty("Template");
                if (!string.IsNullOrWhiteSpace(Text(t, "LibraryName"))) throw new NotSupportedException("LegacyMovingLibraryTemplateNeedsStraus7Resolution");
                int target = Int(p, "Target");
                var path = new MovingLoadPath
                {
                    Id = GpcIdentity.Derive(owner, "moving-path:" + (index++)),
                    CaseGuid = loadCase.Guid,
                    Template = Template(t, units, model),
                    Shape = (MovingPathShape)Range(Int(p, "Shape"), 2, "Shape"),
                    Surface = (MovingPathSurface)Range(Int(p, "Surface"), 1, "Surface"),
                    Target = target == 0 ? MovingPathTarget.AllStructuralElements : target == 2 ? MovingPathTarget.Beams : target == 3 ? MovingPathTarget.Shells
                        : throw new NotSupportedException("LegacyMovingTargetWasRejectedByTheFormerExporter: " + target),
                    Divisions = Int(p, "Divisions"),
                };
                var c = p.GetProperty("Coordinates").EnumerateArray().Select(v => units.ToMm(v.GetDouble())).ToArray();
                if (c.Length != 9) throw new ArgumentException("MovingPathRequiresNineCoordinates");
                path.Start = new Point3d(c[0], c[1], c[2]); path.End = new Point3d(c[3], c[4], c[5]); path.Reference = new Point3d(c[6], c[7], c[8]);
                definition.Paths.Add(path);
                stored.Add((path.Id, new Straus7MovingPathOptions
                {
                    Colour = Int(t, "Colour", 255), TransitionTrailingLoad = Bool(t, "TransitionLoad"), BeamLateralTolerance = Bool(t, "BeamLateralTolerance"),
                    RelativeTolerance = Double(t, "RelativeTolerance", .01), ConvertToStaticCases = Bool(p, "ConvertToStaticCases"),
                }));
            }
            model.MovingLoads = definition;
            foreach (var (id, options) in stored) options.Store(model, id);
            return definition;
        }

        private static MovingLoadTemplate Template(JsonElement t, BridgeUnits units, Model model)
        {
            var template = new MovingLoadTemplate
            {
                Name = Text(t, "Name") ?? "Template",
                LaneFactors = t.GetProperty("LaneFactors").EnumerateArray().Select(v => v.GetDouble()).ToArray(),
                LaneFactorRule = (MovingLaneFactorRule)Range(Int(t, "MultiLaneType"), 1, "MultiLaneType"),
                MinimumLaneWidth = units.ToMm(Double(t, "MinimumLaneWidth", 0)),
                Vehicles = t.GetProperty("Vehicles").EnumerateArray().Select(v => Vehicle(v, units)).ToArray(),
            };
            if (t.TryGetProperty("Centrifugal", out var c) && c.ValueKind == JsonValueKind.Object)
            {
                var expression = new Straus7CentrifugalExpression
                {
                    OffsetExpression = Text(c, "K0") ?? "0", FactorExpression = Text(c, "K1") ?? "0",
                    LengthUnit = Straus7CentrifugalExpression.LengthUnits[Range(Int(c, "LengthUnit"), 4, "LengthUnit")],
                    ForceUnit = Straus7CentrifugalExpression.ForceUnits[Range(Int(c, "ForceUnit"), 6, "ForceUnit")],
                    LimitFactor = Bool(c, "LimitK1"), MinimumFactor = Double(c, "MinimumK1", 0), MaximumFactor = Double(c, "MaximumK1", 0),
                };
                template.Centrifugal = expression.ToDomain(out var record);
                if (record != null && !model.PreservedSourceData.Any(r => r.Kind == record.Kind && r.SourceRecord == record.SourceRecord)) model.PreservedSourceData.Add(record);
            }
            return template;
        }

        private static GPC.Model.Actions.Moving.MovingVehicle Vehicle(JsonElement v, BridgeUnits units)
        {
            var vehicle = new GPC.Model.Actions.Moving.MovingVehicle
            {
                Name = Text(v, "Name") ?? "Vehicle",
                Placement = (MovingVehiclePlacement)Range(Int(v, "Instance"), 1, "Instance"),
                Direction = (MovingVehicleDirection)Range(Int(v, "Direction"), 1, "Direction"),
                Speed = units.ToMm(Double(v, "Velocity", 0)), StartTime = Double(v, "StartTime", 0),
                ExclusiveSet = Text(v, "VehicleSet") ?? "",
                AllowedLanes = v.TryGetProperty("Lanes", out var lanes) ? lanes.EnumerateArray().Select(l => l.GetInt32()).ToArray() : Array.Empty<int>(),
            };
            var points = new List<GPC.Model.Actions.Moving.MovingPointForce>(); var distributed = new List<GPC.Model.Actions.Moving.MovingDistributedForce>(); var heat = new List<GPC.Model.Actions.Moving.MovingHeatSource>();
            foreach (var load in v.GetProperty("Loads").EnumerateArray())
            {
                string kind = load.GetProperty("$kind").GetString();
                var f = load.TryGetProperty("Force", out var force) ? force.EnumerateArray().Select(x => x.GetDouble()).ToArray() : null;
                switch (kind)
                {
                    case "point":
                        points.Add(new GPC.Model.Actions.Moving.MovingPointForce
                        {
                            Mobility = (MovingPointMobility)Range(Int(load, "Mobility"), 1, "Mobility"), Axes = (MovingLoadAxes)Range(Int(load, "Axis"), 1, "Axis"),
                            ApplyAdjacencyFactor = Bool(load, "Adjacent"), ApplyCentrifugal = Bool(load, "Centrifugal") ? true : (bool?)null,
                            X = units.ToMm(Double(load, "X", 0)), Y = units.ToMm(Double(load, "Y", 0)), Force = new Vector3d(units.ToN(f[0]), units.ToN(f[1]), units.ToN(f[2])),
                        });
                        break;
                    case "distributed":
                        distributed.Add(new GPC.Model.Actions.Moving.MovingDistributedForce
                        {
                            Mobility = (MovingDistributionMobility)Range(Int(load, "Mobility"), 4, "Mobility"), Axes = (MovingLoadAxes)Range(Int(load, "Axis"), 1, "Axis"),
                            ApplyAdjacencyFactor = Bool(load, "Adjacent"), ApplyCentrifugal = Bool(load, "Centrifugal") ? true : (bool?)null,
                            X1 = units.ToMm(Double(load, "X1", 0)), X2 = units.ToMm(Double(load, "X2", 0)), Y1 = units.ToMm(Double(load, "Y1", 0)), Y2 = units.ToMm(Double(load, "Y2", 0)),
                            ForcePerLength = new Vector3d(units.ToNPerMm(f[0]), units.ToNPerMm(f[1]), units.ToNPerMm(f[2])),
                        });
                        break;
                    case "heat":
                        if (!units.IsIdentity) throw new NotSupportedException("LegacyMovingHeatSourceUnitsRequireNmm");
                        // Former value in J/s (native energy unit J); GPC power in N mm/s.
                        heat.Add(new GPC.Model.Actions.Moving.MovingHeatSource { X = Double(load, "X", 0), Y = Double(load, "Y", 0), SizeX = Double(load, "SizeX", 0), SizeY = Double(load, "SizeY", 0), Power = Double(load, "Heat", 0) * 1000 });
                        break;
                    default: throw new NotSupportedException("LegacyMovingLoadKind: " + kind);
                }
            }
            vehicle.PointForces = points.ToArray(); vehicle.DistributedForces = distributed.ToArray(); vehicle.HeatSources = heat.Count == 0 ? null : heat.ToArray();
            return vehicle;
        }

        private static int Range(int value, int max, string name) => value >= 0 && value <= max ? value : throw new ArgumentException("LegacyMovingValueOutOfRange: " + name + "=" + value);
        private static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static int Int(JsonElement e, string name, int fallback = 0) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;
        private static double Double(JsonElement e, string name, double fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
        private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    }
}

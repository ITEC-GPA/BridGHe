using BridGHe.Common.Models;
using GPC.Utilities.Units;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Common.Helpers
{
    public static class ModelHelper
    {
        /// <summary>
        /// Setup the conversion parameter for converting measures through the Utilities library
        /// </summary>
        /// <param name="units">The model units</param>
        /// <param name="lengthUnits">The output lenght conversion param</param>
        /// <param name="forceUnits">The output force conversion param</param>
        /// <param name="massUnits">The output mass conversion param</param>
        /// <param name="pressureUnits">The output pressure conversion param</param>
        /// <param name="temperatureUnits">The output temperature conversion param</param>
        public static void GetUtilitiesConversionParams(
            ModelModel.ModelUnits units,
            out UnitsConvert.LengthUnits lengthUnits,
            out UnitsConvert.ForceUnits forceUnits,
            out UnitsConvert.MassUnits massUnits,
            out UnitsConvert.PressureUnits pressureUnits,
            out UnitsConvert.TemperatureUnits temperatureUnits)
        {
            switch (units)
            {
                case ModelModel.ModelUnits.Knm:
                    lengthUnits = UnitsConvert.LengthUnits.m;
                    forceUnits = UnitsConvert.ForceUnits.kN;
                    massUnits = UnitsConvert.MassUnits.ton;
                    pressureUnits = UnitsConvert.PressureUnits.kPa;
                    temperatureUnits = UnitsConvert.TemperatureUnits.C;
                    break;
                case ModelModel.ModelUnits.Nmm:
                    lengthUnits = UnitsConvert.LengthUnits.mm;
                    forceUnits = UnitsConvert.ForceUnits.N;
                    massUnits = UnitsConvert.MassUnits.ton;
                    pressureUnits = UnitsConvert.PressureUnits.MPa;
                    temperatureUnits = UnitsConvert.TemperatureUnits.C;
                    break;
                case ModelModel.ModelUnits.SI:
                    lengthUnits = UnitsConvert.LengthUnits.m;
                    forceUnits = UnitsConvert.ForceUnits.N;
                    massUnits = UnitsConvert.MassUnits.kg;
                    pressureUnits = UnitsConvert.PressureUnits.Pa;
                    temperatureUnits = UnitsConvert.TemperatureUnits.C;
                    break;
                case ModelModel.ModelUnits.IPS:
                    lengthUnits = UnitsConvert.LengthUnits.inch;
                    forceUnits = UnitsConvert.ForceUnits.lbf;
                    massUnits = UnitsConvert.MassUnits.lb;
                    pressureUnits = UnitsConvert.PressureUnits.psi;
                    temperatureUnits = UnitsConvert.TemperatureUnits.F;
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        public static void GetLoadCases(ModelModel model, out List<LoadCaseModel> loadCases)
        {
            loadCases = new List<LoadCaseModel>();// { LoadCaseModel.Dead };
            var generatedNames = new HashSet<string>(model.GeneratedMovingCases.Values);
            var candidates = model.LoadCases.Where(c => !generatedNames.Contains(c.Name))
                .Concat(model.Elements.SelectMany(e => e.Loads).Select(l => l.LoadCase))
                .Concat(model.LoadCombinations.SelectMany(c => c.Values.Keys))
                .Concat(model.StagedConstruction.SelectMany(s => s.LoadCombinations).SelectMany(c => c.Values.Keys)).ToList();
            foreach (var candidate in candidates)
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.Name))
                    throw new ArgumentException("Caso di carico nullo o senza nome.");
            foreach (var group in candidates.GroupBy(c => c.Name, StringComparer.Ordinal))
            {
                var first = group.First();
                if (group.Any(c => !SameLoadDefinition(first, c)))
                    throw new ArgumentException("Definizioni incompatibili per il caso di carico " + group.Key);
                loadCases.Add(first);
            }
            foreach (var path in model.MovingPaths)
                if (!loadCases.Any(c => c.Name == path.LoadCaseName))
                    loadCases.Add(new LoadCaseModel("", path.LoadCaseName));
            AddDeadLoadCase(ref loadCases);
            loadCases.Sort();
        }

        public static Dictionary<string, int> GetLoadCaseNumbers(IReadOnlyList<LoadCaseModel> cases) =>
            cases.Select((c, i) => (c.Name, Number: i + 1)).ToDictionary(c => c.Name, c => c.Number, StringComparer.Ordinal);

        private static bool SameLoadDefinition(LoadCaseModel a, LoadCaseModel b) =>
            a.Temperature == b.Temperature && a.StructuralMassAcceleration == b.StructuralMassAcceleration &&
            a.NonStructuralMassAcceleration == b.NonStructuralMassAcceleration && a.GlobalInertiaLoad == b.GlobalInertiaLoad &&
            a.GravityDirection == b.GravityDirection && a.GravityValue == b.GravityValue &&
            a.A == b.A && a.V == b.V && a.W == b.W && a.Origin == b.Origin;

        public static void AddDeadLoadCase(ref List<LoadCaseModel> loadCases)
        {
            //To define stages, dead load case can be already present in the list, and must have right values. If not is added. 
            LoadCaseModel deadCase = loadCases.FirstOrDefault(lc => new LoadCaseModelsEqualityComparer().Equals(lc, LoadCaseModel.Dead));
            if (deadCase == null)
            {
                //add DeadLoadCase
                loadCases.Add(new LoadCaseModel(LoadCaseModel.Dead));
            }
            else if (deadCase.Name != LoadCaseModel.Dead.Name ||
                     deadCase.Id != LoadCaseModel.Dead.Id ||
                     deadCase.GlobalInertiaLoad != LoadCaseModel.Dead.GlobalInertiaLoad ||
                     deadCase.GravityDirection != LoadCaseModel.Dead.GravityDirection ||
                     deadCase.GravityValue != LoadCaseModel.Dead.GravityValue ||
                     deadCase.StructuralMassAcceleration != LoadCaseModel.Dead.StructuralMassAcceleration ||
                     deadCase.NonStructuralMassAcceleration != LoadCaseModel.Dead.NonStructuralMassAcceleration)
            {
                //NB: stages not checked
                throw new NotSupportedException("Dead load case error");
            }
        }

        public static void GetSuperElements(ModelModel model, out List<SuperElementAttributeModel> superElements)
        {
            superElements = new List<SuperElementAttributeModel>();
            superElements.AddRange(model.Elements.Where(e => e.HasAttribute(typeof(SuperElementAttributeModel)))
                .SelectMany(e => e.Attributes.Where(a => a.GetType() == typeof(SuperElementAttributeModel)))
                .Cast<SuperElementAttributeModel>()
                .Distinct());
        }

        /// <summary>
        /// Assign the elements id and creates the missing nodes
        /// </summary>
        /// <param name="nodes">The nodes list</param>
        /// <param name="beams">The beams list</param>
        /// <param name="meshes">The plates list</param>
        public static void MergeGeometry(ref List<Point3d> inputNodes, ref List<Curve> inputBeams, ref List<Mesh> inputMeshes, double tolerance)
        {
            if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            var points = new List<Point3d>();
            using var tree = new RTree();
            Point3d Resolve(Point3d point)
            {
                int index = -1;
                double nearest = double.PositiveInfinity;
                tree.Search(new Sphere(point, tolerance), (sender, e) =>
                {
                    double distance = points[e.Id].DistanceToSquared(point);
                    if (distance < nearest || (distance == nearest && e.Id < index))
                    { index = e.Id; nearest = distance; }
                });
                if (index >= 0) return points[index];
                tree.Insert(point, points.Count);
                points.Add(point);
                return point;
            }
            foreach (var point in inputNodes) Resolve(point);
            var beams = inputBeams.Select(c => c.DuplicateCurve()).ToList();
            foreach (var beam in beams)
            {
                beam.SetStartPoint(Resolve(beam.PointAtStart));
                beam.SetEndPoint(Resolve(beam.PointAtEnd));
            }
            var meshes = inputMeshes.Select(m => m.DuplicateMesh()).ToList();
            foreach (var mesh in meshes)
            {
                mesh.Vertices.UseDoublePrecisionVertices = true;
                // Remap all vertices before changing topology, preserving every face's winding.
                for (int i = 0; i < mesh.Vertices.Count; i++)
                    mesh.Vertices.SetVertex(i, Resolve(mesh.Vertices.Point3dAt(i)));
                mesh.Vertices.CombineIdentical(true, true);
                mesh.Faces.CullDegenerateFaces();
                mesh.Vertices.CullUnused();
                mesh.Normals.ComputeNormals();
                mesh.Compact();
            }
            inputNodes = points;
            inputBeams = beams;
            inputMeshes = meshes;
        }

        /// <summary>
        /// Assign the elements id and creates the missing nodes
        /// </summary>
        /// <param name="nodes">The nodes list</param>
        /// <param name="beams">The beams list</param>
        /// <param name="meshes">The plates list</param>
        public static void MergeModel(ref List<NodeModel> nodes, ref List<BeamModel> beams, ref List<PlateModel> plates, double tolerance)
        {
            if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            // Clone the entire graph once to isolate inputs and preserve shared load cases.
            var graph = ModelArchive.Clone(nodes.Cast<ElementModel>().Concat(beams).Concat(plates).ToList());
            var sourceNodes = graph.Take(nodes.Count).Cast<NodeModel>().ToList();
            var sourceBeams = graph.Skip(nodes.Count).Take(beams.Count).Cast<BeamModel>().ToList();
            var sourcePlates = graph.Skip(nodes.Count + beams.Count).Cast<PlateModel>().ToList();
            List<NodeModel> orderedNodeList = new List<NodeModel>();
            using RTree rTree = new RTree();

            for (int i = 0; i < sourceNodes.Count; i++)
            {
                Point3d pt = sourceNodes[i].Position;
                bool found = false;
                int foundId = -1;
                double nearest = double.PositiveInfinity;
                rTree.Search(new Sphere(pt, tolerance), (sender, e) =>
                {
                    double distance = orderedNodeList[e.Id].Position.DistanceToSquared(pt);
                    if (distance < nearest || (distance == nearest && e.Id < foundId))
                    { found = true; foundId = e.Id; nearest = distance; }
                });

                if (found)
                {
                    var target = orderedNodeList[foundId];
                    var source = sourceNodes[i];
                    foreach (var group in source.Groups) if (!target.Groups.Contains(group)) target.Groups.Add(group);
                    // Equal-valued loads on distinct nodes are additive; only the same object is deduplicated.
                    foreach (var load in source.Loads) if (!target.Loads.Any(l => ReferenceEquals(l, load))) target.Loads.Add(load);
                    foreach (var attribute in source.Attributes)
                        if (!target.Attributes.Contains(attribute) && !(attribute is ElementIdAttributeModel && target.HasAttribute(typeof(ElementIdAttributeModel))))
                            target.Attributes.Add(attribute);
                    foreach (var result in source.Results) if (!target.Results.Contains(result)) target.Results.Add(result);
                }
                if (!found)
                {
                    orderedNodeList.Add(sourceNodes[i]);
                    rTree.Insert(pt, orderedNodeList.Count - 1);
                }
            }

            Point3d ResolvePoint(Point3d point)
            {
                int found = -1;
                double nearest = double.PositiveInfinity;
                rTree.Search(new Sphere(point, tolerance), (sender, e) =>
                {
                    double distance = orderedNodeList[e.Id].Position.DistanceToSquared(point);
                    if (distance < nearest || (distance == nearest && e.Id < found))
                    { found = e.Id; nearest = distance; }
                });
                if (found >= 0) return orderedNodeList[found].Position;
                orderedNodeList.Add(new NodeModel(point));
                rTree.Insert(point, orderedNodeList.Count - 1);
                return point;
            }

            foreach (var beam in sourceBeams)
            {
                beam.PointFrom = ResolvePoint(beam.PointFrom);
                beam.PointTo = ResolvePoint(beam.PointTo);
            }
            sourceBeams.RemoveAll(beam => beam.PointFrom == beam.PointTo);

            var mergedPlates = new List<PlateModel>();
            foreach (var plate in sourcePlates)
            {
                // FEM connectivity comes from Points, never from the extruded preview vertices.
                var points = plate.Points.Select(ResolvePoint).ToList();
                var distinct = points.Distinct().ToList();
                if (distinct.Count < 3) continue;
                if (distinct.Count != points.Count && points.Count > 4)
                    throw new InvalidOperationException("Il merge fa coincidere nodi di una piastra di ordine superiore: ridurre la tolleranza.");
                plate.Points = new System.Collections.ObjectModel.ObservableCollection<Point3d>(distinct);
                plate.CreateShapes();
                mergedPlates.Add(plate);
            }
            beams = sourceBeams;
            plates = mergedPlates;

            // Returns the new lists
            nodes = orderedNodeList;
        }

        public static List<SuperElementAttributeModel> GetSuperElements(ModelModel model)
        {
            List<SuperElementAttributeModel> superElements = new List<SuperElementAttributeModel>();
            superElements.AddRange(model.Elements.Where(e => e.HasAttribute(typeof(SuperElementAttributeModel)))
                .SelectMany(e => e.Attributes.Where(a => a.GetType() == typeof(SuperElementAttributeModel)))
                .Cast<SuperElementAttributeModel>()
                .Distinct());
            return superElements;
        }
    }
}

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
                .Concat(model.LoadCombinations.SelectMany(c => c.Values.Keys)).ToList();
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
            List<Point3d> orderedNodeList = new List<Point3d>();
            List<Point3d> nodes = new List<Point3d>();
            List<Curve> beams = new List<Curve>();
            List<Mesh> meshes = new List<Mesh>();

            for (int i = 0; i < inputNodes.Count; i++)
                nodes.Add(new Point3d(inputNodes[i]));
            for (int i = 0; i < inputBeams.Count; i++)
                beams.Add(inputBeams[i].DuplicateCurve());
            for (int i = 0; i < inputMeshes.Count; i++)
                meshes.Add(inputMeshes[i].DuplicateMesh());

            RTree rTree = new RTree();

            for (int i = 0; i < nodes.Count; i++)
            {
                Point3d pt = nodes[i];
                bool found = false;
                rTree.Search(new Sphere(pt, tolerance), new EventHandler<RTreeEventArgs>((sender, e) => { found = true; }));

                if (!found)
                {
                    orderedNodeList.Add(new Point3d(nodes[i]));
                    rTree.Insert(pt, orderedNodeList.Count - 1);
                }
            }

            for (int i = 0; i < beams.Count; i++)
            {
                var cv = beams[i];

                Point3d startPt = cv.PointAtStart;
                Point3d endPt = cv.PointAtEnd;

                bool found = false;
                int foundId = -1;
                rTree.Search(new Sphere(startPt, tolerance), new EventHandler<RTreeEventArgs>((sender, e) => { found = true; foundId = e.Id; }));

                if (found && foundId != -1)
                {
                    cv.SetStartPoint(orderedNodeList[foundId]);
                }
                else
                {
                    orderedNodeList.Add(new Point3d(startPt));
                    rTree.Insert(startPt, orderedNodeList.Count - 1);
                }

                found = false;
                foundId = -1;
                rTree.Search(new Sphere(endPt, tolerance), new EventHandler<RTreeEventArgs>((sender, e) => { found = true; foundId = e.Id; }));

                if (found && foundId != -1)
                {
                    cv.SetEndPoint(orderedNodeList[foundId]);
                }
                else
                {
                    orderedNodeList.Add(new Point3d(endPt));
                    rTree.Insert(endPt, orderedNodeList.Count - 1);
                }
            }

            for (int i = 0; i < meshes.Count; i++)
            {
                Mesh mesh = meshes[i];

                for (int j = 0; j < mesh.Faces.Count; j++)
                {
                    HashSet<Point3f> newVertices = new HashSet<Point3f>();
                    Point3d ptA = mesh.Vertices[mesh.Faces[j].A];
                    Point3d ptB = mesh.Vertices[mesh.Faces[j].B];
                    Point3d ptC = mesh.Vertices[mesh.Faces[j].C];
                    Point3d ptD = Point3d.Unset;
                    if (mesh.Faces[j].IsQuad)
                        ptD = mesh.Vertices[mesh.Faces[j].D];

                    bool found = false;
                    int foundId = -1;
                    rTree.Search(new Sphere(ptA, tolerance), new EventHandler<RTreeEventArgs>((sender, e) => { found = true; foundId = e.Id; }));

                    if (found && foundId != -1)
                    {
                        newVertices.Add((Point3f)orderedNodeList[foundId]);
                    }
                    else
                    {
                        newVertices.Add((Point3f)ptA);
                        orderedNodeList.Add(new Point3d(ptA));
                        rTree.Insert(ptA, orderedNodeList.Count - 1);
                    }

                    rTree.Search(new Sphere(ptB, tolerance), new EventHandler<RTreeEventArgs>((sender, e) => { found = true; foundId = e.Id; }));

                    if (found && foundId != -1)
                    {
                        newVertices.Add((Point3f)orderedNodeList[foundId]);
                    }
                    else
                    {
                        newVertices.Add((Point3f)ptB);
                        orderedNodeList.Add(new Point3d(ptB));
                        rTree.Insert(ptB, orderedNodeList.Count - 1);
                    }

                    rTree.Search(new Sphere(ptC, tolerance), new EventHandler<RTreeEventArgs>((sender, e) => { found = true; foundId = e.Id; }));

                    if (found && foundId != -1)
                    {
                        newVertices.Add((Point3f)orderedNodeList[foundId]);
                    }
                    else
                    {
                        newVertices.Add((Point3f)ptC);
                        orderedNodeList.Add(new Point3d(ptC));
                        rTree.Insert(ptC, orderedNodeList.Count - 1);
                    }

                    if (mesh.Faces[j].IsQuad)
                    {
                        rTree.Search(new Sphere(ptD, tolerance), new EventHandler<RTreeEventArgs>((sender, e) => { found = true; foundId = e.Id; }));

                        if (found && foundId != -1)
                        {
                            newVertices.Add((Point3f)orderedNodeList[foundId]);
                        }
                        else
                        {
                            newVertices.Add((Point3f)ptD);
                            orderedNodeList.Add(new Point3d(ptD));
                            rTree.Insert(ptD, orderedNodeList.Count - 1);
                        }
                    }

                    if (newVertices.Count > 2)
                    {
                        mesh.Vertices.Clear();
                        mesh.Vertices[mesh.Faces[j].A] = newVertices.ElementAt(0);
                        mesh.Vertices[mesh.Faces[j].B] = newVertices.ElementAt(1);
                        mesh.Vertices[mesh.Faces[j].C] = newVertices.ElementAt(2);
                        if (newVertices.Count > 3)
                            mesh.Vertices[mesh.Faces[j].D] = newVertices.ElementAt(3);
                    }
                    else
                    {
                        mesh.Faces.RemoveAt(j);
                        j--;
                    }
                }
            }

            // Returns the new lists
            inputNodes = orderedNodeList;
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
            // Clone the node graph once: shared load cases stay shared inside the result.
            var sourceNodes = ModelArchive.Clone(nodes);
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

            for (int i = 0; i < beams.Count; i++)
            {
                var cv = beams[i];

                Point3d startPt = cv.PointFrom;
                Point3d endPt = cv.PointTo;

                bool found = false;
                int foundId = -1;
                rTree.Search(new Sphere(startPt, tolerance), new EventHandler<RTreeEventArgs>((sender, e) => { found = true; foundId = e.Id; }));

                if (found && foundId != -1)
                {
                    cv.PointFrom = orderedNodeList[foundId].Position;
                }
                else
                {
                    orderedNodeList.Add(new NodeModel(startPt));
                    rTree.Insert(startPt, orderedNodeList.Count - 1);
                }

                found = false;
                foundId = -1;
                rTree.Search(new Sphere(endPt, tolerance), new EventHandler<RTreeEventArgs>((sender, e) => { found = true; foundId = e.Id; }));

                if (found && foundId != -1)
                {
                    cv.PointTo = orderedNodeList[foundId].Position;
                }
                else
                {
                    orderedNodeList.Add(new NodeModel(endPt));
                    rTree.Insert(endPt, orderedNodeList.Count - 1);
                }

                if (cv.PointFrom == cv.PointTo)
                {
                    beams.RemoveAt(i);
                    i--;
                }
            }

            for (int i = 0; i < plates.Count; i++)
            {
                PlateModel brep = plates[i];

                HashSet<Point3f> newVertices = new HashSet<Point3f>();
                for (int j = 0; j < brep.Shape.Vertices.Count; j++)
                {
                    BrepVertex vertice = brep.Shape.Vertices[j];

                    bool found = false;
                    int foundId = -1;
                    rTree.Search(new Sphere(vertice.Location, tolerance), new EventHandler<RTreeEventArgs>((sender, e) => { found = true; foundId = e.Id; }));

                    if (found && foundId != -1)
                    {
                        newVertices.Add((Point3f)orderedNodeList[foundId].Position);
                    }
                    else
                    {
                        newVertices.Add((Point3f)vertice.Location);
                        orderedNodeList.Add(new NodeModel(new Point3d(vertice.Location)));
                        rTree.Insert(vertice.Location, orderedNodeList.Count - 1);
                    }
                }

                if (newVertices.Count > 2)
                {
                    for (int j = 0; j < newVertices.Count; j++)
                    {
                        brep.Shape.Vertices[j].Location = newVertices.ElementAt(j);
                    }
                    if (brep.Shape.Vertices.Count > newVertices.Count)
                    {
                        for (int j = newVertices.Count; j < newVertices.Count; j++)
                        {
                            brep.Shape.Vertices[j].Location = newVertices.Last();
                        }
                    }
                    brep.Shape.CullUnusedVertices();
                }
                else
                {
                    plates.RemoveAt(i);
                    i--;
                }
            }

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

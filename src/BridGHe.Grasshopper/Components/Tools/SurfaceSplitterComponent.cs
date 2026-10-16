using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Grasshopper.Components.Tools
{
    public class SurfaceSplitterComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the SuperElementComponent class.
        /// </summary>
        public SurfaceSplitterComponent()
          : base("Surface splitter curves", "SSC", "Get surface splitter curves based on internal points/lines", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_TOOLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Surface", "S", "Surface to split", GH_ParamAccess.item);
            pManager.AddPointParameter("Points", "Ps", "Points internal to surface", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddLineParameter("Lines", "Ls", "Lines internal to surface", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Tolerance", "T", "On border tolerance", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Cuttings", "Cs", "The cutting curves", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep surface = null;
            List<Point3d> points = new List<Point3d>();
            List<Line> lines = new List<Line>();
            double toll = 0;
            if (!DA.GetData(0, ref surface))
                return;
            DA.GetDataList(1, points);
            DA.GetDataList(2, lines);
            if (!DA.GetData(3, ref toll))
                return;

            try
            {
                if (surface.Faces.Count != 1)
                {
                    throw new NotSupportedException("Brep composed by more than 1 face not admitted");
                }
                BrepFace bf = surface.Faces[0];
                if (!bf.TryGetPlane(out Plane plane))
                {
                    throw new NotSupportedException("Brep composed by more than 1 face not admitted");
                }

                Transform toPlane = Transform.PlaneToPlane(plane, Plane.WorldXY);
                List<Edge> edges = new List<Edge>();

                if (!bf.OuterLoop.To3dCurve().TryGetPolyline(out Polyline polyline))
                {
                    throw new NotSupportedException("Brep exteral/internal perimeter must be polylines");
                }

                if (!polyline.IsClosed)
                    polyline.Add(polyline[0]);
                Point2d nextP = Point2d.Unset;
                polyline.Transform(toPlane);

                //see area sign
                GPC.Geometry.Polygon2d poly = new GPC.Geometry.Polygon2d();
                for (int i = 0; i < polyline.Count; i++)
                {
                    Point3d p = polyline[i];
                    poly.Add(p.X, p.Y);
                }
                if (!poly.IsRightHandOrdered())
                {
                    //outer, must be right oriented (left orto internal)
                    polyline.Reverse();
                }

                for (int i = 0; i < polyline.Count - 1; i++)
                {
                    Point2d p;
                    if (nextP == Point2d.Unset)
                    {
                        p = new Point2d(polyline[i].X, polyline[i].Y);
                    }
                    else
                    {
                        p = nextP;
                    }
                    nextP = new Point2d(polyline[i + 1].X, polyline[i + 1].Y);
                    edges.Add(new Edge(p, nextP));
                }
                //adding internal nodes
                List<Node> nodes = new List<Node>();
                for (int i = 0; i < points.Count; i++)
                {
                    Point3d planed = new Point3d(points[i]);
                    planed.Transform(toPlane);
                    Point2d point = new Point2d(planed.X, planed.Y);
                    nodes.Add(new Node(point, edges));
                }

                //adding internal lines nodes
                for (int i = 0; i < lines.Count; i++)
                {
                    Line l = lines[i];
                    Point3d planedFrom = new Point3d(l.From);
                    Point3d planedTo = new Point3d(l.To);
                    planedFrom.Transform(toPlane);
                    planedTo.Transform(toPlane);
                    Point2d pointFrom = new Point2d(planedFrom.X, planedFrom.Y);
                    Point2d pointTo = new Point2d(planedTo.X, planedTo.Y);
                    Node nodeFrom = new Node(pointFrom, edges);
                    Node nodeTo = new Node(pointTo, edges);
                    nodeFrom.ForcedNext = nodeTo;
                    nodeTo.ForcedNext = nodeFrom;
                    nodes.Add(nodeFrom);
                    nodes.Add(nodeTo);
                }

                List<KeyValuePair<double, Node>> sortedNodes = new List<KeyValuePair<double, Node>>();
                for (int i = 0; i < nodes.Count; i++)
                {
                    sortedNodes.Add(new KeyValuePair<double, Node>(nodes[i].NextEdges[0].Key, nodes[i]));
                }

                sortedNodes.Sort(new KvpComparer());

                double squareTol = GPC.Utilities.Maths.ErrorPropagation.DefaultProductSquareTolerance(toll);
                List<Polyline> sequences = new List<Polyline>();
                do
                {
                    Node last = null;
                    if (sortedNodes.Count == 0)
                        break;

                    last = sortedNodes[0].Value;
                    sortedNodes.RemoveAt(0);
                    if (last == null)
                        break;

                    List<Node> sequence = new List<Node> { last };

                    do
                    {
                        Node nearest = null;
                        if (last.ForcedNext == null)
                        {
                            //finding nearest nodes that do not cut edges
                            double minDistance = double.MaxValue;
                            for (int i = 0; i < sortedNodes.Count; i++)
                            {
                                Node other = sortedNodes[i].Value;
                                if (other != null && other != last && !last.IntersectEdge(edges, other))
                                {
                                    bool add;
                                    double dist = (other.Pos - last.Pos).Length;
                                    //see if have a good angle
                                    if (sequence.Count < 2)
                                    {
                                        add = true;
                                    }
                                    else
                                    {
                                        add = IsGoodAngle(sequence[sequence.Count - 2].Pos, sequence[sequence.Count - 1].Pos, other.Pos);
                                    }
                                    if (add)
                                    {
                                        //see if intersect something other
                                        for (int j = 0; j < sequence.Count - 2; j++)
                                        {
                                            if (IsSegmentiIntersecanti(sequence[j].Pos, sequence[j + 1].Pos, sequence[sequence.Count - 1].Pos, other.Pos))
                                            {
                                                add = false;
                                                break;
                                            }
                                        }
                                        if (add && (nearest == null || minDistance > dist))
                                        {
                                            nearest = other;
                                            minDistance = dist;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (sequence.Count < 2 || last.ForcedNext != sequence[sequence.Count - 2]) nearest = last.ForcedNext;
                        }

                        if (nearest == null)
                        {
                            //ended sequence
                            Polyline seq = new Polyline();
                            //start point
                            Point2d pointOnFirstEdge = Point2d.Unset;
                            for (int i = 0; i < sequence[0].NextEdges.Count; i++)
                            {
                                pointOnFirstEdge = sequence[0].NextEdges[i].Value.Key;
                                if ((pointOnFirstEdge - sequence[0].Pos).SquareLength > squareTol)
                                {
                                    bool add = true;
                                    if (sequence.Count > 1)
                                    {
                                        add = IsGoodAngle(pointOnFirstEdge, sequence[0].Pos, sequence[1].Pos);
                                    }
                                    if (add)
                                    {
                                        seq.Add(pointOnFirstEdge.X, pointOnFirstEdge.Y, 0);
                                        break;
                                    }
                                }
                                else
                                {
                                    //no need to add
                                    pointOnFirstEdge = sequence[0].Pos;
                                    break;
                                }
                            }
                            for (int i = 0; i < sequence.Count; i++)
                            {
                                seq.Add(sequence[i].Pos.X, sequence[i].Pos.Y, 0);
                            }

                            for (int i = 0; i < sequence[sequence.Count - 1].NextEdges.Count; i++)
                            {
                                Point2d pointOnLastEdge = sequence[sequence.Count - 1].NextEdges[i].Value.Key;
                                if ((pointOnLastEdge - sequence[sequence.Count - 1].Pos).SquareLength > squareTol)
                                {
                                    if ((pointOnLastEdge - pointOnFirstEdge).SquareLength > squareTol)
                                    {
                                        bool add = true;
                                        if (sequence.Count > 1)
                                        {
                                            add = IsGoodAngle(sequence[sequence.Count - 2].Pos, sequence[sequence.Count - 1].Pos, pointOnLastEdge);
                                        }
                                        if (add)
                                        {
                                            seq.Add(pointOnLastEdge.X, pointOnLastEdge.Y, 0);
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    //no eed to add
                                    break;
                                }
                            }
                            sequences.Add(seq);
                            break;
                        }
                        else
                        {
                            sequence.Add(nearest);
                            last = nearest;
                            for (int i = sortedNodes.Count - 1; i > -1; i--)
                            {
                                if (sortedNodes[i].Value == nearest)
                                {
                                    sortedNodes.RemoveAt(i);
                                    break;
                                }
                            }

                            //see if the node intersect other sequences
                            bool endSequence = false;
                            for (int j = 0; j < sequences.Count; j++)
                            {
                                Polyline seq = sequences[j];
                                for (int i = 0; i < seq.Count - 1; i++)
                                {
                                    Point2d a0 = new Point2d(seq[i].X, seq[i].Y);
                                    Point2d a1 = new Point2d(seq[i + 1].X, seq[i + 1].Y);
                                    Vector2d dir = a1 - a0;
                                    double length = dir.Length;
                                    dir.Unitize();
                                    Point2d onLineP = GetNearest(last.Pos, a0, a1, dir, length);

                                    if ((onLineP - last.Pos).SquareLength < squareTol)
                                    {
                                        endSequence = true;
                                        break;
                                    }
                                }
                                if (endSequence) break;
                            }
                            if (endSequence)
                            {
                                //ended sequence
                                Polyline seq = new Polyline();
                                //end the sequence because touch other nodes. sequence interrupt here
                                //start point
                                Point2d pointOnFirstEdge = sequence[0].NextEdges[0].Value.Key;
                                if ((pointOnFirstEdge - sequence[0].Pos).SquareLength > squareTol)
                                {
                                    seq.Add(pointOnFirstEdge.X, pointOnFirstEdge.Y, 0);
                                }
                                for (int i = 0; i < sequence.Count; i++)
                                {
                                    seq.Add(sequence[i].Pos.X, sequence[i].Pos.Y, 0);
                                }
                                sequences.Add(seq);
                                break;
                            }
                        }

                    } while (true == true);

                } while (true == true);

                Transform inverse = Transform.PlaneToPlane(Plane.WorldXY, plane);
                List<PolylineCurve> curves = new List<PolylineCurve>();
                for (int i = 0; i < sequences.Count; i++)
                {
                    Polyline pl = sequences[i];
                    Polyline plT = new Polyline(pl);
                    plT.Transform(inverse);
                    curves.Add(new PolylineCurve(plT));
                }

                DA.SetDataList(0, curves);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

        }

        private class KvpComparer : IComparer<KeyValuePair<double, Node>>
        {
            public int Compare(KeyValuePair<double, Node> x, KeyValuePair<double, Node> y)
            {
                return x.Key.CompareTo(y.Key);
            }
        }

        private static bool IsSegmentiIntersecanti(Point2d a0, Point2d a1, Point2d b0, Point2d b1)
        {
            Vector2d a0a1 = a1 - a0;
            Vector2d a0b1 = b1 - a0;
            Vector2d a0b0 = b0 - a0;
            if (GetVectZ(a0a1, a0b1) * GetVectZ(a0a1, a0b0) < 0)
            {
                Vector2d b0b1, b0a1, b0a0;

                b0b1 = b1 - b0;
                b0a1 = a1 - b0;
                b0a0 = a0 - b0;
                return GetVectZ(b0b1, b0a1) * GetVectZ(b0b1, b0a0) < 0;
            }
            else
            {
                return false;
            }
        }

        private static double GetVectZ(Vector2d first, Vector2d second)
        {
            return first.X * second.Y - first.Y * second.X;
        }

        private static bool IsGoodAngle(Point2d p0, Point2d p1, Point2d p2)
        {
            Vector2d lastDir = p1 - p0;
            lastDir.Unitize();
            Vector2d nextDir = p2 - p1;
            nextDir.Unitize();
            double scalar = lastDir * nextDir;
            return scalar > -0.7 && scalar < 0.95;
        }

        private static Point2d GetNearest(Point2d p, Point2d a0, Point2d a1, Vector2d direction, double length)
        {
            double position = (p - a0) * direction;
            Point2d result;
            if (position > 0 && position < length)
            {
                //internal
                result = a0 + position * direction;
            }
            else if (position <= 0)
            {
                result = a0;
            }
            else
            {
                result = a1;
            }
            return result;
        }

        private class Edge
        {
            public readonly Point2d IniP;
            public readonly Point2d EndP;
            public readonly Vector2d Direction;
            public readonly Vector2d InternalOrto;//internal direction always in left side
            public readonly double Length;

            public Edge(Point2d iniP, Point2d endP)
            {
                IniP = iniP;
                EndP = endP;
                Direction = EndP - IniP;
                Direction.Unitize();
                InternalOrto = new Vector2d(-Direction.Y, Direction.X);
                Length = (EndP - IniP).Length;
            }
        }

        private class Node
        {
            public readonly Point2d Pos;
            public readonly List<KeyValuePair<double, KeyValuePair<Point2d, Edge>>> NextEdges;
            public Node ForcedNext;

            public Node(Point2d pos, IEnumerable<Edge> edges)
            {
                Pos = pos;
                NextEdges = null;
                ForcedNext = null;
                Edge[] array = edges.ToArray();
                for (int i = 0; i < array.Length; i++)
                {
                    Edge e = array[i];
                    Point2d nearestP = GetNearest(pos, e.IniP, e.EndP, e.Direction, e.Length);

                    //try find intersection with oter edges
                    bool intersect = false;
                    for (int j = 0; j < array.Length; j++)
                    {
                        Edge other = array[j];
                        if (other != e)
                        {
                            if (IsSegmentiIntersecanti(pos, nearestP, other.IniP, other.EndP))
                            {
                                intersect = true;
                                break;
                            }
                        }

                    }
                    if (!intersect)
                    {
                        if (NextEdges == null)
                            NextEdges = new List<KeyValuePair<double, KeyValuePair<Point2d, Edge>>>();
                        NextEdges.Add(new KeyValuePair<double, KeyValuePair<Point2d, Edge>>((nearestP - pos).Length, new KeyValuePair<Point2d, Edge>(nearestP, e)));
                    }
                }
                if (NextEdges == null)
                    throw new NotSupportedException("Point without next edges");
                NextEdges.Sort(new KvpComparer());
            }

            public bool IntersectEdge(List<Edge> edges, Node other)
            {
                for (int i = 0; i < edges.Count; i++)
                {
                    if (IsSegmentiIntersecanti(Pos, other.Pos, edges[i].IniP, edges[i].EndP))
                    {
                        return true;
                    }
                }
                return false;
            }

            private class KvpComparer : IComparer<KeyValuePair<double, KeyValuePair<Point2d, Edge>>>
            {
                public int Compare(KeyValuePair<double, KeyValuePair<Point2d, Edge>> x, KeyValuePair<double, KeyValuePair<Point2d, Edge>> y)
                {
                    return x.Key.CompareTo(y.Key);
                }
            }

        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("97bab293-1482-420d-913e-fdf7b9204cae");
    }
}
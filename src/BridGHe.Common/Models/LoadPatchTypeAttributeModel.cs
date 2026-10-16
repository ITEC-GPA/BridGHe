using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using Rhino.Display;
using Rhino.Geometry;
using BridGHe.Common.Helpers;
using System.Collections.Generic;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class LoadPatchTypeAttributeModel : AttributeModel<LoadPatchTypeAttributeModel.AttributeData>,
        IEquatable<LoadPatchTypeAttributeModel>
    {
        public enum PatchType
        {
            Auto1,
            Auto2,
            Auto3,
            Auto4,
            [Description("Angle Split")]
            AngleSplit,
            Manual
        }

        [System.Serializable]
        public class AttributeData : IEquatable<AttributeData>
        {
            public PatchType PatchType { get; set; }
            public int[] SelectedEdges { get; set; }
            public double[] Weights { get; set; }

            public AttributeData()
            {
                SelectedEdges = new int[4];
                Weights = new double[4];
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as AttributeData);
            }

            public bool Equals(AttributeData other)
            {
                return other != null && PatchType == other.PatchType &&
                    EqualityComparer<int[]>.Default.Equals(SelectedEdges, other.SelectedEdges) &&
                    EqualityComparer<double[]>.Default.Equals(Weights, other.Weights);
            }

            public override int GetHashCode()
            {
                var hashCode = 1130288925;
                hashCode = hashCode * -1521134295 + PatchType.GetHashCode();
                hashCode = hashCode * -1521134295 + EqualityComparer<int[]>.Default.GetHashCode(SelectedEdges);
                hashCode = hashCode * -1521134295 + EqualityComparer<double[]>.Default.GetHashCode(Weights);
                return hashCode;
            }
        }

        protected static bool _isVisible = true;
        protected static double _scale = 1;
        protected static Color _color = Color.IndianRed;

        public static bool IsVisible
        {
            get => _isVisible;
            set => _isVisible = value;
        }

        public static double Scale
        {
            get => _scale;
            set => _scale = value;
        }

        public static Color Color
        {
            get => _color;
            set => _color = value;
        }

        public override string Description => "Load Patch Type";

        public LoadPatchTypeAttributeModel()
        {
        }

        public LoadPatchTypeAttributeModel(AttributeData value)
            : base(value)
        {
        }

        public LoadPatchTypeAttributeModel(LoadPatchTypeAttributeModel attr)
        {
            _value = new AttributeData
            {
                PatchType = attr.Value.PatchType
            };
            for (int i = 0; i < _value.SelectedEdges.Length; i++)
                _value.SelectedEdges[i] = attr.Value.SelectedEdges[i];
            for (int i = 0; i < _value.Weights.Length; i++)
                _value.Weights[i] = attr.Value.Weights[i];
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "LoadPatchTypeAttributeModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected LoadPatchTypeAttributeModel(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            int savedRelease;
            try
            {
                savedRelease = info.GetInt32(SERIALIZATION_RELEASE_NAME);
            }
            catch
            {
                savedRelease = 0;
            }
            _value = new AttributeData
            {
                PatchType = (PatchType)info.GetValue("PatchType", typeof(PatchType))
            };
            for (int i = 0; i < _value.SelectedEdges.Length; i++)
                _value.SelectedEdges[i] = info.GetInt32($"SelectedEdges{i}");
            for (int i = 0; i < _value.Weights.Length; i++)
                _value.Weights[i] = info.GetDouble($"Weights{i}");
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("PatchType", _value.PatchType, typeof(PatchType));
            for (int i = 0; i < _value.SelectedEdges.Length; i++)
                info.AddValue($"SelectedEdges{i}", _value.SelectedEdges[i]);
            for (int i = 0; i < _value.Weights.Length; i++)
                info.AddValue($"Weights{i}", _value.Weights[i]);
        }

        #endregion

        public override void Draw(DisplayPipeline display, RhinoViewport viewport, Color color, ElementModel element)
        {
            base.Draw(display, viewport, color, element);

            if (!IsVisible)
                return;

            PlateModel plate = (PlateModel)element;
            if (plate != null)
            {
                Point3d target = display.Viewport.CameraTarget;
                double scale = DrawingHelper.GetScaleFactor(display.Viewport, target);

                Polyline pl = new Polyline(plate.Points);
                pl.Add(plate.Points[0]);
                pl.Transform(Transform.Scale(pl.CenterPoint(), 0.85 * Scale));

                switch (_value.PatchType)
                {
                    case PatchType.Auto1:
                        for (int i = 0; i < pl.SegmentCount; i++)
                        {
                            if (_value.SelectedEdges[0] == i + 1)
                                continue;
                            display.DrawLine(pl[i], pl[i + 1], Color);
                        }
                        break;
                    case PatchType.Auto2:
                        {
                            Line l;
                            int i2;
                            switch (plate.Type)
                            {
                                case PlateModel.PlateType.Tri3:
                                    l = new Line(pl[_value.SelectedEdges[0] - 1], pl[_value.SelectedEdges[0]]);
                                    i2 = _value.SelectedEdges[0] + 1;
                                    if (i2 >= plate.Points.Count)
                                        i2 = 1;
                                    display.DrawLine(l, Color);
                                    display.DrawLine(l.PointAt(0.5), pl[i2], Color);
                                    break;
                                case PlateModel.PlateType.Quad4:
                                    l = new Line(pl[_value.SelectedEdges[0] - 1], pl[_value.SelectedEdges[0]]);
                                    i2 = _value.SelectedEdges[0] + 2;
                                    if (i2 > plate.Points.Count)
                                        i2 -= plate.Points.Count;
                                    Line l2 = new Line(pl[i2 - 1], pl[i2]);
                                    display.DrawLine(l, Color);
                                    display.DrawLine(l2, Color);
                                    display.DrawLine(l.PointAt(0.5), l2.PointAt(0.5), Color);
                                    break;
                            }
                        }
                        break;
                    case PatchType.Auto3:
                        {
                            int i2 = _value.SelectedEdges[0] + 2;
                            if (i2 > plate.Points.Count)
                                i2 -= plate.Points.Count;
                            Line l1 = new Line(pl[i2 - 1], pl[i2]);
                            Vector3d v = pl[_value.SelectedEdges[0]] - pl[_value.SelectedEdges[0] - 1];
                            Plane p = new Plane(pl[1], pl[0], pl[2]);
                            Vector3d vp = Vector3d.CrossProduct(v, p.Normal);
                            Point3d pt = pl[_value.SelectedEdges[0] - 1] + v * 0.5 + vp * 0.5;
                            display.DrawLine(pl[_value.SelectedEdges[0] - 1], pt, Color);
                            display.DrawLine(pl[_value.SelectedEdges[0]], pt, Color);
                            display.DrawLine(l1, Color);
                            display.DrawLine(pt, l1.PointAt(0.5), Color);
                        }
                        break;
                    case PatchType.Auto4:
                        {
                            switch (plate.Type)
                            {
                                case PlateModel.PlateType.Tri3:
                                    display.DrawLine(pl.CenterPoint(), pl[0], Color);
                                    display.DrawLine(pl.CenterPoint(), pl[1], Color);
                                    display.DrawLine(pl.CenterPoint(), pl[2], Color);
                                    break;
                                case PlateModel.PlateType.Quad4:
                                    int i1 = 1;
                                    if (pl[0].DistanceTo(pl[1]) > pl[1].DistanceTo(pl[2]))
                                        i1++;
                                    int i2 = i1 + 2;
                                    Vector3d v1 = pl[i1] - pl[i1 - 1];
                                    Vector3d v2 = pl[i2] - pl[i2 - 1];
                                    Plane p = new Plane(pl[1], pl[0], pl[2]);
                                    Vector3d vp1 = Vector3d.CrossProduct(v1, p.Normal);
                                    Vector3d vp2 = Vector3d.CrossProduct(v2, p.Normal);
                                    Point3d pt1 = pl[i1 - 1] + v1 * 0.5 + vp1 * 0.5;
                                    Point3d pt2 = pl[i2 - 1] + v2 * 0.5 + vp2 * 0.5;
                                    display.DrawLine(pl[i1 - 1], pt1, Color);
                                    display.DrawLine(pl[i1], pt1, Color);
                                    display.DrawLine(pl[i2], pt2, Color);
                                    display.DrawLine(pl[i2 - 1], pt2, Color);
                                    display.DrawLine(pt1, pt2, Color);
                                    break;
                            }
                        }
                        break;
                    case PatchType.AngleSplit:
                        {
                            int[] edges = new[] { 1, 2, 3, 4 };
                            int[] sel_edges = new[] { _value.SelectedEdges[0], _value.SelectedEdges[1] };
                            int[] unsel_edges = edges.Except(sel_edges).ToArray();

                            Point3d[][] sel_points = new[] {
                                new[] { pl[sel_edges[0] - 1], pl[sel_edges[0]] },
                                new[] { pl[sel_edges[1] - 1], pl[sel_edges[1]] }
                            };
                            Point3d pt1 = sel_points[0].Intersect(sel_points[1]).First();
                            Point3d[][] unsel_points = new[] {
                                new[] { pl[unsel_edges[0] - 1], pl[unsel_edges[0]] },
                                new[] { pl[unsel_edges[1] - 1], pl[unsel_edges[1]] }
                            };
                            Point3d pt2 = unsel_points[0].Intersect(unsel_points[1]).First();
                            display.DrawLine(pt1, pt2, Color);
                            display.DrawLine(unsel_points[0][0], unsel_points[0][1], Color);
                            display.DrawLine(unsel_points[1][0], unsel_points[1][1], Color);
                        }
                        break;
                    case PatchType.Manual:
                        for (int i = 0; i < _value.SelectedEdges.Length; i++)
                        {
                            Line l = new Line(pl[_value.SelectedEdges[i] - 1], pl[_value.SelectedEdges[i]]);
                            Plane p = new Plane(plate.Points[1], plate.Points[0], plate.Points[2]);
                            Vector3d v = pl[_value.SelectedEdges[i]] - pl[_value.SelectedEdges[i] - 1];
                            Vector3d vp = Vector3d.CrossProduct(v, p.Normal);
                            vp.Unitize();
                            l.Transform(Transform.Translation(vp * v.Length * _value.Weights[i] / 50));
                            display.DrawLine(l, Color);
                        }
                        break;
                }
            }
        }

        public static bool operator ==(LoadPatchTypeAttributeModel m1, LoadPatchTypeAttributeModel m2)
        {
            if (ReferenceEquals(m1, m2))
                return true;
            return !(m1 is null) && m1.Equals(m2);
        }

        public static bool operator !=(LoadPatchTypeAttributeModel m1, LoadPatchTypeAttributeModel m2)
        {
            return !(m1 == m2);
        }

        public bool Equals(LoadPatchTypeAttributeModel other)
        {
            return other != null && Value.Equals(Value);
        }

        public override bool Equals(object obj)
        {
            if (obj is LoadPatchTypeAttributeModel model)
                return Equals(model);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            var hashCode = 64919;
            hashCode = hashCode * 87931 + Value.GetHashCode();
            return hashCode;
        }

        public static AttributeData GetLoadPatch(PlateModel plate)
        {
            if (plate.HasAttribute(typeof(LoadPatchTypeAttributeModel)))
            {
                return plate.Attributes.Where(n => n.GetType() == typeof(LoadPatchTypeAttributeModel)).
                    Cast<LoadPatchTypeAttributeModel>().First().Value;
            }
            return null;
        }
    }
}

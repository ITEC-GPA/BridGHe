using BridGHe.Common.Helpers;
using Rhino.Display;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Serialization;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class CompositeBeamModel : ElementModel
    {
        protected static bool _isAxisVisible = false;
        protected static double _axisScale = 1;
        protected string _beamId;
        protected Point3d _pointFrom;
        protected Point3d _pointTo;
        protected CompositeBeamPropertyModel _startBeamProperty;
        protected CompositeBeamPropertyModel _endBeamProperty;
        protected List<Brep> _shapes;

        public double Length => (_pointTo - _pointFrom).Length;

        public static bool IsAxisVisible
        {
            get => _isAxisVisible;
            set => _isAxisVisible = value;
        }

        public static double AxisScale
        {
            get => _axisScale;
            set => _axisScale = value;
        }

        public string BeamId
        {
            get => _beamId;
            set => _beamId = value;
        }

        public Point3d PointFrom
        {
            get => _pointFrom;
            set
            {
                _pointFrom = value;
                CreateShapes();
            }
        }

        public Point3d PointTo
        {
            get => _pointTo;
            set
            {
                _pointTo = value;
                CreateShapes();
            }
        }

        public CompositeBeamPropertyModel StartBeamProperty
        {
            get => _startBeamProperty;
            set
            {
                _startBeamProperty = value;
                CreateShapes();
            }
        }

        public CompositeBeamPropertyModel EndBeamProperty
        {
            get => _endBeamProperty;
            set
            {
                _endBeamProperty = value;
                CreateShapes();
            }
        }

        public Brep[] Shapes => _shapes.ToArray();

        public bool IsValid
        {
            get
            {
                return _pointFrom != Point3d.Unset && _pointTo != Point3d.Unset &&
                    _pointFrom.DistanceTo(_pointTo) > 0.0 && _shapes != null && _shapes.Count > 0;
            }
        }

        public bool IsVertical
        {
            get
            {
                Vector3d v = ToVector();
                v.Unitize();
                return Vector3d.CrossProduct(Vector3d.ZAxis, v).Length < 1.0E-12;
            }
        }

        public CompositeBeamModel(Point3d pointFrom, Point3d pointTo, CompositeBeamPropertyModel startBeamprop, GroupModel[] grp = null, CompositeBeamPropertyModel endBeamPro = null)
            : base()
        {
            _beamId = "";
            _pointFrom = pointFrom;
            _pointTo = pointTo;
            _startBeamProperty = startBeamprop;
            if (endBeamPro == null)
                _endBeamProperty = startBeamprop;
            else
                _endBeamProperty = endBeamPro;
            if (grp != null)
                for (int i = 0; i < grp.Length; i++)
                    _groups.Add(grp[i]);
            _shapes = new List<Brep>();

            _startBeamProperty.SteelSection.CalculateSection();
            _endBeamProperty.SteelSection.CalculateSection();

            CreateShapes();
        }

        public CompositeBeamModel()
            : base()
        {
            _beamId = "";
            _pointFrom = Point3d.Unset;
            _pointTo = Point3d.Unset;
            _endBeamProperty = null;
            _startBeamProperty = null;
            _shapes = new List<Brep>();
        }

        public CompositeBeamModel(CompositeBeamModel beam)
            : base(beam)
        {
            _beamId = beam._beamId;
            _pointFrom = beam._pointFrom;
            _pointTo = beam._pointTo;
            var properties = Helpers.ModelArchive.Clone(new[] { beam._startBeamProperty, beam._endBeamProperty });
            _startBeamProperty = properties[0];
            _endBeamProperty = properties[1];
            _shapes = new List<Brep>();
            CreateShapes();
        }

        #region Serialization

        private readonly string SERIALIZATION_RELEASE_NAME = "CompositeBeamModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private readonly int SERIALIZATION_RELEASE = 1;

        protected CompositeBeamModel(SerializationInfo info, StreamingContext context)
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
            _beamId = info.GetString("BeamId");
            _pointFrom = (Point3d)info.GetValue("PointFrom", typeof(Point3d));
            _pointTo = (Point3d)info.GetValue("PointTo", typeof(Point3d));
            _startBeamProperty = (CompositeBeamPropertyModel)info.GetValue("_startBeamProperty", typeof(CompositeBeamPropertyModel));
            _endBeamProperty = (CompositeBeamPropertyModel)info.GetValue("_endBeamProperty", typeof(CompositeBeamPropertyModel));
            _shapes = new List<Brep>();
            CreateShapes();
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("BeamId", _beamId);
            info.AddValue("PointFrom", _pointFrom, typeof(Point3d));
            info.AddValue("PointTo", _pointTo, typeof(Point3d));
            info.AddValue("_endBeamProperty", _endBeamProperty, typeof(CompositeBeamPropertyModel));
            info.AddValue("_startBeamProperty", _startBeamProperty, typeof(CompositeBeamPropertyModel));
        }

        #endregion

        public Vector3d ToVector()
        {
            return _pointTo - _pointFrom;
        }


        public override void DrawWireframe(DisplayPipeline display, RhinoViewport viewport, Color color)
        {
            display.DrawLine(_pointFrom, _pointTo, color);
        }

        public override void DrawSolid(DisplayPipeline display, RhinoViewport viewport, Color color)
        {
            if (_shapes != null)
            {
                foreach (Brep shape in _shapes)
                    display.DrawBrepWires(shape, color);
            }
        }

        public override void DrawAttributes(DisplayPipeline display, RhinoViewport viewport)
        {
            base.DrawAttributes(display, viewport);
            if (IsAxisVisible)
            {
                double length = (_pointTo - _pointFrom).Length;
                GetLocalAxes(out Vector3d axes1, out Vector3d axes2, out Vector3d axes3, out Transform toGlobal);
                Point3d origin = new Point3d(0, 0, 0.5 * length);
                origin.Transform(toGlobal);

                double dimension = length * 0.1 * AxisScale;
                display.DrawArrow(new Line(origin, origin + axes1 * dimension), Color.Red, 0, DrawingHelper.ArrowSize);
                display.DrawArrow(new Line(origin, origin + axes2 * dimension), Color.Green, 0, DrawingHelper.ArrowSize);
                display.DrawArrow(new Line(origin, origin + axes3 * dimension), Color.Blue, 0, DrawingHelper.ArrowSize);
            }
        }

        public override void CreateShapes()
        {
            if (_startBeamProperty == null || _endBeamProperty == null)
            {
                _shapes = new List<Brep>();
                return;
            }
            var shapes = new List<Brep>();
            List<Brep> surfSteel = new List<Brep>();
            List<Brep> surfConcrete = new List<Brep>();
            double tol = 0.001;

            Point2d c = _startBeamProperty.SteelSection.Centroid;
            Point2d c2 = _endBeamProperty.SteelSection.Centroid;

            SetSurfaceSteel(_startBeamProperty, c, ref surfSteel, tol);
            SetSurfaceSteel(_endBeamProperty, new Point2d(c2.X, c2.Y), ref surfSteel, tol);

            if (surfSteel.Count > 0)
            {
                AddBrep(surfSteel, shapes);
            }

            SetSurfaceConcrete(_startBeamProperty, c, ref surfConcrete, tol);
            SetSurfaceConcrete(_endBeamProperty, new Point2d(c2.X, c2.Y), ref surfConcrete, tol);

            if (surfConcrete.Count > 0)
            {
                AddBrep(surfConcrete, shapes);
            }
            _shapes = shapes;
        }

        public void GetLocalAxes(out Vector3d axes1, out Vector3d axes2, out Vector3d axes3, out Transform toGlobal)
        {
            double r = 0 / 180 * Math.PI;
            axes3 = _pointTo - _pointFrom;
            axes3.Unitize();
            if (!IsVertical) // Not vertical
            {
                Vector3d v_xy = new Vector3d(axes3.X, axes3.Y, 0);
                v_xy.Unitize();

                axes1 = Vector3d.CrossProduct(Vector3d.ZAxis, axes3);
                axes1.Unitize();
                axes2 = Vector3d.CrossProduct(axes3, axes1);
                axes2.Unitize();
            }
            else // Vertical
            {
                axes2 = Vector3d.CrossProduct(axes3, Vector3d.YAxis);
                axes2.Unitize();
                axes1 = Vector3d.CrossProduct(axes2, axes3);
                axes1.Unitize();
            }
            Plane pl = new Plane(_pointFrom, axes1, axes2);
            pl.Rotate(r, pl.Normal);
            axes1 = pl.XAxis;
            axes2 = pl.YAxis;
            toGlobal = Transform.PlaneToPlane(Plane.WorldXY, pl);
        }

        public override string ToString()
        {
            string text = "BridGHe Beam";
            if (_beamId != string.Empty)
                text += $" #{_beamId}";
            text += $"\nStart Property: {_startBeamProperty.Name}";
            text += $"\nEnd Property: {_endBeamProperty.Name}";
            text += "\nGroups: " + string.Join(", ", _groups);
            text += $"\nFrom point: {_pointFrom}";
            text += $"\nTo point: {_pointTo}";
            text += "\nLenght: " + _pointFrom.DistanceTo(_pointTo).ToString("F2");
            text += "\n" + base.ToString();
            return text;
        }

        private void SetSurfaceConcrete(CompositeBeamPropertyModel sectionModel, Point2d centroid, ref List<Brep> main_surfaces, double tol)
        {
            List<Point3d> ptsConcrete = new List<Point3d>
            {
                new Point3d(-sectionModel.ConcreteSection.Width / 2, -sectionModel.ConcreteSection.Height / 2, 0),
                new Point3d(sectionModel.ConcreteSection.Width / 2, -sectionModel.ConcreteSection.Height / 2, 0),
                new Point3d(sectionModel.ConcreteSection.Width / 2, sectionModel.ConcreteSection.Height / 2, 0),
                new Point3d(-sectionModel.ConcreteSection.Width / 2, sectionModel.ConcreteSection.Height / 2 , 0),
                new Point3d(-sectionModel.ConcreteSection.Width / 2, -sectionModel.ConcreteSection.Height / 2, 0)
            };
            PolylineCurve borderC = new PolylineCurve(ptsConcrete);
            Brep[] breps = Brep.CreatePlanarBreps(borderC, tol);
            breps[0].Translate(0, +sectionModel.SteelSection.D / 2 + sectionModel.ConcreteSection.Height / 2, 0);
            if (breps != null)
                main_surfaces.Add(breps[0]);
        }

        private void SetSurfaceSteel(CompositeBeamPropertyModel sectionModel, Point2d centroid, ref List<Brep> main_surfaces, double tol)
        {
            List<Point3d> pts = new List<Point3d>
            {
                new Point3d(-sectionModel.SteelSection.B1 / 2, -centroid.Y, 0),
                new Point3d(sectionModel.SteelSection.B1 / 2, -centroid.Y, 0),
                new Point3d(sectionModel.SteelSection.B1 / 2, -centroid.Y + sectionModel.SteelSection.T1, 0),
                new Point3d(sectionModel.SteelSection.T3 / 2, -centroid.Y + sectionModel.SteelSection.T1, 0),
                new Point3d(sectionModel.SteelSection.T3 / 2, -centroid.Y + sectionModel.SteelSection.D - sectionModel.SteelSection.T2, 0),
                new Point3d(sectionModel.SteelSection.B2 / 2, -centroid.Y + sectionModel.SteelSection.D - sectionModel.SteelSection.T2, 0),
                new Point3d(sectionModel.SteelSection.B2 / 2, -centroid.Y + sectionModel.SteelSection.D, 0),
                new Point3d(-sectionModel.SteelSection.B2 / 2, -centroid.Y + sectionModel.SteelSection.D, 0),
                new Point3d(-sectionModel.SteelSection.B2 / 2, -centroid.Y + sectionModel.SteelSection.D - sectionModel.SteelSection.T2, 0),
                new Point3d(-sectionModel.SteelSection.T3 / 2, -centroid.Y + sectionModel.SteelSection.D - sectionModel.SteelSection.T2, 0),
                new Point3d(-sectionModel.SteelSection.T3 / 2, -centroid.Y + sectionModel.SteelSection.T1, 0),
                new Point3d(-sectionModel.SteelSection.B1 / 2, -centroid.Y + sectionModel.SteelSection.T1, 0),
                new Point3d(-sectionModel.SteelSection.B1 / 2, -centroid.Y, 0)
            };
            PolylineCurve border = new PolylineCurve(pts);
            Brep[] breps = Brep.CreatePlanarBreps(border, tol);
            if (breps != null)
                main_surfaces.Add(breps[0]);
        }

        private void AddBrep(List<Brep> surf, List<Brep> shapes)
        {
            Brep main_from_surface = surf[0].DuplicateBrep();
            main_from_surface.MakeDeformable();

            Brep main_to_surface = surf[1].DuplicateBrep();
            main_to_surface.MakeDeformable();

            GetLocalAxes(out Vector3d x, out Vector3d y, out Vector3d z, out Transform t);

            main_from_surface.Transform(t);
            main_to_surface.Transform(t);

            LineCurve line = new LineCurve(_pointFrom, _pointTo);

            t = Transform.Translation(_pointTo - _pointFrom);

            main_to_surface.Transform(t);

            List<Brep> surfaces = new List<Brep>();
            for (int i = 0; i < main_from_surface.Loops.Count; i++)
            {
                Curve fromC = main_from_surface.Loops[i].To3dCurve();
                Curve toC = main_to_surface.Loops[i].To3dCurve();

                SweepOneRail s1r = new SweepOneRail { GlobalShapeBlending = true };
                surfaces.AddRange(s1r.PerformSweep(line, new Curve[] { fromC, toC }));
            }

            surfaces.Add(main_from_surface);
            surfaces.Add(main_to_surface);

            Brep[] join = Brep.JoinBreps(surfaces, 0.001);
            shapes.Add(join[0]);
        }
    }
}

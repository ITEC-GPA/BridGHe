using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel;
using GPC.Utilities.Units;
using Rhino.Collections;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_BeamSection : GH_Goo<SectionModel>, IGH_PreviewData
    {
        private IEnumerable<Polyline> NativeOutlines()
        {
            foreach(var coordinates in Value?.NativeSection?.PreviewContours ?? Array.Empty<double[]>())
            {
                var points=new List<Point3d>();
                for(int i=0;i+1<coordinates.Length;i+=2)points.Add(new Point3d(coordinates[i],coordinates[i+1],0));
                if(points.Count>1)yield return new Polyline(points);
            }
        }
        public BoundingBox ClippingBox
        {
            get { var box=BoundingBox.Empty;foreach(var outline in NativeOutlines())box.Union(outline.BoundingBox);return box; }
        }
        public void DrawViewportWires(GH_PreviewWireArgs args)
        { if(!Display.ModelPreview.Automatic || !Display.DisplayPreferences.Current.Sections)return;foreach(var outline in NativeOutlines())args.Pipeline.DrawPolyline(outline,args.Color,args.Thickness); }
        public void DrawViewportMeshes(GH_PreviewMeshArgs args) { }

        public SectionModel.SectionTypes SectionType
        {
            get => Value.SectionType;
            set => Value.SectionType = value;
        }

        public double D
        {
            get => Value.D;
            set => Value.D = value;
        }

        public double B
        {
            get => Value.B;
            set => Value.B = value;
        }

        public double B1
        {
            get => Value.B1;
            set => Value.B1 = value;
        }

        public double B2
        {
            get => Value.B2;
            set => Value.B2 = value;
        }

        public double L
        {
            get => Value.L;
            set => Value.L = value;
        }

        public double T
        {
            get => Value.T;
            set => Value.T = value;
        }

        public double T1
        {
            get => Value.T1;
            set => Value.T1 = value;
        }

        public double T2
        {
            get => Value.T2;
            set => Value.T2 = value;
        }

        public double T3
        {
            get => Value.T3;
            set => Value.T3 = value;
        }

        public double R
        {
            get => Value.R;
            set => Value.R = value;
        }

        public SectionModel.MirrorTypes MirrorType
        {
            get => Value.Mirror;
            set => Value.Mirror = value;
        }

        public double MirrorGapA
        {
            get => Value.MirrorGapA;
            set => Value.MirrorGapA = value;
        }

        public double MirrorGapB
        {
            get => Value.MirrorGapB;
            set => Value.MirrorGapB = value;
        }

        public double SectionArea
        {
            get => Value.SectionArea;
            set => Value.SectionArea = value;
        }

        public double J
        {
            get => Value.J;
            set => Value.J = value;
        }

        public double I11
        {
            get => Value.I11;
            set => Value.I11 = value;
        }

        public double I22
        {
            get => Value.I22;
            set => Value.I22 = value;
        }

        public double SL1
        {
            get => Value.ShearL1;
            set => Value.ShearL1 = value;
        }

        public double SL2
        {
            get => Value.ShearL2;
            set => Value.ShearL2 = value;
        }

        public double SA1
        {
            get => Value.ShearA1;
            set => Value.ShearA1 = value;
        }

        public double SA2
        {
            get => Value.ShearA2;
            set => Value.ShearA2 = value;
        }

        public double W11
        {
            get => Value.W11;
            set => Value.W11 = value;
        }

        public double W22
        {
            get => Value.W22;
            set => Value.W22 = value;
        }

        public Point2d Centroid
        {
            get => Value.Centroid;
            set => Value.Centroid = value;
        }

        public double AngleX1
        {
            get => Value.AngleX1Rad;
            set => Value.AngleX1Rad = value;
        }

        public bool IsDBSection
        {
            get => Value.IsDBSection;
            set => Value.IsDBSection = value;
        }

        public Common.Geometry.Shapes GenericShapes
        {
            get => Value.GenericShapes;
            set => Value.GenericShapes = value;
        }

        public override bool IsValid => SectionType != SectionModel.SectionTypes.Undefined;

        public override string IsValidWhyNot => "The section type is not defined";

        public override string TypeName => "BeamSection";

        public override string TypeDescription => "Defines a beam section";

        public GH_BeamSection()
        {
            Value = new SectionModel();
            SectionType = SectionModel.SectionTypes.Undefined;
            MirrorType = SectionModel.MirrorTypes.None;
            GenericShapes = null;
        }

        public GH_BeamSection(SectionModel section)
        {
            Value = section;
        }

        public GH_BeamSection(GH_BeamSection section)
        {
            Value = section.Value;
        }

        public override bool CastFrom(object source)
        {
            if (source is GH_String str)
            {
                // CHS circle
                // RHS rect 
                // SHS square

                IsDBSection = false;
                if (str.Value.StartsWith("CHS"))
                {
                    try
                    {
                        double[] vals = str.Value.Substring(3).ToLower().Split(new char[] { 'x' }).Select(s => Convert.ToDouble(s)).ToArray();
                        SectionType = SectionModel.SectionTypes.HollowCircle;
                        D = vals[0];
                        T = vals[1];
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                else if (str.Value.StartsWith("SHS"))
                {
                    try
                    {
                        double[] vals = str.Value.Substring(3).ToLower().Split(new char[] { 'x' }).Select(s => Convert.ToDouble(s)).ToArray();
                        SectionType = SectionModel.SectionTypes.HollowRectangle;
                        D = vals[0];
                        B = vals[0];
                        T1 = vals[1];
                        T2 = vals[1];
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                else if (str.Value.StartsWith("RHS"))
                {
                    try
                    {

                        double[] vals = str.Value.Substring(3).ToLower().Split(new char[] { 'x' }).Select(s => Convert.ToDouble(s)).ToArray();

                        if (vals.Length == 3 || vals.Length == 4)
                        {
                            SectionType = SectionModel.SectionTypes.HollowRectangle;
                            D = vals[0];
                            B = vals[1];
                            T1 = vals[2];
                            T2 = vals.Length == 4 ? vals[3] : vals[2];
                            return true;
                        }
                        else
                        {
                            throw new ArgumentException();
                        }
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                else if (str.Value.StartsWith("I"))
                {
                    try
                    {
                        double[] vals = str.Value.Substring(1).ToLower().Split(new char[] { 'x' }).Select(s => Convert.ToDouble(s)).ToArray();
                        SectionType = SectionModel.SectionTypes.I;
                        D = vals[0];
                        B1 = vals[1];
                        B2 = vals[2];
                        T1 = vals[3];
                        T2 = vals[4];
                        T3 = vals[5];
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                else if (str.Value.StartsWith("C"))
                {
                    try
                    {
                        double[] vals = str.Value.Substring(1).ToLower().Split(new char[] { 'x' }).Select(s => Convert.ToDouble(s)).ToArray();
                        SectionType = SectionModel.SectionTypes.C;
                        D = vals[0];
                        B = vals[1];
                        T1 = vals[2];
                        T2 = vals[3];
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                else if (str.Value.StartsWith("L"))
                {
                    try
                    {
                        double[] vals = str.Value.Substring(1).ToLower().Split(new char[] { 'x' }).Select(s => Convert.ToDouble(s)).ToArray();
                        SectionType = SectionModel.SectionTypes.Angle;
                        D = vals[0];
                        B = vals[1];
                        T1 = vals[2];
                        T2 = vals[3];
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                else if (str.Value.StartsWith("R"))
                {
                    try
                    {
                        double[] vals = str.Value.ToLower().Substring(1).Split(new char[] { 'x' }).Select(s => Convert.ToDouble(s)).ToArray();
                        SectionType = SectionModel.SectionTypes.SolidRectangle;
                        D = vals[0];
                        B = vals[1];
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                else if (str.Value.StartsWith("T"))
                {
                    try
                    {
                        double[] vals = str.Value.Substring(1).Split(new char[] { 'x' }).Select(s => Convert.ToDouble(s)).ToArray();
                        SectionType = SectionModel.SectionTypes.T;
                        D = vals[0];
                        B = vals[1];
                        T1 = vals[2];
                        T2 = vals[3];
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                else if (str.Value.StartsWith("SC"))
                {
                    try
                    {
                        //double[] vals = str.Value.Substring(2).Trim().Select(s => Convert.ToDouble(s)).ToArray();
                        SectionType = SectionModel.SectionTypes.SolidCircle;
                        D = Convert.ToInt32(str.Value.Substring(2).Trim());
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                else if (str.Value.StartsWith("2x"))
                {
                    string sec = str.Value.Substring(2);
                    MirrorType = SectionModel.MirrorTypes.Left;
                    if (sec.StartsWith("C"))
                    {
                        try
                        {
                            double[] vals = sec.Substring(1).ToLower().Split(new char[] { 'x' }).Select(s => Convert.ToDouble(s)).ToArray();
                            SectionType = SectionModel.SectionTypes.C;
                            D = vals[0];
                            B = vals[1];
                            T1 = vals[2];
                            T2 = vals[3];
                            MirrorGapA = vals[4];
                            return true;
                        }
                        catch (Exception)
                        {
                            return false;
                        }
                    }
                    else if (sec.StartsWith("L"))
                    {
                        try
                        {
                            double[] vals = sec.Substring(1).ToLower().Split(new char[] { 'x' }).Select(s => Convert.ToDouble(s)).ToArray();
                            SectionType = SectionModel.SectionTypes.Angle;
                            D = vals[0];
                            B = vals[1];
                            T1 = vals[2];
                            T2 = vals[3];
                            MirrorGapA = vals[4];
                            return true;
                        }
                        catch (Exception)
                        {
                            return false;
                        }
                    }
                }
                return false;
            }
            else if (source.GetType() == typeof(GH_Curve))
            {
                GH_Curve curve = (GH_Curve)source;
                if (curve.Value is ArcCurve)
                {
                    ArcCurve circle = curve.Value as ArcCurve;
                    if (circle.IsCircle())
                    {
                        SectionType = SectionModel.SectionTypes.SolidCircle;
                        D = circle.Radius * 2;
                        return true;
                    }
                }
                else
                {
                    if (curve.Value.IsPolyline() && curve.Value.IsClosed && curve.Value.IsPlanar())
                    {
                        if (curve.Value.TryGetPolyline(out Polyline pline))
                        {
                            if (pline.Count > 2)
                            {
                                Plane pl = new Plane(pline[0], pline[1], pline[2]);
                                pline.Transform(Transform.PlaneToPlane(pl, Plane.WorldXY));
                                GPC.Geometry.Polygon2d fill;
                                fill = new GPC.Geometry.Polygon2d();
                                foreach (Point3d p in pline)
                                {
                                    fill.Add(p.X, p.Y);
                                }
                                GenericShapes = new Common.Geometry.Shapes(new[] { new GPC.Geometry.Shape2d(fill, null) });
                                SectionType = SectionModel.SectionTypes.GenericShapes;
                                return true;
                            }
                        }
                    }

                    return true;
                }
            }

            return false;
        }

        public bool CastTo<T>(out T target)
        {
            try
            {
                double t = 0.01;
                switch (SectionType)
                {
                    case SectionModel.SectionTypes.SolidCircle:
                        {
                            ArcCurve border = new ArcCurve(new Circle(D / 2));

                            if (typeof(T) == typeof(GH_Curve))
                            {
                                target = (T)Convert.ChangeType(new GH_Curve(border), typeof(T));
                                return true;
                            }
                            else if (typeof(T) == typeof(GH_Surface))
                            {
                                Brep[] brep = Brep.CreatePlanarBreps(border, t);
                                target = (T)Convert.ChangeType(new GH_Surface(brep[0]), typeof(T));
                                return true;
                            }
                        }
                        break;
                    case SectionModel.SectionTypes.HollowCircle:
                        {
                            ArcCurve border = new ArcCurve(new Circle(D / 2));
                            ArcCurve inner = new ArcCurve(new Circle(D / 2 - this.T));
                            if (typeof(T) == typeof(GH_Curve))
                            {
                                target = (T)Convert.ChangeType(new GH_Curve(border), typeof(T));
                                return true;
                            }
                            else if (typeof(T) == typeof(GH_Surface))
                            {
                                CurveList curves = new CurveList
                                {
                                    border,
                                    inner
                                };
                                Brep[] brep = Brep.CreatePlanarBreps(curves, t);
                                target = (T)Convert.ChangeType(new GH_Surface(brep[0]), typeof(T));
                                return true;
                            }
                        }
                        break;
                    case SectionModel.SectionTypes.SolidRectangle:
                        {
                            List<Point3d> pts = new List<Point3d>
                            {
                                new Point3d(-B / 2, -D / 2, 0),
                                new Point3d(B / 2, -D / 2, 0),
                                new Point3d(B / 2, D / 2, 0),
                                new Point3d(-B / 2, D / 2, 0),
                                new Point3d(-B / 2, -D / 2, 0)
                            };
                            PolylineCurve border = new PolylineCurve(pts);
                            if (typeof(T) == typeof(GH_Curve))
                            {
                                target = (T)Convert.ChangeType(new GH_Curve(border), typeof(T));
                                return true;
                            }
                            else if (typeof(T) == typeof(GH_Surface))
                            {
                                Brep[] brep = Brep.CreatePlanarBreps(border, t);
                                target = (T)Convert.ChangeType(new GH_Surface(brep[0]), typeof(T));
                                return true;
                            }
                        }
                        break; ;
                    case SectionModel.SectionTypes.HollowRectangle:
                        {
                            List<Point3d> ptsOut = new List<Point3d>
                            {
                                new Point3d(-B / 2, -D / 2, 0),
                                new Point3d(B / 2, -D / 2, 0),
                                new Point3d(B / 2, D / 2, 0),
                                new Point3d(-B / 2, D / 2, 0),
                                new Point3d(-B / 2, -D / 2, 0)
                            };
                            PolylineCurve border = new PolylineCurve(ptsOut);
                            List<Point3d> ptsIn = new List<Point3d>
                            {
                                new Point3d(-B / 2 + T2, -D / 2 + T1, 0),
                                new Point3d(B / 2 - T2, -D / 2 + T1, 0),
                                new Point3d(B / 2 - T2, D / 2 - T1, 0),
                                new Point3d(-B / 2 + T2, D / 2 - T1, 0),
                                new Point3d(-B / 2 + T2, -D / 2 + T1, 0)
                            };
                            PolylineCurve inner = new PolylineCurve(ptsIn);
                            if (typeof(T) == typeof(GH_Curve))
                            {
                                target = (T)Convert.ChangeType(new GH_Curve(border), typeof(T));
                                return true;
                            }
                            else if (typeof(T) == typeof(GH_Surface))
                            {
                                CurveList curves = new CurveList
                                {
                                    border,
                                    inner
                                };
                                Brep[] brep = Brep.CreatePlanarBreps(curves, t);
                                target = (T)Convert.ChangeType(new GH_Surface(brep[0]), typeof(T));
                                return true;
                            }
                        }
                        break;
                    case SectionModel.SectionTypes.I:
                        {
                            List<Point3d> pts = new List<Point3d>
                            {
                                new Point3d(-B1 / 2, -D / 2, 0),
                                new Point3d(B1 / 2, -D / 2, 0),
                                new Point3d(B1 / 2, -D / 2 + T1, 0),
                                new Point3d(T3 / 2, -D / 2 + T1, 0),
                                new Point3d(T3 / 2, D / 2 - T2, 0),
                                new Point3d(B2 / 2, D / 2 - T2, 0),
                                new Point3d(B2 / 2, D / 2, 0),
                                new Point3d(-B2 / 2, D / 2, 0),
                                new Point3d(-B2 / 2, D / 2 - T2, 0),
                                new Point3d(-T3 / 2, D / 2 - T2, 0),
                                new Point3d(-T3 / 2, -D / 2 + T1, 0),
                                new Point3d(-B1 / 2, -D / 2 + T1, 0),
                                new Point3d(-B1 / 2, -D / 2, 0)
                            };
                            PolylineCurve border = new PolylineCurve(pts);
                            if (typeof(T) == typeof(GH_Curve))
                            {
                                target = (T)Convert.ChangeType(new GH_Curve(border), typeof(T));
                                return true;
                            }
                            else if (typeof(T) == typeof(GH_Surface))
                            {
                                Brep[] brep = Brep.CreatePlanarBreps(border, t);
                                target = (T)Convert.ChangeType(new GH_Surface(brep[0]), typeof(T));
                                return true;
                            }
                        }
                        break;
                    case SectionModel.SectionTypes.T:
                        {
                            List<Point3d> pts = new List<Point3d>
                            {
                                new Point3d(-T2 / 2, -D / 2, 0),
                                new Point3d(T2 / 2, -D / 2, 0),
                                new Point3d(T2 / 2, D / 2 - T1, 0),
                                new Point3d(B / 2 - T3, D / 2 - T1, 0)
                            };
                            if (T3 > 0 && L > 0)
                            {
                                pts.Add(new Point3d(B / 2 - T3, D / 2 - T1 - L, 0));
                                pts.Add(new Point3d(B / 2, D / 2 - T1 - L, 0));
                            }
                            pts.Add(new Point3d(B / 2, D / 2, 0));
                            pts.Add(new Point3d(-B / 2, D / 2, 0));
                            pts.Add(new Point3d(-B / 2, D / 2 - T1 - L, 0));
                            if (T3 > 0 && L > 0)
                            {
                                pts.Add(new Point3d(-B / 2 + T3, D / 2 - T1 - L, 0));
                                pts.Add(new Point3d(-B / 2 + T3, D / 2 - T1, 0));
                            }
                            pts.Add(new Point3d(-T2 / 2, D / 2 - T1, 0));
                            pts.Add(new Point3d(-T2 / 2, -D / 2, 0));
                            PolylineCurve border = new PolylineCurve(pts);
                            if (typeof(T) == typeof(GH_Curve))
                            {
                                target = (T)Convert.ChangeType(new GH_Curve(border), typeof(T));
                                return true;
                            }
                            else if (typeof(T) == typeof(GH_Surface))
                            {
                                Brep[] brep = Brep.CreatePlanarBreps(border, t);
                                target = (T)Convert.ChangeType(new GH_Surface(brep[0]), typeof(T));
                                return true;
                            }
                        }
                        break;
                    //case SectionType.GenericShapes:
                    //    {
                    //        //covert only section with one shape
                    //        if (GenericShapes.Count == 1)
                    //        {
                    //            CurveList loops;
                    //            List<Point3d> pts;
                    //            Utilities.Geometry.Polygon fill;
                    //            loops = new CurveList();
                    //            pts = new List<Point3d>();
                    //            fill = GenericShapes[0].Fill;
                    //            foreach (Utilities.Geometry.Point2d p in fill)
                    //            {
                    //                pts.Add(new Point3d(p.X, p.Y, 0));
                    //            }
                    //            pts.Add(new Point3d(fill[0].X, fill[0].Y, 0));
                    //            loops.Add(new PolylineCurve(pts));
                    //            if (GenericShapes[0].HolesCanBeNull != null)
                    //            {
                    //                foreach (Utilities.Geometry.Polygon hole in GenericShapes[0].HolesCanBeNull)
                    //                {
                    //                    pts = new List<Point3d>();
                    //                    foreach (Utilities.Geometry.Point2d p in hole)
                    //                    {
                    //                        pts.Add(new Point3d(p.X, p.Y, 0));
                    //                    }
                    //                    pts.Add(new Point3d(hole[hole.Count - 1].X, hole[hole.Count - 1].Y, 0));
                    //                    loops.Add(new PolylineCurve(pts));
                    //                }
                    //            }
                    //            Brep[] brep = Brep.CreatePlanarBreps(loops, t);
                    //            target = (T)Convert.ChangeType(new GHBrep(brep[0]), typeof(T));
                    //            return true;
                    //        }
                    //    }
                    //    break;
                    default:
                        {
                            List<Point3d> pts = new List<Point3d>
                            {
                                new Point3d(0, 0, 0),
                                new Point3d(0, 20, 0)
                            };
                            target = (T)Convert.ChangeType(new GH_Curve(new PolylineCurve(pts)), typeof(T));
                        }
                        return true;
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e.Message);
            }

            target = default;
            return false;
        }

        public override IGH_Goo Duplicate()
        {
            return new GH_BeamSection(this);
        }

        public override IGH_GooProxy EmitProxy()
        {
            return null;
        }

        public void ConvertUnits(ModelModel.ModelUnits startingUnits, ModelModel.ModelUnits destinationUnits)
        {
            Common.Helpers.ModelHelper.GetUtilitiesConversionParams(
                startingUnits,
                out UnitsConvert.LengthUnits startingLengthUnits,
                out UnitsConvert.ForceUnits bufferF,
                out UnitsConvert.MassUnits bufferM,
                out UnitsConvert.PressureUnits bufferP,
                out UnitsConvert.TemperatureUnits bufferT);
            Common.Helpers.ModelHelper.GetUtilitiesConversionParams(
                destinationUnits,
                out UnitsConvert.LengthUnits destinationLengthUnits,
                out bufferF,
                out bufferM,
                out bufferP,
                out bufferT);

            D = UnitsConvert.Convert(D, startingLengthUnits, destinationLengthUnits, 1);
            B = UnitsConvert.Convert(B, startingLengthUnits, destinationLengthUnits, 1);
            B1 = UnitsConvert.Convert(B1, startingLengthUnits, destinationLengthUnits, 1);
            B2 = UnitsConvert.Convert(B2, startingLengthUnits, destinationLengthUnits, 1);
            L = UnitsConvert.Convert(L, startingLengthUnits, destinationLengthUnits, 1);
            T = UnitsConvert.Convert(T, startingLengthUnits, destinationLengthUnits, 1);
            T1 = UnitsConvert.Convert(T1, startingLengthUnits, destinationLengthUnits, 1);
            T2 = UnitsConvert.Convert(T2, startingLengthUnits, destinationLengthUnits, 1);
            T3 = UnitsConvert.Convert(T3, startingLengthUnits, destinationLengthUnits, 1);
            MirrorGapA = UnitsConvert.Convert(MirrorGapA, startingLengthUnits, destinationLengthUnits, 1);
            MirrorGapB = UnitsConvert.Convert(MirrorGapB, startingLengthUnits, destinationLengthUnits, 1);
            SectionArea = UnitsConvert.Convert(SectionArea, startingLengthUnits, destinationLengthUnits, 2);
            J = UnitsConvert.Convert(J, startingLengthUnits, destinationLengthUnits, 4);
            I11 = UnitsConvert.Convert(I11, startingLengthUnits, destinationLengthUnits, 4);
            I22 = UnitsConvert.Convert(I22, startingLengthUnits, destinationLengthUnits, 4);
            SL1 = UnitsConvert.Convert(SL1, startingLengthUnits, destinationLengthUnits, 1);
            SL2 = UnitsConvert.Convert(SL2, startingLengthUnits, destinationLengthUnits, 1);
            SA1 = UnitsConvert.Convert(SA1, startingLengthUnits, destinationLengthUnits, 2);
            SA2 = UnitsConvert.Convert(SA2, startingLengthUnits, destinationLengthUnits, 2);
            W11 = UnitsConvert.Convert(W11, startingLengthUnits, destinationLengthUnits, 3);
            W22 = UnitsConvert.Convert(W22, startingLengthUnits, destinationLengthUnits, 3);
            var x = UnitsConvert.Convert(Centroid.X, startingLengthUnits, destinationLengthUnits, 1);
            var y = UnitsConvert.Convert(Centroid.Y, startingLengthUnits, destinationLengthUnits, 1);
            Centroid = new Point2d(x, y);
            if (GenericShapes != null)
            {
                foreach (GPC.Geometry.Shape2d s in GenericShapes)
                {
                    foreach (GPC.Geometry.Point2d p in s.Fill)
                    {
                        p.Move(UnitsConvert.Convert(p.X, startingLengthUnits, destinationLengthUnits, 1),
                               UnitsConvert.Convert(p.Y, startingLengthUnits, destinationLengthUnits, 1));
                    }
                    if (s.Holes != null)
                    {
                        foreach (GPC.Geometry.Polygon2d hole in s.Holes2d)
                        {
                            foreach (GPC.Geometry.Point2d p in hole)
                            {
                                p.Move(UnitsConvert.Convert(p.X, startingLengthUnits, destinationLengthUnits, 1),
                                       UnitsConvert.Convert(p.Y, startingLengthUnits, destinationLengthUnits, 1));
                            }
                        }
                    }
                }
            }
        }

        public override bool Read(GH_IReader reader)
        {
            try { Value = Common.Helpers.ModelArchive.Read<SectionModel>(reader.GetByteArray("Data")); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override object ScriptVariable()
        {
            return null;
        }

        public override bool Write(GH_IWriter writer)
        {
            try { writer.SetByteArray("Data", Common.Helpers.ModelArchive.Write(Value)); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            string text;
            if (SectionType == SectionModel.SectionTypes.GenericShapes)
            {
                text = string.Format("{0} {1} shapes", SectionType, GenericShapes.Count);
            }
            else
            {
                switch (SectionType)
                {
                    case SectionModel.SectionTypes.HollowCircle:
                        // Diameter x Thickness
                        text = $"HollowCircle: {D}x{T}";
                        break;
                    case SectionModel.SectionTypes.HollowRectangle:
                        // Height x Width x Horizontal Thickness x Vertical Thickness
                        text = $"HollowRectangle: {D}x{B}x{T1}x{T2}";
                        break;
                    case SectionModel.SectionTypes.C:
                        // Height x Flange width x Flange thickness x Web thickness
                        text = $"CSection: {D}x{B}x{T1}x{T2}";
                        break;
                    case SectionModel.SectionTypes.Angle:
                        if (MirrorType == SectionModel.MirrorTypes.Left)
                            // Height x Flange width x Flange thickness x Web thickness x Gap
                            text = $"DoubleAngle: {D}x{B}x{T1}x{T2}x{MirrorGapA}";
                        else
                            // Height x Flange width x Flange thickness x Web thickness
                            text = $"Angle: {D}x{B}x{T1}x{T2}";
                        break;
                    case SectionModel.SectionTypes.T:
                        // Height x Flange width x Flange thickness x Web thickness
                        text = $"TSection: {D}x{B}x{T1}x{T2}";
                        break;
                    case SectionModel.SectionTypes.I:
                        // Height x Web Thickness x Bottom flange width x Bottom flange thickness x Top flange width x Top flange thickness x Radius
                        text = $"ISection: {D}x{T3}x{B1}x{T1}x{B2}x{T2}x{R}";
                        break;
                    case SectionModel.SectionTypes.SolidCircle:
                        // Diameter
                        text = $"SolidCircle: {D}";
                        break;
                    case SectionModel.SectionTypes.SolidRectangle:
                        // Height x Width
                        text = $"SolidRectangle: {D}x{B}";
                        break;
                    case SectionModel.SectionTypes.Composite:
                        text = $"ISection: {D}x{T3}x{B1}x{T1}x{B2}x{T2}\n";
                        text += $"SolidRectangle: {T}x{B}";
                        break;
                    default:
                        text = $"{SectionType}: {Math.Max(B1, Math.Max(B2, B))}x{D}";
                        break;
                }
            }
            if (MirrorType != SectionModel.MirrorTypes.None)
            {
                text += $"\nMirror: {MirrorType}, Gap: {MirrorGapA}";
            }
            return text;
        }
    }
}


using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace BridGHe.Common.Helpers
{
    public class GraphicCreator
    {
        protected class Curve
        {
            protected string _name;
            protected Color _color;
            protected List<double[]> _values;

            public string Name
            {
                get { return _name; }
            }

            public Color Color
            {
                get { return _color; }
            }

            public List<double[]> Values
            {
                get { return _values; }
            }

            public Curve(string name, Color color)
            {
                _name = name;
                _color = color;
                _values = new List<double[]>();
            }
        }

        protected float _w_bmp;
        protected float _h_bmp;
        protected float[] _margins; // 0 = left, 1 = top, 2 = right, 3 = bottom
        protected double _max_x;
        protected double _min;
        protected double _max;
        protected string _title;
        protected List<double[]> _maximumValues;
        protected List<Curve> _curves;
        protected List<double> _frameLenths;
        protected Dictionary<double, Color> _referenceLines;
        protected bool _background;

        public string Title { get => _title; set => _title = value; }
        public string XAxisTitle { get; set; }
        public string YAxisTitle { get; set; }

        public List<double> FrameLenths => _frameLenths;

        public GraphicCreator(float w_bmp, float h_bmp, bool background)
        {
            _w_bmp = w_bmp;
            _h_bmp = h_bmp;
            _margins = new float[] { 20, 50, 20, 40 };
            _max_x = 0;
            _min = 0;
            _max = 0;
            _title = "";
            _frameLenths = new List<double>();
            _maximumValues = new List<double[]>();
            _curves = new List<Curve>();
            _referenceLines = new Dictionary<double, Color>();
            _background = background;
        }

        public void AddMaximumValues(double delta, double min, double max)
        {
            var existingValue = _maximumValues.Where(v => v[0] == delta);
            if (existingValue.Count() > 0)
            {
                for (int i = 0; i < existingValue.Count(); i++)
                {
                    var selectedValue = existingValue.ElementAt(i);
                    if (max > selectedValue[2])
                        selectedValue[2] = max;
                    if (min < selectedValue[1])
                        selectedValue[1] = min;
                    if (min < _min)
                        _min = min;
                    if (max > _max)
                        _max = max;
                }
                return;
            }

            double[] vals = new double[] { delta, min, max };
            _maximumValues.Add(vals);
            if (min < _min)
                _min = min;
            if (max > _max)
                _max = max;
            if (delta > _max_x)
                _max_x = delta;
        }

        public bool AddCurve(string name, Color color)
        {
            if (_curves.SingleOrDefault(c => c.Name == name) != null)
                return false;
            _curves.Add(new Curve(name, color));
            return true;
        }

        public bool AddCurveValue(string name, double delta, double val)
        {
            var curve = _curves.SingleOrDefault(c => c.Name == name);

            if (curve == null)
                return false;

            if (curve != null)
            {
                var existingValuePoint = curve.Values.Where(v => v[0] == delta).FirstOrDefault();

                if (existingValuePoint != null)
                {
                    if (val < _min)
                        _min = val;
                    if (val > _max)
                        _max = val;

                    if (val > existingValuePoint[1])
                        existingValuePoint[1] = val;
                }
                else
                {
                    double[] vals = new double[] { delta, val };
                    curve.Values.Add(vals);

                    if (val < _min)
                        _min = val;
                    if (val > _max)
                        _max = val;
                    if (delta > _max_x)
                        _max_x = delta;
                }
            }

            return true;
        }

        public bool AddReferenceLine(double value, Color color)
        {
            try
            {
                _referenceLines.Add(value, color);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        public void AddMaxValue(double value)
        {
            if (value > _max)
                _max = value;
        }

        public void AddMinValue(double value)
        {
            if (value < _min)
                _min = value;
        }

        public Bitmap Create(bool showMaxMin, float scale)
        {
            Bitmap image = new Bitmap((int)_w_bmp, (int)_h_bmp);

            try
            {
                using (Graphics g = Graphics.FromImage(image))
                {
                    // Draw the background
                    g.FillRectangle(Brushes.White, 0, 0, _w_bmp - 1, _h_bmp - 1);
                    //using (Image watermark = Resources.GraphicWatermark)
                    //{
                    //    using (TextureBrush watermarkBrush = new TextureBrush(watermark, System.Drawing.Drawing2D.WrapMode.Tile))
                    //    {
                    //        g.FillRectangle(watermarkBrush, 0, 0, _w_bmp - 1, _h_bmp - 1);
                    //    }
                    //}
                    g.DrawRectangle(Pens.Black, 0, 0, _w_bmp - 1, _h_bmp - 1);
                    using (Font font = new Font("Arial", 15))
                    {
                        StringFormat format = new StringFormat();
                        format.Alignment = StringAlignment.Center;
                        g.DrawString(_title, font, Brushes.Black, new PointF(image.Width / 2, 5), format);
                    }

                    // Legend
                    using (Font font = new Font("Arial", 10))
                    {
                        float w_legend = _curves.Max(c => g.MeasureString(c.Name, font).Width) + 5;
                        float h_legend = g.MeasureString("ABC", font).Height;

                        g.DrawRectangle(Pens.Black, _w_bmp - _margins[2] - 5 - w_legend, _margins[1] - 5, w_legend, (h_legend + 1) * _curves.Count + 5);

                        StringFormat format = new StringFormat();
                        format.Alignment = StringAlignment.Near;
                        format.LineAlignment = StringAlignment.Near;
                        float y = _margins[1];
                        foreach (Curve curve in _curves)
                        {
                            using (Brush brush = new SolidBrush(curve.Color))
                                g.DrawString(curve.Name, font, brush, new PointF(_w_bmp - _margins[2] - w_legend, y), format);
                            y += h_legend + 1;
                        }
                        _margins[2] += w_legend + 10;
                    }

                    // Calculate margin for numeric values of y axis
                    Font axisFont = new Font("Arial", 10);
                    _margins[0] += Math.Max(g.MeasureString(_min.ToString("N0"), axisFont).Width, g.MeasureString(_max.ToString("N0"), axisFont).Width);

                    PointF[] limits_crvs_pts = new PointF[2];
                    bool curveLimitsInitialized = false;
                    double[] limits_crvs_val = new double[2];

                    PointF[] limits_maxs_pts = new PointF[2];
                    bool maximumLimitsInitialized = false;
                    double[] limits_maxs_val = new double[2];

                    double mmm = Math.Abs(_min) < 1 ? 1 : Math.Abs(_min);
                    double nnn = Math.Abs(_max) < 1 ? 1 : Math.Abs(_max);
                    double step = Math.Round(Math.Max(mmm, nnn) / 5, MidpointRounding.ToEven) + 1; //attention if _min and _max are = 0 this leads to infinite iteration below
                    List<PointF> y_refs_list = new List<PointF>();
                    List<string> y_values = new List<string>();
                    for (double y = 0; y < _max + step; y += step)
                    {
                        y_refs_list.Add(new PointF(0, (float)y * scale));
                        y_values.Add(y.ToString());

                        if (y_values.Count > 200) //exit if too much iteration
                        {
                            y = _max + 1;
                        }
                    }
                    for (double y = 0; y > _min - step; y -= step)
                    {
                        y_refs_list.Add(new PointF(0, (float)y * scale));
                        y_values.Add(y.ToString());

                        if (y_values.Count > 200) //exit if too much iteration
                        {
                            y = _min - 1;
                        }
                    }

                    _min = Math.Min(_min, y_refs_list.Select(i => i.Y).Min());
                    _max = Math.Max(_max, y_refs_list.Select(i => i.Y).Max());

                    // Define logical viewport
                    RectangleF srcRect = new RectangleF((float)0, (float)_min, (float)_max_x, (float)(_max - _min));
                    RectangleF dstRect = new RectangleF(_margins[0], _margins[1], _w_bmp - _margins[0] - _margins[2], _h_bmp - _margins[1] - _margins[3]);

                    System.Drawing.Drawing2D.GraphicsContainer container = g.BeginContainer(dstRect, srcRect, GraphicsUnit.Millimeter);

                    g.ScaleTransform(1.0F, -1.0F);
                    g.TranslateTransform(0.0F, -(float)(_min * 2) - srcRect.Height);
                    g.PageUnit = GraphicsUnit.Millimeter;

                    if (showMaxMin)
                    {
                        for (int n = 0; n < _maximumValues.Count - 1; n++)
                        {
                            PointF[] points_max = new PointF[4];
                            points_max[0] = new PointF((float)_maximumValues[n][0], 0);
                            points_max[1] = new PointF((float)_maximumValues[n][0], (float)_maximumValues[n][2] * scale);
                            points_max[2] = new PointF((float)_maximumValues[n + 1][0], (float)_maximumValues[n + 1][2] * scale);
                            points_max[3] = new PointF((float)_maximumValues[n + 1][0], 0);
                            g.FillPolygon(Brushes.LightGray, points_max);
                            if (points_max[1].Y >= 0 && points_max[2].Y >= 0)
                                g.DrawLine(Pens.Gray, points_max[1], points_max[2]);

                            PointF[] points_min = new PointF[4];
                            points_min[0] = new PointF((float)_maximumValues[n][0], 0);
                            points_min[1] = new PointF((float)_maximumValues[n][0], (float)_maximumValues[n][1] * scale);
                            points_min[2] = new PointF((float)_maximumValues[n + 1][0], (float)_maximumValues[n + 1][1] * scale);
                            points_min[3] = new PointF((float)_maximumValues[n + 1][0], 0);
                            g.FillPolygon(Brushes.LightGray, points_min);
                            if (points_min[1].Y <= 0 && points_min[2].Y <= 0)
                                g.DrawLine(Pens.Gray, points_min[1], points_min[2]);

                            if (!maximumLimitsInitialized)
                            {
                                maximumLimitsInitialized = true;
                                limits_maxs_pts[0] = points_max[1];
                                limits_maxs_pts[1] = points_max[1];
                                limits_maxs_val[0] = _maximumValues[n][1];
                                limits_maxs_val[1] = _maximumValues[n][2];
                            }
                            else
                            {
                                if (points_min[1].Y < limits_maxs_pts[0].Y)
                                {
                                    limits_maxs_pts[0] = points_min[1];
                                    limits_maxs_val[0] = _maximumValues[n][1];
                                }
                                if (points_min[2].Y < limits_maxs_pts[0].Y)
                                {
                                    limits_maxs_pts[0] = points_min[2];
                                    limits_maxs_val[0] = _maximumValues[n + 1][1];
                                }
                                if (points_max[1].Y > limits_maxs_pts[1].Y)
                                {
                                    limits_maxs_pts[1] = points_max[1];
                                    limits_maxs_val[1] = _maximumValues[n][2];
                                }
                                if (points_max[2].Y > limits_maxs_pts[1].Y)
                                {
                                    limits_maxs_pts[1] = points_max[2];
                                    limits_maxs_val[1] = _maximumValues[n + 1][2];
                                }
                            }

                        }
                    }

                    foreach (Curve curve in _curves)
                    {
                        Pen pen = new Pen(curve.Color, 0.1F);

                        for (int n = 0; n < curve.Values.Count - 1; n++)
                        {
                            PointF start = new PointF((float)curve.Values[n][0], (float)curve.Values[n][1] * scale);
                            PointF end = new PointF((float)curve.Values[n + 1][0], (float)curve.Values[n + 1][1] * scale);
                            if (!curveLimitsInitialized)
                            {
                                curveLimitsInitialized = true;
                                limits_crvs_pts[0] = start.Y < end.Y ? start : end;
                                limits_crvs_pts[1] = start.Y > end.Y ? start : end;
                                limits_crvs_val[0] = limits_crvs_pts[0].Y;
                                limits_crvs_val[1] = limits_crvs_pts[1].Y;
                            }
                            else
                            {
                                if (start.Y < limits_crvs_pts[0].Y)
                                {
                                    limits_crvs_pts[0] = start;
                                    limits_crvs_val[0] = curve.Values[n][1];
                                }
                                if (end.Y < limits_crvs_pts[0].Y)
                                {
                                    limits_crvs_pts[0] = end;
                                    limits_crvs_val[0] = curve.Values[n + 1][1];
                                }
                                if (start.Y > limits_crvs_pts[1].Y)
                                {
                                    limits_crvs_pts[1] = start;
                                    limits_crvs_val[1] = curve.Values[n][1];
                                }
                                if (end.Y > limits_crvs_pts[1].Y)
                                {
                                    limits_crvs_pts[1] = end;
                                    limits_crvs_val[1] = curve.Values[n + 1][1];
                                }
                            }

                            g.DrawLine(pen, start, end);
                        }

                        pen.Dispose();
                    }

                    // Points for drawing axies
                    PointF[] x_axis_pts = new PointF[] { new PointF(0F, 0F), new PointF((float)_max_x, 0F) };
                    g.TransformPoints(System.Drawing.Drawing2D.CoordinateSpace.Device, System.Drawing.Drawing2D.CoordinateSpace.World, x_axis_pts);
                    PointF[] y_axis_pts = new PointF[] { new PointF(0F, (float)_max), new PointF(0F, (float)_min) };
                    g.TransformPoints(System.Drawing.Drawing2D.CoordinateSpace.Device, System.Drawing.Drawing2D.CoordinateSpace.World, y_axis_pts);

                    List<PointF> x_refs_list = new List<PointF>();
                    foreach (double len in _frameLenths)
                        x_refs_list.Add(new PointF((x_refs_list.Count > 0 ? x_refs_list.Last().X : 0) + (float)len, 0));
                    PointF[] x_refs_pts = x_refs_list.ToArray();
                    if (x_refs_pts.Length > 0)
                        g.TransformPoints(System.Drawing.Drawing2D.CoordinateSpace.Device, System.Drawing.Drawing2D.CoordinateSpace.World, x_refs_pts);

                    PointF[] y_refs_pts = y_refs_list.ToArray();
                    if (y_refs_pts.Length > 0)
                    {
                        g.TransformPoints(System.Drawing.Drawing2D.CoordinateSpace.Device, System.Drawing.Drawing2D.CoordinateSpace.World, y_refs_pts);
                        g.TransformPoints(System.Drawing.Drawing2D.CoordinateSpace.Device, System.Drawing.Drawing2D.CoordinateSpace.World, limits_crvs_pts);
                        g.TransformPoints(System.Drawing.Drawing2D.CoordinateSpace.Device, System.Drawing.Drawing2D.CoordinateSpace.World, limits_maxs_pts);
                    }

                    // Saves the reference lines y coords
                    PointF[] ref_line_start = new PointF[_referenceLines.Count];
                    int r = 0;
                    foreach (KeyValuePair<double, Color> ref_line in _referenceLines)
                        ref_line_start[r++] = new PointF(0, (float)ref_line.Key * scale);
                    if (_referenceLines.Count > 0)
                        g.TransformPoints(System.Drawing.Drawing2D.CoordinateSpace.Device, System.Drawing.Drawing2D.CoordinateSpace.World, ref_line_start);

                    g.EndContainer(container);

                    /*
                    using (Image watermark = Image.FromFile("G:\\Users\\g.todeschini\\Documents\\Sorgenti\\BridGHe\\Docs\\logo_small.bmp"))
                    {
                        g.DrawImage(watermark, new PointF(5, x_refs_pts[0].Y - 20));
                        g.DrawImage(watermark, new PointF(_w_bmp - 45, x_refs_pts[0].Y - 20));
                    }
                    */

                    float[] dashValues = { 10, 10 };
                    int grey = 0;
                    int alphaDash = 40;
                    Pen dashPen = new Pen(Color.FromArgb(alphaDash, grey, grey, grey), 1);
                    dashPen.DashPattern = dashValues;

                    // Draw the X axis
                    g.DrawLine(Pens.Black, x_axis_pts[0], x_axis_pts[1]);
                    if (x_refs_pts.Length > 0)
                    {
                        for (int i = 0; i < x_refs_pts.Length; i++)
                            g.DrawLine(dashPen, x_refs_pts[i].X, y_refs_pts.Select(j => j.Y).Max(), x_refs_pts[i].X, y_refs_pts.Select(j => j.Y).Min());
                        for (int i = 0; i < x_refs_pts.Length; i++)
                            g.DrawLine(Pens.Black, x_refs_pts[i].X, x_refs_pts[i].Y - 5, x_refs_pts[i].X, x_refs_pts[i].Y + 5);
                        for (int i = 0; i < x_refs_pts.Length; i++)
                            g.DrawString((x_refs_list[i].X / 1000).ToString("F2"), axisFont, Brushes.Black, x_refs_pts[i].X - 10, x_refs_pts[i].Y + 10);
                    }

                    g.DrawString(XAxisTitle, axisFont, Brushes.Black, x_axis_pts[1].X + 10, x_axis_pts[1].Y - 8);

                    // Draw the Y axis
                    g.DrawLine(Pens.Black, y_axis_pts[0], y_axis_pts[1]);
                    if (y_refs_pts.Length > 0)
                    {
                        StringFormat format_axis = new StringFormat();
                        format_axis.Alignment = StringAlignment.Far;
                        format_axis.LineAlignment = StringAlignment.Center;
                        for (int i = 0; i < y_refs_pts.Length; i++)
                        {
                            g.DrawLine(Pens.Black, y_refs_pts[i].X - 5, y_refs_pts[i].Y, y_refs_pts[i].X + 5, y_refs_pts[i].Y);
                            g.DrawString(y_values[i], axisFont, Brushes.Black, y_refs_pts[i].X - 6, y_refs_pts[i].Y, format_axis);
                        }
                        for (int i = 0; i < y_refs_pts.Length; i++)
                            g.DrawLine(dashPen, y_refs_pts[i].X, y_refs_pts[i].Y, y_refs_pts[i].X + _w_bmp - _margins[0] - _margins[2], y_refs_pts[i].Y);

                        format_axis.Alignment = StringAlignment.Center;
                        format_axis.LineAlignment = StringAlignment.Near;
                        g.DrawString(limits_crvs_val[0].ToString("N2"), axisFont, Brushes.Black, limits_crvs_pts[0], format_axis);
                        if (showMaxMin && limits_maxs_val[0] != limits_crvs_val[0])
                            g.DrawString(limits_maxs_val[0].ToString("N2"), axisFont, Brushes.Black, limits_maxs_pts[0], format_axis);

                        format_axis.LineAlignment = StringAlignment.Far;
                        g.DrawString(limits_crvs_val[1].ToString("N2"), axisFont, Brushes.Black, limits_crvs_pts[1], format_axis);
                        if (showMaxMin && limits_maxs_val[1] != limits_crvs_val[1])
                            g.DrawString(limits_maxs_val[1].ToString("N2"), axisFont, Brushes.Black, limits_maxs_pts[1], format_axis);
                    }

                    g.DrawString(YAxisTitle, axisFont, Brushes.Black, y_axis_pts[1].X, y_axis_pts[1].Y);

                    // Draw reference lines
                    r = 0;
                    foreach (KeyValuePair<double, Color> ref_line in _referenceLines)
                    {
                        using (Pen pen = new Pen(ref_line.Value))
                            g.DrawLine(pen, ref_line_start[r], new PointF(x_axis_pts[1].X, ref_line_start[r].Y));
                        StringFormat format_refline = new StringFormat();
                        format_refline.Alignment = StringAlignment.Near;
                        format_refline.LineAlignment = ref_line.Key > 0 ? StringAlignment.Far : StringAlignment.Near;
                        using (Brush brush = new SolidBrush(ref_line.Value))
                            g.DrawString(ref_line.Key.ToString("N2"), axisFont, brush, ref_line_start[r], format_refline);
                        r++;
                    }

                    axisFont.Dispose();

                    DrawWatermark(g, _background);
                }
            }
            catch (Exception)
            {

            }

            return image;
        }

        private void DrawWatermark(Graphics g, bool wanted)
        {
            if (!wanted) return;
            using (Font font = new Font("Arial", 24, FontStyle.Bold))
            using (Brush brush = new SolidBrush(Color.FromArgb(35, Color.SteelBlue)))
                g.DrawString("BridGHe", font, brush, 12, 12);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;

namespace BridGHe.Common.Plots;

public readonly record struct PlotPoint(double X, double Y);
public sealed record PlotSeries(string Name, IReadOnlyList<PlotPoint> Points);
public readonly record struct PlotRange(double X, double Minimum, double Maximum);

/// <summary>Presentation only. Original samples and discontinuities are retained; no interpolation or smoothing.</summary>
public sealed class BridgePlot
{
    private static readonly string[] Colors = { "#176f87", "#d27828", "#6b4da0", "#a63c54", "#38815b", "#596776", "#9b6b25" };
    public string Title { get; }
    public string XLabel { get; }
    public string YLabel { get; }
    public IReadOnlyList<PlotSeries> Series { get; }
    public IReadOnlyList<IReadOnlyList<PlotRange>> Envelope { get; }
    public IReadOnlyList<double> ElementBoundaries { get; }
    private readonly string[] names;
    private readonly double xmin, xmax, ymin, ymax;
    private const int Width=1500, PlotHeight=680, Left=110, Right=35, Top=80, Bottom=65;
    private int Height => PlotHeight + 64 + 24 * ((names.Length + 2) / 3);
    private static string N(double value) => value.ToString("0.######",CultureInfo.InvariantCulture);

    public BridgePlot(string title, string xLabel, string yLabel, IEnumerable<PlotSeries> series,
        IEnumerable<IReadOnlyList<PlotRange>> envelope = null, IEnumerable<double> elementBoundaries = null)
    {
        Title=title??"";XLabel=xLabel??"";YLabel=yLabel??"";
        Series=series.Select(s=>new PlotSeries(s.Name??"",s.Points.ToArray())).ToArray();
        Envelope=(envelope??Array.Empty<IReadOnlyList<PlotRange>>()).Select(r=>(IReadOnlyList<PlotRange>)r.ToArray()).ToArray();
        ElementBoundaries=(elementBoundaries??Array.Empty<double>()).Distinct().OrderBy(x=>x).ToArray();
        if(ElementBoundaries.Any(x=>!double.IsFinite(x)) || Envelope.SelectMany(r=>r).Any(r=>
            !double.IsFinite(r.X) || !(double.IsNaN(r.Minimum)&&double.IsNaN(r.Maximum)) &&
            (!double.IsFinite(r.Minimum)||!double.IsFinite(r.Maximum)||r.Minimum>r.Maximum)))
            throw new ArgumentException("Invalid envelope or element boundary.");
        names=Series.Select(s=>s.Name).Distinct(StringComparer.Ordinal).ToArray();
        if(Series.SelectMany(s=>s.Points).Any(p=>!double.IsFinite(p.X)||double.IsInfinity(p.Y)))
            throw new ArgumentException("Non-finite plot coordinate. NaN ordinates are allowed as gaps.");
        var points=Series.SelectMany(s=>s.Points).Concat(Envelope.SelectMany(r=>r)
            .SelectMany(r=>new[]{new PlotPoint(r.X,r.Minimum),new PlotPoint(r.X,r.Maximum)}))
            .Where(p=>double.IsFinite(p.Y)).ToArray();
        if(points.Length==0)throw new ArgumentException("No finite results to plot.");
        xmin=points.Min(p=>p.X);xmax=points.Max(p=>p.X);
        if(ElementBoundaries.Count>0){xmin=Math.Min(xmin,ElementBoundaries[0]);xmax=Math.Max(xmax,ElementBoundaries[^1]);}
        ymin=Math.Min(0,points.Min(p=>p.Y));ymax=Math.Max(0,points.Max(p=>p.Y));
        if(xmin==xmax){xmin-=.5;xmax+=.5;}
        if(ymin==ymax){ymin-=.5;ymax+=.5;}
        double margin=(ymax-ymin)*.06;ymin-=margin;ymax+=margin;
    }
    private float X(double x)=>(float)(Left+(x-xmin)/(xmax-xmin)*(Width-Left-Right));
    private float Y(double y)=>(float)(Top+(ymax-y)/(ymax-ymin)*(PlotHeight-Top-Bottom));
    private string ColorFor(string name)=>Colors[Array.IndexOf(names,name)%Colors.Length];

    // Each group is one element. NaN ranges split missing stations; no segment crosses a gap or an element boundary.
    private IEnumerable<PlotRange[]> EnvelopeSegments()
    {
        foreach(var element in Envelope)
        {
            var segment=new List<PlotRange>();
            foreach(var range in element)
            {
                if(double.IsNaN(range.Minimum))
                {if(segment.Count>0)yield return segment.ToArray();segment.Clear();}
                else segment.Add(range);
            }
            if(segment.Count>0)yield return segment.ToArray();
        }
    }
    private PointF[] BandPoints(PlotRange[] segment)=>segment.Select(r=>new PointF(X(r.X),Y(r.Maximum)))
        .Concat(segment.Reverse().Select(r=>new PointF(X(r.X),Y(r.Minimum)))).ToArray();
    private (PlotPoint Min,PlotPoint Max) Extrema()
    {
        var points=(Envelope.Count>0?Envelope.SelectMany(e=>e).SelectMany(r=>new[]{new PlotPoint(r.X,r.Minimum),new PlotPoint(r.X,r.Maximum)}):Series.SelectMany(s=>s.Points))
            .Where(p=>double.IsFinite(p.Y)).ToArray();
        return(points.MinBy(p=>p.Y),points.MaxBy(p=>p.Y));
    }

    public string Svg()
    {
        var text=new StringBuilder();
        using(var xml=XmlWriter.Create(text,new XmlWriterSettings{OmitXmlDeclaration=true,Indent=false}))
        {
            xml.WriteStartElement("svg","http://www.w3.org/2000/svg");
            xml.WriteAttributeString("width",Width.ToString());xml.WriteAttributeString("height",Height.ToString());
            xml.WriteAttributeString("viewBox",$"0 0 {Width} {Height}");
            void Element(string tag,params (string Key,string Value)[] attrs){xml.WriteStartElement(tag);foreach(var a in attrs)xml.WriteAttributeString(a.Key,a.Value);xml.WriteEndElement();}
            void Label(double x,double y,string value,string anchor="start",int size=16){xml.WriteStartElement("text");xml.WriteAttributeString("x",N(x));xml.WriteAttributeString("y",N(y));xml.WriteAttributeString("font-family","Segoe UI,Arial,sans-serif");xml.WriteAttributeString("font-size",size.ToString());xml.WriteAttributeString("fill","#203b50");xml.WriteAttributeString("text-anchor",anchor);xml.WriteString(value);xml.WriteEndElement();}
            Element("rect",("width","100%"),("height","100%"),("fill","white"));
            Label(Left,38,Title,size:24);Label(Width-Right,38,"BridGHe",anchor:"end",size:18);
            foreach(var segment in EnvelopeSegments())
                Element("polygon",("class","envelope"),("points",string.Join(" ",BandPoints(segment).Select(p=>$"{N(p.X)},{N(p.Y)}"))),
                    ("fill","#e3e6e8"),("stroke","#a4adb3"),("stroke-width","1"));
            for(int i=0;i<=5;i++)
            {
                double x=xmin+(xmax-xmin)*i/5,y=ymin+(ymax-ymin)*i/5;
                Element("path",("d",$"M{N(X(x))},{Top}V{PlotHeight-Bottom} M{Left},{N(Y(y))}H{Width-Right}"),("stroke","#e1e8eb"),("fill","none"));
                Label(X(x),PlotHeight-Bottom+25,x.ToString("G5",CultureInfo.InvariantCulture),"middle");
                Label(Left-12,Y(y)+5,y.ToString("G5",CultureInfo.InvariantCulture),"end");
            }
            Element("path",("d",$"M{Left},{N(Y(0))}H{Width-Right}"),("stroke","#8396a0"));
            foreach(double station in ElementBoundaries)
            {
                Element("path",("class","element-boundary"),("d",$"M{N(X(station))},{Top}V{PlotHeight-Bottom}"),("stroke","#81919b"),("stroke-dasharray","4 5"));
                Label(X(station),PlotHeight-Bottom+43,station.ToString("G5",CultureInfo.InvariantCulture),"middle",12);
            }
            Label(Left,65,YLabel);Label((Left+Width-Right)/2.0,PlotHeight-10,XLabel,"middle");
            foreach(var s in Series)
            {
                var d=new StringBuilder();bool start=true;
                foreach(var p in s.Points)
                {
                    if(double.IsNaN(p.Y)){start=true;continue;}
                    d.Append(start?'M':'L').Append(N(X(p.X))).Append(',').Append(N(Y(p.Y)));start=false;
                }
                xml.WriteStartElement("path");xml.WriteAttributeString("d",d.ToString());xml.WriteAttributeString("stroke",ColorFor(s.Name));xml.WriteAttributeString("stroke-width","2");xml.WriteAttributeString("fill","none");xml.WriteElementString("title",s.Name);xml.WriteEndElement();
                for(int i=0;i<s.Points.Count;i++)
                    if(double.IsFinite(s.Points[i].Y) && (i==0||double.IsNaN(s.Points[i-1].Y)) && (i==s.Points.Count-1||double.IsNaN(s.Points[i+1].Y)))
                    {var p=s.Points[i];Element("circle",("cx",N(X(p.X))),("cy",N(Y(p.Y))),("r","3"),("fill",ColorFor(s.Name)));}
            }
            for(int i=0;i<names.Length;i++)
            {
                int x=Left+i%3*445,y=PlotHeight+26+i/3*24;
                Element("path",("d",$"M{x},{y-5}h25"),("stroke",ColorFor(names[i])),("stroke-width","3"));Label(x+35,y,names[i],size:14);
            }
            var extrema=Extrema();
            foreach(var (point,label,dy) in new[]{(extrema.Min,"Min",20),(extrema.Max,"Max",-12)})
            {
                Element("circle",("cx",N(X(point.X))),("cy",N(Y(point.Y))),("r","3"),("fill","#203b50"));
                Label(X(point.X),Y(point.Y)+dy,$"{label} {point.Y.ToString("G6",CultureInfo.InvariantCulture)}",X(point.X)>Width/2?"end":"start",14);
            }
            if(Envelope.Count>0)Label(Left,Height-12,"Grey envelope: all combinations",size:13);
            xml.WriteEndElement();
        }
        return text.ToString();
    }

    public Bitmap Png()
    {
        var bitmap=new Bitmap(Width,Height);
        try
        {
            using var g=Graphics.FromImage(bitmap);g.Clear(Color.White);g.SmoothingMode=SmoothingMode.AntiAlias;
            using var font=new Font("Segoe UI",11);using var heading=new Font("Segoe UI",17);
            using var ink=new SolidBrush(Color.FromArgb(32,59,80));using var grid=new Pen(Color.FromArgb(225,232,235));
            g.DrawString(Title,heading,ink,Left,20);g.DrawString("BridGHe",font,ink,Width-120,25);
            using var bandBrush=new SolidBrush(Color.FromArgb(227,230,232));
            using var bandPen=new Pen(Color.FromArgb(164,173,179));
            foreach(var segment in EnvelopeSegments())
            {
                var polygon=BandPoints(segment);
                if(polygon.Length>=3){g.FillPolygon(bandBrush,polygon);g.DrawPolygon(bandPen,polygon);}
                else g.DrawLine(bandPen,polygon[0],polygon[1]);
            }
            for(int i=0;i<=5;i++)
            {
                double x=xmin+(xmax-xmin)*i/5,y=ymin+(ymax-ymin)*i/5;
                g.DrawLine(grid,X(x),Top,X(x),PlotHeight-Bottom);g.DrawLine(grid,Left,Y(y),Width-Right,Y(y));
                g.DrawString(x.ToString("G5",CultureInfo.InvariantCulture),font,ink,X(x)-15,PlotHeight-Bottom+8);
                g.DrawString(y.ToString("G5",CultureInfo.InvariantCulture),font,ink,8,Y(y)-10);
            }
            g.DrawString(YLabel,font,ink,Left,52);g.DrawString(XLabel,font,ink,Width/2,PlotHeight-30);
            using var boundaryPen=new Pen(Color.FromArgb(129,145,155)){DashStyle=DashStyle.Dash};
            foreach(double station in ElementBoundaries)
            {
                g.DrawLine(boundaryPen,X(station),Top,X(station),PlotHeight-Bottom);
                g.DrawString(station.ToString("G5",CultureInfo.InvariantCulture),font,ink,X(station)-15,PlotHeight-Bottom+28);
            }
            using var zeroPen=new Pen(Color.FromArgb(131,150,160));g.DrawLine(zeroPen,Left,Y(0),Width-Right,Y(0));
            foreach(var s in Series)
            {
                using var pen=new Pen(ColorTranslator.FromHtml(ColorFor(s.Name)),2);PointF? previous=null;
                foreach(var p in s.Points)
                {
                    if(double.IsNaN(p.Y)){previous=null;continue;}
                    var point=new PointF(X(p.X),Y(p.Y));if(previous.HasValue)g.DrawLine(pen,previous.Value,point);else g.DrawEllipse(pen,point.X-2,point.Y-2,4,4);previous=point;
                }
            }
            for(int i=0;i<names.Length;i++)
            {
                int x=Left+i%3*445,y=PlotHeight+10+i/3*24;using var pen=new Pen(ColorTranslator.FromHtml(ColorFor(names[i])),3);
                g.DrawLine(pen,x,y+9,x+25,y+9);g.DrawString(names[i],font,ink,x+35,y);
            }
            var extrema=Extrema();
            foreach(var (point,label,dy) in new[]{(extrema.Min,"Min",5),(extrema.Max,"Max",-24)})
            {
                string value=$"{label} {point.Y.ToString("G6",CultureInfo.InvariantCulture)}";
                float x=X(point.X);if(x>Width/2)x-=g.MeasureString(value,font).Width;
                g.FillEllipse(ink,X(point.X)-3,Y(point.Y)-3,6,6);g.DrawString(value,font,ink,x,Y(point.Y)+dy);
            }
            if(Envelope.Count>0)g.DrawString("Grey envelope: all combinations",font,ink,Left,Height-25);
            return bitmap;
        }
        catch{bitmap.Dispose();throw;}
    }
}

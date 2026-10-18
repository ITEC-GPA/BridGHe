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

/// <summary>Presentation only. Original samples and discontinuities are retained; no interpolation or smoothing.</summary>
public sealed class BridgePlot
{
    private static readonly string[] Colors = { "#176f87", "#d27828", "#6b4da0", "#a63c54", "#38815b", "#596776", "#9b6b25" };
    public string Title { get; }
    public string XLabel { get; }
    public string YLabel { get; }
    public IReadOnlyList<PlotSeries> Series { get; }
    private readonly string[] names;
    private readonly double xmin, xmax, ymin, ymax;
    private const int Width=1500, PlotHeight=680, Left=110, Right=35, Top=80, Bottom=65;
    private int Height => PlotHeight + 40 + 24 * ((names.Length + 2) / 3);
    private static string N(double value) => value.ToString("0.######",CultureInfo.InvariantCulture);

    public BridgePlot(string title, string xLabel, string yLabel, IEnumerable<PlotSeries> series)
    {
        Title=title??"";XLabel=xLabel??"";YLabel=yLabel??"";
        Series=series.Select(s=>new PlotSeries(s.Name??"",s.Points.ToArray())).ToArray();
        names=Series.Select(s=>s.Name).Distinct(StringComparer.Ordinal).ToArray();
        if(Series.SelectMany(s=>s.Points).Any(p=>!double.IsFinite(p.X)||double.IsInfinity(p.Y)))
            throw new ArgumentException("Non-finite plot coordinate. NaN ordinates are allowed as gaps.");
        var points=Series.SelectMany(s=>s.Points).Where(p=>double.IsFinite(p.Y)).ToArray();
        if(points.Length==0)throw new ArgumentException("No finite results to plot.");
        xmin=points.Min(p=>p.X);xmax=points.Max(p=>p.X);
        ymin=Math.Min(0,points.Min(p=>p.Y));ymax=Math.Max(0,points.Max(p=>p.Y));
        if(xmin==xmax){xmin-=.5;xmax+=.5;}
        if(ymin==ymax){ymin-=.5;ymax+=.5;}
        double margin=(ymax-ymin)*.06;ymin-=margin;ymax+=margin;
    }
    private float X(double x)=>(float)(Left+(x-xmin)/(xmax-xmin)*(Width-Left-Right));
    private float Y(double y)=>(float)(Top+(ymax-y)/(ymax-ymin)*(PlotHeight-Top-Bottom));
    private string ColorFor(string name)=>Colors[Array.IndexOf(names,name)%Colors.Length];

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
            for(int i=0;i<=5;i++)
            {
                double x=xmin+(xmax-xmin)*i/5,y=ymin+(ymax-ymin)*i/5;
                Element("path",("d",$"M{N(X(x))},{Top}V{PlotHeight-Bottom} M{Left},{N(Y(y))}H{Width-Right}"),("stroke","#e1e8eb"),("fill","none"));
                Label(X(x),PlotHeight-Bottom+25,x.ToString("G5",CultureInfo.InvariantCulture),"middle");
                Label(Left-12,Y(y)+5,y.ToString("G5",CultureInfo.InvariantCulture),"end");
            }
            Element("path",("d",$"M{Left},{N(Y(0))}H{Width-Right}"),("stroke","#8396a0"));
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
            for(int i=0;i<=5;i++)
            {
                double x=xmin+(xmax-xmin)*i/5,y=ymin+(ymax-ymin)*i/5;
                g.DrawLine(grid,X(x),Top,X(x),PlotHeight-Bottom);g.DrawLine(grid,Left,Y(y),Width-Right,Y(y));
                g.DrawString(x.ToString("G5",CultureInfo.InvariantCulture),font,ink,X(x)-15,PlotHeight-Bottom+8);
                g.DrawString(y.ToString("G5",CultureInfo.InvariantCulture),font,ink,8,Y(y)-10);
            }
            g.DrawString(YLabel,font,ink,Left,52);g.DrawString(XLabel,font,ink,Width/2,PlotHeight-30);
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
            return bitmap;
        }
        catch{bitmap.Dispose();throw;}
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Display
{
    public static class ModelPreview
    {
        private sealed class Cached { public long Revision=-1; public CompositeSectionPreviewGeometry Start, End; public BeamModel Beam; }
        private static readonly ConditionalWeakTable<CompositeBeamModel,Cached> composites=new();
        public static bool Automatic => DisplayPreferences.Current.Enabled && DisplayPreferences.Current.Automatic;
        public static Brep[] Shapes(ElementModel element) => element switch
        { BeamModel b=>b.Shapes, CompositeBeamModel b=>b.Shapes, PlateModel p when p.Shape!=null=>new[]{p.Shape}, _=>Array.Empty<Brep>() };
        public static IEnumerable<ElementModel> Elements(object value) => value switch
        { ModelModel m=>m.Elements, ElementModel e=>new[]{e}, _=>Array.Empty<ElementModel>() };
        public static BoundingBox Bounds(ElementModel element)
        {
            var box=BoundingBox.Empty;
            foreach(var shape in Shapes(element)) if(shape!=null)box.Union(shape.GetBoundingBox(true));
            switch(element){case NodeModel n:box.Union(n.Position);break;case BeamModel b:box.Union(b.PointFrom);box.Union(b.PointTo);break;case CompositeBeamModel b:box.Union(b.PointFrom);box.Union(b.PointTo);break;}
            return box;
        }
        private static Cached Cache(CompositeBeamModel beam)
        {
            var c=composites.GetOrCreateValue(beam);
            if(c.Revision!=beam.GeometryRevision)
            {
                c.Start=new(beam.StartBeamProperty);c.End=new(beam.EndBeamProperty);
                c.Beam=new BeamModel(beam.BeamId,beam.PointFrom,beam.PointTo,new BeamPropertyModel{Section=c.Start.Steel,Material=beam.StartBeamProperty.SteelMaterial,PropertyType=BeamPropertyType.Beam});
                c.Revision=beam.GeometryRevision;
            }
            return c;
        }
        public static Line[] ReinforcementLines(ElementModel element)
        {
            CompositeSectionPreviewGeometry start,end;Transform transform;Vector3d length;Vector2d offset=Vector2d.Zero;
            if(element is BeamModel b && b.BeamProperty?.Section?.CompositePreview is {} preview)
            {start=end=preview;b.GetLocalAxes(false,out _,out _,out _,out transform);length=b.PointTo-b.PointFrom;offset=b.Offset;}
            else if(element is CompositeBeamModel c && c.StartBeamProperty!=null && c.EndBeamProperty!=null)
            {var cache=Cache(c);start=cache.Start;end=cache.End;c.GetLocalAxes(out _,out _,out _,out transform);length=c.PointTo-c.PointFrom;}
            else return Array.Empty<Line>();
            var lines=new List<Line>();
            foreach(bool top in new[]{true,false})if(start.HasRow(top)||end.HasRow(top))for(int i=0;i<3;i++)
            {var a=start.RebarPoint(top,i);var z=end.RebarPoint(top,i);a+=new Vector3d(offset.X,offset.Y,0);z+=new Vector3d(offset.X,offset.Y,0);a.Transform(transform);z.Transform(transform);z+=length;lines.Add(new Line(a,z));}
            return lines.ToArray();
        }
        public static void Wires(ElementModel element,DisplayPipeline pipeline,RhinoViewport viewport,Color color,DisplayOptions options)
        {
            if(element==null || !options.Enabled || !DisplayPreferences.Current.Enabled)return;
            if(options.Geometry)element.DrawWireframe(pipeline,viewport,color);
            bool composite=element is CompositeBeamModel || element is BeamModel cb && cb.BeamProperty?.Section?.CompositePreview!=null;
            if(options.Sections){var shapes=Shapes(element);for(int i=0;i<shapes.Length;i++)if(shapes[i]!=null)pipeline.DrawBrepWires(shapes[i],composite?(i==0?Color.SteelBlue:Color.DarkGray):color);}
            if(options.Reinforcement)foreach(var line in ReinforcementLines(element))pipeline.DrawLine(line,Color.DarkOrange,2);
            var drawElement=element is CompositeBeamModel c && c.StartBeamProperty!=null && c.EndBeamProperty!=null?Cache(c).Beam:element;
            if(options.Loads)using(DrawingHelper.Scale(options.LoadScale))foreach(var load in element.Loads)
                if(options.Matches(load.LoadCase?.Name))load.Draw(pipeline,viewport,Color.OrangeRed,drawElement);
            using(DrawingHelper.Scale(options.SymbolScale))foreach(var attribute in element.Attributes)
            {
                bool show=attribute switch
                {NodeRestraintAttributeModel or NodeStiffnessAttributeModel or BeamSupportAttributeModel or PlateSupportAttributeModel=>options.Supports,
                 BeamReleasesAttributeModel=>options.Releases,_=>options.OtherAttributes};
                if(show)attribute.Draw(pipeline,viewport,color,drawElement);
            }
            var bounds=Bounds(element);if(!bounds.IsValid)return;
            if(options.Axes)
            {
                Vector3d x=Vector3d.XAxis,y=Vector3d.YAxis,z=Vector3d.ZAxis;
                if(element is BeamModel beam)beam.GetLocalAxes(true,out x,out y,out z,out _);
                else if(element is CompositeBeamModel beam2)beam2.GetLocalAxes(out x,out y,out z,out _);
                else if(element is PlateModel plate)plate.GetLocalAxes(out x,out y,out z);
                using(DrawingHelper.Scale(options.SymbolScale)){double size=DrawingHelper.GetScaleFactor(viewport,bounds.Center)*2;pipeline.DrawArrow(new Line(bounds.Center,bounds.Center+x*size),Color.Red);pipeline.DrawArrow(new Line(bounds.Center,bounds.Center+y*size),Color.Green);pipeline.DrawArrow(new Line(bounds.Center,bounds.Center+z*size),Color.Blue);}
            }
            if(options.Labels){string label=element switch{BeamModel b=>b.BeamId,CompositeBeamModel b=>b.BeamId,NodeModel n=>n.NodeId,PlateModel p=>p.PlateId,_=>""};if(!string.IsNullOrEmpty(label))pipeline.DrawDot(bounds.Center,label,color,Color.White);}
        }
        public static void Meshes(ElementModel element,DisplayPipeline pipeline,DisplayOptions options)
        {
            if(!options.Enabled || !DisplayPreferences.Current.Enabled || !options.Sections || !options.Shaded)return;
            bool composite=element is CompositeBeamModel || element is BeamModel b && b.BeamProperty?.Section?.CompositePreview!=null;
            var shapes=Shapes(element);for(int i=0;i<shapes.Length;i++)if(shapes[i]!=null)
            {using var material=new DisplayMaterial(composite?(i==0?Color.SteelBlue:Color.LightGray):Color.CadetBlue,options.Transparency);pipeline.DrawBrepShaded(shapes[i],material);}
        }
        public static void Moving(MovingPath path,DisplayPipeline pipeline,Color color,DisplayOptions options)
        {
            if(!options.Enabled || !DisplayPreferences.Current.Enabled || !options.Loads || !options.Matches(path.LoadCaseName) || path.Coordinates?.Length!=9 || path.Coordinates.Any(x=>!double.IsFinite(x)))return;
            var c=path.Coordinates;var points=new[]{new Point3d(c[0],c[1],c[2]),new Point3d(c[3],c[4],c[5]),new Point3d(c[6],c[7],c[8])};
            if(path.Shape==0)pipeline.DrawLine(points[0],points[1],color);
            for(int i=0;i<3;i++)if(options.Labels)pipeline.DrawDot(points[i],i==0?"Start":i==1?"End":"Definition",color,Color.White);else pipeline.DrawPoint(points[i],color);
        }
    }
}

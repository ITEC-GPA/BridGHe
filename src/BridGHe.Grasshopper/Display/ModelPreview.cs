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
        private sealed class Cached { public long Revision=-1; public object StartKey, EndKey; public CompositeSectionPreviewGeometry Start, End; public BeamModel Beam; }
        /// <summary>Chain E (c): the composite sections of a composite beam (its definitions), what the preview of the beam reads; without them
        /// (a composite beam value without its GH type) the FEM properties of the value, until the FeMM models go.</summary>
        public readonly record struct CompositeSections(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition Start, GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition End)
        {
            public bool Complete => Start != null && End != null;
        }
        private static readonly MaterialModel previewMaterial = new MaterialModel("BridGHe preview");
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
        private static bool HasSections(CompositeBeamModel beam,CompositeSections sections)=>sections.Complete || beam.StartBeamProperty!=null && beam.EndBeamProperty!=null;
        private static Cached Cache(CompositeBeamModel beam,CompositeSections sections)
        {
            var c=composites.GetOrCreateValue(beam);
            object startKey=sections.Complete?sections.Start:beam.StartBeamProperty, endKey=sections.Complete?sections.End:beam.EndBeamProperty;
            if(c.Revision!=beam.GeometryRevision || !ReferenceEquals(c.StartKey,startKey) || !ReferenceEquals(c.EndKey,endKey))
            {
                if(sections.Complete){c.Start=new(sections.Start);c.End=ReferenceEquals(sections.Start,sections.End)?c.Start:new(sections.End);}
                else{c.Start=new(beam.StartBeamProperty);c.End=new(beam.EndBeamProperty);}
                // The beam that draws the loads and the attributes: its axes and ends (the material is not drawn).
                c.Beam=new BeamModel(beam.BeamId,beam.PointFrom,beam.PointTo,new BeamPropertyModel{Section=c.Start.Steel,Material=previewMaterial,PropertyType=BeamPropertyType.Beam});
                c.Revision=beam.GeometryRevision;c.StartKey=startKey;c.EndKey=endKey;
            }
            return c;
        }
        public static Line[] ReinforcementLines(ElementModel element) => SectionReinforcementLines(element,default);
        public static Line[] SectionReinforcementLines(ElementModel element,CompositeSections sections)
        {
            CompositeSectionPreviewGeometry start,end;Transform transform;Vector3d length;Vector2d offset=Vector2d.Zero;
            if(element is BeamModel b && b.BeamProperty?.Section?.CompositePreview is {} preview)
            {start=end=preview;b.GetLocalAxes(false,out _,out _,out _,out transform);length=b.PointTo-b.PointFrom;offset=b.Offset;}
            else if(element is CompositeBeamModel c && HasSections(c,sections))
            {var cache=Cache(c,sections);start=cache.Start;end=cache.End;c.GetLocalAxes(out _,out _,out _,out transform);length=c.PointTo-c.PointFrom;}
            else return Array.Empty<Line>();
            var lines=new List<Line>();
            foreach(bool top in new[]{true,false})if(start.HasRow(top)||end.HasRow(top))for(int i=0;i<3;i++)
            {var a=start.RebarPoint(top,i);var z=end.RebarPoint(top,i);a+=new Vector3d(offset.X,offset.Y,0);z+=new Vector3d(offset.X,offset.Y,0);a.Transform(transform);z.Transform(transform);z+=length;lines.Add(new Line(a,z));}
            return lines.ToArray();
        }
        public static void Wires(ElementModel element,DisplayPipeline pipeline,RhinoViewport viewport,Color color,DisplayOptions options) => SectionWires(element,pipeline,viewport,color,options,default);
        /// <summary>Chain E (c): <see cref="Wires(ElementModel, DisplayPipeline, RhinoViewport, Color, DisplayOptions)"/> of a composite beam with its definitions.</summary>
        public static void SectionWires(ElementModel element,DisplayPipeline pipeline,RhinoViewport viewport,Color color,DisplayOptions options,CompositeSections sections)
        {
            if(element==null || !options.Enabled || !DisplayPreferences.Current.Enabled)return;
            if(options.Geometry)element.DrawWireframe(pipeline,viewport,color);
            bool composite=element is CompositeBeamModel || element is BeamModel cb && cb.BeamProperty?.Section?.CompositePreview!=null;
            if(options.Sections){var shapes=Shapes(element);for(int i=0;i<shapes.Length;i++)if(shapes[i]!=null)pipeline.DrawBrepWires(shapes[i],composite?(i==0?Color.SteelBlue:Color.DarkGray):color);}
            if(options.Reinforcement)foreach(var line in SectionReinforcementLines(element,sections))pipeline.DrawLine(line,Color.DarkOrange,2);
            var drawElement=element is CompositeBeamModel c && HasSections(c,sections)?Cache(c,sections).Beam:element;
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
        /// <summary>C3 (11/11): the section solids and the bar lines of a draft given by its GPC data alone, made once per draft (the drafts and
        /// the composite datum are immutable by convention): a beam (<see cref="Common.Gpc.GpcDraftShapes.Beam"/>), a composite beam from its
        /// two definitions (<see cref="Common.Gpc.GpcDraftShapes.Composite"/> and its bars), a plate (its solid,
        /// <see cref="DataTypes.GpcDraftGeometry.Shape"/>), as the FEM value drew them.</summary>
        private sealed class DraftGeometry { public Brep[] Shapes=Array.Empty<Brep>(); public Line[] Bars=Array.Empty<Line>(); public bool Composite; }
        private static readonly ConditionalWeakTable<object,DraftGeometry> draftGeometry=new();
        private static DraftGeometry Draft(object draft)=>draft==null?new DraftGeometry():draftGeometry.GetValue(draft,d=>
        {
            // The preview never throws: a draft whose geometry cannot be made (an invalid section) draws its axis and label only.
            try
            {
                return d switch
                {
                    Common.Gpc.Legacy.LegacyBeamDraft b=>new DraftGeometry{Shapes=Common.Gpc.GpcDraftShapes.Beam(b),Bars=Common.Gpc.GpcDraftShapes.Bars(b),Composite=Common.Gpc.GpcDraftShapes.HasCompositePreview(b)}, // P11 (P2D): a homogenised composite property with its outline
                    DataTypes.GpcCompositeBeam c=>new DraftGeometry{Shapes=Common.Gpc.GpcDraftShapes.Composite(c.Beam,c.Start,c.End),Bars=Common.Gpc.GpcDraftShapes.CompositeBars(c.Beam,c.Start,c.End),Composite=true},
                    Common.Gpc.Legacy.LegacyPlateDraft p=>new DraftGeometry{Shapes=DataTypes.GpcDraftGeometry.Shape(p) is {} s?new[]{s}:Array.Empty<Brep>()},
                    _=>new DraftGeometry(),
                };
            }
            catch(Exception){return new DraftGeometry();}
        });
        /// <summary>C3: the section solids of a draft (<see cref="Draft"/>), in the order the FEM preview drew them.</summary>
        public static Brep[] DraftShapes(object draft)=>Draft(draft).Shapes;
        /// <summary>C3: the schematic bar lines of a draft (a composite beam given by its GPC datum alone).</summary>
        public static Line[] DraftReinforcementLines(object draft)=>Draft(draft).Bars;
        /// <summary>C3: the bounding box of a draft with its solids, as <see cref="Bounds(ElementModel)"/> of the FEM value (the beam of a
        /// composite datum).</summary>
        public static BoundingBox DraftBounds(object draft)
        {
            var box=DataTypes.GpcDraftGeometry.Bounds(draft is DataTypes.GpcCompositeBeam c?c.Beam:draft);
            foreach(var shape in DraftShapes(draft))if(shape!=null)box.Union(shape.GetBoundingBox(true));
            return box;
        }
        /// <summary>R6.5 P0 (coordinator, 7/11): the preview of an element given by its GPC draft alone (no FEM value): the geometry (point,
        /// axis, outline), the label and (P2X) the local axes of a plate; C3 (11/11): the section solids of beams and plates and the solids and
        /// bars of a composite beam (its GPC datum, <see cref="DataTypes.GpcCompositeBeam"/>), with the colours of the FEM preview; loads and
        /// attributes are drawn from the FEM value until the components draw them on GPC.</summary>
        public static void DraftWires(object draft,DisplayPipeline pipeline,RhinoViewport viewport,Color color,DisplayOptions options)
        {
            if(draft==null || !options.Enabled || !DisplayPreferences.Current.Enabled)return;
            var element=draft is DataTypes.GpcCompositeBeam cb?cb.Beam:draft;
            if(element==null)return;
            if(options.Geometry)switch(element)
            {
                case Common.Gpc.Legacy.LegacyNodeDraft n:{var p=DataTypes.GpcDraftGeometry.Position(n);if(p.IsValid)pipeline.DrawPoint(p,color);break;}
                case Common.Gpc.Legacy.LegacyBeamDraft b:{var l=DataTypes.GpcDraftGeometry.Axis(b);if(l.IsValid)pipeline.DrawLine(l,color);break;}
                case Common.Gpc.Legacy.LegacyPlateDraft p:{var outline=DataTypes.GpcDraftGeometry.Outline(p);if(outline!=null)pipeline.DrawCurve(outline,color);break;}
            }
            var geometry=Draft(draft);
            if(options.Sections)for(int i=0;i<geometry.Shapes.Length;i++)if(geometry.Shapes[i]!=null)pipeline.DrawBrepWires(geometry.Shapes[i],geometry.Composite?(i==0?Color.SteelBlue:Color.DarkGray):color);
            if(options.Reinforcement)foreach(var line in geometry.Bars)pipeline.DrawLine(line,Color.DarkOrange,2);
            var bounds=DraftBounds(draft);
            // P2X: the local axes of a plate draft (turned by its local axis angle), drawn as the FEM preview draws those of a plate.
            if(options.Axes && bounds.IsValid && element is Common.Gpc.Legacy.LegacyPlateDraft pd && pd.Corners!=null && (pd.Corners.Count==3 || pd.Corners.Count==4))
            {
                Common.Gpc.Builders.GpcPlateBuilders.LocalAxes(pd.Corners,pd.AngleDeg,out var x,out var y,out var z);
                using(DrawingHelper.Scale(options.SymbolScale)){double size=DrawingHelper.GetScaleFactor(viewport,bounds.Center)*2;pipeline.DrawArrow(new Line(bounds.Center,bounds.Center+x*size),Color.Red);pipeline.DrawArrow(new Line(bounds.Center,bounds.Center+y*size),Color.Green);pipeline.DrawArrow(new Line(bounds.Center,bounds.Center+z*size),Color.Blue);}
            }
            if(options.Labels && bounds.IsValid){string label=DataTypes.GpcDraftGeometry.Label(element);if(label.Length!=0)pipeline.DrawDot(bounds.Center,label,color,Color.White);}
        }
        /// <summary>R6.5 P0: the shaded preview of an element given by its GPC draft alone; C3 (11/11): its solids with the colours of the FEM
        /// preview (<see cref="Meshes"/>), a plate without a solid by its flat mesh as before.</summary>
        public static void DraftMeshes(object draft,DisplayPipeline pipeline,DisplayOptions options)
        {
            if(draft==null || !options.Enabled || !DisplayPreferences.Current.Enabled || !options.Sections || !options.Shaded)return;
            var geometry=Draft(draft);
            for(int i=0;i<geometry.Shapes.Length;i++)if(geometry.Shapes[i]!=null)
            {using var material=new DisplayMaterial(geometry.Composite?(i==0?Color.SteelBlue:Color.LightGray):Color.CadetBlue,options.Transparency);pipeline.DrawBrepShaded(geometry.Shapes[i],material);}
            if(geometry.Shapes.Length==0 && draft is Common.Gpc.Legacy.LegacyPlateDraft p && DataTypes.GpcDraftGeometry.FlatMesh(p) is {} mesh)
            {using var material=new DisplayMaterial(Color.CadetBlue,options.Transparency);pipeline.DrawMeshShaded(mesh,material);}
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

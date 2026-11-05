using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace BridGHe.Common.Models
{
    /// <summary>Physical outlines for display only. Never used to recalculate/export FEM properties.</summary>
    [Serializable]
    public sealed class CompositeSectionPreviewGeometry
    {
        public SectionModel Steel { get; private set; }
        public SectionConcreteSlabModel Slab { get; private set; }

        public CompositeSectionPreviewGeometry(CompositeBeamPropertyModel property)
        {
            Steel = new SectionModel(property.SteelSection);
            Steel.CalculateSection();
            Slab = Helpers.ModelArchive.Clone(property.ConcreteSection);
        }

        /// <summary>Chain E (c): the outlines of a composite section from its definition (the steel H, the slab, the rows of bars as given), the
        /// preview of a composite beam that carries its definitions: the same outlines and bar lines as from the composite property
        /// (<c>gpc-composite-sections</c> compares them bit for bit).</summary>
        public CompositeSectionPreviewGeometry(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var section = definition.Section; var h = (GPC.Model.Sections.SectionH)section.SteelSections[0].Section.SectionShape; var rows = definition.RebarRows;
            Steel = new SectionModel(h.Name)
            {
                Name = h.Name, SectionType = SectionModel.SectionTypes.I, D = h.Height, B1 = h.LenghtBottomFlange, T1 = h.ThicknessBottomFlange,
                B2 = h.LenghtTopFlange, T2 = h.ThicknessTopFlange, T3 = h.ThicknessWeb,
            };
            Steel.CalculateSection();
            Slab = new SectionConcreteSlabModel(section.Height, section.Width, rows?.TopAreaMm2 ?? 0, rows?.TopCoverMm ?? 0, rows?.BottomAreaMm2 ?? 0, rows?.BottomCoverMm ?? 0);
        }

        public CompositeSectionPreviewGeometry Clone() => Helpers.ModelArchive.Clone(this);

        public IEnumerable<PolylineCurve> Outlines()
        {
            double bottom = -Steel.Centroid.Y, top = Steel.D - Steel.Centroid.Y;
            yield return Polygon(new[] {
                (-Steel.B1/2,bottom),(Steel.B1/2,bottom),(Steel.B1/2,bottom+Steel.T1),
                (Steel.T3/2,bottom+Steel.T1),(Steel.T3/2,top-Steel.T2),(Steel.B2/2,top-Steel.T2),
                (Steel.B2/2,top),(-Steel.B2/2,top),(-Steel.B2/2,top-Steel.T2),
                (-Steel.T3/2,top-Steel.T2),(-Steel.T3/2,bottom+Steel.T1),(-Steel.B1/2,bottom+Steel.T1)});
            yield return Polygon(new[]{(-Slab.Width/2,top),(Slab.Width/2,top),(Slab.Width/2,top+Slab.Height),(-Slab.Width/2,top+Slab.Height)});
        }

        private static PolylineCurve Polygon((double x,double y)[] vertices)
        {
            var points=new List<Point3d>();foreach(var p in vertices)points.Add(new Point3d(p.x,p.y,0));
            points.Add(points[0]);return new PolylineCurve(points);
        }

        // The input supplies total row area, not a bar schedule. Draw three schematic
        // longitudinal lines per active row; no bar count/diameter is inferred.
        public Point3d RebarPoint(bool topRow,int index) => new Point3d((index-1)*Slab.Width/3,
            Steel.D-Steel.Centroid.Y+(topRow ? Slab.Height-Slab.TopCover : Slab.BottomCover),0);
        public bool HasRow(bool topRow) => (topRow?Slab.TopRebarArea:Slab.BottomRebarArea)>0;
    }
}

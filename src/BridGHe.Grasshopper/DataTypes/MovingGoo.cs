using System;
using System.Text.Json;
using BridGHe.Common.MovingLoads;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Linq;
using System.Drawing;

namespace BridGHe.Grasshopper.DataTypes
{
    public sealed class MovingGoo : GH_Goo<MovingObject>, IGH_PreviewData
    {
        private Point3d[] DefinitionPoints()
        {
            if(Value is not MovingPath path || path.Coordinates?.Length!=9 || path.Coordinates.Any(x=>!double.IsFinite(x)))return Array.Empty<Point3d>();
            var c=path.Coordinates;return new[]{new Point3d(c[0],c[1],c[2]),new Point3d(c[3],c[4],c[5]),new Point3d(c[6],c[7],c[8])};
        }
        public BoundingBox ClippingBox=>new BoundingBox(DefinitionPoints());
        public void DrawViewportMeshes(GH_PreviewMeshArgs args) { }
        public void DrawViewportWires(GH_PreviewWireArgs args)
        {
            if(Display.ModelPreview.Automatic && Value is MovingPath path)Display.ModelPreview.Moving(path,args.Pipeline,args.Color,Display.DisplayPreferences.Current);
        }
        public MovingGoo() { }
        public MovingGoo(MovingObject value) { Value = value; }
        public override bool IsValid { get { try { Value?.Validate(); return Value != null; } catch { return false; } } }
        public override string TypeName => "Straus moving load";
        public override string TypeDescription => "Carico mobile, veicolo, template o percorso Straus R3.";
        public override IGH_Goo Duplicate() => new MovingGoo(Value?.Clone());
        public override string ToString() => Value?.ToString() ?? "Empty moving load";
        public override bool Write(GH_IWriter writer) { if (Value != null) writer.SetString("Data", JsonSerializer.Serialize(Value)); return true; }
        public override bool Read(GH_IReader reader)
        {
            string json = null;
            if (!reader.TryGetString("Data", ref json)) return false;
            try { Value = JsonSerializer.Deserialize<MovingObject>(json); return IsValid; }
            catch (JsonException) { Value = null; return false; }
        }
    }
}

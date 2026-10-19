using System;
using BridGHe.Common.Straus;
using BridGHe.Grasshopper.DataTypes.Fem;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Sections
{
    public abstract class StrausSectionComponent : GH_Component
    {
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
        protected abstract string ShapeKey { get; }
        protected StrausSectionComponent(string name, string nickname)
            : base(name, nickname, "Sezione nativa Straus R3. Dimensioni nelle unità di lunghezza del modello; angoli in gradi. Richiede licenza API R3.", Helpers.CategoryNameConstants.CATEGORY_MODEL, Helpers.CategoryNameConstants.SUBCATEGORY_SECTIONS) { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            var shape = StrausSectionCatalog.Get(ShapeKey);
            foreach (string dimension in shape.Dimensions)
            {
                string description = dimension.StartsWith("alpha") ? "Angolo [gradi], convenzione BGL Straus R3." : "Dimensione nelle unità di lunghezza del modello; convenzione Straus R3 " + shape.Key + ".";
                if (dimension.StartsWith("R") || dimension.StartsWith("alpha") || dimension == "L")
                    p.AddNumberParameter(dimension, dimension, description, GH_ParamAccess.item, 0);
                else p.AddNumberParameter(dimension, dimension, description, GH_ParamAccess.item);
            }
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Section", "S", "Sezione con proprietà calcolate da Straus R3; compatibile con Beam Property.", GH_ParamAccess.item);
        protected override void SolveInstance(IGH_DataAccess da)
        {
            var dimensions = new double[StrausSectionCatalog.Get(ShapeKey).Dimensions.Length];
            for (int i = 0; i < dimensions.Length; i++) if (!da.GetData(i, ref dimensions[i])) return;
            try { da.SetData(0, new GH_BeamSection(new StrausSection { Key = ShapeKey, Dimensions = dimensions }.Calculate())); }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); }
        }
    }

    public sealed class StrausBxsSectionComponent : GH_Component
    {
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
        public StrausBxsSectionComponent() : base("Straus BXS Section", "S7BXS", "Sezione arbitraria da file BXS esistente. Richiede licenza API R3.", Helpers.CategoryNameConstants.CATEGORY_MODEL, Helpers.CategoryNameConstants.SUBCATEGORY_SECTIONS) { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("BXS file", "File", "Percorso assoluto del file .bxs.", GH_ParamAccess.item);
            p.AddIntegerParameter("Length unit", "U", "Unità del modello: 0=m, 1=cm, 2=mm, 3=ft, 4=in.", GH_ParamAccess.item, 2);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Section", "S", "Sezione BXS.", GH_ParamAccess.item);
        protected override void SolveInstance(IGH_DataAccess da)
        {
            string file = null; int unit = 2; if (!da.GetData(0, ref file) || !da.GetData(1, ref unit)) return;
            try { da.SetData(0, new GH_BeamSection(new StrausSection { Key = "BXS", BxsPath = file, LengthUnit = unit }.Calculate())); }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); }
        }
        public override Guid ComponentGuid => new Guid("ce4934d8-7d81-4b8e-b04d-5b765c9af5a1");
    }

    public sealed class StrausLibrarySectionComponent : GH_Component
    {
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
        public StrausLibrarySectionComponent() : base("Straus Library Section", "S7Lib", "Profilo delle librerie BSL/BGL installate in Straus R3. Materiale definito dal componente Beam Property.", Helpers.CategoryNameConstants.CATEGORY_MODEL, Helpers.CategoryNameConstants.SUBCATEGORY_SECTIONS) { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Library", "Lib", "Nome della libreria senza estensione.", GH_ParamAccess.item);
            p.AddTextParameter("Item", "I", "Nome esatto del profilo in libreria.", GH_ParamAccess.item);
            p.AddBooleanParameter("BGL", "BGL", "True: libreria geometrica BGL. False: libreria proprietà BSL.", GH_ParamAccess.item, false);
            p.AddIntegerParameter("Length unit", "U", "Unità del modello: 0=m, 1=cm, 2=mm, 3=ft, 4=in.", GH_ParamAccess.item, 2);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Section", "S", "Sezione di libreria compatibile con Beam Property.", GH_ParamAccess.item);
        protected override void SolveInstance(IGH_DataAccess da)
        {
            string library = null, item = null; bool bgl = false; int unit = 2;
            if (!da.GetData(0, ref library) || !da.GetData(1, ref item) || !da.GetData(2, ref bgl) || !da.GetData(3, ref unit)) return;
            try { da.SetData(0, new GH_BeamSection(new StrausSection { Key = bgl ? "BGL" : "BSL", LibraryName = library, LibraryItem = item, LengthUnit = unit }.Calculate())); }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); }
        }
        public override Guid ComponentGuid => new Guid("31a5ba10-c508-4de9-8704-d311243bb845");
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input;
using Rhino.Input.Custom;

namespace BridGHe.Grasshopper.Components.Parameters
{
    public class PlateParam : GH_PersistentParam<GH_Plate>
    {
        public PlateParam()
            : base("Plate", "P", "Contains a collection of plates", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_PARAMETERS)
        {
        }

        protected override GH_Plate InstantiateT()
        {
            return new GH_Plate();
        }

        protected override GH_GetterResult Prompt_Singular(ref GH_Plate value)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Selezione dal catalogo/documento precedente non disponibile. Collegare i componenti di costruzione BridGHe.");
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Plural(ref List<GH_Plate> values)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Selezione dal catalogo/documento precedente non disponibile. Collegare i componenti di costruzione BridGHe.");
            return GH_GetterResult.cancel;
        }

        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        public override Guid ComponentGuid => new Guid("5483bff2-64ab-40d7-a5de-1af1e6f9bd12");
    }
}

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
    public class BeamParam : GH_PersistentParam<GH_Beam>
    {
        public BeamParam()
            : base("Beam", "B", "Contains a collection of beams", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_PARAMETERS)
        {
        }

        protected override GH_Beam InstantiateT()
        {
            return new GH_Beam();
        }

        protected override GH_GetterResult Prompt_Singular(ref GH_Beam value)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Selezione dal catalogo/documento precedente non disponibile. Collegare i componenti di costruzione BridGHe.");
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Plural(ref List<GH_Beam> values)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Selezione dal catalogo/documento precedente non disponibile. Collegare i componenti di costruzione BridGHe.");
            return GH_GetterResult.cancel;
        }

        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        public override Guid ComponentGuid => new Guid("dd6f2aa8-5224-45e3-9616-f9b8e0f0274b");
    }
}

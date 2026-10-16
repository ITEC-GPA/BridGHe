using BridGHe.Common.Models;
using BridGHe.Grasshopper.Helpers;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BridGHe.Grasshopper.Components.Parameters
{
    public class MaterialParam : GH_PersistentParam<DataTypes.Fem.GH_Material>
    {
        public MaterialParam()
            : base("Material", "M", "Material", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_PARAMETERS)
        {
        }

        protected override GH_GetterResult Prompt_Singular(ref DataTypes.Fem.GH_Material value)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Selezione dal catalogo/documento precedente non disponibile. Collegare i componenti di costruzione BridGHe.");
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Plural(ref List<DataTypes.Fem.GH_Material> values)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Selezione dal catalogo/documento precedente non disponibile. Collegare i componenti di costruzione BridGHe.");
            return GH_GetterResult.cancel;
        }

        public override bool AppendMenuItems(ToolStripDropDown menu)
        {
            Menu_AppendObjectNameEx(menu);
            Menu_AppendWireDisplay(menu);
            Menu_AppendDisconnectWires(menu);
            Menu_AppendSeparator(menu);

            //section menu
            Menu_AppendPromptOne(menu);
            Menu_AppendSeparator(menu);

            Menu_AppendDestroyPersistent(menu);
            Menu_AppendInternaliseData(menu);
            return true;
        }

        protected override DataTypes.Fem.GH_Material PreferredCast(object data)
        {
            return base.PreferredCast(data);
        }

        protected override void OnVolatileDataCollected()
        {
            base.OnVolatileDataCollected();
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        public override Guid ComponentGuid => new Guid("b2a70c33-31f5-463e-87a5-968c112dd4bb");
    }
}

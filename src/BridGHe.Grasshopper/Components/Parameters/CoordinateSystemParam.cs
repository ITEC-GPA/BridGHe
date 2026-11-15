using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;

namespace BridGHe.Grasshopper.Components.Parameters
{
    public class CoordinateSystemParam : GH_PersistentParam<GH_CoordinateSystem>
    {
        public CoordinateSystemParam()
            : base("CoordinateSystem", "CS", "The model coordinate system", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_PARAMETERS)
        {
            Optional = true; // An absent saved selection means Local, including the former empty representation.
        }

        protected override GH_GetterResult Prompt_Plural(ref List<GH_CoordinateSystem> values)
        {
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Singular(ref GH_CoordinateSystem value)
        {
            return GH_GetterResult.success;
        }

        public override bool AppendMenuItems(ToolStripDropDown menu)
        {
            Menu_AppendObjectNameEx(menu);
            Menu_AppendWireDisplay(menu);
            Menu_AppendDisconnectWires(menu);
            Menu_AppendSeparator(menu);

            Menu_AppendItem(menu, "Local", (s, e) => { RecordUndoEvent("Local coordinate system"); SetLocal(); ExpireSolution(true); }, true,
                LocalSelection);
            Menu_AppendItem(menu, "Global", (s, e) => { RecordUndoEvent("Global coordinate system"); SetGlobal(); ExpireSolution(true); }, true,
                !LocalSelection);

            Menu_AppendSeparator(menu);
            Menu_AppendDestroyPersistent(menu);
            Menu_AppendInternaliseData(menu);

            return true;
        }

        /// <summary>The «Global» menu: the global coordinate system as the persistent data. R6.5 P1 lane 2b, P2K (P1): the one given by its
        /// GPC frame alone (<see cref="GH_CoordinateSystem.Global"/>).</summary>
        internal void SetGlobal() => SetSelection(GH_CoordinateSystem.Global());

        internal void SetLocal() => SetSelection(GH_CoordinateSystem.Local());

        private void SetSelection(GH_CoordinateSystem value)
        {
            // SetPersistentData appends: a menu choice must replace the former frame, never produce [Global, Local].
            PersistentData.Clear();
            SetPersistentData(value);
        }

        /// <summary>The effective frame once collected; before collection, the saved choice (empty means Local in former files).</summary>
        internal bool LocalSelection => (SourceCount > 0 ? VolatileData.AllData(true) : PersistentData.AllData(true))
            .OfType<GH_CoordinateSystem>().All(cs => cs.IsLocal);

        protected override void OnVolatileDataCollected()
        {
            // Preserve the old empty Local representation without changing persistent defaults.
            if (SourceCount == 0 && PersistentData.DataCount == 0 && m_data.DataCount == 0)
                m_data.Append(GH_CoordinateSystem.Local());
            base.OnVolatileDataCollected();
            // Keep the empty default contract. An explicit Local carries a valid goo, so GH has data to collect.
            // Preserve user labels; update only the built-in labels after a menu choice or a source change.
            var values = VolatileData.AllData(true).OfType<GH_CoordinateSystem>().ToArray();
            if (values.Length > 0 && (NickName == "CS" || NickName == "Local" || NickName == "Global"))
                NickName = values.All(v => v.IsLocal) ? "Local" : values.All(v => Common.Gpc.GpcLoadFrames.IsGlobal(v.Gpc)) ? "Global" : "CS";
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        public override Guid ComponentGuid => new Guid("35436342-56df-40a6-ad21-0f02449ecb3e");

    }
}

using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GPC.Utilities.Units;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BridGHe.Grasshopper.Components.Parameters
{
    public class BeamSectionParam : GH_PersistentParam<GH_BeamSection>
    {
        // P2K (P1): the GPC DefinitionUnits (same names, order and integers as the FEM ModelModel.ModelUnits: the menu and the saved "Units").
        private Common.Gpc.DefinitionUnits _units;

        private bool CanMirror
        {
            get
            {
                if (PersistentData.DataCount > 0)
                {
                    GH_BeamSection bs = PersistentData.Branches[0][0];
                    // L2C (lane 2c, as in 0.3.0): a section is mirrored on its recipe (C and angle sections). P2K (P1): the only path.
                    return RecipeOf(bs) is { NativeKey: null } r && (r.Kind == Common.Gpc.BridgeSectionKind.C || r.Kind == Common.Gpc.BridgeSectionKind.Angle);
                }
                return false;
            }
        }

        private bool IsMirror
        {
            get
            {
                if (PersistentData.DataCount > 0)
                {
                    GH_BeamSection bs = PersistentData.Branches[0][0];
                    return RecipeOf(bs) is { } r && r.Mirror != Common.Gpc.Legacy.LegacySectionRecipe.NoMirror; // L2C: the mirror of the recipe
                }
                return false;
            }
        }

        /// <summary>P2K (P1): the recipe of a stored section for the menus, null when the reader refuses it (a section read with a FEM value:
        /// the menus then offer no mirror instead of failing).</summary>
        private static Common.Gpc.Legacy.LegacySectionRecipe RecipeOf(GH_BeamSection bs)
        {
            try { return bs?.Gpc; }
            catch (Exception x) when (x is ArgumentException || x is NotSupportedException) { return null; }
        }

        public BeamSectionParam()
            : base("Section", "S", "Section", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_PARAMETERS)
        {
            _units = Common.Gpc.DefinitionUnits.Nmm;
        }

        protected override GH_GetterResult Prompt_Singular(ref GH_BeamSection value)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Selection from a catalogue or a previous document is not available. Connect the BridGHe components that build this datum.");
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Plural(ref List<GH_BeamSection> values)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Selection from a catalogue or a previous document is not available. Connect the BridGHe components that build this datum.");
            return GH_GetterResult.cancel;
        }

        public override bool AppendMenuItems(ToolStripDropDown menu)
        {
            Menu_AppendObjectNameEx(menu);
            Menu_AppendWireDisplay(menu);
            Menu_AppendDisconnectWires(menu);

            // Section menu
            Menu_AppendSeparator(menu);
            Menu_AppendPromptOne(menu);

            // Mirror
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "Set Mirror", new EventHandler(OnSetMirror), CanMirror, IsMirror);
            string gap_text = "Gap";
            if (PersistentData.DataCount > 0)
            {
                GH_BeamSection bs = PersistentData.Branches[0][0];
                double gapA = RecipeOf(bs)?.MirrorGapA ?? 0; // L2C: the gap of the recipe
                if (gapA > 0)
                    gap_text = gapA.ToString();
            }
            Menu_AppendTextItem(menu, gap_text, new GH_MenuTextBox.KeyDownEventHandler(OnGapKeyDown), null, IsMirror, 0, true);

            // Units menu
            Menu_AppendSeparator(menu);
            foreach (object en in Enum.GetValues(typeof(Common.Gpc.DefinitionUnits)))
            {
                Common.Gpc.DefinitionUnits value;
                value = (Common.Gpc.DefinitionUnits)en;

                Menu_AppendItem(menu, Enum.GetName(typeof(Common.Gpc.DefinitionUnits), en), OnUnitsClick,
                    PersistentDataCount > 0 || VolatileDataCount > 0, value == _units ? true : false);
            }

            Menu_AppendSeparator(menu);
            Menu_AppendDestroyPersistent(menu);
            Menu_AppendInternaliseData(menu);
            return true;
        }

        private void OnSetMirror(object sender, EventArgs e)
        {
            // L2C (lane 2c, as in 0.3.0): the section is mirrored on its recipe (the same toggle; a refused recipe gives an error and the stored
            // section stays). P2K (P1): the only path, the new section by its recipe alone.
            if (PersistentData.DataCount > 0)
            {
                var stored = PersistentData.Branches[0][0];
                try { Replace(GH_BeamSection.FromGpc(Common.Gpc.Builders.GpcSectionBuilders.Mirrored(stored.Gpc ?? throw new NotSupportedException("LegacySectionWithoutRecipe")))); ExpireSolution(true); }
                catch (Exception x) when (x is ArgumentException || x is NotSupportedException) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, GpcOnlyProperties.Refused + x.Message); }
            }
        }

        private void OnGapKeyDown(GH_MenuTextBox textBox, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                // L2C (lane 2c, as in 0.3.0): the gap of the section, on its recipe. P2K (P1): the only path, the new section by its recipe alone.
                if (double.TryParse(textBox.Text, out double recipeGap))
                {
                    var stored = PersistentData.Branches[0][0];
                    try { Replace(GH_BeamSection.FromGpc(Common.Gpc.Builders.GpcSectionBuilders.Gap(stored.Gpc ?? throw new NotSupportedException("LegacySectionWithoutRecipe"), recipeGap))); ExpireSolution(true); }
                    catch (Exception x) when (x is ArgumentException || x is NotSupportedException) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, GpcOnlyProperties.Refused + x.Message); }
                }
            }
        }

        private void OnUnitsClick(object sender, EventArgs e)
        {
            if (Enum.TryParse(((ToolStripMenuItem)sender).Text, out Common.Gpc.DefinitionUnits value))
            {
                Common.Gpc.DefinitionUnits prevUnits = _units;
                GH_BeamSection bs;

                _units = value;

                // Get utilities units
                // P2K (P1): the table of the former FEM ModelHelper, without the FEM type (same units).
                Helpers.GpcOnlyProperties.UtilitiesUnits(_units, out UnitsConvert.LengthUnits lengthUnits,
                    out UnitsConvert.ForceUnits bufferF, out UnitsConvert.MassUnits bufferM,
                    out UnitsConvert.PressureUnits bufferP, out UnitsConvert.TemperatureUnits bufferT);
                Helpers.GpcOnlyProperties.UtilitiesUnits(prevUnits, out UnitsConvert.LengthUnits prevLengthCode,
                    out bufferF, out bufferM, out bufferP, out bufferT);

                // Recompute with current units
                // The stored section is replaced (it may be shared downstream). Data from sources belongs to the upstream components and is
                // never converted in place (before: every volatile item, upstream sections included).
                bs = PersistentData.get_FirstItem(false);
                // R6.5 P1 lane 2a: the section is converted on its recipe alone (a refused recipe gives an error and the stored section stays).
                // P2K (P1): the only path.
                if (bs != null)
                {
                    try { Replace(GH_BeamSection.FromGpc(Common.Gpc.Builders.GpcSectionBuilders.ConvertUnits(bs.Gpc, prevLengthCode, lengthUnits))); }
                    catch (Exception x) when (x is ArgumentException || x is NotSupportedException) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, GpcOnlyProperties.Refused + x.Message); }
                }
                ExpireSolution(true);
            }
        }

        /// <summary>The stored section replaced by <paramref name="section"/> (SetPersistentData appends to the stored data: before,
        /// each menu click added an item).</summary>
        private void Replace(GH_BeamSection section)
        {
            PersistentData.Clear();
            PersistentData.Append(section);
        }

        protected override GH_BeamSection PreferredCast(object data)
        {
            return base.PreferredCast(data);
        }

        public override bool Write(GH_IWriter writer)
        {
            bool success = base.Write(writer);
            if (!success)
                return false;
            writer.SetInt32("Units", Convert.ToInt32(_units));
            return true;
        }

        public override bool Read(GH_IReader reader)
        {
            bool success = base.Read(reader);
            if (!success)
                return false;

            int units = -1;
            if (reader.TryGetInt32("Units", ref units))
            {
                _units = (Common.Gpc.DefinitionUnits)Enum.Parse(typeof(Common.Gpc.DefinitionUnits), units.ToString());
            }
            else
            {
                return false;
            }
            return true;
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        public override Guid ComponentGuid => new Guid("0f79b0ba-a920-405a-9cb4-309c1160633a");
    }
}

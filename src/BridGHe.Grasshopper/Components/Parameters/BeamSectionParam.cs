using BridGHe.Common.Models;
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
        private ModelModel.ModelUnits _units;

        private bool CanMirror
        {
            get
            {
                if (PersistentData.DataCount > 0)
                {
                    GH_BeamSection bs = PersistentData.Branches[0][0];
                    // L2C (lane 2c, as in 0.3.0): a section given by its recipe alone is mirrored on its recipe (C and angle sections).
                    if (bs.Value == null) return bs.Gpc is { NativeKey: null } r && (r.Kind == Common.Gpc.BridgeSectionKind.C || r.Kind == Common.Gpc.BridgeSectionKind.Angle);
                    return bs.SectionType == SectionModel.SectionTypes.C || bs.SectionType == SectionModel.SectionTypes.Angle;
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
                    if (bs.Value == null) return bs.Gpc != null && bs.Gpc.Mirror != Common.Gpc.Legacy.LegacySectionRecipe.NoMirror; // L2C: the mirror of the recipe
                    return bs.MirrorType != SectionModel.MirrorTypes.None;
                }
                return false;
            }
        }

        public BeamSectionParam()
            : base("Section", "S", "Section", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_PARAMETERS)
        {
            _units = ModelModel.ModelUnits.Nmm;
        }

        protected override GH_GetterResult Prompt_Singular(ref GH_BeamSection value)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Selezione dal catalogo/documento precedente non disponibile. Collegare i componenti di costruzione BridGHe.");
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Plural(ref List<GH_BeamSection> values)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Selezione dal catalogo/documento precedente non disponibile. Collegare i componenti di costruzione BridGHe.");
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
                double gapA = bs.Value == null ? bs.Gpc?.MirrorGapA ?? 0 : bs.MirrorGapA; // L2C: the gap of the recipe
                if (gapA > 0)
                    gap_text = gapA.ToString();
            }
            Menu_AppendTextItem(menu, gap_text, new GH_MenuTextBox.KeyDownEventHandler(OnGapKeyDown), null, IsMirror, 0, true);

            // Units menu
            Menu_AppendSeparator(menu);
            foreach (object en in Enum.GetValues(typeof(ModelModel.ModelUnits)))
            {
                ModelModel.ModelUnits value;
                value = (ModelModel.ModelUnits)en;

                Menu_AppendItem(menu, Enum.GetName(typeof(ModelModel.ModelUnits), en), OnUnitsClick,
                    PersistentDataCount > 0 || VolatileDataCount > 0, value == _units ? true : false);
            }

            Menu_AppendSeparator(menu);
            Menu_AppendDestroyPersistent(menu);
            Menu_AppendInternaliseData(menu);
            return true;
        }

        private void OnSetMirror(object sender, EventArgs e)
        {
            // L2C (lane 2c, as in 0.3.0): a section given by its recipe alone is mirrored on its recipe (the same toggle; a refused recipe gives
            // an error and the stored section stays).
            if (PersistentData.DataCount > 0 && PersistentData.Branches[0][0].Value == null)
            {
                var stored = PersistentData.Branches[0][0];
                try { Replace(GH_BeamSection.FromGpc(Common.Gpc.Builders.GpcSectionBuilders.Mirrored(stored.Gpc ?? throw new NotSupportedException("LegacySectionWithoutRecipe")))); ExpireSolution(true); }
                catch (Exception x) when (GpcBuilderAttach.Refusal(x)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, GpcOnlyProperties.Refused + x.Message); }
                return;
            }
            if (PersistentData.DataCount > 0)
            {
                var input = PersistentData.Branches[0][0];
                // A changed copy: the stored section may already be shared with the data of downstream components.
                GH_BeamSection bs = Copy(PersistentData.Branches[0][0]);
                if (bs.MirrorType == SectionModel.MirrorTypes.None)
                    bs.MirrorType = SectionModel.MirrorTypes.Left;
                else
                    bs.MirrorType = SectionModel.MirrorTypes.None;
                // R6.2 part C (dual rule until R6.5); L2C: the recipe of the input with the mirror changed (a mirrored section is a recipe).
                GpcBuilderAttach.Section(bs, () => Common.Gpc.Builders.GpcSectionBuilders.Mirrored(input.Gpc ?? throw new NotSupportedException("LegacySectionWithoutRecipe")));
                Replace(bs);
                ExpireSolution(true);
            }
        }

        private void OnGapKeyDown(GH_MenuTextBox textBox, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                // L2C (lane 2c, as in 0.3.0): the gap of a section given by its recipe alone, on its recipe.
                if (double.TryParse(textBox.Text, out double recipeGap) && PersistentData.Branches[0][0].Value == null)
                {
                    var stored = PersistentData.Branches[0][0];
                    try { Replace(GH_BeamSection.FromGpc(Common.Gpc.Builders.GpcSectionBuilders.Gap(stored.Gpc ?? throw new NotSupportedException("LegacySectionWithoutRecipe"), recipeGap))); ExpireSolution(true); }
                    catch (Exception x) when (GpcBuilderAttach.Refusal(x)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, GpcOnlyProperties.Refused + x.Message); }
                    return;
                }
                if (double.TryParse(textBox.Text, out double gap))
                {
                    var input = PersistentData.Branches[0][0];
                    GH_BeamSection bs = Copy(input);
                    bs.MirrorGapA = gap;
                    bs.IsDBSection = false;
                    // R6.2 part C (dual rule); L2C: the recipe of the input with the gap (a mirrored section is a recipe).
                    GpcBuilderAttach.Section(bs, () => Common.Gpc.Builders.GpcSectionBuilders.Gap(input.Gpc ?? throw new NotSupportedException("LegacySectionWithoutRecipe"), gap));
                    Replace(bs);
                    ExpireSolution(true);
                }
            }
        }

        private void OnUnitsClick(object sender, EventArgs e)
        {
            if (Enum.TryParse(((ToolStripMenuItem)sender).Text, out ModelModel.ModelUnits value))
            {
                ModelModel.ModelUnits prevUnits = _units;
                GH_BeamSection bs;

                _units = value;

                // Get utilities units
                Common.Helpers.ModelHelper.GetUtilitiesConversionParams(_units, out UnitsConvert.LengthUnits lengthUnits,
                    out UnitsConvert.ForceUnits bufferF, out UnitsConvert.MassUnits bufferM,
                    out UnitsConvert.PressureUnits bufferP, out UnitsConvert.TemperatureUnits bufferT);
                Common.Helpers.ModelHelper.GetUtilitiesConversionParams(prevUnits, out UnitsConvert.LengthUnits prevLengthCode,
                    out bufferF, out bufferM, out bufferP, out bufferT);

                // Recompute with current units
                // The stored section is converted on a copy (it may be shared downstream). Data from sources belongs to the
                // upstream components and is never converted in place (before: every volatile item, upstream sections included).
                bs = PersistentData.get_FirstItem(false);
                // R6.5 P1 lane 2a: a section given by its recipe alone is converted on its recipe alone (the same builder as below; a refused
                // recipe gives an error and the stored section stays).
                if (bs != null && bs.Value == null)
                {
                    try { Replace(GH_BeamSection.FromGpc(Common.Gpc.Builders.GpcSectionBuilders.ConvertUnits(bs.Gpc, prevLengthCode, lengthUnits))); }
                    catch (Exception x) when (GpcBuilderAttach.Refusal(x)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, GpcOnlyProperties.Refused + x.Message); }
                }
                else if (bs != null)
                {
                    var input = bs;
                    bs = Copy(bs);
                    bs.ConvertUnits(prevUnits, _units);
                    // R6.2 part C (dual rule until R6.5): the recipe of the input converted on GPC (a mirrored input has no recipe: refused).
                    GpcBuilderAttach.Section(bs, () => Common.Gpc.Builders.GpcSectionBuilders.ConvertUnits(input.Gpc ?? throw new NotSupportedException("LegacySectionWithoutRecipe"),
                        prevLengthCode, lengthUnits));
                    Replace(bs);
                }
                ExpireSolution(true);
            }
        }

        private static GH_BeamSection Copy(GH_BeamSection section) => new GH_BeamSection(Common.Helpers.ModelArchive.Clone(section.Value));

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
                _units = (ModelModel.ModelUnits)Enum.Parse(typeof(ModelModel.ModelUnits), units.ToString());
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

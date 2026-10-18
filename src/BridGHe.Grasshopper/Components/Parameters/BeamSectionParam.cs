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
                if (bs.MirrorGapA > 0)
                    gap_text = bs.MirrorGapA.ToString();
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
            if (PersistentData.DataCount > 0)
            {
                GH_BeamSection bs = PersistentData.Branches[0][0];
                if (bs.MirrorType == SectionModel.MirrorTypes.None)
                    bs.MirrorType = SectionModel.MirrorTypes.Left;
                else
                    bs.MirrorType = SectionModel.MirrorTypes.None;
                ExpireSolution(true);
            }
        }

        private void OnGapKeyDown(GH_MenuTextBox textBox, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                GH_BeamSection bs = PersistentData.Branches[0][0];
                if (double.TryParse(textBox.Text, out double gap))
                {
                    bs.MirrorGapA = gap;
                    bs.IsDBSection = false;
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
                bs = PersistentData.get_FirstItem(false);
                if (bs != null)
                {
                    bs.ConvertUnits(prevUnits, _units);
                    SetPersistentData(bs);
                    ExpireSolution(true);
                }

                foreach (GH_BeamSection obj in VolatileData.AllData(true))
                {
                    obj.ConvertUnits(prevUnits, _units);
                    ExpireSolution(true);
                }
            }
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

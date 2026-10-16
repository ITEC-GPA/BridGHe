using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.GUI;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BridGHe.Grasshopper.Components.Parameters
{
    public class GroupParam : GH_PersistentParam<GH_Group>
    {
        private string _separator;

        public GroupParam()
            : base("Group", "G", "Contains a collection of groups", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_PARAMETERS)
        {
            _separator = "::";
        }

        protected override GH_GetterResult Prompt_Plural(ref List<GH_Group> values)
        {
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Singular(ref GH_Group value)
        {
            return GH_GetterResult.success;
        }

        protected void SetSeparator(string separator)
        {
            _separator = separator;
        }

        public override bool AppendMenuItems(ToolStripDropDown menu)
        {
            Menu_AppendObjectNameEx(menu);
            Menu_AppendWireDisplay(menu);

            Menu_AppendSeparator(menu);

            Menu_AppendDisconnectWires(menu);

            Menu_AppendSeparator(menu);

            Menu_AppendReverseParameter(menu);
            Menu_AppendFlattenParameter(menu);
            Menu_AppendGraftParameter(menu);
            Menu_AppendSimplifyParameter(menu);

            Menu_AppendSeparator(menu);

            Menu_AppendItem(menu, "Set string separator");
            Menu_AppendTextItem(menu, _separator, new GH_MenuTextBox.KeyDownEventHandler(OnGapKeyDown), null, true, 0, true);

            Menu_AppendSeparator(menu);

            Menu_AppendDestroyPersistent(menu);
            Menu_AppendInternaliseData(menu);

            return true;
        }

        protected override GH_Group PreferredCast(object data)
        {
            if (data is GH_String str)
            {
                string strValue = str.Value;
                var a = strValue.Split(new string[] { _separator }, StringSplitOptions.RemoveEmptyEntries);

                GH_Group group = null;

                for (int i = 0; i < a.Length; i++)
                {
                    if (!string.IsNullOrEmpty(a[i]))
                    {
                        if (i == 0)
                            group = new GH_Group(new Common.Models.GroupModel(a[i]));
                        else
                            group = new GH_Group(new Common.Models.GroupModel(a[i], group.Value));
                    }
                }

                return group;
            }
            return base.PreferredCast(data);
        }

        private void OnGapKeyDown(GH_MenuTextBox textBox, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (textBox.Text != null)
                {
                    SetSeparator(textBox.Text);
                    ExpireSolution(true);
                }
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        public override Guid ComponentGuid => new Guid("8e3d57e8-79d2-4f5f-9153-bd7e25a9f39c");
    }
}

using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Forms;
using BridGHe.Grasshopper.Helpers;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BridGHe.Grasshopper.Components.Parameters
{
    public class LoadCombinationsParam : GH_PersistentParam<GH_LoadCombination>
    {
        /// <summary>R65-C2-05: the case records given with the cases (the columns of the form).</summary>
        public IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> CaseRecordList { get; private set; } = Array.Empty<Common.Gpc.Legacy.LegacyCaseData>();

        public LoadCombinationsParam()
            : base("Load Combinations", "LCS", "The load combinations definition", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_PARAMETERS)
        {
        }

        /// <summary>R6.3: the case records of Build Model or Staged Construction: the combinations of the parameter follow them (the GPC datum
        /// built on the case records, <c>GpcCaseBuilders.UpdatedCombination</c>). P2CE phase 2a: the FEM cases parameter (always null) removed.
        /// P2P-01 (13/11/2026): null is an unknown catalogue and changes nothing (before any write); an empty array is a known empty one.</summary>
        public void UpdateLoadCases(IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> caseRecords)
        {
            if (caseRecords == null) return;
            // Recreate the LoadCase names list
            CaseRecordList = caseRecords;

            if (VolatileDataCount == 0)
                return;

            // Update the exiting combinations
            bool changed = false;
            List<GH_LoadCombination> combinations = (List<GH_LoadCombination>)VolatileData.get_Branch(0);

            for (int k = 0; k < combinations.Count; k++)
            {
                GH_LoadCombination combo = combinations[k];
                // R6.3: the GPC datum before the change (null when the reader refuses it: the combination stays as it is).
                Common.Gpc.Legacy.LegacyCombinationDraft before;
                try { before = combo.Gpc; } catch (Exception x) when (x is ArgumentException || x is NotSupportedException) { before = null; }
                // R6.5 P1 lane 2a: the combination follows the case records alone (the factors of the cases no longer present removed, the new
                // cases added with 1), replaced by the updated one. R65-C2-06 (review of lane 2, 8/11): changed in place (the stage that collected
                // the combination sees the new cases, the persistent data with it), and the solution expired on a removal only.
                // P2K (P1): the only path, for every combination.
                if (before == null) continue;
                if (before.Factors.Any(f => !caseRecords.Any(c => c.Name == f.Case.Name))) changed = true;
                var updated = Common.Gpc.Builders.GpcCaseBuilders.UpdatedCombination(before, caseRecords);
                if (updated != null) combo.AttachGpc(updated);
            }

            if (changed)
            {
                PersistentData.Clear();
                SetPersistentData(combinations);
                ExpireSolution(true);
            }
        }

        protected override GH_GetterResult Prompt_Plural(ref List<GH_LoadCombination> values)
        {
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Singular(ref GH_LoadCombination value)
        {
            return GH_GetterResult.success;
        }

        public override void CreateAttributes()
        {
            m_attributes = new LoadCombinationsAttributes(this);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        public override Guid ComponentGuid => new Guid("9bfe3964-9ec3-4a9f-a757-487de39c661d");

        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
    }

    internal class LoadCombinationsAttributes : GH_ResizableAttributes<LoadCombinationsParam>
    {
        private Rectangle _internalRect;

        protected override Size MinimumSize => new Size(120, 60);

        protected override Size MaximumSize { get { return new Size(730, 240); } }

        protected override Padding SizingBorders { get { return new Padding(10); } }

        public LoadCombinationsAttributes(LoadCombinationsParam owner)
            : base(owner)
        {
            Bounds = new RectangleF(Pivot, new Size(150, 80));
        }

        protected override void Layout()
        {
            base.Layout();
            _internalRect = GH_Convert.ToRectangle(Bounds);
            _internalRect.Inflate(-5, -5);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                GH_Palette palette = GH_Palette.Normal;

                // Adjust palette based on the Owner's worst case messaging level.
                switch (Owner.RuntimeMessageLevel)
                {
                    case GH_RuntimeMessageLevel.Warning:
                        palette = GH_Palette.Warning;
                        break;

                    case GH_RuntimeMessageLevel.Error:
                        palette = GH_Palette.Error;
                        break;
                }

                RectangleF capsuleBox = new RectangleF(Bounds.Location, Bounds.Size);

                GH_Capsule capsule = GH_Capsule.CreateCapsule(capsuleBox, palette);
                capsule.AddOutputGrip(OutputGrip.Y);
                capsule.Render(graphics, Selected, Owner.Locked, false);
                capsule.Dispose();

                using (Pen pen = new Pen(Color.Black))
                    graphics.DrawRectangle(pen, _internalRect);

                StringFormat format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter
                };
                RectangleF textRectangle = Bounds;
                textRectangle.Y += 2;
                textRectangle.Height = 20;
                textRectangle.Inflate(-4, 0);
                graphics.DrawString("Combinations", GH_FontServer.Standard, Brushes.Black, textRectangle, format);

                if (!Owner.VolatileData.IsEmpty)
                {
                    format.Alignment = StringAlignment.Near;
                    textRectangle.X += 2;
                    IList<GH_LoadCombination> list = (IList<GH_LoadCombination>)Owner.VolatileData.get_Branch(0);
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] == null)
                            continue;
                        textRectangle.Y += 20;
                        if (textRectangle.Y + textRectangle.Height + 20 > Bounds.Y + Bounds.Height)
                        {
                            graphics.DrawString("etc.", GH_FontServer.Standard, Brushes.Black, textRectangle,
                            format);
                            break;
                        }
                        graphics.DrawString(list[i].ToString(), GH_FontServer.Standard, Brushes.Black, textRectangle,
                            format);
                    }
                }
            }
            else
            {
                base.Render(canvas, graphics, channel);
            }
        }

        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left)
            {
                List<GH_LoadCombination> list = null;
                if (!Owner.VolatileData.IsEmpty)
                    list = (List<GH_LoadCombination>)Owner.VolatileData.get_Branch(0);
                LoadCombinationsForm form = new LoadCombinationsForm(list, Owner.CaseRecordList); // R65-C2-05: with the case records (P2K (P1): no FEM cases)
                GH_WindowsFormUtil.CenterFormOnCursor(form, true);
                if (form.ShowDialog(Instances.DocumentEditor) == DialogResult.OK)
                {
                    Owner.PersistentData.Clear();
                    Owner.SetPersistentData(form.Values);
                    Owner.ExpireSolution(true);
                }
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDoubleClick(sender, e);
        }
    }
}

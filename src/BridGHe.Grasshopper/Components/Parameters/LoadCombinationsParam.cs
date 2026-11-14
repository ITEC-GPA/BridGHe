using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Forms;
using BridGHe.Grasshopper.Helpers;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BridGHe.Grasshopper.Components.Parameters
{
    public class LoadCombinationsParam : GH_PersistentParam<GH_LoadCombination>
    {
        /// <summary>R65-C2-05: the case records given with the cases (the columns of the form). P2CG (13/11/2026, CE-01): null until Build Model or
        /// Staged Construction gives a known catalogue (an unknown one: the form cannot be confirmed); an empty array is a known empty catalogue.</summary>
        public IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> CaseRecordList { get; private set; }

        /// <summary>P2CG (CE-01): the case catalogue is known (the form may make the combinations on it).</summary>
        public bool HasCaseCatalog => CaseRecordList != null;

        public LoadCombinationsParam()
            : base("Load Combinations", "LCS", "The load combinations definition", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_PARAMETERS)
        {
        }

        /// <summary>P2CG (CE-01, CE-03): the known catalogue of Build Model given by Staged Construction when it receives it, for the form only: the
        /// combinations are not changed (they follow the catalogue when the stage solves, <see cref="UpdateLoadCases"/>). Null changes nothing.</summary>
        internal void GiveCaseCatalog(IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> caseRecords)
        {
            // P2CH (83-01): not while the components this parameter feeds have different catalogues (the form is refused then).
            if (caseRecords != null && !Disagrees()) CaseRecordList = caseRecords.ToArray();
        }

        /// <summary>P2CH (14/11/2026, review of ChatGPT task 83, 83-01, design of task 102): the error while this parameter feeds components with
        /// different case catalogues, or null. Then no combination is changed and the form is refused; before, each stage removed the cases the
        /// other one had added (factors other than 1 lost) and every removal asked for a new solution, without end.</summary>
        public string CatalogueConflict { get; private set; }

        /// <summary>P2CH (83-01): the conflict as the components this parameter feeds have it now (the form asks it before opening).</summary>
        internal string CurrentConflict() { Disagrees(); return CatalogueConflict; }

        /// <summary>P2CH (83-01): the known catalogues of the components this parameter feeds. A Build Model that takes it directly gives its own;
        /// a Staged Construction gives the catalogue of the Build Models it feeds (the source of its own catalogue, newer than the one it keeps),
        /// or its own when no Build Model it feeds has given one yet. Unknown catalogues are not compared (as before).</summary>
        private List<(string Who, IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> Cases)> Declared()
        {
            var declared = new List<(string, IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData>)>();
            static string Label(GH_Component c) => c.Name.Trim() + (string.IsNullOrEmpty(c.Message) ? "" : " «" + c.Message + "»");
            foreach (var recipient in Recipients)
            {
                var owner = recipient.Attributes?.GetTopLevel?.DocObject;
                if (owner is Models.BuildModelComponent build && build.Params.Input.Count > 3 && ReferenceEquals(build.Params.Input[3], recipient))
                {
                    if (build.CaseCatalog != null) declared.Add((Label(build), build.CaseCatalog));
                }
                else if (owner is Definitions.StagedConstructionComponent stage && stage.Params.Input.Count > 2 && ReferenceEquals(stage.Params.Input[2], recipient))
                {
                    bool fromBuild = false;
                    foreach (var next in stage.Params.Output[0].Recipients)
                        if (next.Attributes?.GetTopLevel?.DocObject is Models.BuildModelComponent b && b.Params.Input.Count > 4 && ReferenceEquals(b.Params.Input[4], next) && b.CaseCatalog != null)
                        {
                            fromBuild = true;
                            declared.Add((Label(b), b.CaseCatalog));
                        }
                    if (!fromBuild && stage.CaseRecords != null) declared.Add((Label(stage), stage.CaseRecords));
                }
            }
            return declared;
        }

        /// <summary>P2CH (83-01): the conflict of the catalogues this parameter feeds, or null. Two catalogues agree when they hold the same cases
        /// (a case: its name and its definition, <c>LegacyCaseData</c> by value), in any order. The error is on the parameter; it goes when the
        /// catalogues agree again.</summary>
        private bool Disagrees()
        {
            var declared = Declared();
            var names = new SortedSet<string>(StringComparer.Ordinal);
            if (declared.Count > 1)
            {
                var first = new HashSet<Common.Gpc.Legacy.LegacyCaseData>(declared[0].Cases);
                foreach (var (_, cases) in declared.Skip(1))
                {
                    var other = new HashSet<Common.Gpc.Legacy.LegacyCaseData>(cases);
                    if (!first.SetEquals(other)) foreach (var c in first.Except(other).Concat(other.Except(first))) names.Add(c.Name);
                }
            }
            string conflict = names.Count == 0 ? null
                : "Load Combinations is connected to components with incompatible load-case catalogues. No combinations were changed. Use a separate Load Combinations "
                + "parameter for each catalogue, or make the catalogues identical. Conflicting components: " + string.Join(", ", declared.Select(d => d.Who).Distinct())
                + ". Cases: " + string.Join(", ", names) + ".";
            bool shown = conflict != null && RuntimeMessages(GH_RuntimeMessageLevel.Error).Contains(conflict);
            if (CatalogueConflict != null && conflict != CatalogueConflict) ClearRuntimeMessages(); // the former conflict, solved or with other cases
            if (conflict != null && !shown) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, conflict); // also after a solution of the parameter cleared it
            if (conflict != CatalogueConflict || (conflict != null && !shown)) OnDisplayExpired(false);
            CatalogueConflict = conflict;
            return conflict != null;
        }

        /// <summary>R6.3: the case records of Build Model or Staged Construction: the combinations of the parameter follow them (the GPC datum
        /// built on the case records, <c>GpcCaseBuilders.UpdatedCombination</c>). P2CE phase 2b: the FEM cases parameter (always null) removed.
        /// P2P-01 (13/11/2026): null is an unknown catalogue and changes nothing (before any write); an empty array is a known empty one.
        /// P2CG (13/11/2026, CE-03): true when a combination changed; a removal no longer expires the solution from inside the solution that
        /// calls it (Build Model, Staged Construction): the parameter is expired after it (<see cref="DeferredExpiry"/>, merged requests).</summary>
        public bool UpdateLoadCases(IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> caseRecords)
        {
            if (caseRecords == null) return false;
            // P2CH (83-01): components with other catalogues: nothing changes (no removal, so no new solution asked for).
            if (Disagrees()) return false;
            // Recreate the LoadCase names list
            CaseRecordList = caseRecords.ToArray();

            if (VolatileDataCount == 0)
                return false;

            // Update the exiting combinations. P2CH (14/11/2026, review of ChatGPT task 83, 83-02): every branch, in the order of the data (before,
            // and in 0.3.0, the first branch only: the combinations of the other branches kept the factors of the cases removed, and a removal
            // replaced the whole persistent tree by the first branch, so the other branches were lost).
            bool removed = false, changed = false;
            var tree = m_data;
            foreach (GH_Path path in tree.Paths)
            {
                foreach (GH_LoadCombination combo in tree[path])
                {
                    if (combo == null) continue;
                    // R6.3: the GPC datum before the change (null when the reader refuses it: the combination stays as it is).
                    Common.Gpc.Legacy.LegacyCombinationDraft before;
                    try { before = combo.Gpc; } catch (Exception x) when (x is ArgumentException || x is NotSupportedException) { before = null; }
                    // R6.5 P1 lane 2a: the combination follows the case records alone (the factors of the cases no longer present removed, the new
                    // cases added with 1), replaced by the updated one. R65-C2-06 (review of lane 2, 8/11): changed in place (the stage that collected
                    // the combination sees the new cases, the persistent data with it), and the solution expired on a removal only.
                    // P2K (P1): the only path, for every combination.
                    if (before == null) continue;
                    if (before.Factors.Any(f => !caseRecords.Any(c => c.Name == f.Case.Name))) removed = true;
                    var updated = Common.Gpc.Builders.GpcCaseBuilders.UpdatedCombination(before, caseRecords);
                    if (updated != null) { combo.AttachGpc(updated); changed = true; }
                }
            }

            if (removed)
            {
                // P2CG (CE-03): the persistent data replaced as SetPersistentData did (an undo record and the change event), without its
                // ExpireSolution(false): called from inside the solution of Build Model or Staged Construction, it expired this parameter and the
                // objects after it, the component solving among them, and Grasshopper stops on «object expired during a solution» (its assert, a
                // modal window: the host of gate p2ce3 hung on it). The parameter is expired after the solution (DeferredExpiry, merged requests;
                // before, ExpireSolution(true)).
                // P2CH (83-02): the undo record is taken before the change, so Undo gives back the combinations as they were (before, it was taken
                // after Clear and held an empty list). A standalone parameter: each persistent combination is updated in place, so the tree stays as
                // it is (paths, empty branches, order). With sources (the persistent data is not used): the collected data is written as persistent
                // data, as before, but every branch of it with its path (before, the first branch only).
                if (tree.AllData(true).Any(c => c != null))
                    RecordPersistentDataEvent("Set Multiple " + GH_Convert.ToPlural(tree.AllData(true).First(c => c != null).TypeName));
                if (SourceCount == 0)
                    foreach (var combo in PersistentData.AllData(true).OfType<GH_LoadCombination>())
                    {
                        Common.Gpc.Legacy.LegacyCombinationDraft before;
                        try { before = combo.Gpc; } catch (Exception x) when (x is ArgumentException || x is NotSupportedException) { before = null; }
                        var updated = before == null ? null : Common.Gpc.Builders.GpcCaseBuilders.UpdatedCombination(before, caseRecords);
                        if (updated != null) combo.AttachGpc(updated);
                    }
                else
                {
                    PersistentData.Clear();
                    foreach (GH_Path path in tree.Paths)
                    {
                        PersistentData.EnsurePath(path);
                        PersistentData.AppendRange(tree[path], path);
                    }
                }
                OnObjectChanged(GH_ObjectEventType.PersistentData);
                DeferredExpiry.Request(this);
            }
            return changed;
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
                // P2CG (CE-01): with an unknown catalogue the form would make the combinations without their factors: refused, nothing changed.
                if (LoadCombinationRows.Refusal(Owner.CaseRecordList) is string refusal)
                {
                    MessageBox.Show(refusal, "Load Combinations", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return GH_ObjectResponse.Handled;
                }
                // P2CH (83-01): not while the components this parameter feeds have different catalogues.
                if (Owner.CurrentConflict() is string conflict)
                {
                    MessageBox.Show(conflict, "Load Combinations", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return GH_ObjectResponse.Handled;
                }
                // P2CH (83-02): the form edits one list; with combinations in several branches, OK would keep the first branch only: refused.
                if (LoadCombinationRows.TreeRefusal(Owner.VolatileData.PathCount) is string branches)
                {
                    MessageBox.Show(branches, "Load Combinations", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return GH_ObjectResponse.Handled;
                }
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

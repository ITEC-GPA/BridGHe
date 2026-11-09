using System;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;

namespace BridGHe.Common.Gpc.Builders
{
    /// <summary>
    /// R6.4 (2), extension (α): the changes the model transformers make to the elements of their source (Bridge Phase Model, Bridge Construction
    /// Sequence), on the drafts of the elements the source carries: the draft the reader gives the changed former value, as every builder of
    /// §1bis (point 6). Never changes its input (a copy); the identity of the draft kept.
    /// </summary>
    internal static class GpcDraftTransforms
    {
        internal const string SlabStrainRecord = "CompositeBeamSlabStrainModel", SlabStrainCarry = "Composite slab strain (checks input: the strain of the workflow shrinkage action)";

        /// <summary>A node without its loads (former <c>Loads.Clear()</c>): forces, moments, temperatures and their records; the reader gives
        /// physical data to a node only for its temperatures.</summary>
        internal static LegacyNodeDraft WithoutLoads(LegacyNodeDraft draft)
        {
            var d = GpcDrafts.Copy(draft ?? throw new ArgumentNullException(nameof(draft)));
            d.Node.Loads.Clear(); d.Node.Assignments.Physical = null;
            d.Records.LoadList.Clear(); d.Records.LoadCaseList.Clear();
            return d;
        }

        /// <summary>A beam without its loads: distributed and point loads, prestrains, gradients, slab strains (and what the reader carries for
        /// them) and their records; the physical data go when nothing else holds them (the reader creates them for a support, the output
        /// stations, a taper or a string group); the fingerprint of a current section state of the complete beam.</summary>
        internal static LegacyBeamDraft WithoutLoads(LegacyBeamDraft draft)
        {
            var d = GpcDrafts.Copy(draft ?? throw new ArgumentNullException(nameof(draft)));
            d.Beam.Loads.Clear(); d.Beam.Assignments.Loads.Clear();
            var p = d.Beam.Assignments.Physical;
            if (p != null)
            {
                p.TemperatureGradients.Clear(); p.InitialStates.Clear();
                if (p.Supports.Count == 0 && p.MinimumOutputStations == null && p.StringGroup == null && p.Taper == null) d.Beam.Assignments.Physical = null;
            }
            int strains = d.Records.LoadRecords.Count(l => l.Kind == SlabStrainRecord);
            if (strains != 0 && d.Report.NotExported.TryGetValue(SlabStrainCarry, out int n))
            {
                if (n > strains) d.Report.NotExported[SlabStrainCarry] = n - strains; else d.Report.NotExported.Remove(SlabStrainCarry);
            }
            d.Records.LoadList.Clear(); d.Records.LoadCaseList.Clear();
            return GpcBeamBuilders.Finish(new GpcBeamBuilders.Work(d));
        }

        /// <summary>A plate without its loads: pressures, prestress, non-structural masses and their records; the physical data go when no load
        /// patch and no support hold them.</summary>
        internal static LegacyPlateDraft WithoutLoads(LegacyPlateDraft draft)
        {
            var d = GpcDrafts.Copy(draft ?? throw new ArgumentNullException(nameof(draft)));
            d.Plate.Loads.Clear();
            var p = d.Plate.Assignments.Physical;
            if (p != null)
            {
                p.InitialStresses.Clear(); p.NonStructuralMasses.Clear();
                if (p.LoadPatch == null && p.Supports.Count == 0) d.Plate.Assignments.Physical = null;
            }
            d.Records.LoadList.Clear(); d.Records.LoadCaseList.Clear();
            return d;
        }

        /// <summary>A beam with a composite slab strain in a case (the merged model of Build BridGHe Model: the strain of a workflow action on
        /// the beams it acts on, a checks input): the reader records the load and its case and carries it (no GPC load).</summary>
        internal static LegacyBeamDraft WithSlabStrain(LegacyBeamDraft draft, LegacyCaseData loadCase)
        {
            var d = GpcDrafts.Copy(draft ?? throw new ArgumentNullException(nameof(draft)));
            d.Records.LoadList.Add((SlabStrainRecord, loadCase.Name)); d.Records.LoadCaseList.Add(loadCase);
            d.Report.Carry(SlabStrainCarry);
            return GpcBeamBuilders.Finish(new GpcBeamBuilders.Work(d));
        }

        /// <summary>A node without its restraints and springs (Bridge Construction Sequence, a node that is not an active bearing: the former
        /// transformer removed its restraint and stiffness attributes; a coordinate system stays).</summary>
        internal static LegacyNodeDraft WithoutRestraints(LegacyNodeDraft draft)
        {
            var d = GpcDrafts.Copy(draft ?? throw new ArgumentNullException(nameof(draft)));
            d.Node.Assignments.Restrains.RemoveAll(r => r.SourceRecord == LegacyModelImport.RestraintRecord || r.SourceRecord == LegacyModelImport.StiffnessRecord);
            return d;
        }
    }
}

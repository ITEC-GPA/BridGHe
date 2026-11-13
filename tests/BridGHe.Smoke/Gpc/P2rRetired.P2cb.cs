using System;
using System.Collections.Generic;

/// <summary>
/// P2CB (13/11/2026, rule U3 of the user, brief P2CT): the checks of the native, parity and example side retired by name with the FEM model types
/// they used, key <c>scope|name</c> → reason (with the GPC coverage that replaces it). Read by <see cref="P2rRetired.Checks"/> (one hook line), so
/// <c>--regression &lt;retired name&gt;</c> answers with its reason as for the retirements of P2CUT. Scopes as in <see cref="P2rRetired.Checks"/>, and
/// <c>native</c> for a targeted native run that no gate runs. A retirement is never a widened tolerance: every other check keeps its oracle.
/// </summary>
internal static class P2rRetiredP2cb
{
    /// <summary>The retirements of lane P2CB, added to <see cref="P2rRetired.Checks"/> by its builder (the reason gets the decision of P2CUT).</summary>
    internal static void AddTo(Action<string, string> add)
    {
        const string FemType = "a check of the FEM model types (BridGHe.Common.Models), which the GPC path does not use";
        add("core-native|par native transitional parity difference", "transitional (R4b): the 0.3.0 parity model against the conversion of its former FEM model (R4bInputs.Former, a printed line, not a condition); "
            + "the GPC parity model read back from Straus7 against the 0.3.0 rules: ParityNative (PAR, core-native)");
        add("native|gpc-pilot", FemType + " (the F4 pilot, --gpc-pilot, in no gate: the FEM solve of Build Model against GpcPilotAdapter, the F4 adapter of a FEM model); "
            + "on GPC: the analysis of the GPC-only input (gpc-only-analysis-native, --examples), the archive of an analysed GPC model and its reopening (gpc-results-native, gpc-draft-archive)");
        const string PhaseFem = "the FEM output of 0.3.0 Phase Model (BridgePhaseModel.Create on a FEM model), which the GPC path does not use; on GPC: the same releases of P1 on the double angle "
            + "through the GPC transform (GpcModelTransforms.PhaseModel), recorded against the calls of 0.3.0 (gpc-c5: «phase model (GPC transform)», 4 checks) and read back from Straus7 (C5 native, «phase model»)";
        add("regression-part|gpc-c5: phase FEM output: P1 on the double angle keeps its releases (0.3.0 Phase Model)", PhaseFem + " (1 check)");
        add("regression-part|gpc-c5: phase model (conversion of the FEM output)", PhaseFem + " (4 checks: refusal without the option, written with it, the release calls of P1, every other call)");
        const string C3Reader = "the reader of the FEM value of the same recipe (GpcMaterialBuilders.Femm, GpcMaterials.FromFemm, GpcModels.Material and BeamProperty of a FEM "
            + "property, the plate property of a FEM archive) against the GPC builders; on GPC: the builders checked against the recipe (analysis data, law, G, damping, thermal "
            + "data, table on the types of 0.3.0, plates without G, membranes refused, slab grillages: gpc-c3), written to Straus7 against the calls of 0.3.0 (C3 native)";
        add("regression-part|gpc-c3: the reader of the FEM value gives the same material, and the same analysis material", C3Reader + " (14 checks: 2 per material)");
        add("regression-part|gpc-c3: the reader of the FEM value makes the beam, truss and cable property, the same as the builder", C3Reader + " (38 checks: 2 per property)");
        add("regression-part|gpc-c3: the reader of the FEM value makes the plate property, the same as the builder", C3Reader + " (14 checks: 2 per material)");
        const string C3Drawing = "the FEM drawing as the oracle of the GPC drawing (BeamModel.CreateShapes and Shapes, ModelPreview.Bounds of a FEM element, CompositeBeamModel, "
            + "PlateModel.Shape, the FEM homogenised property); on GPC: the preview of the drafts the components give (the solids of the draft, its clipping box, the composite "
            + "datum: gpc-c3 and C3 native) and the explicit counts, boxes and volumes of a beam, a composite beam, a plate and a homogenised property (gpc-c3), p2d (U5)";
        add("regression-part|gpc-c3: TRANSITIONAL the GPC drawing of each preview kind equals the FEM drawing", C3Drawing + " (11 checks; the box check keeps its GPC half: the box of the draft)");
        add("regression-part|gpc-c3: a beam with its FEM value clips to the FEM box", C3Drawing + " (1 check)");
        add("regression-part|gpc-c3: TRANSITIONAL the FEM composite beam, plate and homogenised composite property against their GPC drafts", C3Drawing + " (4 checks: composite solids, composite bar lines, plate solid, homogenised property)");
        add("core-native|c3 native: TRANSITIONAL the GPC drawing of each native section kind equals the FEM drawing", C3Drawing + " (5 checks; the box check keeps its GPC half)");
        const string C1Swap = "the conversion of the FEM output of 0.3.0 as the oracle of the GPC transform; on GPC: the oracle of P2 on every output (Build Model on its drafts: "
            + "the same axes and the same Straus7 calls, 2 checks per output) and the offset axes after the swap (gpc-review-fixes)";
        add("regression-part|gpc-review-fixes: R65-C3-02 Bridge Phase Model against the conversion of the FEM output", "BridgePhaseModel.Create on a FEM model: " + C1Swap + " (12 checks)");
        add("regression-part|gpc-review-fixes: R65-C3-02 Bridge Construction Sequence of the FEM simple bridge (against the FEM sequence and Build Model on its drafts)",
            "BridgeConstructionSequence.Create on the girders of the FEM simple bridge (BridgeConstructionTests.Model, an offset, the loads and a support on the first girder beam): "
            + "the GPC sequence needs the members of Bridge Member Assignment, and the GPC simple bridge (GpcSimpleBridge) has them without drafts; on GPC: the construction "
            + "sequence of the simple bridge (BridgeConstructionTests on GPC: selection, explicit property, pruned nodes, removed bearings, isolated input) and the same swap of an "
            + "ordinary property to the double angle with the offset, loads and support through Bridge Phase Model, against Build Model on its drafts (gpc-review-fixes, R65-C3-02) "
            + "(12 checks: 4 against the FEM output, 8 of the oracle of P2)");
        add("regression-part|gpc-review-fixes: R25-01 a composite property with n0 = 0 is not a definition, and Composite Beam refuses it at each end", FemType
            + " (the refused section was a FEM composite property given to its GH type; the GPC components give no composite property that is not a definition); on GPC: two valid "
            + "sections give the composite beam (gpc-review-fixes), the definitions of the composite components (gpc-only-models, p2k-components) (5 checks)");
        add("regression-part|gpc-review-fixes: P30-01 the model with its FEM value (drafts and results saved, damaged, read back)", FemType
            + " (Build Model on FEM-backed elements, GH_Model.FromModel of a FEM model); on GPC: the same checks on the model without FEM value (gpc-review-fixes), the former files: p2d (14 checks)");
        add("regression|gpc-bridge-model", "the GPC bridge model of Build BridGHe Model (GpcBridgeBuilder) against the former seam on the merged FEM model (BridgeWorkflowBuilder.Build on FEM "
            + "models with FEM results, GpcBridgeComparison.Legacy), and the GPC links of a duplicated action built on a FEM source: FEM types; on GPC: the bridge model of the examples with "
            + "their frozen captures and fingerprints (natives 50 and 99, phase-native D3), merge, reactions and Super Element Check forces on GPC data (r2b-merge-forces), the GPC "
            + "links of the bridge documents (RequireGpcLinks in the natives 50 and 99) (18 workflows and 1 duplicate check)");
        add("regression-part|gpc-composite-sections: FEM parity (GpcCompositeSectionChecks.Fem.cs)", FemType + " (the definitions of the FEM composite properties of the same numbers, the "
            + "analysis values of the FEM properties, the former beam definitions, the GH type and the preview on a FEM property: 38 + 720 + 334 checks and the rest of the partial); on GPC: "
            + "the definitions from the numbers against the former construction, the analysis values, the beam definitions and the R6.0 capture (gpc-composite-sections, unchanged)");
        add("regression-part|inr-inertia: the FEM homogenised property of the same numbers (I11, I22, angle 0, shear areas, area)", "CompositeBeamPropertyModel.CreateHomogenizedProperty, a FEM "
            + "type; on GPC: the GPC homogenised property against the same expected values from the analysis properties (inr-inertia: I11, I22, shear areas, area bit for bit) (36 checks: "
            + "the FEM I11/I22 and angle of 18 variants; the shear area and area checks keep their GPC half)");
        add("regression-part|gpc-builders-a: a node with a former displacement (FEM node conversion, its BGHA1 payload, a former model with it, Node Filter on it)",
            "a FEM node with a NodeDisplacementLoadModel (GpcBeamDrafts.FromFemm, ModelArchive.Write of the node and of a model), which no GPC component gives (Node Displacement "
            + "is refused); on GPC: Node Displacement refused with the same message (gpc-builders-a), the frozen 0.3.0 payloads refusal-prescribed-restrained and -free refused "
            + "with the same message (gpc-legacy-import) (3 expected refusals and 1 run of Node Filter)");
        add("regression-part|gpc-builders-a: P2X the BGHA1 payload of a 0.3.0 plate with an angle and a whole 0.3.0 model with it (written from FEM values)",
            "a FEM plate with an angle written by ModelArchive.Write as the 0.3.0 payload and model; on GPC: the layer axes of the plates with an angle the components give "
            + "(Plate, Align Plate Axes, Build Model: Turned and PlateAngles in gpc-builders-a), the 0.3.0 plate angle against the frozen Plate Results of 0.3.0 (PlateAngleNative, "
            + "Fixtures/plate-angle-030) (2 checks)");
        add("regression-part|gpc-review-fixes: R65-C3-02 the global point load of the swap model", "a FEM beam point load in the global system (BeamPointLoadModel.LoadData.CoordinateSystem), "
            + "which Beam Point Load does not give (its loads are in the principal axes): the GPC swap model has the three other loads (no check of its own: fewer recorded calls)");
    }

    /// <summary>The reason of a retirement of this lane, as <see cref="P2rRetired.Checks"/> records it.</summary>
    internal static string Reason(string key) => P2rRetired.Checks.TryGetValue(key, out var reason) ? reason : throw new KeyNotFoundException("P2CB: not retired by name: " + key);
}

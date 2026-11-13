using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Helpers;

namespace BridGHe.Common.Straus
{
    public static class StrausAnalysis
    {
        /// <summary>Solver names of the analysis component, by solver index.</summary>
        public static readonly string[] Solvers = { "Linear Static", "Nonlinear Static", "Staged analysis" };




        /// <summary>R6.5 P1 lane 3e: the analysis of a model given by its GPC input alone, with the same requests and refusals as the FEM path: the
        /// paths and result names come from the input, and the analysed GPC model (with the results catalog) is returned; no FEM value is made.</summary>
        public static GPC.Model.Models.Model Run(Gpc.GpcModels.Conversion input, string outputPath, int solver, IReadOnlyCollection<string> requested, bool movingOnly, out List<string> warnings)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            outputPath = Path.GetFullPath(outputPath);
            CheckRequest(outputPath, solver, movingOnly);
            RefuseCables(input.Model); // P2K (P8): before any Straus7 call
            var paths = input.Model.MovingLoads?.Paths ?? new List<GPC.Model.Actions.Moving.MovingLoadPath>();
            if (movingOnly && !paths.Any(p => p.Options?.ConvertToStaticCases == true)) throw new ArgumentException(NoStaticCasesMessage);
            if (movingOnly && paths.Any(p => p.Template.Vehicles.Any(v => v.HeatSources?.Length > 0))) throw new NotSupportedException(HeatSourceMessage);
            var selection = movingOnly ? Array.Empty<Gpc.GpcResultSelection.Entry>() : Gpc.GpcResultSelection.Select(input.Model, solver, requested);
            warnings = new List<string>();
            var analysed = Gpc.Legacy.GpcAnalysisSeam.Run(input, outputPath, solver, selection, movingOnly, warnings);
            if (string.IsNullOrEmpty(analysed.Name)) analysed.Name = Path.GetFileNameWithoutExtension(outputPath);
            return analysed;
        }

        /// <summary>P2K (P8, coordinator decision of 12/11: the refusal in BridGHe, the library unchanged): BridGHe 0.4.0 does not write a cable
        /// (Beam Property type Cable [1], formulation <see cref="GPC.Model.Structure.Assignments.BeamFormulation.Cable"/>) to Straus7: Straus7 R31
        /// refuses the beam type (error 41 of St7SetBeamShearModulusMode, as for 0.3.0, which refused every cable). The model is refused before any
        /// Straus7 call (no file written), with the beam, its property and what to use instead. Called by the analyses and by Straus7 R3 Export. A
        /// cutoff bar (written since 0.4.0, user decision of 11/11) is not touched.</summary>
        public static void RefuseCables(GPC.Model.Models.Model model)
        {
            if (model?.BeamElements == null) return;
            foreach (var beam in model.BeamElements.Values)
                if (beam.Assignments.Formulation == GPC.Model.Structure.Assignments.BeamFormulation.Cable)
                    throw new NotSupportedException(CableMessage(beam.Name, beam.BeamProperty?.Name));
        }

        /// <summary>The message of the refusal of a cable (stable code <c>StrausCableNotSupported</c> first).</summary>
        public static string CableMessage(string beam, string property) =>
            "StrausCableNotSupported: la trave '" + beam + "' ha la proprietà '" + property + "' di tipo Cable [1]. BridGHe 0.4.0 non scrive le funi in Straus7: "
            + "il modello non è stato esportato né analizzato. Usare una proprietà Cutoff Bar [3] con Max compression 0 (asta solo tesa) oppure Truss [2].";

        private const string NoStaticCasesMessage = "Nessun percorso configurato per generare casi statici.";
        private const string HeatSourceMessage = "Moving Static Analysis è strutturale. Le sorgenti termiche vanno esportate e risolte con il solutore termico Straus.";

        private static void CheckRequest(string outputPath, int solver, bool movingOnly)
        {
            if (solver < 0 || solver > 2) throw new ArgumentOutOfRangeException(nameof(solver));
            if (!string.Equals(Path.GetExtension(outputPath), ".st7", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Estensione richiesta: .st7");
            if (movingOnly && solver != 0) throw new ArgumentException("Moving Static Analysis richiede il solutore lineare.");
        }
    }
}

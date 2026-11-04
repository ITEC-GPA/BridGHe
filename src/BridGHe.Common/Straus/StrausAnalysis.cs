using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Straus
{
    public static class StrausAnalysis
    {
        /// <summary>Solver names of the analysis component, by solver index.</summary>
        public static readonly string[] Solvers = { "Linear Static", "Nonlinear Static", "Staged analysis" };

        public static ModelModel Run(ModelModel input, string outputPath, int solver, IReadOnlyCollection<string> requested, double tolerance, bool movingOnly, out List<string> warnings)
            => Run(input, null, outputPath, solver, requested, tolerance, movingOnly, out warnings, out _);

        public static ModelModel Run(ModelModel input, string outputPath, int solver, IReadOnlyCollection<string> requested, double tolerance, bool movingOnly,
            out List<string> warnings, out GPC.Model.Models.Model analysed)
            => Run(input, null, outputPath, solver, requested, tolerance, movingOnly, out warnings, out analysed);

        /// <param name="conversion">F6 phase (c), C3: the GPC conversion the GH model keeps for <paramref name="input"/> (GH_Model.GpcInput), copied by
        /// the analysis instead of converting again; null converts the FEM model.</param>
        /// <param name="analysed">F6 phase (c), C3: the analysed GPC model (results as GPC datasets), for the GH model of the output.</param>
        public static ModelModel Run(ModelModel input, Gpc.GpcModels.Conversion conversion, string outputPath, int solver, IReadOnlyCollection<string> requested, double tolerance, bool movingOnly,
            out List<string> warnings, out GPC.Model.Models.Model analysed)
        {
            // The native solver accepts only canonical Windows paths when opening
            // its result file, although St7NewFile also accepts forward slashes.
            outputPath = Path.GetFullPath(outputPath);
            if (solver < 0 || solver > 2) throw new ArgumentOutOfRangeException(nameof(solver));
            if (!string.Equals(Path.GetExtension(outputPath), ".st7", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Estensione richiesta: .st7");
            if (movingOnly && solver != 0) throw new ArgumentException("Moving Static Analysis richiede il solutore lineare.");
            if (movingOnly && !input.MovingPaths.Any(p => p.ConvertToStaticCases)) throw new ArgumentException("Nessun percorso configurato per generare casi statici.");
            if (movingOnly && input.MovingPaths.Any(p => p.Template.Vehicles.Any(v => v.Loads.Any(l => l is MovingLoads.MovingHeatSource))))
                throw new NotSupportedException("Moving Static Analysis è strutturale. Le sorgenti termiche vanno esportate e risolte con il solutore termico Straus.");
            var model = ModelArchive.Clone(input);
            var oldMovingNames = new HashSet<string>(model.GeneratedMovingCases.Values);
            model.LoadCases.RemoveAll(c => oldMovingNames.Contains(c.Name));
            foreach (var element in model.Elements) element.Results.Clear();
            ModelHelper.GetLoadCases(model, out var cases);
            var selection = movingOnly ? Array.Empty<StrausResultSelection.Entry>() : StrausResultSelection.Select(model, solver, requested);
            // F6A: the analysis runs through GPC and the GPC Converter (writer, solver, verified result import); the former
            // Straus7 exporter and result reader are no longer used. The FEM model of the components crosses a transitional seam.
            warnings = new List<string>();
            // C3: the generated cases of an earlier moving analysis are removed from the FEM clone above; the conversion of the GH model still
            // has them as load cases, so such a model is converted from the clone, as before.
            if (oldMovingNames.Count != 0) conversion = null;
            var result = Gpc.Legacy.FemmAnalysisSeam.Run(model, conversion, outputPath, solver, selection, tolerance, movingOnly, warnings, out analysed);
            if (string.IsNullOrEmpty(result.Name)) result.Name = Path.GetFileNameWithoutExtension(outputPath);
            return result;
        }
    }
}

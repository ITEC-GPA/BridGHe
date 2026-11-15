using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Model.Core.Diagnostics;
using GPC.Model.Models;

namespace BridGHe.Common.Gpc
{
    /// <summary>Domain preflight of a GPC model before an analysis or a check (coordinator note of 4/11: since L1, reading, copying and
    /// fingerprinting no longer validate a model). Every BridGHe analysis/check path calls <see cref="Run"/> and shows the messages
    /// as Grasshopper runtime messages; errors stop the run.</summary>
    public static class GpcPreflight
    {
        public sealed class Result
        {
            public IReadOnlyList<string> Errors { get; internal set; }
            public IReadOnlyList<string> Warnings { get; internal set; }
            public bool Passed => Errors.Count == 0;
        }

        public static Result Run(Model model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var diagnostics = model.ValidateDomain();
            string Text(ModelDiagnostic d) => d.Code + (d.ElementId.HasValue ? " (" + d.Family + " " + d.ElementId + ")" : "") + (string.IsNullOrWhiteSpace(d.Message) ? "" : ": " + d.Message);
            return new Result
            {
                Errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(Text).ToArray(),
                Warnings = diagnostics.Where(d => d.Severity != DiagnosticSeverity.Error).Select(Text).ToArray(),
            };
        }

        /// <summary>Throws with every error code when the model cannot be analysed or checked.</summary>
        public static Result Require(Model model)
        {
            var result = Run(model);
            if (!result.Passed) throw new InvalidOperationException("GpcModelPreflightFailed: " + string.Join("; ", result.Errors));
            return result;
        }
    }
}

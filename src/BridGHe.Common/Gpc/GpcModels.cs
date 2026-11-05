using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// F6 phase (c), C2: the GPC model (N-mm) of a former FEM model of the components, converted with the legacy reader (the semantics of
    /// the former exporter, verified on examples 50/99, 01–10 and 77 captured models) until the components build GPC models themselves.
    /// The identity is derived from the content and the tolerance: the same FEM model gives the same GPC model, element GUIDs included.
    /// Results of the former model are not converted (an analysis attaches its own GPC results).
    /// </summary>
    public static class GpcModels
    {
        /// <summary>C3: a conversion and what the analysis needs with it: the import report (warnings, resolved native sections by beam
        /// GUID) and the tolerance used. Immutable by convention: analysis and export copy <see cref="Model"/> before changing it.</summary>
        public sealed class Conversion
        {
            public GPC.Model.Models.Model Model { get; }
            public LegacyImportReport Report { get; }
            public double Tolerance { get; }
            internal Conversion(GPC.Model.Models.Model model, LegacyImportReport report, double tolerance) { Model = model; Report = report; Tolerance = tolerance; }
        }

        public static Conversion Convert(ModelModel model, double toleranceDefinitionUnits)
        {
            var gpc = FromFemm(model, toleranceDefinitionUnits, out var report);
            return new Conversion(gpc, report, toleranceDefinitionUnits);
        }

        public static GPC.Model.Models.Model FromFemm(ModelModel model, double toleranceDefinitionUnits, out LegacyImportReport report)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            // F6 chain C: the results of an analysed model are never converted, so they are not archived either: the conversion reads a copy
            // without them. The identity does not depend on the results, and the size limit of the legacy reader concerns the input only (the
            // analysed phase models of example 50, the references of Build BridGHe Model, exceed 256 MB with their results).
            if (model.Elements.Any(e => e.Results.Count != 0))
            {
                model = new ModelModel(model);
                foreach (var e in model.Elements) e.Results.Clear();
            }
            byte[] archive = ModelArchive.Write(model);
            var owner = GpcIdentity.Derive(GpcIdentity.NameSpace, "femm-model:" + System.Convert.ToHexString(SHA256.HashData(archive)) + ":"
                + toleranceDefinitionUnits.ToString("R", CultureInfo.InvariantCulture));
            report = new LegacyImportReport();
            return LegacyModelImport.Import(LegacyArchive.Read(archive), owner, toleranceDefinitionUnits, report);
        }
    }
}

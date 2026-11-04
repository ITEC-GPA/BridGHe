using System;
using System.Globalization;
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
        public static GPC.Model.Models.Model FromFemm(ModelModel model, double toleranceDefinitionUnits, out LegacyImportReport report)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            byte[] archive = ModelArchive.Write(model);
            var owner = GpcIdentity.Derive(GpcIdentity.NameSpace, "femm-model:" + Convert.ToHexString(SHA256.HashData(archive)) + ":"
                + toleranceDefinitionUnits.ToString("R", CultureInfo.InvariantCulture));
            report = new LegacyImportReport();
            return LegacyModelImport.Import(LegacyArchive.Read(archive), owner, toleranceDefinitionUnits, report);
        }
    }
}

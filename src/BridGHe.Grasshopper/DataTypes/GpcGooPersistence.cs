using System;
using GH_IO.Serialization;

namespace BridGHe.Grasshopper.DataTypes
{
    /// <summary>
    /// R6.1 (coordinator, 6/11): the chunks of the GH types that carry a GPC datum (§1bis, «Persistenza»).
    /// <list type="bullet">
    /// <item><c>Data</c>: the BGHGPC1 value of the GPC object (<see cref="Common.Gpc.GpcDraftArchive"/>), or the bridge data of a composite
    /// property; absent for the data that are BridGHe records (case, combination, stage, section);</item>
    /// <item><c>BridGHeRecords</c>: the records of the datum (<see cref="Common.Gpc.GpcDraftArchive"/>, DataContract XML, schema 1);</item>
    /// <item><c>FemmR6</c>: <b>transitional</b>, until the removal of FeMM (R6.5): the BGHA1 archive of the FEM value the components still
    /// build beside the GPC datum, so that copies, undo and saved files keep it for the consumers not yet on GPC. Never released.</item>
    /// </list>
    /// A former payload (0.3.0: <c>Data</c> = BGHA1 of the FEM value) is read by value (<see cref="Common.Gpc.GpcLegacyPayloads"/>); the FEM
    /// value is still read from it until R6.5. Never BinaryFormatter, never NRBF.
    /// </summary>
    internal static class GpcGooPersistence
    {
        internal const string Data = "Data", Records = "BridGHeRecords", Femm = "FemmR6";

        internal enum Kind { None, Gpc, Legacy, FemmOnly }

        /// <summary>Writes the datum (its chunks, either may be null) and, until R6.5, the FEM value (null: none). A datum the reader
        /// refuses (the FEM value has data without a GPC destination) is not written: the FEM value alone, as before R6.1.</summary>
        internal static void Write(GH_IWriter writer, Func<(byte[] Data, byte[] Records)> datum, byte[] femm)
        {
            byte[] data = null, records = null;
            try { (data, records) = datum(); }
            catch (Exception ex) when (femm != null && (ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException || ex is System.Runtime.Serialization.SerializationException)) { }
            if (data != null) writer.SetByteArray(Data, data);
            if (records != null) writer.SetByteArray(Records, records);
            if (femm != null) writer.SetByteArray(Femm, femm);
        }

        /// <summary>What a chunk holds: a datum (BGHGPC1 and records), a former payload (BGHA1 in <c>Data</c>), only the FEM value, or nothing.</summary>
        internal static Kind Read(GH_IReader reader, out byte[] data, out byte[] records, out byte[] femm)
        {
            data = reader.ItemExists(Data) ? reader.GetByteArray(Data) : null;
            records = reader.ItemExists(Records) ? reader.GetByteArray(Records) : null;
            femm = reader.ItemExists(Femm) ? reader.GetByteArray(Femm) : null;
            if (data != null && Common.Gpc.GpcGooArchive.IsLegacy(data)) { femm = data; return Kind.Legacy; }
            if (data != null || records != null) return Kind.Gpc;
            return femm != null ? Kind.FemmOnly : Kind.None;
        }
    }
}

using System;
using GH_IO.Serialization;

namespace BridGHe.Grasshopper.DataTypes
{
    /// <summary>
    /// R6.1 (coordinator, 6/11): the chunks of the GH types that carry a GPC datum (§1bis, «Persistenza»).
    /// <list type="bullet">
    /// <item><c>Data</c>: the BGHGPC1 value of the GPC object (<see cref="Common.Gpc.GpcDraftArchive"/>), or the bridge data of a composite
    /// property; absent for the data that are BridGHe records (case, combination, stage, section);</item>
    /// <item><c>BridGHeRecords</c>: the records of the datum (<see cref="Common.Gpc.GpcDraftArchive"/>, DataContract XML, schema 1).</item>
    /// </list>
    /// P2D (P2, 12/11): the transitional chunk <c>FemmR6</c> (the BGHA1 archive of the FEM value the components built beside the datum until
    /// P2) is never written. In a development file it is read by value, as a former payload: beside a datum it is not read as a FEM value (the
    /// datum is the value; <c>GH_Model</c> takes from it only the drafts of its elements, see there), alone it is the datum by value
    /// (<see cref="Kind.Legacy"/>). A former payload (0.3.0: <c>Data</c> = BGHA1) is read by value (<see cref="Common.Gpc.GpcLegacyPayloads"/>)
    /// and no FEM value is made of it. Never BinaryFormatter, never NRBF.
    /// </summary>
    internal static class GpcGooPersistence
    {
        internal const string Data = "Data", Records = "BridGHeRecords", Femm = "FemmR6";

        /// <summary>What a chunk holds: a datum (BGHGPC1 and records), a former payload or a development FEM chunk alone (BGHA1, read by
        /// value), or nothing.</summary>
        internal enum Kind { None, Gpc, Legacy }

        /// <summary>Writes the datum (its chunks, either may be null, not both). P2D: no FEM chunk; a datum that cannot be written throws (the GH
        /// type fails its write: no partial datum, no FEM value in its place).</summary>
        internal static void Write(GH_IWriter writer, (byte[] Data, byte[] Records) datum)
        {
            if (datum.Data == null && datum.Records == null) throw new InvalidOperationException("BridgheGooWithoutDatum");
            if (datum.Data != null) writer.SetByteArray(Data, datum.Data);
            if (datum.Records != null) writer.SetByteArray(Records, datum.Records);
        }

        /// <summary>What a chunk holds (see <see cref="Kind"/>). <paramref name="femm"/>: the BGHA1 bytes to read by value for
        /// <see cref="Kind.Legacy"/> (the former <c>Data</c>, or the FEM chunk alone of a development file); for <see cref="Kind.Gpc"/> the FEM
        /// chunk of a development file beside the datum, or null.</summary>
        internal static Kind Read(GH_IReader reader, out byte[] data, out byte[] records, out byte[] femm)
        {
            data = reader.ItemExists(Data) ? reader.GetByteArray(Data) : null;
            records = reader.ItemExists(Records) ? reader.GetByteArray(Records) : null;
            femm = reader.ItemExists(Femm) ? reader.GetByteArray(Femm) : null;
            if (data != null && Common.Gpc.GpcGooArchive.IsLegacy(data)) { femm = data; data = null; return Kind.Legacy; }
            if (data != null || records != null) return Kind.Gpc;
            return femm != null && Common.Gpc.GpcGooArchive.IsLegacy(femm) ? Kind.Legacy : Kind.None;
        }

        /// <summary>P2D (P4): the message of a datum the reader cannot read, to the file reader (Grasshopper lists it when the file opens), and
        /// false. A datum of a newer schema gets the message of <see cref="Common.Gpc.GpcDraftArchive.NewerSchemaMessage"/>.</summary>
        internal static bool Refused(GH_IReader reader, string type, Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            try { reader?.AddMessage(type + ": " + Common.Gpc.GpcDraftArchive.UserMessage(ex), GH_Message_Type.error); } catch (Exception) { }
            return false;
        }
    }
}

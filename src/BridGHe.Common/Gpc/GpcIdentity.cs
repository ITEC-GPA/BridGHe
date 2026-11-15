using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BridGHe.Common.Gpc
{
    /// <summary>Deterministic identities of GPC objects created by BridGHe: the owner (component instance, definition
    /// or imported model) and a stable key (topology address, case name, ...). A recomputation gives the same GUIDs.</summary>
    public static class GpcIdentity
    {
        public static Guid Derive(Guid owner, string key)
        {
            if (owner == Guid.Empty) throw new ArgumentException("EmptyIdentityOwner", nameof(owner));
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("EmptyIdentityKey", nameof(key));
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(owner.ToString("D") + "\n" + key)).Take(16).ToArray();
            bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // RFC 4122 name-based layout (version 5 bits), never Guid.Empty
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            return new Guid(bytes);
        }

        /// <summary>
        /// The digest of a FEM archive (BGHA1) written by this build, as the source of an identity (the drafts of FEM values, the owner of
        /// the conversion of a FEM model): the archive names its types by their assembly-qualified names, with the version of
        /// BridGHe.Common, so the digest is taken with that version written as <see cref="IdentityVersion"/>, the version of the builds that
        /// made the identities until now. The same value gives the same GUIDs whatever the version of the plug-in (P2S, master dry run
        /// 7/11: with the version of master the GUIDs of every computed draft changed, and with them the bridge-data fingerprints, the
        /// recipes and the input fingerprints of the analyses). The stored payloads of former files are not archives of this build: their
        /// digest stays that of their bytes (<see cref="GpcLegacyPayloads"/>).
        /// </summary>
        public static string ArchiveDigest(byte[] archive)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            byte[] from = VersionToken(typeof(GpcIdentity).Assembly.GetName().Version.ToString()), to = VersionToken(IdentityVersion);
            if (from.AsSpan().SequenceEqual(to) || archive.AsSpan().IndexOf(from) < 0) return Convert.ToHexString(SHA256.HashData(archive));
            using var written = new System.IO.MemoryStream(archive.Length);
            var rest = archive.AsSpan();
            for (int i = rest.IndexOf(from); i >= 0; i = rest.IndexOf(from)) { written.Write(rest.Slice(0, i)); written.Write(to); rest = rest.Slice(i + from.Length); }
            written.Write(rest);
            return Convert.ToHexString(SHA256.HashData(written.ToArray()));
        }

        /// <summary>The version of BridGHe.Common the identity digests are taken with (<see cref="ArchiveDigest"/>).</summary>
        // Frozen: identities must not depend on the assembly version (never change it with the version of the plug-in).
        public const string IdentityVersion = "0.2.0.0";

        // A type name in the archive is "T" and the hexadecimal UTF-8 of the assembly-qualified name (ModelArchive).
        private static byte[] VersionToken(string version) =>
            Encoding.ASCII.GetBytes(Convert.ToHexString(Encoding.UTF8.GetBytes("BridGHe.Common, Version=" + version + ",")));

        /// <summary>Load cases, combinations and groups are identified by name across the whole definition (as in BridGHe today,
        /// and as ModelAssembly requires: one GUID per name, review B6), so their namespace does not depend on the component.</summary>
        public static readonly Guid NameSpace = new Guid("0b7a6f0e-3c1d-4f53-9a51-5b9d2a6c7e11");
        public static Guid Case(string name) => Derive(NameSpace, "case:" + name);
        public static Guid FreedomCase(string name) => Derive(NameSpace, "freedom:" + name);
        public static Guid Combination(string name) => Derive(NameSpace, "combination:" + name);
        public static Guid Group(string fullName) => Derive(NameSpace, "group:" + fullName);
        public static Guid Stage(string name) => Derive(NameSpace, "stage:" + name);
    }
}

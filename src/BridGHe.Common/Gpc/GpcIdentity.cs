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

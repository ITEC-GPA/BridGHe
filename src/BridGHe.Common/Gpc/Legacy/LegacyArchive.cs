using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace BridGHe.Common.Gpc.Legacy
{
    /// <summary>One value of a legacy BridGHe archive: a typed object with named fields, a collection, a scalar or null.
    /// Field names are the SerializationInfo names (or the member names of [Serializable] types, without backing-field decoration).
    /// No CLR type is instantiated: the type name is data.</summary>
    public sealed class LegacyValue
    {
        public string Type { get; internal set; }
        public bool IsNull { get; internal set; }
        public string Text { get; internal set; }
        public bool IsCollection { get; internal set; }
        public IReadOnlyList<LegacyValue> Items => items;
        public IReadOnlyDictionary<string, LegacyValue> Fields => fields;
        internal readonly List<LegacyValue> items = new List<LegacyValue>();
        internal readonly Dictionary<string, LegacyValue> fields = new Dictionary<string, LegacyValue>(StringComparer.Ordinal);
        internal static readonly LegacyValue Null = new LegacyValue { IsNull = true };

        /// <summary>Simple type name without namespace or nesting prefix, e.g. "NodeModel" or "LoadData".</summary>
        public string ShortType
        {
            get
            {
                if (Type == null) return null;
                string t = Type; int generic = t.IndexOf('`'); if (generic >= 0) t = t.Substring(0, generic);
                int dot = Math.Max(t.LastIndexOf('.'), t.LastIndexOf('+')); return dot < 0 ? t : t.Substring(dot + 1);
            }
        }
        public bool Is(string shortType) => !IsNull && ShortType == shortType;
        public bool Has(string name) => fields.TryGetValue(name, out var v) && !v.IsNull;
        public LegacyValue this[string name] => fields.TryGetValue(name, out var v) ? v : Null;
        public LegacyValue Get(string name) => this[name];
        /// <summary>The first present field among the names: types written by ISerializable use the SerializationInfo names,
        /// [Serializable] types without ISerializable (for example PlatePropertyModel, StagedConstructionModel) their field names.</summary>
        public LegacyValue Any(params string[] names) { foreach (var n in names) if (fields.TryGetValue(n, out var v)) return v; return Null; }
        public LegacyValue Field(string name) => fields.TryGetValue(name, out var v) ? v : throw new SerializationException("LegacyFieldMissing: " + (Type ?? "?") + "." + name);

        public double Double(string name) => Field(name).AsDouble();
        public double Double(string name, double fallback) => Has(name) ? this[name].AsDouble() : fallback;
        public int Int(string name) => Field(name).AsInt();
        public int Int(string name, int fallback) => Has(name) ? this[name].AsInt() : fallback;
        public bool Bool(string name) => Field(name).AsBool();
        public bool Bool(string name, bool fallback) => Has(name) ? this[name].AsBool() : fallback;
        public string String(string name) => this[name].IsNull ? null : this[name].Text ?? "";
        public string Enum(string name) => Field(name).Text?.Trim();

        public double AsDouble()
        {
            if (IsNull || Text == null) throw new SerializationException("LegacyDoubleMissing");
            return XmlConvert.ToDouble(Text);
        }
        public int AsInt()
        {
            if (IsNull || Text == null) throw new SerializationException("LegacyIntMissing");
            return XmlConvert.ToInt32(Text);
        }
        public bool AsBool()
        {
            if (IsNull || Text == null) throw new SerializationException("LegacyBoolMissing");
            return XmlConvert.ToBoolean(Text);
        }
        public override string ToString() => IsNull ? "null" : IsCollection ? (Type + "[" + items.Count + "]") : Text != null ? Text : Type;
    }

    /// <summary>Reader of the BGHA1 BridGHe payloads (DataContractSerializer binary XML of the former FeMM graphs).
    /// The payload is parsed as data: type tokens are decoded to names, references are resolved, nothing is deserialized
    /// into CLR objects. Limits follow the former archive (256 MiB, depth 512).</summary>
    public static class LegacyArchive
    {
        private static readonly byte[] Header = Encoding.ASCII.GetBytes("BGHA1");
        public const int MaximumBytes = 256 * 1024 * 1024;
        private const string Z = "http://schemas.microsoft.com/2003/10/Serialization/";
        private const string I = "http://www.w3.org/2001/XMLSchema-instance";
        private const string TokenNamespace = "urn:gpc:bridghe:model:v1";

        public static bool IsLegacy(byte[] data) => data != null && data.Length >= Header.Length && data.Take(Header.Length).SequenceEqual(Header);

        public static LegacyValue Read(byte[] data)
        {
            if (!IsLegacy(data)) throw new SerializationException("NotALegacyBridgheArchive");
            if (data.Length > MaximumBytes) throw new SerializationException("LegacyBridgheArchiveTooLarge");
            var quotas = new XmlDictionaryReaderQuotas { MaxArrayLength = 100000000, MaxStringContentLength = 64000000, MaxDepth = 512, MaxBytesPerRead = 65536, MaxNameTableCharCount = 64000000 };
            using var reader = XmlDictionaryReader.CreateBinaryReader(data, Header.Length, data.Length - Header.Length, quotas);
            var ids = new Dictionary<string, LegacyValue>(StringComparer.Ordinal);
            reader.MoveToContent();
            // The root element carries its data contract name instead of a type token.
            const string contractPrefix = "http://schemas.datacontract.org/2004/07/";
            string rootType = reader.NamespaceURI.StartsWith(contractPrefix, StringComparison.Ordinal)
                ? reader.NamespaceURI.Substring(contractPrefix.Length) + "." + reader.LocalName : reader.LocalName;
            var root = ReadValue(reader, ids, 0);
            if (root.Type == null) root.Type = rootType;
            if (reader.MoveToContent() != XmlNodeType.None) throw new SerializationException("TrailingLegacyBridgheContent");
            return root;
        }

        private static LegacyValue ReadValue(XmlDictionaryReader reader, Dictionary<string, LegacyValue> ids, int depth)
        {
            if (depth > 400) throw new SerializationException("LegacyBridgheArchiveTooDeep");
            string reference = reader.GetAttribute("Ref", Z);
            string id = reader.GetAttribute("Id", Z);
            string size = reader.GetAttribute("Size", Z);
            string nil = reader.GetAttribute("nil", I);
            string type = DecodeType(reader, reader.GetAttribute("type", I));
            if (reference != null)
            {
                if (!ids.TryGetValue(reference, out var shared)) throw new SerializationException("UnresolvedLegacyReference");
                reader.Skip(); return shared;
            }
            if (nil == "true") { reader.Skip(); return LegacyValue.Null; }
            var value = new LegacyValue { Type = type, IsCollection = size != null };
            if (id != null) ids[id] = value;
            if (reader.IsEmptyElement) { reader.Read(); if (!value.IsCollection) value.Text = ""; return value; }
            reader.Read();
            var text = new StringBuilder(); bool children = false;
            while (reader.NodeType != XmlNodeType.EndElement)
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        children = true;
                        string name = FieldName(reader.LocalName);
                        var child = ReadValue(reader, ids, depth + 1);
                        if (value.IsCollection) value.items.Add(child);
                        else value.fields[name] = child;
                        break;
                    case XmlNodeType.Text: case XmlNodeType.CDATA: case XmlNodeType.SignificantWhitespace: case XmlNodeType.Whitespace:
                        text.Append(reader.ReadContentAsString()); break;
                    default: reader.Read(); break;
                }
            }
            reader.ReadEndElement();
            if (!children && !value.IsCollection) value.Text = text.ToString();
            return value;
        }

        /// <summary>"&lt;Family&gt;k__BackingField" becomes "Family"; ISerializable names are unchanged.</summary>
        private static string FieldName(string local)
        {
            string decoded = XmlConvert.DecodeName(local);
            if (decoded.StartsWith("<", StringComparison.Ordinal))
            {
                int end = decoded.IndexOf('>'); if (end > 1) return decoded.Substring(1, end - 1);
            }
            return decoded;
        }

        private static string DecodeType(XmlReader reader, string qualified)
        {
            if (string.IsNullOrEmpty(qualified)) return null;
            int colon = qualified.IndexOf(':');
            string prefix = colon < 0 ? "" : qualified.Substring(0, colon), local = colon < 0 ? qualified : qualified.Substring(colon + 1);
            string ns = reader.LookupNamespace(prefix);
            if (ns != TokenNamespace || local.Length < 3 || local[0] != 'T' || local.Length % 2 != 1) return local;
            string aqn;
            try
            {
                var bytes = new byte[(local.Length - 1) / 2];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = byte.Parse(local.Substring(1 + 2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                aqn = Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException error) { throw new SerializationException("InvalidLegacyTypeToken", error); }
            return StripAssemblies(aqn);
        }

        /// <summary>Type name without assembly qualification, also inside generic arguments.</summary>
        internal static string StripAssemblies(string aqn)
        {
            // "List`1[[A.B, Asm, Version=..]], System.Private.CoreLib, ..." -> "List`1[[A.B]]".
            var result = new StringBuilder(); int depth = 0, skipUntil = -1;
            for (int k = 0; k < aqn.Length; k++)
            {
                char c = aqn[k];
                if (skipUntil >= 0)
                {
                    if (c == '[') depth++;
                    else if (c == ']') { depth--; if (depth < skipUntil) { skipUntil = -1; result.Append(c); } }
                    continue;
                }
                if (c == '[') { depth++; result.Append(c); continue; }
                if (c == ']') { depth--; result.Append(c); continue; }
                if (c == ',')
                {
                    int next = k + 1; while (next < aqn.Length && aqn[next] == ' ') next++;
                    if (next < aqn.Length && aqn[next] == '[') { result.Append(c); continue; } // generic argument separator
                    if (depth == 0) break;
                    skipUntil = depth; continue;
                }
                result.Append(c);
            }
            return result.ToString();
        }
    }
}

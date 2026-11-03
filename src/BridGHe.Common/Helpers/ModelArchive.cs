using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace BridGHe.Common.Helpers
{
    /// <summary>Versioned binary XML archive, including shared references and ISerializable model contracts.</summary>
    public static class ModelArchive
    {
        private static readonly byte[] Header = Encoding.ASCII.GetBytes("BGHA1");
        private static DataContractSerializer Serializer<T>() => new DataContractSerializer(typeof(T), new DataContractSerializerSettings
        {
            PreserveObjectReferences = true,
            MaxItemsInObjectGraph = 10000000,
            DataContractResolver = new ModelTypeResolver()
        });
        public static byte[] Write<T>(T value)
        {
            using var stream = new MemoryStream(); stream.Write(Header);
            using (var writer = XmlDictionaryWriter.CreateBinaryWriter(stream, null, null, false)) Serializer<T>().WriteObject(writer, value);
            return stream.ToArray();
        }
        public static T Read<T>(byte[] data)
        {
            if (data == null || data.Length < Header.Length || !data.Take(Header.Length).SequenceEqual(Header))
                throw new SerializationException("Formato archivio non BridGHe. Gli archivi legacy non vengono deserializzati con BinaryFormatter.");
            using var reader = XmlDictionaryReader.CreateBinaryReader(data, Header.Length, data.Length - Header.Length, new XmlDictionaryReaderQuotas { MaxArrayLength = 10000000, MaxStringContentLength = 16000000, MaxDepth = 512, MaxBytesPerRead = 65536, MaxNameTableCharCount = 16000000 });
            T value = (T)Serializer<T>().ReadObject(reader);
            if (value is Models.ModelModel model) model.RebuildIndexes();
            if (value is Checks.BridgeActionDefinition action) action.Source?.RebuildIndexes();
            if (value is Checks.BridgePhaseDefinition phase)
                foreach (var source in phase.Actions.Select(a => a.Source).Where(m => m != null).Distinct()) source.RebuildIndexes();
            return value;
        }
        public static T Clone<T>(T value) => Read<T>(Write(value));

        private sealed class ModelTypeResolver : DataContractResolver
        {
            private const string Namespace = "urn:gpc:bridghe:model:v1";
            private static bool AllowedAssembly(AssemblyName name) => name.Name == "BridGHe.Common" || name.Name == "RhinoCommon" || name.Name == "System.Private.CoreLib" || name.Name == "System.Collections" || name.Name == "System.ObjectModel" || name.Name == "System.Drawing.Primitives" || name.Name == "System.Drawing.Common" || name.Name == "GPCModel" || name.Name == "GPCGeometry" || name.Name == "GPCUtilities" || name.Name == "GPCChecker.Concrete" || name.Name == "GPCChecker.Steel" || name.Name == "GPCChecker.CompositeBridge";
            public override bool TryResolveType(Type type, Type declaredType, DataContractResolver knownTypeResolver, out XmlDictionaryString typeName, out XmlDictionaryString typeNamespace)
            {
                if (!AllowedAssembly(type.Assembly.GetName())) throw new SerializationException("Tipo non supportato nell'archivio: " + type.FullName);
                var dictionary = new XmlDictionary();
                typeName = dictionary.Add("T" + Convert.ToHexString(Encoding.UTF8.GetBytes(type.AssemblyQualifiedName)));
                typeNamespace = dictionary.Add(Namespace); return true;
            }
            public override Type ResolveName(string typeName, string typeNamespace, Type declaredType, DataContractResolver knownTypeResolver)
            {
                if (typeNamespace != Namespace) return knownTypeResolver.ResolveName(typeName, typeNamespace, declaredType, null);
                string name = Encoding.UTF8.GetString(Convert.FromHexString(typeName.Substring(1)));
                // Old archives stored empty lists of these unsupported model types.
                // Accept the empty containers without keeping the retired classes.
                // Nonempty entries still fail type resolution rather than losing data silently.
                if (name.StartsWith("System.Collections.Generic.List`1[[BridGHe.Common.Models.NamedSetsModel,", StringComparison.Ordinal) ||
                    name.StartsWith("System.Collections.Generic.List`1[[BridGHe.Common.Models.FunctionModel,", StringComparison.Ordinal) ||
                    name.StartsWith("System.Collections.Generic.List`1[[BridGHe.Common.Models.LoadCaseModel+Stage+Operation,", StringComparison.Ordinal))
                    return typeof(System.Collections.Generic.List<object>);
                return Type.GetType(name, assembly => AllowedAssembly(assembly) ? Assembly.Load(assembly) : throw new SerializationException("Assembly non ammesso: " + assembly.Name), null, true);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private static readonly byte[] Header = Encoding.ASCII.GetBytes("BGHA2");
        private static readonly byte[] LegacyHeader = Encoding.ASCII.GetBytes("BGHA1");
        private const int MaximumBytes = 256 * 1024 * 1024;
        private static DataContractSerializer Serializer<T>() => new DataContractSerializer(typeof(T), new DataContractSerializerSettings
        {
            PreserveObjectReferences = true,
            MaxItemsInObjectGraph = 10000000,
            DataContractResolver = new ModelTypeResolver()
        });
        public static byte[] Write<T>(T value)
        {
            using var stream = new MemoryStream(); stream.Write(Header);
            using (var writer = XmlDictionaryWriter.CreateBinaryWriter(stream, null, null, false)) GPC.Model.Core.ContractXml.Write(Serializer<T>(), writer, value);
            if (stream.Length > MaximumBytes) throw new SerializationException("BridgheArchiveTooLarge");
            return stream.ToArray();
        }
        public static T Read<T>(byte[] data)
        {
            if (data == null || data.Length < Header.Length || data.Length > MaximumBytes ||
                !(data.Take(Header.Length).SequenceEqual(Header) || data.Take(Header.Length).SequenceEqual(LegacyHeader)))
                throw new SerializationException("Formato archivio non BridGHe. Gli archivi legacy non vengono deserializzati con BinaryFormatter.");
            using var reader = XmlDictionaryReader.CreateBinaryReader(data, Header.Length, data.Length - Header.Length, new XmlDictionaryReaderQuotas { MaxArrayLength = 10000000, MaxStringContentLength = 16000000, MaxDepth = 512, MaxBytesPerRead = 65536, MaxNameTableCharCount = 16000000 });
            T value = (T)GPC.Model.Core.ContractXml.Read(Serializer<T>(), reader);
            if (reader.MoveToContent() != XmlNodeType.None) throw new SerializationException("TrailingBridgheArchiveContent");
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
            // Resolve only into this fixed dependency set, loaded before examining a payload.
            // Type names read from an archive never cause Assembly.Load.
            private static readonly Assembly[] DomainAssemblies = new[]
            {
                typeof(ModelArchive).Assembly,
                typeof(GPC.Model.Models.Model).Assembly,
                typeof(GPC.Geometry.Point3d).Assembly,
                typeof(GPC.Checkers.CompositeBridge.HBridgeSection).Assembly,
                Assembly.Load("GPCUtilities"), Assembly.Load("GPCChecker.Concrete"), Assembly.Load("GPCChecker.Steel")
            }.Distinct().ToArray();
            private static readonly HashSet<Type> Collections = new()
            {
                typeof(List<>), typeof(Dictionary<,>), typeof(KeyValuePair<,>), typeof(ObservableCollection<>),
                typeof(HashSet<>), typeof(SortedDictionary<,>), typeof(SortedList<,>), typeof(Nullable<>),
                typeof(Tuple<,>), typeof(ValueTuple<,>), typeof(ValueTuple<,,>), typeof(ValueTuple<,,,>)
            };
            private static readonly HashSet<Type> Types = new(DomainAssemblies.SelectMany(a => a.GetTypes())
                .Where(t => !t.ContainsGenericParameters && !t.IsAbstract && (t.IsSerializable || typeof(ISerializable).IsAssignableFrom(t) || t.IsDefined(typeof(DataContractAttribute), false)))
                .Concat(GPC.Model.Core.ModelValues.DataContracts)
                .Concat(typeof(Rhino.Geometry.Point3d).Assembly.GetTypes().Where(t => t.Namespace == "Rhino.Geometry" && t.IsSerializable))
                .Concat(new[] { typeof(object), typeof(string), typeof(decimal), typeof(Guid), typeof(DateTime), typeof(TimeSpan), typeof(System.Drawing.Color) }));
            private static readonly Dictionary<string, Assembly> Assemblies = DomainAssemblies.Concat(Types.Select(t => t.Assembly)).Concat(Collections.Select(t => t.Assembly))
                .Distinct().ToDictionary(a => a.GetName().Name, StringComparer.Ordinal);
            private static bool Allowed(Type type) => type != null && (Types.Contains(type) || type.IsPrimitive
                || type.IsArray && type.GetArrayRank() == 1 && Allowed(type.GetElementType())
                || type.IsGenericType && Collections.Contains(type.GetGenericTypeDefinition()) && type.GetGenericArguments().All(Allowed));
            public override bool TryResolveType(Type type, Type declaredType, DataContractResolver knownTypeResolver, out XmlDictionaryString typeName, out XmlDictionaryString typeNamespace)
            {
                if (!Allowed(type)) throw new SerializationException("Tipo non supportato nell'archivio: " + type.FullName);
                var dictionary = new XmlDictionary();
                typeName = dictionary.Add("T" + Convert.ToHexString(Encoding.UTF8.GetBytes(type.AssemblyQualifiedName)));
                typeNamespace = dictionary.Add(Namespace); return true;
            }
            public override Type ResolveName(string typeName, string typeNamespace, Type declaredType, DataContractResolver knownTypeResolver)
            {
                if (typeNamespace != Namespace)
                {
                    var known = knownTypeResolver.ResolveName(typeName, typeNamespace, declaredType, null);
                    if (!Allowed(known)) throw new SerializationException("UnknownBridgheArchiveType");
                    return known;
                }
                if (typeName == null || typeName.Length < 3 || typeName[0] != 'T' || typeName.Length % 2 != 1 || typeName.Length > 65537)
                    throw new SerializationException("InvalidBridgheTypeToken");
                string name;
                try { name = Encoding.UTF8.GetString(Convert.FromHexString(typeName.Substring(1))); }
                catch (FormatException error) { throw new SerializationException("InvalidBridgheTypeToken", error); }
                // Old archives stored empty lists of these unsupported model types.
                // Accept the empty containers without keeping the retired classes.
                // Nonempty entries still fail type resolution rather than losing data silently.
                if (name.StartsWith("System.Collections.Generic.List`1[[BridGHe.Common.Models.NamedSetsModel,", StringComparison.Ordinal) ||
                    name.StartsWith("System.Collections.Generic.List`1[[BridGHe.Common.Models.FunctionModel,", StringComparison.Ordinal) ||
                    name.StartsWith("System.Collections.Generic.List`1[[BridGHe.Common.Models.LoadCaseModel+Stage+Operation,", StringComparison.Ordinal))
                    return typeof(System.Collections.Generic.List<object>);
                var type = Type.GetType(name, assembly => Assemblies.TryGetValue(assembly.Name, out var known) ? known
                    : throw new SerializationException("Assembly non ammesso: " + assembly.Name), null, true);
                if (!Allowed(type)) throw new SerializationException("Tipo non supportato nell'archivio: " + type?.FullName);
                return type;
            }
        }
    }
}

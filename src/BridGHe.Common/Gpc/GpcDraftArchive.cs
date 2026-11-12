using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.Elements;
using GPC.Model.Materials;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// BGHGPC1 (coordinator decision of 6/11, option (b), part (i)): the final persistence of the GH types that carry a GPC draft, built and
    /// proven now, switched on at the removal of FeMM (R6; until then the GH types write BGHA1 as before). Two chunks per datum:
    /// <list type="bullet">
    /// <item><see cref="DataChunk"/>: the BGHGPC1 value of the GPC object (<see cref="GpcGooArchive.Value{T}"/>: the node, beam or plate
    /// element of the draft, or the material), root type chosen by the reader;</item>
    /// <item><see cref="RecordsChunk"/>: the BridGHe records of a draft (<see cref="DraftRecords"/>: list-level records, case records, the data
    /// of the beam property with its section recipe, the bridge data, the angle, the corners, the report): an own DataContract, schema 1,
    /// additions only, XML (<see cref="DataContractSerializer"/> without known types), never NRBF or BinaryFormatter, at most
    /// <see cref="MaximumRecordBytes"/>. A material has no records.</item>
    /// </list>
    /// Proof (regression <c>gpc-draft-archive</c>): draft → write → read → draft bit for bit on the import-dump corpus and on the drafts of
    /// the Build Model inputs of the examples.
    /// </summary>
    public static class GpcDraftArchive
    {
        public const string DataChunk = "Data", RecordsChunk = "BridGHeRecords";
        public const int MaximumRecordBytes = 64 * 1024 * 1024;

        public static (byte[] Data, byte[] Records) Write(LegacyNodeDraft draft) => Write(draft, GpcGooArchive.Value);
        public static (byte[] Data, byte[] Records) Write(LegacyBeamDraft draft) => Write(draft, GpcGooArchive.Value);
        public static (byte[] Data, byte[] Records) Write(LegacyPlateDraft draft) => Write(draft, GpcGooArchive.Value);

        /// <summary>The two chunks of a draft with the value of its element not compressed and without the BGHGPC1 header
        /// (<see cref="GpcGooArchive.RawValue{T}"/>): the drafts of a model compress them in one stream (<see cref="GpcModelDrafts.Write"/>).</summary>
        internal static (byte[] Value, byte[] Records) WriteRaw(LegacyNodeDraft draft) => Write(draft, GpcGooArchive.RawValue);
        internal static (byte[] Value, byte[] Records) WriteRaw(LegacyBeamDraft draft) => Write(draft, GpcGooArchive.RawValue);
        internal static (byte[] Value, byte[] Records) WriteRaw(LegacyPlateDraft draft) => Write(draft, GpcGooArchive.RawValue);

        private static (byte[] Data, byte[] Records) Write(LegacyNodeDraft draft, Func<NodeElement, byte[]> value)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            return (value(draft.Node), Save(RecordExtensions.Carry(DraftRecords.Of("node", draft.Records, draft.Report), draft)));
        }

        private static (byte[] Data, byte[] Records) Write(LegacyBeamDraft draft, Func<BeamElement, byte[]> value)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var r = DraftRecords.Of("beam", draft.Records, draft.Report);
            r.AngleDeg = draft.AngleDeg; r.Property = PropertyRecord.Of(draft.Property);
            if (draft.Bridge != null)
            {
                var one = new GPC.Checkers.CompositeBridge.BridgeModelData(); one.Set(draft.Bridge); r.Bridge = one.Save();
            }
            return (value(draft.Beam), Save(RecordExtensions.Carry(r, draft)));
        }

        private static (byte[] Data, byte[] Records) Write(LegacyPlateDraft draft, Func<AreaElement, byte[]> value)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var r = DraftRecords.Of("plate", draft.Records, draft.Report);
            r.Corners = draft.Corners.Select(c => new double[] { c.X, c.Y, c.Z }).ToList();
            r.AngleDeg = draft.AngleDeg; // P2X: the local axis angle of the plate (the member of the beam angle; 0 as before for a plate without one)
            return (value(draft.Plate), Save(RecordExtensions.Carry(r, draft)));
        }

        public static byte[] Write(Material material) => GpcGooArchive.Value(material ?? throw new ArgumentNullException(nameof(material)));

        /// <summary>PAR (A10): a material and the kind it declares; the records only when the kind is not the kind of its GPC type
        /// (<see cref="GpcMaterials.KindOf"/>): every material saved so far is written as before (no records).</summary>
        public static (byte[] Data, byte[] Records) Write(Material material, string kind)
        {
            // P4: a material read with records that hold members of a newer writer keeps its records (and them).
            bool own = kind != null && kind != GpcMaterials.KindOf(material);
            if (!own && !RecordExtensions.Carries(material)) return (Write(material), null);
            return (Write(material), Save(RecordExtensions.Carry(new DraftRecords { Kind = "material", MaterialKind = own ? kind : null }, material)));
        }

        public static LegacyNodeDraft ReadNode(byte[] data, byte[] records) => Node(GpcGooArchive.ReadValue<NodeElement>(data), records);
        public static LegacyBeamDraft ReadBeam(byte[] data, byte[] records) => Beam(GpcGooArchive.ReadValue<BeamElement>(data), records);
        public static LegacyPlateDraft ReadPlate(byte[] data, byte[] records) => Plate(GpcGooArchive.ReadValue<AreaElement>(data), records);

        /// <summary>The readers of <see cref="WriteRaw(LegacyNodeDraft)"/> and the others.</summary>
        internal static LegacyNodeDraft ReadNodeRaw(byte[] value, byte[] records) => Node(GpcGooArchive.ReadRawValue<NodeElement>(value), records);
        internal static LegacyBeamDraft ReadBeamRaw(byte[] value, byte[] records) => Beam(GpcGooArchive.ReadRawValue<BeamElement>(value), records);
        internal static LegacyPlateDraft ReadPlateRaw(byte[] value, byte[] records) => Plate(GpcGooArchive.ReadRawValue<AreaElement>(value), records);

        private static LegacyNodeDraft Node(NodeElement node, byte[] records)
        {
            var r = Load(records, "node");
            return RecordExtensions.Remember(new LegacyNodeDraft(node, r.Report(null)) { Records = r.Records() }, r);
        }

        private static LegacyBeamDraft Beam(BeamElement beam, byte[] records)
        {
            var r = Load(records, "beam");
            var bridge = r.Bridge == null ? null : GPC.Checkers.CompositeBridge.BridgeModelData.Read(r.Bridge);
            var report = r.Report(bridge);
            return RecordExtensions.Remember(new LegacyBeamDraft(beam, bridge?.Beam(beam.Guid), r.AngleDeg, r.Property?.Data(), report) { Records = r.Records() }, r);
        }

        private static LegacyPlateDraft Plate(AreaElement plate, byte[] records)
        {
            var r = Load(records, "plate");
            var corners = (r.Corners ?? new List<double[]>()).Select(c => c != null && c.Length == 3 ? new GPC.Geometry.Point3d(c[0], c[1], c[2])
                : throw new SerializationException("BridgheDraftRecordsInvalidCorner")).ToList();
            return RecordExtensions.Remember(new LegacyPlateDraft(plate, corners, r.Report(null)) { Records = r.Records(), AngleDeg = r.AngleDeg }, r);
        }

        public static Material ReadMaterial(byte[] data) => GpcGooArchive.ReadValue<Material>(data);

        /// <summary>PAR (A10): a material and the kind written beside it (null: none written, the kind of its GPC type).</summary>
        public static Material ReadMaterial(byte[] data, byte[] records, out string kind)
        {
            if (records == null) { kind = null; return ReadMaterial(data); }
            var r = Load(records, "material");
            kind = r.MaterialKind;
            return RecordExtensions.Remember(ReadMaterial(data), r);
        }

        // The GH types whose datum is a BridGHe record today (no GPC object of their own yet: the assembly builds the GPC case, combination
        // and stage from them, the property builds the section): the records chunk only.
        public static byte[] Write(LegacyCaseData c) => Save(RecordExtensions.Carry(new DraftRecords { Kind = "case", Case = CaseRecord.Of(c ?? throw new ArgumentNullException(nameof(c))) }, c));
        public static LegacyCaseData ReadCase(byte[] records) { var r = Load(records, "case"); return RecordExtensions.Remember(r.Case?.Data() ?? throw new SerializationException("BridgheDraftRecordsMissingCase"), r); }
        public static byte[] Write(LegacyCombinationDraft c) => Save(RecordExtensions.Carry(new DraftRecords { Kind = "combination", Combination = CombinationRecord.Of(c ?? throw new ArgumentNullException(nameof(c))) }, c));
        public static LegacyCombinationDraft ReadCombination(byte[] records) { var r = Load(records, "combination"); return RecordExtensions.Remember(r.Combination?.Data() ?? throw new SerializationException("BridgheDraftRecordsMissingCombination"), r); }
        public static byte[] Write(LegacyStageDraft s) => Save(RecordExtensions.Carry(new DraftRecords { Kind = "stage", Stage = StageRecord.Of(s ?? throw new ArgumentNullException(nameof(s))) }, s));
        public static LegacyStageDraft ReadStage(byte[] records) { var r = Load(records, "stage"); return RecordExtensions.Remember(r.Stage?.Data() ?? throw new SerializationException("BridgheDraftRecordsMissingStage"), r); }
        public static byte[] Write(LegacySectionRecipe s) => Save(RecordExtensions.Carry(new DraftRecords { Kind = "section", Section = SectionRecord.Of(s ?? throw new ArgumentNullException(nameof(s))) }, s));
        public static LegacySectionRecipe ReadSection(byte[] records) { var r = Load(records, "section"); return RecordExtensions.Remember(r.Section?.Recipe() ?? throw new SerializationException("BridgheDraftRecordsMissingSection"), r); }

        /// <summary>A beam property: the GPC property and the data of the property (formulation, angle, native section, signature, recipe).</summary>
        public static (byte[] Data, byte[] Records) Write(LegacyBeamProperty p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            return (GpcGooArchive.Value(p.Property as GPC.Model.ElementProperties.ElasticBeamProperty ?? throw new NotSupportedException("BridgheBeamPropertyType: " + p.Property?.GetType().Name)),
                Save(RecordExtensions.Carry(new DraftRecords { Kind = "beam-property", Property = PropertyRecord.Of(p.Data) }, p)));
        }

        /// <summary>P30-02 (C1, 11/11): a record without the data of the property is refused, as the other kinds without their datum (before, a
        /// beam property with null data was given).</summary>
        public static LegacyBeamProperty ReadBeamProperty(byte[] data, byte[] records)
        {
            var r = Load(records, "beam-property");
            return RecordExtensions.Remember(new LegacyBeamProperty(GpcGooArchive.ReadValue<GPC.Model.ElementProperties.ElasticBeamProperty>(data),
                r.Property?.Data() ?? throw new SerializationException("BridgheDraftRecordsMissingBeamProperty")), r);
        }

        /// <param name="membraneBending">L2C: the bending thickness of a membrane property, beside the GPC property (<see cref="DraftRecords.MembraneBending"/>).</param>
        public static (byte[] Data, byte[] Records) Write(GPC.Model.ElementProperties.PlateProperty p, double membraneBending = 0) => p switch
        {
            GPC.Model.ElementProperties.ElasticPlateProperty e => (GpcGooArchive.Value(e), Save(RecordExtensions.Carry(new DraftRecords { Kind = "plate-property", PlateType = "elastic", MembraneBending = membraneBending }, p))),
            GPC.Model.ElementProperties.MembranePlateProperty m => (GpcGooArchive.Value(m), Save(RecordExtensions.Carry(new DraftRecords { Kind = "plate-property", PlateType = "membrane", MembraneBending = membraneBending }, p))),
            _ => throw new NotSupportedException("BridghePlatePropertyType: " + p?.GetType().Name),
        };
        public static GPC.Model.ElementProperties.PlateProperty ReadPlateProperty(byte[] data, byte[] records) => ReadPlateProperty(data, records, out _);

        /// <summary>L2C: the plate property and the bending thickness of a membrane written beside it (0: none, or a record written before).</summary>
        public static GPC.Model.ElementProperties.PlateProperty ReadPlateProperty(byte[] data, byte[] records, out double membraneBending)
        {
            var r = Load(records, "plate-property");
            membraneBending = r.MembraneBending;
            GPC.Model.ElementProperties.PlateProperty p = r.PlateType switch
            {
                "elastic" => GpcGooArchive.ReadValue<GPC.Model.ElementProperties.ElasticPlateProperty>(data),
                "membrane" => GpcGooArchive.ReadValue<GPC.Model.ElementProperties.MembranePlateProperty>(data),
                var t => throw new SerializationException("BridgheDraftRecordsUnknownPlateType: " + t),
            };
            return RecordExtensions.Remember(p, r);
        }

        /// <summary>A model (<c>GH_Model</c>): the GPC model with its bridge data (<see cref="GpcGooArchive.BridgeModel"/>) and the definition units.</summary>
        public static (byte[] Data, byte[] Records) Write(GPC.Checkers.CompositeBridge.BridgeModel model, BridgeUnits units) =>
            (GpcGooArchive.BridgeModel(model ?? throw new ArgumentNullException(nameof(model))), Save(RecordExtensions.Carry(new DraftRecords { Kind = "model", Units = (int)units.Units }, model)));

        public static (GPC.Checkers.CompositeBridge.BridgeModel Model, BridgeUnits Units) ReadModel(byte[] data, byte[] records)
        {
            var r = Load(records, "model");
            if (!Enum.IsDefined(typeof(DefinitionUnits), r.Units)) throw new SerializationException("BridgheDraftRecordsUnknownUnits");
            return (RecordExtensions.Remember(GpcGooArchive.ReadBridgeModel(data), r), new BridgeUnits((DefinitionUnits)r.Units));
        }

        /// <summary>R6.0/R6.1: a model with its conversion report (<c>GH_Model</c> after the removal of FeMM: what the analysis, the export and
        /// the transformers read besides the model: warnings, carried data, resolved native sections by beam GUID, BridGHe angles, registered
        /// cases, owner, case origins, data of the registered beam properties, shadowed properties, element load cases, element warnings) and
        /// the tolerance of the conversion. A native section shared by a beam and a property is written once (the report shares it again).
        /// The counters of Build Model (<see cref="LegacyImportReport.BuildModel"/>) are diagnostics of one run: not written.</summary>
        public static (byte[] Data, byte[] Records) Write(GpcModels.Conversion conversion)
        {
            if (conversion == null) throw new ArgumentNullException(nameof(conversion));
            var report = conversion.Report;
            var r = DraftRecords.Of("model", new LegacyElementRecords(), Without(report));
            r.HasReport = true; r.Tolerance = conversion.Tolerance; r.RegisteredCases = report.RegisteredCases;
            var natives = new List<GPC.Converter.Straus7.Straus7SectionDefinition>();
            int Native(GPC.Converter.Straus7.Straus7SectionDefinition n)
            {
                if (n == null) return -1;
                int i = natives.FindIndex(x => ReferenceEquals(x, n)); if (i >= 0) return i;
                natives.Add(n); return natives.Count - 1;
            }
            r.NativeSections = report.NativeSections.Select(p => new NativeRecord { Beam = p.Key, Index = Native(p.Value) }).ToList();
            r.CaseOrigins = report.CaseOrigins.Select(p => new CountRecord { Key = p.Key, Count = (int)p.Value }).ToList();
            r.BeamProperties = report.BeamProperties.Select(p =>
            {
                var record = PropertyRecord.Of(p.Value with { Native = null }); record.NativeIndex = Native(p.Value.Native);
                return new NamedPropertyRecord { Name = p.Key, Property = record };
            }).ToList();
            r.ShadowedPropertyBeams = report.ShadowedPropertyBeams.ToList();
            r.ElementLoadCases = report.ElementLoadCases.Select(c => new ElementCasesRecord { Family = (int)c.Family, Number = c.Number, Cases = c.Cases.ToList() }).ToList();
            r.Natives = natives.Select(n => n.Save()).ToList();
            return (GpcGooArchive.BridgeModel(conversion.BridgeModel), Save(RecordExtensions.Carry(r, conversion)));
        }

        /// <summary>The conversion of <see cref="Write(GpcModels.Conversion)"/>: the model, its bridge data, the report and the tolerance.</summary>
        public static GpcModels.Conversion ReadConversion(byte[] data, byte[] records)
        {
            var r = Load(records, "model");
            if (!r.HasReport) throw new SerializationException("BridgheDraftRecordsModelWithoutReport");
            var model = GpcGooArchive.ReadBridgeModel(data);
            var report = r.Report(model.Data);
            report.RegisteredCases = r.RegisteredCases;
            var natives = (r.Natives ?? new List<byte[]>()).Select(GPC.Converter.Straus7.Straus7SectionDefinition.Load).ToList();
            GPC.Converter.Straus7.Straus7SectionDefinition Native(int i) => i < 0 ? null : i < natives.Count ? natives[i] : throw new SerializationException("BridgheDraftRecordsNativeIndex");
            foreach (var n in r.NativeSections ?? new List<NativeRecord>()) report.NativeSections.Add(n.Beam, Native(n.Index));
            foreach (var o in r.CaseOrigins ?? new List<CountRecord>()) report.CaseOrigins.Add(o.Key, (LegacyCaseOrigins)o.Count);
            foreach (var p in r.BeamProperties ?? new List<NamedPropertyRecord>()) report.BeamProperties.Add(p.Name, p.Property.Data() with { Native = Native(p.Property.NativeIndex) });
            report.ShadowedPropertyBeams.UnionWith(r.ShadowedPropertyBeams ?? new List<int>());
            foreach (var c in r.ElementLoadCases ?? new List<ElementCasesRecord>())
                report.ElementLoadCases.Add(new LegacyElementCases((GPC.Model.Core.Identity.EntityFamily)c.Family, c.Number, (c.Cases ?? new List<string>()).ToArray()));
            return RecordExtensions.Remember(new GpcModels.Conversion(model.Model, report, r.Tolerance), r);
        }

        /// <summary>A report with the element-level data of a model only (for <see cref="DraftRecords.Of"/>, which refuses model data).</summary>
        private static LegacyImportReport Without(LegacyImportReport report)
        {
            var r = new LegacyImportReport { Units = report.Units, Owner = report.Owner };
            r.Warnings.AddRange(report.Warnings);
            foreach (var p in report.NotExported) r.NotExported.Add(p.Key, p.Value);
            r.ElementWarnings.AddRange(report.ElementWarnings);
            foreach (var p in report.BeamAngles) r.BeamAngles.Add(p.Key, p.Value);
            return r;
        }

        /// <summary>A composite property (<c>GH_CompositeBeamProperty</c>): its definition in the contract of the Checker (a bridge data set of
        /// one record, <see cref="GPC.Checkers.CompositeBridge.BridgeModelData.Save"/>).</summary>
        public static byte[] Write(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition definition)
        {
            var one = new GPC.Checkers.CompositeBridge.BridgeModelData();
            one.Set(new GPC.Checkers.CompositeBridge.BridgeBeamData(CompositeRecord, compositeSection: new GPC.Checkers.CompositeBridge.BridgeBeamCompositeSection(definition ?? throw new ArgumentNullException(nameof(definition)))));
            return one.Save();
        }

        public static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition ReadComposite(byte[] data) =>
            GPC.Checkers.CompositeBridge.BridgeModelData.Read(data).CompositeSection(CompositeRecord)?.Start ?? throw new SerializationException("BridgheCompositeDefinitionMissing");

        private static readonly Guid CompositeRecord = GpcIdentity.Derive(GpcIdentity.NameSpace, "bridghe-composite-property-datum");

        private static DataContractSerializer Serializer() => new DataContractSerializer(typeof(DraftRecords), new DataContractSerializerSettings { MaxItemsInObjectGraph = 1_000_000 });

        /// <summary>P4 (P2D, user decision U1 of 12/11): the message of data written by a newer BridGHe in a format or schema this version does
        /// not read (BGHGPC1 records of schema 2 or later, a BGHGPC1 or BGHDRF2 archive of a later version). Shown when the file opens.</summary>
        public const string NewerSchemaMessage = "Dati salvati da una versione più recente di BridGHe ({0}): questa versione legge il formato BGHGPC1, schema dei record 1. Il dato non è stato caricato: aprire il file con la versione di BridGHe che lo ha salvato, o aggiornare BridGHe.";

        /// <summary>The text the file reader shows for a datum it cannot read: <see cref="NewerSchemaMessage"/> for data of a newer format,
        /// otherwise the reason.</summary>
        public static string UserMessage(Exception ex) => ex is GpcNewerFormatException ? ex.Message
            : "dato BridGHe non leggibile (" + (ex?.Message ?? "?") + "): il dato non è stato caricato.";

        internal static byte[] Save(DraftRecords records)
        {
            using var stream = new MemoryStream();
            using (var writer = XmlDictionaryWriter.CreateTextWriter(stream, System.Text.Encoding.UTF8, false)) Serializer().WriteObject(writer, records);
            if (stream.Length > MaximumRecordBytes) throw new SerializationException("BridgheDraftRecordsTooLarge");
            return stream.ToArray();
        }

        /// <summary>P4: the schema of a records chunk, read from its first member before the records are read (a record of a later schema may
        /// not follow the contract of schema 1); -1 when the chunk does not start with it.</summary>
        private static int PeekSchema(byte[] bytes, XmlDictionaryReaderQuotas quotas)
        {
            try
            {
                using var reader = XmlDictionaryReader.CreateTextReader(bytes, quotas);
                if (reader.MoveToContent() != XmlNodeType.Element || reader.IsEmptyElement) return -1;
                reader.Read();
                if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "Schema") return -1;
                return int.TryParse(reader.ReadElementContentAsString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int schema) ? schema : -1;
            }
            catch (XmlException) { return -1; }
        }

        internal static DraftRecords Load(byte[] bytes, string kind)
        {
            if (bytes == null || bytes.Length == 0) throw new SerializationException("BridgheDraftRecordsMissing");
            if (bytes.Length > MaximumRecordBytes) throw new SerializationException("BridgheDraftRecordsTooLarge");
            var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 64, MaxStringContentLength = MaximumRecordBytes, MaxArrayLength = MaximumRecordBytes, MaxBytesPerRead = 4096, MaxNameTableCharCount = 16384 };
            // P4 (U1): schema 1 takes additions only (kept by the records, IExtensibleDataObject); a required member is schema 2, refused clearly.
            int schema = PeekSchema(bytes, quotas);
            if (schema > DraftRecords.CurrentSchema) throw new GpcNewerFormatException("schema dei record BGHGPC1 " + schema);
            using var reader = XmlDictionaryReader.CreateTextReader(bytes, quotas);
            var records = (DraftRecords)Serializer().ReadObject(reader);
            // P30-04 (C1, 11/11): the records end the chunk (the writer writes the root element alone): anything after it is damage.
            bool end;
            try { end = reader.ReadState == ReadState.EndOfFile || reader.NodeType == XmlNodeType.None && !reader.Read(); }
            catch (XmlException) { end = false; }
            if (!end) throw new SerializationException("BridgheDraftRecordsTrailingData");
            if (records == null || records.Schema != DraftRecords.CurrentSchema) throw new SerializationException("BridgheDraftRecordsUnsupportedSchema");
            if (records.Kind != kind) throw new SerializationException("BridgheDraftRecordsWrongKind: " + records.Kind);
            return records;
        }
    }

    /// <summary>P4 (P2D, user decision U1 of 12/11): data written by a newer BridGHe in a format or schema this version does not read; its
    /// message is the one the user sees (<see cref="GpcDraftArchive.NewerSchemaMessage"/>).</summary>
    public sealed class GpcNewerFormatException : SerializationException
    {
        public GpcNewerFormatException(string what) : base(string.Format(System.Globalization.CultureInfo.InvariantCulture, GpcDraftArchive.NewerSchemaMessage, what)) { }
    }

    /// <summary>
    /// P4 (P2D, user decision U1 of 12/11): schema 1 of the records takes additions only, and a reader keeps the members it does not know. Every
    /// record class is an <see cref="IExtensibleDataObject"/>: the serializer keeps the unknown members of a record of a newer writer in its
    /// extension data. The records read with a datum are remembered for that datum (by reference, while it lives) when they hold any; when the
    /// same datum is written again the extension data go back on the records written for it, record by record where the structure matches
    /// (the same member, the same index in a list). A changed datum (a copy a component changes) is another object and writes its own records.
    /// </summary>
    internal static class RecordExtensions
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, DraftRecords> read = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.FieldInfo[]> members = new();

        private static System.Reflection.FieldInfo[] Members(Type type) => members.GetOrAdd(type, t => t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(f => f.IsDefined(typeof(DataMemberAttribute), false) && (typeof(IExtensibleDataObject).IsAssignableFrom(f.FieldType) || RecordList(f.FieldType))).ToArray());

        private static bool RecordList(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>) && typeof(IExtensibleDataObject).IsAssignableFrom(t.GetGenericArguments()[0]);

        /// <summary>The datum read with <paramref name="records"/>, remembered with them when they hold members of a newer writer.</summary>
        internal static T Remember<T>(T datum, DraftRecords records) where T : class
        {
            if (datum != null && records != null && Holds(records)) read.AddOrUpdate(datum, records);
            return datum;
        }

        /// <summary>True when <paramref name="datum"/> was read with records that hold members of a newer writer.</summary>
        internal static bool Carries(object datum) => datum != null && read.TryGetValue(datum, out _);

        /// <summary>The records written for <paramref name="datum"/> with the extension data of the records it was read with.</summary>
        internal static DraftRecords Carry(DraftRecords fresh, object datum)
        {
            if (fresh != null && datum != null && read.TryGetValue(datum, out var original)) Merge(fresh, original);
            return fresh;
        }

        private static bool Holds(object record)
        {
            if (record is not IExtensibleDataObject e) return false;
            if (e.ExtensionData != null) return true;
            foreach (var m in Members(record.GetType()))
                switch (m.GetValue(record))
                {
                    case IExtensibleDataObject child when Holds(child): return true;
                    case System.Collections.IList list: foreach (var item in list) if (Holds(item)) return true; break;
                }
            return false;
        }

        private static void Merge(object fresh, object original)
        {
            if (fresh is not IExtensibleDataObject f || original is not IExtensibleDataObject o || fresh.GetType() != original.GetType()) return;
            f.ExtensionData ??= o.ExtensionData;
            foreach (var m in Members(fresh.GetType()))
            {
                object a = m.GetValue(fresh), b = m.GetValue(original);
                if (a is System.Collections.IList la && b is System.Collections.IList lb) { if (la.Count == lb.Count) for (int i = 0; i < la.Count; i++) Merge(la[i], lb[i]); }
                else Merge(a, b);
            }
        }
    }

    /// <summary>The BridGHe records of a draft (<see cref="GpcDraftArchive"/>): schema 1, additions only (a new member takes the next order
    /// and a default that reads the old data as before). P4 (P2D, U1): the members a reader does not know are kept
    /// (<see cref="IExtensibleDataObject"/>, <see cref="RecordExtensions"/>); a member that must be read means schema 2, which a reader of
    /// schema 1 refuses with <see cref="GpcDraftArchive.NewerSchemaMessage"/>. Frozen at the end of P2D.</summary>
    [DataContract(Name = "DraftRecords", Namespace = Ns)]
    internal sealed class DraftRecords : IExtensibleDataObject
    {
        internal const string Ns = "urn:bridghe:gpc-draft-records";
        internal const int CurrentSchema = 1;
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Order = 0)] public int Schema = CurrentSchema;
        [DataMember(Order = 1)] public string Kind;
        // LegacyElementRecords
        [DataMember(Order = 2)] public List<string> Groups;
        [DataMember(Order = 3)] public List<string> Labels;
        [DataMember(Order = 4)] public List<string> SuperElements;
        [DataMember(Order = 5)] public int Members;
        [DataMember(Order = 6)] public int SectionStates;
        [DataMember(Order = 7)] public int Tapers;
        [DataMember(Order = 8)] public int EmptyLabels;
        [DataMember(Order = 9)] public List<string> SupportCarries;
        [DataMember(Order = 10)] public string FirstLabel;
        [DataMember(Order = 11)] public List<CaseRecord> LoadCases;
        [DataMember(Order = 12)] public List<CountRecord> PropertyCarries;
        [DataMember(Order = 13)] public List<LoadRecord> Loads;
        // The report of the draft
        [DataMember(Order = 14)] public int Units;
        [DataMember(Order = 15)] public Guid Owner;
        [DataMember(Order = 16)] public List<string> Warnings;
        [DataMember(Order = 17)] public List<CountRecord> NotExported;
        [DataMember(Order = 18)] public List<WarningRecord> ElementWarnings;
        // Beam (P2X: AngleDeg also the local axis angle of a plate draft, always written: 0 for every plate before)
        [DataMember(Order = 19)] public double AngleDeg;
        [DataMember(Order = 20)] public PropertyRecord Property;
        /// <summary>The bridge data of the beam: a <see cref="GPC.Checkers.CompositeBridge.BridgeModelData"/> of one record, in its own
        /// contract (<see cref="GPC.Checkers.CompositeBridge.BridgeModelData.Save"/>).</summary>
        [DataMember(Order = 21)] public byte[] Bridge;
        // Plate
        [DataMember(Order = 22)] public List<double[]> Corners;
        // Case, combination, stage, section (the GH types whose datum is a record)
        [DataMember(Order = 23)] public CaseRecord Case;
        [DataMember(Order = 24)] public CombinationRecord Combination;
        [DataMember(Order = 25)] public StageRecord Stage;
        [DataMember(Order = 26)] public SectionRecord Section;
        /// <summary>The BridGHe angle of the beam by its number in the report of a beam draft (number 1).</summary>
        [DataMember(Order = 27)] public List<double[]> BeamAngles;
        [DataMember(Order = 28)] public string PlateType;
        // Model (R6.0/R6.1): the conversion report besides the fields above (units, owner, warnings, carried, element warnings, angles).
        [DataMember(Order = 29)] public bool HasReport;
        [DataMember(Order = 30)] public double Tolerance;
        [DataMember(Order = 31)] public int RegisteredCases;
        [DataMember(Order = 32)] public List<NativeRecord> NativeSections;
        [DataMember(Order = 33)] public List<CountRecord> CaseOrigins;
        [DataMember(Order = 34)] public List<NamedPropertyRecord> BeamProperties;
        [DataMember(Order = 35)] public List<int> ShadowedPropertyBeams;
        [DataMember(Order = 36)] public List<ElementCasesRecord> ElementLoadCases;
        /// <summary>The distinct native sections of the model in their own format (<see cref="GPC.Converter.Straus7.Straus7SectionDefinition.Save"/>).</summary>
        [DataMember(Order = 37)] public List<byte[]> Natives;
        /// <summary>L2C (lane 2c): the bending thickness of a membrane plate property (the former property held it; the GPC membrane has
        /// none): Bridge Deck Mesh checks it against the slab as before. Not written at 0 (the records written before read it as 0).</summary>
        [DataMember(Order = 38, EmitDefaultValue = false)] public double MembraneBending;
        /// <summary>PAR (A10): the kind a material declares when it is not the kind of its GPC type (a Custom material of kind Steel whose
        /// strengths the GPC steel type refuses). Written only then.</summary>
        [DataMember(Order = 39, EmitDefaultValue = false)] public string MaterialKind;

        internal static DraftRecords Of(string kind, LegacyElementRecords records, LegacyImportReport report)
        {
            if (report.NativeSections.Count != 0 || report.CaseOrigins.Count != 0 || report.BeamProperties.Count != 0
                || report.ShadowedPropertyBeams.Count != 0 || report.ElementLoadCases.Count != 0 || report.RegisteredCases != 0 || report.BuildModel != null)
                throw new InvalidOperationException("BridgheDraftReportHasModelData"); // a draft report has none (they belong to a converted model)
            return new DraftRecords
            {
                Kind = kind,
                Groups = records.Groups.ToList(), Labels = records.Labels.ToList(), SuperElements = records.SuperElements.ToList(),
                Members = records.Members, SectionStates = records.SectionStates, Tapers = records.Tapers, EmptyLabels = records.EmptyLabels,
                SupportCarries = records.SupportCarries.ToList(), FirstLabel = records.FirstLabel,
                LoadCases = records.LoadCases.Select(CaseRecord.Of).ToList(),
                PropertyCarries = records.PropertyCarries.Select(p => new CountRecord { Key = p.Key, Count = p.Value }).ToList(),
                Loads = records.LoadRecords.Select(l => new LoadRecord { Kind = l.Kind, Case = l.Case }).ToList(),
                Units = (int)report.Units.Units, Owner = report.Owner, Warnings = report.Warnings.ToList(),
                NotExported = report.NotExported.Select(p => new CountRecord { Key = p.Key, Count = p.Value }).ToList(),
                ElementWarnings = report.ElementWarnings.Select(w => new WarningRecord { Family = (int)w.Family, Number = w.Number, Format = w.Format, Restraint = w.Restraint }).ToList(),
                BeamAngles = report.BeamAngles.Select(a => new double[] { a.Key, a.Value }).ToList(),
            };
        }

        internal LegacyElementRecords Records()
        {
            var r = new LegacyElementRecords { Members = Members, SectionStates = SectionStates, Tapers = Tapers, EmptyLabels = EmptyLabels, FirstLabel = FirstLabel };
            r.GroupList.AddRange(Groups ?? new List<string>()); r.LabelList.AddRange(Labels ?? new List<string>()); r.SuperElementList.AddRange(SuperElements ?? new List<string>());
            r.SupportCarryList.AddRange(SupportCarries ?? new List<string>());
            r.LoadCaseList.AddRange((LoadCases ?? new List<CaseRecord>()).Select(c => c.Data()));
            r.PropertyCarryList.AddRange((PropertyCarries ?? new List<CountRecord>()).Select(p => new KeyValuePair<string, int>(p.Key, p.Count)));
            r.LoadList.AddRange((Loads ?? new List<LoadRecord>()).Select(l => (l.Kind, l.Case)));
            return r;
        }

        internal LegacyImportReport Report(GPC.Checkers.CompositeBridge.BridgeModelData bridge)
        {
            if (!Enum.IsDefined(typeof(DefinitionUnits), Units)) throw new SerializationException("BridgheDraftRecordsUnknownUnits");
            var report = new LegacyImportReport { Units = new BridgeUnits((DefinitionUnits)Units), Owner = Owner, Bridge = bridge ?? new GPC.Checkers.CompositeBridge.BridgeModelData() };
            report.Warnings.AddRange(Warnings ?? new List<string>());
            foreach (var p in NotExported ?? new List<CountRecord>()) report.NotExported.Add(p.Key, p.Count);
            foreach (var w in ElementWarnings ?? new List<WarningRecord>())
                report.ElementWarnings.Add(new LegacyElementWarning((GPC.Model.Core.Identity.EntityFamily)w.Family, w.Number, w.Format, w.Restraint));
            foreach (var a in BeamAngles ?? new List<double[]>())
            {
                if (a == null || a.Length != 2) throw new SerializationException("BridgheDraftRecordsInvalidBeamAngle");
                report.BeamAngles[(int)a[0]] = a[1];
            }
            return report;
        }
    }

    [DataContract(Name = "Case", Namespace = DraftRecords.Ns)]
    internal sealed class CaseRecord : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Order = 0)] public string Name;
        [DataMember(Order = 1)] public double Temperature;
        [DataMember(Order = 2)] public bool StructuralMass;
        [DataMember(Order = 3)] public bool NonStructuralMass;
        [DataMember(Order = 4)] public string Inertia;
        [DataMember(Order = 5)] public string GravityDirection;
        [DataMember(Order = 6)] public double GravityValue;
        [DataMember(Order = 7)] public int Stages;
        [DataMember(Order = 8)] public bool HasStageList;
        [DataMember(Order = 9)] public bool IsNull;
        internal static CaseRecord Of(LegacyCaseData c) => c == null ? new CaseRecord { IsNull = true } : new CaseRecord
        {
            Name = c.Name, Temperature = c.Temperature, StructuralMass = c.StructuralMass, NonStructuralMass = c.NonStructuralMass, Inertia = c.Inertia,
            GravityDirection = c.GravityDirection, GravityValue = c.GravityValue, Stages = c.Stages, HasStageList = c.HasStageList,
        };
        internal LegacyCaseData Data() => IsNull ? null : new LegacyCaseData(Name, Temperature, StructuralMass, NonStructuralMass, Inertia, GravityDirection, GravityValue, Stages, HasStageList);
    }

    [DataContract(Name = "Combination", Namespace = DraftRecords.Ns)]
    internal sealed class CombinationRecord : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Order = 0)] public string Name;
        [DataMember(Order = 1)] public List<CaseRecord> Cases;
        [DataMember(Order = 2)] public List<double> Factors;
        internal static CombinationRecord Of(LegacyCombinationDraft c) => new CombinationRecord
        {
            Name = c.Name, Cases = c.Factors.Select(f => CaseRecord.Of(f.Case)).ToList(), Factors = c.Factors.Select(f => f.Factor).ToList(),
        };
        internal LegacyCombinationDraft Data()
        {
            var cases = Cases ?? new List<CaseRecord>(); var factors = Factors ?? new List<double>();
            if (cases.Count != factors.Count) throw new SerializationException("BridgheDraftRecordsCombinationFactors");
            return new LegacyCombinationDraft(Name, cases.Select((c, i) => (c.Data(), factors[i])).ToList());
        }
    }

    [DataContract(Name = "Stage", Namespace = DraftRecords.Ns)]
    internal sealed class StageRecord : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Order = 0)] public string Name;
        [DataMember(Order = 1)] public List<string> Groups;
        [DataMember(Order = 2)] public bool Morph;
        [DataMember(Order = 3)] public bool Rotate;
        [DataMember(Order = 4)] public List<CombinationRecord> Increments;
        internal static StageRecord Of(LegacyStageDraft s) => new StageRecord
        {
            Name = s.Name, Groups = s.Groups?.ToList(), Morph = s.Morph, Rotate = s.Rotate, Increments = s.Increments?.Select(CombinationRecord.Of).ToList(),
        };
        internal LegacyStageDraft Data() => new LegacyStageDraft(Name, Groups, Morph, Rotate, Increments?.Select(i => i.Data()).ToList());
    }

    [DataContract(Name = "Count", Namespace = DraftRecords.Ns)]
    internal sealed class CountRecord : IExtensibleDataObject { public ExtensionDataObject ExtensionData { get; set; } [DataMember(Order = 0)] public string Key; [DataMember(Order = 1)] public int Count; }

    [DataContract(Name = "Load", Namespace = DraftRecords.Ns)]
    internal sealed class LoadRecord : IExtensibleDataObject { public ExtensionDataObject ExtensionData { get; set; } [DataMember(Order = 0)] public string Kind; [DataMember(Order = 1)] public string Case; }

    [DataContract(Name = "Native", Namespace = DraftRecords.Ns)]
    internal sealed class NativeRecord : IExtensibleDataObject { public ExtensionDataObject ExtensionData { get; set; } [DataMember(Order = 0)] public Guid Beam; [DataMember(Order = 1)] public int Index; }

    [DataContract(Name = "NamedProperty", Namespace = DraftRecords.Ns)]
    internal sealed class NamedPropertyRecord : IExtensibleDataObject { public ExtensionDataObject ExtensionData { get; set; } [DataMember(Order = 0)] public string Name; [DataMember(Order = 1)] public PropertyRecord Property; }

    [DataContract(Name = "ElementCases", Namespace = DraftRecords.Ns)]
    internal sealed class ElementCasesRecord : IExtensibleDataObject { public ExtensionDataObject ExtensionData { get; set; } [DataMember(Order = 0)] public int Family; [DataMember(Order = 1)] public int Number; [DataMember(Order = 2)] public List<string> Cases; }

    [DataContract(Name = "Warning", Namespace = DraftRecords.Ns)]
    internal sealed class WarningRecord : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Order = 0)] public int Family; [DataMember(Order = 1)] public int Number; [DataMember(Order = 2)] public string Format; [DataMember(Order = 3)] public bool Restraint;
    }

    /// <summary>The data of the property of a beam draft (<see cref="LegacyBeamPropertyData"/>) with its section recipe.</summary>
    [DataContract(Name = "Property", Namespace = DraftRecords.Ns)]
    internal sealed class PropertyRecord : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Order = 0)] public int Formulation;
        [DataMember(Order = 1)] public double Angle;
        /// <summary>The resolved native Straus7 section in its own format (<see cref="GPC.Converter.Straus7.Straus7SectionDefinition.Save"/>), or null.</summary>
        [DataMember(Order = 2)] public byte[] Native;
        [DataMember(Order = 3)] public string Signature;
        [DataMember(Order = 4)] public SectionRecord Section;
        /// <summary>In a model: the index of the native section in <see cref="DraftRecords.Natives"/> (-1: none; <see cref="Native"/> is then null).</summary>
        [DataMember(Order = 5)] public int NativeIndex = -1;
        /// <summary>L2C (limit (b) of passo 77): the kind of the material of the former property (<see cref="LegacyBeamPropertyData.MaterialKind"/>;
        /// not written when null, so a record written before reads it as not recorded).</summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public string MaterialKind;

        internal static PropertyRecord Of(LegacyBeamPropertyData p) => p == null ? null : new PropertyRecord
        {
            Formulation = (int)p.Formulation, Angle = p.Angle, Native = p.Native?.Save(), Signature = p.Signature, Section = SectionRecord.Of(p.Section), MaterialKind = p.MaterialKind,
        };

        internal LegacyBeamPropertyData Data() => new LegacyBeamPropertyData((GPC.Model.Structure.Assignments.BeamFormulation)Formulation, Angle,
            Native == null ? null : GPC.Converter.Straus7.Straus7SectionDefinition.Load(Native), Signature) { Section = Section?.Recipe(), MaterialKind = MaterialKind };
    }

    [DataContract(Name = "Section", Namespace = DraftRecords.Ns)]
    internal sealed class SectionRecord : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Order = 0)] public int Kind;
        [DataMember(Order = 1)] public string Name;
        [DataMember(Order = 2)] public double[] Values; // Angle, D, B, B1, B2, T, T1, T2, T3, Area, I11, I22, J, CentroidX, CentroidY, ShearL1, ShearL2, ShearA1, ShearA2
        [DataMember(Order = 3)] public bool CompositePreview;
        [DataMember(Order = 4)] public string NativeKey;
        [DataMember(Order = 5)] public double[] NativeDimensions;
        [DataMember(Order = 6)] public string BxsPath;
        [DataMember(Order = 7)] public string LibraryName;
        [DataMember(Order = 8)] public string LibraryItem;
        [DataMember(Order = 9)] public int NativeLengthUnit;
        // L2C (lane 2c): the data the former section held besides its recipe (additions of schema 1, not written at their defaults, so a
        // record of a section without them is the record written before, and a record written before reads them as 0, none, no mirror).
        [DataMember(Order = 10, EmitDefaultValue = false)] public double R;
        [DataMember(Order = 11, EmitDefaultValue = false)] public double W11;
        [DataMember(Order = 12, EmitDefaultValue = false)] public double W22;
        [DataMember(Order = 13, EmitDefaultValue = false)] public List<ShapeRecord> Shapes;
        /// <summary>The mirror (former MirrorTypes value), null for none.</summary>
        [DataMember(Order = 14, EmitDefaultValue = false)] public int? Mirror;
        [DataMember(Order = 15, EmitDefaultValue = false)] public double MirrorGapA;
        [DataMember(Order = 16, EmitDefaultValue = false)] public double MirrorGapB;
        /// <summary>P11 (P2D, user decision U5 of 12/11): the outline of a homogenised composite section, for the preview only (an addition of
        /// schema 1, not written when null: a record written before reads it as none).</summary>
        [DataMember(Order = 17, EmitDefaultValue = false)] public CompositeOutlineRecord CompositeOutline;

        internal static SectionRecord Of(LegacySectionRecipe s) => s == null ? null : new SectionRecord
        {
            Kind = (int)s.Kind, Name = s.Name,
            Values = new[] { s.Angle, s.D, s.B, s.B1, s.B2, s.T, s.T1, s.T2, s.T3, s.Area, s.I11, s.I22, s.J, s.CentroidX, s.CentroidY, s.ShearL1, s.ShearL2, s.ShearA1, s.ShearA2 },
            CompositePreview = s.CompositePreview, NativeKey = s.NativeKey, NativeDimensions = s.NativeDimensions?.ToArray(), BxsPath = s.BxsPath,
            LibraryName = s.LibraryName, LibraryItem = s.LibraryItem, NativeLengthUnit = s.NativeLengthUnit,
            R = s.R, W11 = s.W11, W22 = s.W22, Shapes = s.Shapes?.Select(ShapeRecord.Of).ToList(),
            Mirror = s.Mirror == LegacySectionRecipe.NoMirror ? null : s.Mirror, MirrorGapA = s.MirrorGapA, MirrorGapB = s.MirrorGapB,
            CompositeOutline = CompositeOutlineRecord.Of(s.CompositeOutline),
        };

        internal LegacySectionRecipe Recipe()
        {
            var v = Values ?? throw new SerializationException("BridgheDraftRecordsSectionWithoutValues");
            if (v.Length != 19) throw new SerializationException("BridgheDraftRecordsSectionValues");
            return new LegacySectionRecipe
            {
                Kind = (BridgeSectionKind)Kind, Name = Name, Angle = v[0], D = v[1], B = v[2], B1 = v[3], B2 = v[4], T = v[5], T1 = v[6], T2 = v[7], T3 = v[8],
                Area = v[9], I11 = v[10], I22 = v[11], J = v[12], CentroidX = v[13], CentroidY = v[14], ShearL1 = v[15], ShearL2 = v[16], ShearA1 = v[17], ShearA2 = v[18],
                CompositePreview = CompositePreview, NativeKey = NativeKey, NativeDimensions = NativeDimensions, BxsPath = BxsPath, LibraryName = LibraryName,
                LibraryItem = LibraryItem, NativeLengthUnit = NativeLengthUnit,
                R = R, W11 = W11, W22 = W22, Shapes = Shapes?.Select(s => s.Shape()).ToArray(), Mirror = Mirror ?? LegacySectionRecipe.NoMirror, MirrorGapA = MirrorGapA, MirrorGapB = MirrorGapB,
                CompositeOutline = CompositeOutline?.Outline(),
            };
        }
    }

    /// <summary>P11 (P2D, user decision U5 of 12/11): the outline of a homogenised composite section (<see cref="LegacyCompositeOutline"/>):
    /// the steel I, the slab and the two rows of bars, in the units of the recipe.</summary>
    [DataContract(Name = "CompositeOutline", Namespace = DraftRecords.Ns)]
    internal sealed class CompositeOutlineRecord : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Order = 0)] public double D;
        [DataMember(Order = 1)] public double B1;
        [DataMember(Order = 2)] public double T1;
        [DataMember(Order = 3)] public double B2;
        [DataMember(Order = 4)] public double T2;
        [DataMember(Order = 5)] public double T3;
        [DataMember(Order = 6)] public double Width;
        [DataMember(Order = 7)] public double Height;
        [DataMember(Order = 8)] public double TopArea;
        [DataMember(Order = 9)] public double TopCover;
        [DataMember(Order = 10)] public double BottomArea;
        [DataMember(Order = 11)] public double BottomCover;

        internal static CompositeOutlineRecord Of(LegacyCompositeOutline o) => o == null ? null : new CompositeOutlineRecord
        {
            D = o.D, B1 = o.B1, T1 = o.T1, B2 = o.B2, T2 = o.T2, T3 = o.T3, Width = o.Width, Height = o.Height,
            TopArea = o.TopArea, TopCover = o.TopCover, BottomArea = o.BottomArea, BottomCover = o.BottomCover,
        };

        internal LegacyCompositeOutline Outline() => new LegacyCompositeOutline(D, B1, T1, B2, T2, T3, Width, Height, TopArea, TopCover, BottomArea, BottomCover);
    }

    /// <summary>L2C: one shape of a Brep section (<see cref="LegacySectionShape"/>): the x, y pairs of its fill and of its holes.</summary>
    [DataContract(Name = "Shape", Namespace = DraftRecords.Ns)]
    internal sealed class ShapeRecord : IExtensibleDataObject
    {
        public ExtensionDataObject ExtensionData { get; set; }
        [DataMember(Order = 0)] public double[] Fill;
        [DataMember(Order = 1)] public List<double[]> Holes;
        internal static ShapeRecord Of(LegacySectionShape s) => new ShapeRecord { Fill = s.Fill.ToArray(), Holes = s.Holes.Select(h => h.ToArray()).ToList() };
        internal LegacySectionShape Shape()
        {
            if (Fill == null || Fill.Length % 2 != 0 || (Holes ?? new List<double[]>()).Any(h => h == null || h.Length % 2 != 0)) throw new SerializationException("BridgheDraftRecordsInvalidShape");
            return new LegacySectionShape { Fill = Fill, Holes = (Holes ?? new List<double[]>()).Select(h => (IReadOnlyList<double>)h).ToArray() };
        }
    }
}

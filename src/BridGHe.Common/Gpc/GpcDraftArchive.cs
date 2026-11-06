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

        public static (byte[] Data, byte[] Records) Write(LegacyNodeDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            return (GpcGooArchive.Value(draft.Node), Save(DraftRecords.Of("node", draft.Records, draft.Report)));
        }

        public static (byte[] Data, byte[] Records) Write(LegacyBeamDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var r = DraftRecords.Of("beam", draft.Records, draft.Report);
            r.AngleDeg = draft.AngleDeg; r.Property = PropertyRecord.Of(draft.Property);
            if (draft.Bridge != null)
            {
                var one = new GPC.Checkers.CompositeBridge.BridgeModelData(); one.Set(draft.Bridge); r.Bridge = one.Save();
            }
            return (GpcGooArchive.Value(draft.Beam), Save(r));
        }

        public static (byte[] Data, byte[] Records) Write(LegacyPlateDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var r = DraftRecords.Of("plate", draft.Records, draft.Report);
            r.Corners = draft.Corners.Select(c => new double[] { c.X, c.Y, c.Z }).ToList();
            return (GpcGooArchive.Value(draft.Plate), Save(r));
        }

        public static byte[] Write(Material material) => GpcGooArchive.Value(material ?? throw new ArgumentNullException(nameof(material)));

        public static LegacyNodeDraft ReadNode(byte[] data, byte[] records)
        {
            var node = GpcGooArchive.ReadValue<NodeElement>(data); var r = Load(records, "node");
            return new LegacyNodeDraft(node, r.Report(null)) { Records = r.Records() };
        }

        public static LegacyBeamDraft ReadBeam(byte[] data, byte[] records)
        {
            var beam = GpcGooArchive.ReadValue<BeamElement>(data); var r = Load(records, "beam");
            var bridge = r.Bridge == null ? null : GPC.Checkers.CompositeBridge.BridgeModelData.Read(r.Bridge);
            var report = r.Report(bridge);
            return new LegacyBeamDraft(beam, bridge?.Beam(beam.Guid), r.AngleDeg, r.Property?.Data(), report) { Records = r.Records() };
        }

        public static LegacyPlateDraft ReadPlate(byte[] data, byte[] records)
        {
            var plate = GpcGooArchive.ReadValue<AreaElement>(data); var r = Load(records, "plate");
            var corners = (r.Corners ?? new List<double[]>()).Select(c => c != null && c.Length == 3 ? new GPC.Geometry.Point3d(c[0], c[1], c[2])
                : throw new SerializationException("BridgheDraftRecordsInvalidCorner")).ToList();
            return new LegacyPlateDraft(plate, corners, r.Report(null)) { Records = r.Records() };
        }

        public static Material ReadMaterial(byte[] data) => GpcGooArchive.ReadValue<Material>(data);

        // The GH types whose datum is a BridGHe record today (no GPC object of their own yet: the assembly builds the GPC case, combination
        // and stage from them, the property builds the section): the records chunk only.
        public static byte[] Write(LegacyCaseData c) => Save(new DraftRecords { Kind = "case", Case = CaseRecord.Of(c ?? throw new ArgumentNullException(nameof(c))) });
        public static LegacyCaseData ReadCase(byte[] records) => Load(records, "case").Case?.Data() ?? throw new SerializationException("BridgheDraftRecordsMissingCase");
        public static byte[] Write(LegacyCombinationDraft c) => Save(new DraftRecords { Kind = "combination", Combination = CombinationRecord.Of(c ?? throw new ArgumentNullException(nameof(c))) });
        public static LegacyCombinationDraft ReadCombination(byte[] records) => Load(records, "combination").Combination?.Data() ?? throw new SerializationException("BridgheDraftRecordsMissingCombination");
        public static byte[] Write(LegacyStageDraft s) => Save(new DraftRecords { Kind = "stage", Stage = StageRecord.Of(s ?? throw new ArgumentNullException(nameof(s))) });
        public static LegacyStageDraft ReadStage(byte[] records) => Load(records, "stage").Stage?.Data() ?? throw new SerializationException("BridgheDraftRecordsMissingStage");
        public static byte[] Write(LegacySectionRecipe s) => Save(new DraftRecords { Kind = "section", Section = SectionRecord.Of(s ?? throw new ArgumentNullException(nameof(s))) });
        public static LegacySectionRecipe ReadSection(byte[] records) => Load(records, "section").Section?.Recipe() ?? throw new SerializationException("BridgheDraftRecordsMissingSection");

        /// <summary>A beam property: the GPC property and the data of the property (formulation, angle, native section, signature, recipe).</summary>
        public static (byte[] Data, byte[] Records) Write(LegacyBeamProperty p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            return (GpcGooArchive.Value(p.Property as GPC.Model.ElementProperties.ElasticBeamProperty ?? throw new NotSupportedException("BridgheBeamPropertyType: " + p.Property?.GetType().Name)), Save(new DraftRecords { Kind = "beam-property", Property = PropertyRecord.Of(p.Data) }));
        }

        public static LegacyBeamProperty ReadBeamProperty(byte[] data, byte[] records) =>
            new LegacyBeamProperty(GpcGooArchive.ReadValue<GPC.Model.ElementProperties.ElasticBeamProperty>(data), Load(records, "beam-property").Property?.Data());

        public static (byte[] Data, byte[] Records) Write(GPC.Model.ElementProperties.PlateProperty p) => p switch
        {
            GPC.Model.ElementProperties.ElasticPlateProperty e => (GpcGooArchive.Value(e), Save(new DraftRecords { Kind = "plate-property", PlateType = "elastic" })),
            GPC.Model.ElementProperties.MembranePlateProperty m => (GpcGooArchive.Value(m), Save(new DraftRecords { Kind = "plate-property", PlateType = "membrane" })),
            _ => throw new NotSupportedException("BridghePlatePropertyType: " + p?.GetType().Name),
        };
        public static GPC.Model.ElementProperties.PlateProperty ReadPlateProperty(byte[] data, byte[] records) => Load(records, "plate-property").PlateType switch
        {
            "elastic" => GpcGooArchive.ReadValue<GPC.Model.ElementProperties.ElasticPlateProperty>(data),
            "membrane" => GpcGooArchive.ReadValue<GPC.Model.ElementProperties.MembranePlateProperty>(data),
            var t => throw new SerializationException("BridgheDraftRecordsUnknownPlateType: " + t),
        };

        /// <summary>A model (<c>GH_Model</c>): the GPC model with its bridge data (<see cref="GpcGooArchive.BridgeModel"/>) and the definition units.</summary>
        public static (byte[] Data, byte[] Records) Write(GPC.Checkers.CompositeBridge.BridgeModel model, BridgeUnits units) =>
            (GpcGooArchive.BridgeModel(model ?? throw new ArgumentNullException(nameof(model))), Save(new DraftRecords { Kind = "model", Units = (int)units.Units }));

        public static (GPC.Checkers.CompositeBridge.BridgeModel Model, BridgeUnits Units) ReadModel(byte[] data, byte[] records)
        {
            var r = Load(records, "model");
            if (!Enum.IsDefined(typeof(DefinitionUnits), r.Units)) throw new SerializationException("BridgheDraftRecordsUnknownUnits");
            return (GpcGooArchive.ReadBridgeModel(data), new BridgeUnits((DefinitionUnits)r.Units));
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

        internal static byte[] Save(DraftRecords records)
        {
            using var stream = new MemoryStream();
            using (var writer = XmlDictionaryWriter.CreateTextWriter(stream, System.Text.Encoding.UTF8, false)) Serializer().WriteObject(writer, records);
            if (stream.Length > MaximumRecordBytes) throw new SerializationException("BridgheDraftRecordsTooLarge");
            return stream.ToArray();
        }

        internal static DraftRecords Load(byte[] bytes, string kind)
        {
            if (bytes == null || bytes.Length == 0) throw new SerializationException("BridgheDraftRecordsMissing");
            if (bytes.Length > MaximumRecordBytes) throw new SerializationException("BridgheDraftRecordsTooLarge");
            var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 64, MaxStringContentLength = MaximumRecordBytes, MaxArrayLength = MaximumRecordBytes, MaxBytesPerRead = 4096, MaxNameTableCharCount = 16384 };
            using var reader = XmlDictionaryReader.CreateTextReader(bytes, quotas);
            var records = (DraftRecords)Serializer().ReadObject(reader);
            if (records == null || records.Schema != DraftRecords.CurrentSchema) throw new SerializationException("BridgheDraftRecordsUnsupportedSchema");
            if (records.Kind != kind) throw new SerializationException("BridgheDraftRecordsWrongKind: " + records.Kind);
            return records;
        }
    }

    /// <summary>The BridGHe records of a draft (<see cref="GpcDraftArchive"/>): schema 1, additions only (a new member takes the next order
    /// and a default that reads the old data as before; a reader refuses another schema).</summary>
    [DataContract(Name = "DraftRecords", Namespace = Ns)]
    internal sealed class DraftRecords
    {
        internal const string Ns = "urn:bridghe:gpc-draft-records";
        internal const int CurrentSchema = 1;
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
        // Beam
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
    internal sealed class CaseRecord
    {
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
    internal sealed class CombinationRecord
    {
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
    internal sealed class StageRecord
    {
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
    internal sealed class CountRecord { [DataMember(Order = 0)] public string Key; [DataMember(Order = 1)] public int Count; }

    [DataContract(Name = "Load", Namespace = DraftRecords.Ns)]
    internal sealed class LoadRecord { [DataMember(Order = 0)] public string Kind; [DataMember(Order = 1)] public string Case; }

    [DataContract(Name = "Warning", Namespace = DraftRecords.Ns)]
    internal sealed class WarningRecord
    {
        [DataMember(Order = 0)] public int Family; [DataMember(Order = 1)] public int Number; [DataMember(Order = 2)] public string Format; [DataMember(Order = 3)] public bool Restraint;
    }

    /// <summary>The data of the property of a beam draft (<see cref="LegacyBeamPropertyData"/>) with its section recipe.</summary>
    [DataContract(Name = "Property", Namespace = DraftRecords.Ns)]
    internal sealed class PropertyRecord
    {
        [DataMember(Order = 0)] public int Formulation;
        [DataMember(Order = 1)] public double Angle;
        /// <summary>The resolved native Straus7 section in its own format (<see cref="GPC.Converter.Straus7.Straus7SectionDefinition.Save"/>), or null.</summary>
        [DataMember(Order = 2)] public byte[] Native;
        [DataMember(Order = 3)] public string Signature;
        [DataMember(Order = 4)] public SectionRecord Section;

        internal static PropertyRecord Of(LegacyBeamPropertyData p) => p == null ? null : new PropertyRecord
        {
            Formulation = (int)p.Formulation, Angle = p.Angle, Native = p.Native?.Save(), Signature = p.Signature, Section = SectionRecord.Of(p.Section),
        };

        internal LegacyBeamPropertyData Data() => new LegacyBeamPropertyData((GPC.Model.Structure.Assignments.BeamFormulation)Formulation, Angle,
            Native == null ? null : GPC.Converter.Straus7.Straus7SectionDefinition.Load(Native), Signature) { Section = Section?.Recipe() };
    }

    [DataContract(Name = "Section", Namespace = DraftRecords.Ns)]
    internal sealed class SectionRecord
    {
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

        internal static SectionRecord Of(LegacySectionRecipe s) => s == null ? null : new SectionRecord
        {
            Kind = (int)s.Kind, Name = s.Name,
            Values = new[] { s.Angle, s.D, s.B, s.B1, s.B2, s.T, s.T1, s.T2, s.T3, s.Area, s.I11, s.I22, s.J, s.CentroidX, s.CentroidY, s.ShearL1, s.ShearL2, s.ShearA1, s.ShearA2 },
            CompositePreview = s.CompositePreview, NativeKey = s.NativeKey, NativeDimensions = s.NativeDimensions?.ToArray(), BxsPath = s.BxsPath,
            LibraryName = s.LibraryName, LibraryItem = s.LibraryItem, NativeLengthUnit = s.NativeLengthUnit,
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
            };
        }
    }
}

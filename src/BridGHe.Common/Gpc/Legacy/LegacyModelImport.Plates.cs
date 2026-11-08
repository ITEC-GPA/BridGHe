using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Geometry;
using GPC.Model.Attributes;
using GPC.Model.Combinations;
using GPC.Model.Constraints;
using GPC.Model.Core.Identity;
using GPC.Model.ElementProperties;
using GPC.Model.Elements;
using GPC.Model.LoadCases;
using GPC.Model.Loads;
using GPC.Model.Materials;
using GPC.Model.Models;
using GPC.Model.Restraints;
using GPC.Model.Sections;
using GPC.Model.Structure.Assignments;

namespace BridGHe.Common.Gpc.Legacy
{
    /// <summary>R6.1 (lane A of R6.2): the reader of the former plate values (plate, attributes, loads).</summary>
    public static partial class LegacyModelImport
    {
        /// <summary>
        /// One former plate as a GPC plate (the body of the plate loop of <see cref="Import"/>, chain E (e1): also the conversion of a single
        /// plate value, <see cref="PlateDraft"/>): on the given property, connected by <paramref name="connect"/> right after its creation; offset,
        /// the first group, Element ID, load patch, supports, prestress, non-structural masses and pressures. P2X (user decision of 8/11,
        /// «Mappalo come nella 0.3.0»): the local axis angle as the layer axes of the plate turned by it (<see cref="Builders.GpcPlateBuilders.LayerAxes"/>)
        /// on <paramref name="corners"/> (a single plate value: its corners), or on its nodes (a whole model: <paramref name="corners"/> null).
        /// </summary>
        private static AreaElement Plate(LegacyValue pl, int k, Guid guid, Guid owner, PlateProperty property, Action<AreaElement> connect, Func<LegacyValue, Group> groupOf,
            Dictionary<string, LoadCaseBase> cases, BridgeUnits units, LegacyImportReport report, LegacyElementRecords records, IReadOnlyList<Point3d> corners = null)
        {
            var plate = NewPlate(guid, property, pl.String("PlateId") ?? "", k + 1, owner);
            connect?.Invoke(plate);
            double angle = pl.Double("Angle");
            if (!double.IsFinite(angle)) throw new ArgumentException("LegacyPlateAngleNotFinite");
            if (angle != 0) plate.Assignments.LayerAxes = Builders.GpcPlateBuilders.LayerAxes(corners ?? plate.Points, angle);
            if (pl.Double("Offset") != 0) plate.Assignments.Offset = units.ToMm(pl.Double("Offset"));
            // e3: every group at list level (the former exporter wrote the first; the warning comes with the resolution, LegacyAssembly.ResolvePlate).
            var pg = pl.Field("Groups").Items; if (pg.Count > 0) plate.AddGroup(groupOf(pg[0]));
            foreach (var g in pg) records.GroupList.Add(FullName(g));
            foreach (var a in pl.Field("Attributes").Items)
            {
                if (a.Is("ElementIdAttributeModel")) Label(plate, a, report, records);
                else if (a.Is("LoadPatchTypeAttributeModel"))
                {
                    var edges = new List<int>(); for (int e = 0; a.Has("SelectedEdges" + e); e++) edges.Add(a.Int("SelectedEdges" + e));
                    var weights = new double[4]; for (int e = 0; e < 4 && a.Has("Weights" + e); e++) weights[e] = a.Double("Weights" + e);
                    (plate.Assignments.Physical ??= new ShellPhysicalData()).LoadPatch = LoadPatch(a.Enum("PatchType"), edges.ToArray(), weights);
                }
                else if (a.Is("PlateSupportAttributeModel"))
                    AddSupport(plate, a.Double("Stiffness"), a.Bool("CompressionOnly"), a.Enum("Face"), units, report, records);
                else if (a.Is("SuperElementAttributeModel")) throw new NotSupportedException(SuperElementNotOnBeam); // decision of the user (7/11)
                else throw new NotSupportedException("LegacyPlateAttributeNotMappedYet: " + a.ShortType);
            }
            foreach (var l in pl.Field("Loads").Items)
            {
                var loadCase = cases[l.Field("LoadCase").String("Name")];
                if (l.Is("PlatePreStressModel")) { AddPrestress(plate, loadCase, Vector(l["LocalPressure"]), units); continue; }
                if (l.Is("PlateNSMassModel")) { AddNonStructuralMass(plate, loadCase, l.Double("Mass"), l.Double("DynFactor"), Vector(l["Offset"]), units); continue; }
                if (!l.Is("PlatePressureModel")) throw new NotSupportedException("LegacyPlateLoadNotMappedYet: " + l.ShortType);
                var pressure = Vector(l["Pressure"]);
                // The face and the projection are read only for a global pressure (as before the refactor of R6.2).
                plate.Loads.Add(l["CoordinateSystem"].IsNull ? Pressure(loadCase, false, null, pressure, false, units) : Pressure(loadCase, true, l.Enum("Face"), pressure, l.Bool("Projected"), units));
            }
            return plate;
        }

        /// <summary>
        /// Chain E (e1): the draft of one former plate value (a <c>GH_Plate</c> datum upstream of Build Model): the GPC plate with the rules of
        /// the conversion of a whole model (<see cref="Plate"/>) without nodes, number 1, its corners (<see cref="LegacyPlateDraft.Corners"/>, in
        /// order), its property converted on its own (name as given), its first group by full name (with its parents), the cases of its loads by
        /// name. Dimensions in the definition <paramref name="units"/> (identity for a draft).
        /// </summary>
        public static LegacyPlateDraft PlateDraft(LegacyValue pl, BridgeUnits units, Guid guid)
        {
            if (pl == null || pl.IsNull || !pl.Is("PlateModel")) throw new ArgumentException("LegacyPlateModelExpected");
            var report = new LegacyImportReport { Units = units, Owner = guid, Bridge = new GPC.Checkers.CompositeBridge.BridgeModelData() };
            int count = pl.Int("NPoints"); if (count != 3 && count != 4) throw new NotSupportedException("LegacyPlateWithMoreThanFourNodes");
            var corners = Enumerable.Range(0, count).Select(i => Point(pl["Point" + i], units)).ToArray();
            var pp = pl.Field("PlateProperty"); string ppName = PlatePropertyName(pp);
            var property = PlateProperty(pp, ppName, units);
            var records = new LegacyElementRecords();
            var cases = ElementCases(pl, out var groupOf, records);
            return new LegacyPlateDraft(Plate(pl, 0, guid, guid, property, null, groupOf, cases, units, report, records, corners), corners, report) { Records = records, AngleDeg = pl.Double("Angle") };
        }

        private enum PatchKinds { Auto1, Auto2, Auto3, Auto4, AngleSplit, Manual }

        // R6.2 lane A: the pieces of a former plate shared by the reader (Plate) and the GPC builders of the plate drafts (GpcPlateBuilders): the
        // same expressions on the same numbers, so a builder gives the draft the reader gives the changed value.

        /// <summary>A GPC plate as the reader creates it (no nodes): GUID, property, name = BridGHe plate ID, number, source (the plate ID, or the
        /// number when empty).</summary>
        internal static AreaElement NewPlate(Guid guid, PlateProperty property, string plateId, int number, Guid owner) =>
            new AreaElement(guid, null, property, null, plateId, number)
            { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Shell, plateId.Length != 0 ? plateId : number.ToString()) };

        /// <summary>A former load patch type (<c>LoadPatchTypeAttributeModel</c>) by the name of its former type, its selected edges and its four
        /// edge weights as written (the former attribute wrote four of each).</summary>
        internal static LoadPatchDistribution LoadPatch(string patchType, int[] edges, double[] weights) =>
            new LoadPatchDistribution { Kind = (LoadPatchDistributionKind)(int)Enum.Parse(typeof(PatchKinds), patchType), SelectedEdges = edges, EdgeWeights = weights };

        /// <summary>A former plate support (<c>PlateSupportAttributeModel</c>): the -Z face, as the former exporter (in every load-case number used as
        /// freedom case: one freedom case exists); the former face (by the name of its type) is carried.</summary>
        internal static void AddSupport(AreaElement plate, double stiffness, bool compressionOnly, string face, BridgeUnits units, LegacyImportReport report, LegacyElementRecords records)
        {
            (plate.Assignments.Physical ??= new ShellPhysicalData()).Supports.Add(new ShellDistributedSupport
            {
                Stiffness = stiffness * units.Force / (units.Length * units.Length * units.Length), Gap = 0, Face = ShellFace.NegativeNormal,
                Behavior = compressionOnly ? DistributedSpringBehavior.CompressionOnly : DistributedSpringBehavior.Bilateral
            });
            report.Carry("Plate support face " + face + " (the former exporter wrote the -Z face)");
            records.SupportCarryList.Add("Plate support face " + face + " (the former exporter wrote the -Z face)");
        }

        /// <summary>A former plate prestress (<c>PlatePreStressModel</c>, local x, y, z in pressure units).</summary>
        internal static void AddPrestress(AreaElement plate, LoadCaseBase loadCase, Vector3d stress, BridgeUnits units) =>
            (plate.Assignments.Physical ??= new ShellPhysicalData()).InitialStresses.Add(new ShellInitialStress { Case = loadCase, Components = new Vector3d(units.ToMPa(stress.X), units.ToMPa(stress.Y), units.ToMPa(stress.Z)) });

        /// <summary>A former plate non-structural mass (<c>PlateNSMassModel</c>): only in N-mm (the mass per area as written).</summary>
        internal static void AddNonStructuralMass(AreaElement plate, LoadCaseBase loadCase, double mass, double dynamicFactor, Vector3d offset, BridgeUnits units)
        {
            if (!units.IsIdentity) throw new NotSupportedException("LegacyPlateNonStructuralMassUnitsRequireNmm");
            (plate.Assignments.Physical ??= new ShellPhysicalData()).NonStructuralMasses.Add(new ShellNonStructuralMass { Case = loadCase, MassPerArea = mass, DynamicFactor = dynamicFactor, GlobalOffset = offset });
        }

        /// <summary>A former plate pressure (<c>PlatePressureModel</c>). Review L1 B2: without a coordinate system a local pressure =
        /// St7SetPlateNormalPressure2 with one value (the -Z face, the Z component); with one a global pressure = St7SetPlateGlobalPressure3S on the
        /// face given by the name of its former type (Top: +Z, any other: -Z), projected or not.</summary>
        internal static ShellFacePressure Pressure(LoadCaseBase loadCase, bool hasCoordinateSystem, string face, Vector3d pressure, bool projected, BridgeUnits units) =>
            !hasCoordinateSystem
                ? ShellFacePressure.Normal(loadCase, ShellFace.NegativeNormal, units.ToMPa(pressure.Z))
                : ShellFacePressure.Global(loadCase, face == "Top" ? ShellFace.PositiveNormal : ShellFace.NegativeNormal,
                    new Vector3d(units.ToMPa(pressure.X), units.ToMPa(pressure.Y), units.ToMPa(pressure.Z)),
                    projected ? SurfacePressureProjection.Resultant : SurfacePressureProjection.None);

        /// <summary>A former Element ID by its value on a beam or plate draft, as <c>Label</c> reads one (an empty value is carried; the element
        /// takes the first value as its label; every value at list level), and the first Element ID of the records, for the GPC builders.</summary>
        internal static void LabelValue(Element element, string value, LegacyImportReport report, LegacyElementRecords records)
        {
            records.FirstLabel ??= value ?? "";
            if (string.IsNullOrEmpty(value)) { report.Carry("Empty Element ID attribute"); records.EmptyLabels++; return; }
            records.LabelList.Add(value);
            if (records.LabelList.Count == 1) element.Attributes.Add(new ElementLabelAttribute(value));
        }
    }
}

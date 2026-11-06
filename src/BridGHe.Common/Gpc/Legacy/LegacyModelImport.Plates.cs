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
        /// the first group, Element ID, load patch, supports, prestress, non-structural masses and pressures.
        /// </summary>
        private static AreaElement Plate(LegacyValue pl, int k, Guid guid, Guid owner, PlateProperty property, Action<AreaElement> connect, Func<LegacyValue, Group> groupOf,
            Dictionary<string, LoadCaseBase> cases, BridgeUnits units, LegacyImportReport report, LegacyElementRecords records)
        {
            string plateId = pl.String("PlateId") ?? "";
            var plate = new AreaElement(guid, null, property, null, plateId, k + 1)
            { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Shell, plateId.Length != 0 ? plateId : (k + 1).ToString()) };
            connect?.Invoke(plate);
            if (pl.Double("Angle") != 0) throw new NotSupportedException("LegacyPlateAngleNotMappedYet");
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
                    (plate.Assignments.Physical ??= new ShellPhysicalData()).LoadPatch = new LoadPatchDistribution
                    { Kind = (LoadPatchDistributionKind)(int)Enum.Parse(typeof(PatchKinds), a.Enum("PatchType")), SelectedEdges = edges.ToArray(), EdgeWeights = weights };
                }
                else if (a.Is("PlateSupportAttributeModel"))
                {
                    // Former exporter: the -Z face, in every load-case number used as freedom case (one freedom case exists).
                    (plate.Assignments.Physical ??= new ShellPhysicalData()).Supports.Add(new ShellDistributedSupport
                    {
                        Stiffness = a.Double("Stiffness") * units.Force / (units.Length * units.Length * units.Length), Gap = 0, Face = ShellFace.NegativeNormal,
                        Behavior = a.Bool("CompressionOnly") ? DistributedSpringBehavior.CompressionOnly : DistributedSpringBehavior.Bilateral
                    });
                    report.Carry("Plate support face " + a.Enum("Face") + " (the former exporter wrote the -Z face)");
                    records.SupportCarryList.Add("Plate support face " + a.Enum("Face") + " (the former exporter wrote the -Z face)");
                }
                else throw new NotSupportedException("LegacyPlateAttributeNotMappedYet: " + a.ShortType);
            }
            foreach (var l in pl.Field("Loads").Items)
            {
                var loadCase = cases[l.Field("LoadCase").String("Name")];
                if (l.Is("PlatePreStressModel"))
                {
                    var stress = Vector(l["LocalPressure"]);
                    (plate.Assignments.Physical ??= new ShellPhysicalData()).InitialStresses.Add(new ShellInitialStress { Case = loadCase, Components = new Vector3d(units.ToMPa(stress.X), units.ToMPa(stress.Y), units.ToMPa(stress.Z)) });
                    continue;
                }
                if (l.Is("PlateNSMassModel"))
                {
                    if (!units.IsIdentity) throw new NotSupportedException("LegacyPlateNonStructuralMassUnitsRequireNmm");
                    (plate.Assignments.Physical ??= new ShellPhysicalData()).NonStructuralMasses.Add(new ShellNonStructuralMass { Case = loadCase, MassPerArea = l.Double("Mass"), DynamicFactor = l.Double("DynFactor"), GlobalOffset = Vector(l["Offset"]) });
                    continue;
                }
                if (!l.Is("PlatePressureModel")) throw new NotSupportedException("LegacyPlateLoadNotMappedYet: " + l.ShortType);
                var pressure = Vector(l["Pressure"]);
                // Review L1 B2: local pressure = St7SetPlateNormalPressure2 with one value (the -Z face); global = St7SetPlateGlobalPressure3S.
                plate.Loads.Add(l["CoordinateSystem"].IsNull
                    ? ShellFacePressure.Normal(loadCase, ShellFace.NegativeNormal, units.ToMPa(pressure.Z))
                    : ShellFacePressure.Global(loadCase, l.Enum("Face") == "Top" ? ShellFace.PositiveNormal : ShellFace.NegativeNormal,
                        new Vector3d(units.ToMPa(pressure.X), units.ToMPa(pressure.Y), units.ToMPa(pressure.Z)),
                        l.Bool("Projected") ? SurfacePressureProjection.Resultant : SurfacePressureProjection.None));
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
            return new LegacyPlateDraft(Plate(pl, 0, guid, guid, property, null, groupOf, cases, units, report, records), corners, report) { Records = records };
        }

        private enum PatchKinds { Auto1, Auto2, Auto3, Auto4, AngleSplit, Manual }
    }
}

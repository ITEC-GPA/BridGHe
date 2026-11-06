using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;

namespace BridGHe.Common.Gpc.Builders
{
    /// <summary>
    /// R6.2, lane A (R62A): the GPC builders of the plate drafts. Each builder gives the draft the reader gives the former plate value with the
    /// change (<see cref="LegacyModelImport.PlateDraft"/>), with the pieces of the reader (<c>LegacyModelImport.NewPlate</c>, <c>AddSupport</c>,
    /// <c>LoadPatch</c>, <c>AddPrestress</c>, <c>AddNonStructuralMass</c>, <c>Pressure</c>, <c>LabelValue</c>): same collections, same order, same
    /// records, same report. It never changes its input (<see cref="GpcDrafts.Copy(LegacyPlateDraft)"/>) and ends with the identity given
    /// (<see cref="GpcDrafts.Reidentify(LegacyPlateDraft, Guid)"/>). Units: those of the draft (the definition units). A builder refuses what the
    /// reader refuses, with its message.
    /// </summary>
    public static class GpcPlateBuilders
    {
        /// <summary>Plate (and the plates of Bridge Deck Mesh): the draft the reader gives a former plate with these corners (definition units, in
        /// order: 3 or 4), property (the GPC property of the Plate Property datum, copied), groups (each a chain of names from the root; the plate
        /// takes the first, the records all), local axis angle (only 0: the reader refuses another), offset and plate ID; number 1, the identity as
        /// GUID and owner.</summary>
        public static LegacyPlateDraft Create(IReadOnlyList<(double X, double Y, double Z)> corners, GPC.Model.ElementProperties.PlateProperty property,
            IReadOnlyList<IReadOnlyList<string>> groups, double angleDeg, double offset, string plateId, Guid identity)
        {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (property == null) throw new ArgumentNullException(nameof(property));
            var units = GpcBeamDrafts.DraftUnits;
            var report = new LegacyImportReport { Units = units, Owner = identity, Bridge = new GPC.Checkers.CompositeBridge.BridgeModelData() };
            if (corners.Count != 3 && corners.Count != 4) throw new NotSupportedException("LegacyPlateWithMoreThanFourNodes");
            var points = corners.Select(c => new GPC.Geometry.Point3d(units.ToMm(c.X), units.ToMm(c.Y), units.ToMm(c.Z))).ToArray();
            var records = new LegacyElementRecords();
            var plate = LegacyModelImport.NewPlate(identity, GPC.Model.Core.ModelValues.CopyValue(property), plateId ?? "", 1, identity);
            if (angleDeg != 0) throw new NotSupportedException("LegacyPlateAngleNotMappedYet");
            if (offset != 0) plate.Assignments.Offset = units.ToMm(offset);
            groups ??= Array.Empty<IReadOnlyList<string>>();
            if (groups.Count > 0) plate.AddGroup(new DraftGroups().Of(groups[0]));
            foreach (var chain in groups) records.GroupList.Add(DraftGroups.FullName(chain));
            return new LegacyPlateDraft(plate, points, report) { Records = records };
        }

        /// <summary>Element ID on a plate: a former <c>ElementIdAttributeModel</c> after the attributes (the plate takes the first value as its label).</summary>
        public static LegacyPlateDraft WithLabel(LegacyPlateDraft plate, string value, Guid identity)
        {
            var d = GpcDrafts.Copy(plate ?? throw new ArgumentNullException(nameof(plate)));
            LegacyModelImport.LabelValue(d.Plate, value, d.Report, d.Records);
            return GpcDrafts.Reidentify(d, identity);
        }

        /// <summary>Plate Support: a former support (stiffness per volume, compression only or bilateral) on the -Z face as the former exporter; the
        /// former face is carried by the name of its type (Top, Bottom), in the report and in the records.</summary>
        public static LegacyPlateDraft WithSupport(LegacyPlateDraft plate, double stiffness, bool compressionOnly, string face, Guid identity)
        {
            var d = GpcDrafts.Copy(plate ?? throw new ArgumentNullException(nameof(plate)));
            LegacyModelImport.AddSupport(d.Plate, stiffness, compressionOnly, face, d.Report.Units, d.Report, d.Records);
            return GpcDrafts.Reidentify(d, identity);
        }

        /// <summary>Load Patch Type: the load patch distribution of the plate (the last one wins, as the reader): the former type by name, the four
        /// selected edges and the four edge weights as the former attribute wrote them (unset entries 0).</summary>
        public static LegacyPlateDraft WithLoadPatch(LegacyPlateDraft plate, string patchType, IReadOnlyList<int> edges, IReadOnlyList<double> weights, Guid identity)
        {
            var d = GpcDrafts.Copy(plate ?? throw new ArgumentNullException(nameof(plate)));
            var e = new int[4]; var w = new double[4];
            if (edges.Count > 4 || weights.Count > 4) throw new ArgumentException("GpcPlateBuilderLoadPatchWithMoreThanFourEdges");
            for (int i = 0; i < edges.Count; i++) e[i] = edges[i];
            for (int i = 0; i < weights.Count; i++) w[i] = weights[i];
            (d.Plate.Assignments.Physical ??= new GPC.Model.Structure.Assignments.ShellPhysicalData()).LoadPatch = LegacyModelImport.LoadPatch(patchType, e, w);
            return GpcDrafts.Reidentify(d, identity);
        }

        /// <summary>Plate Pre-Stress: a former prestress (local x, y, z) in a load case, after the prestresses of the draft.</summary>
        public static LegacyPlateDraft WithPrestress(LegacyPlateDraft plate, LegacyCaseData loadCase, double px, double py, double pz, Guid identity)
        {
            var d = GpcDrafts.Copy(plate ?? throw new ArgumentNullException(nameof(plate)));
            var c = LegacyModelImport.ElementCase(d.Plate, d.Records, loadCase);
            LegacyModelImport.AddPrestress(d.Plate, c, new GPC.Geometry.Vector3d(px, py, pz), d.Report.Units);
            Record(d, "PlatePreStressModel", loadCase);
            return GpcDrafts.Reidentify(d, identity);
        }

        /// <summary>Plate NS Mass: a former non-structural mass (mass per area, dynamic factor, global offset) in a load case, after the masses of the
        /// draft; only in N-mm, as the reader.</summary>
        public static LegacyPlateDraft WithNonStructuralMass(LegacyPlateDraft plate, LegacyCaseData loadCase, double mass, double dynamicFactor, double ox, double oy, double oz, Guid identity)
        {
            var d = GpcDrafts.Copy(plate ?? throw new ArgumentNullException(nameof(plate)));
            var c = LegacyModelImport.ElementCase(d.Plate, d.Records, loadCase);
            LegacyModelImport.AddNonStructuralMass(d.Plate, c, mass, dynamicFactor, new GPC.Geometry.Vector3d(ox, oy, oz), d.Report.Units);
            Record(d, "PlateNSMassModel", loadCase);
            return GpcDrafts.Reidentify(d, identity);
        }

        /// <summary>Plate Pressure: a former pressure in a load case, after the pressures of the draft. Without a coordinate system a local normal
        /// pressure (the Z value, -Z face); with one a global pressure on the face given by the name of its former type (Top, Bottom), projected or
        /// not (the coordinate system itself is not read, as the reader).</summary>
        public static LegacyPlateDraft WithPressure(LegacyPlateDraft plate, LegacyCaseData loadCase, bool hasCoordinateSystem, string face, double px, double py, double pz, bool projected, Guid identity)
        {
            var d = GpcDrafts.Copy(plate ?? throw new ArgumentNullException(nameof(plate)));
            var c = LegacyModelImport.ElementCase(d.Plate, d.Records, loadCase);
            var pressure = new GPC.Geometry.Vector3d(px, py, pz);
            d.Plate.Loads.Add(hasCoordinateSystem ? LegacyModelImport.Pressure(c, true, face, pressure, projected, d.Report.Units) : LegacyModelImport.Pressure(c, false, null, pressure, false, d.Report.Units));
            Record(d, "PlatePressureModel", loadCase);
            return GpcDrafts.Reidentify(d, identity);
        }

        /// <summary>The records of a new former load (kind, case) and the definition of its case, after the loads of the draft.</summary>
        private static void Record(LegacyPlateDraft d, string kind, LegacyCaseData loadCase)
        {
            d.Records.LoadList.Add((kind, loadCase.Name)); d.Records.LoadCaseList.Add(loadCase);
        }
    }

    /// <summary>R6.2 lane A (R62A): the builders of the drafts of every element family for the Element ID component; the beam one here (lane A owns
    /// the component, lane B the beam builders of the other components): the reader of a former beam with an Element ID after its attributes.</summary>
    public static class GpcElementBuilders
    {
        /// <summary>Element ID on a beam: a former <c>ElementIdAttributeModel</c> after the attributes of the beam (the reader reads the beam
        /// attributes in order: the beam takes the first value as its label, after its releases; every value at list level).</summary>
        public static LegacyBeamDraft WithLabel(LegacyBeamDraft beam, string value, Guid identity)
        {
            var d = GpcDrafts.Copy(beam ?? throw new ArgumentNullException(nameof(beam)));
            LegacyModelImport.LabelValue(d.Beam, value, d.Report, d.Records);
            return GpcDrafts.Reidentify(d, identity);
        }
    }
}

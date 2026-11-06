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

        /// <summary>Align Plate Axes: the draft the reader gives the former plate after the component, decided on the corners of the draft with
        /// the expressions of the former component and of the former local axes (Straus7 orientation): axis 2 (Blue) with a normal against the
        /// direction reverses the corners (nothing else changes: the reader reads the reversed points with the same attributes and loads); axes 0
        /// (Red) and 1 (Green) turn the local axis angle of the plate by the increment that aligns the axis with the direction projected on the
        /// plane, refused as the reader refuses a plate with a non-zero angle; a direction along the normal leaves the plate unchanged (the
        /// warning of the component). The draft of a plate has angle 0 (the reader refuses another), so the angle after the component is the
        /// increment.</summary>
        public static LegacyPlateDraft AlignAxes(LegacyPlateDraft plate, int axis, double dx, double dy, double dz, Guid identity)
        {
            var d = GpcDrafts.Copy(plate ?? throw new ArgumentNullException(nameof(plate)));
            if (axis < 0 || axis > 2) throw new ArgumentException("GpcPlateBuilderAlignAxisOutOfRange");
            var dir = Unit((dx, dy, dz));
            var p = d.Corners.Select(c => (c.X, c.Y, c.Z)).ToArray();
            LocalAxes(p, out var axes1, out var axes2, out var axes3);
            if (axis == 2)
            {
                if (Dot(axes3, dir) < 0) d = d with { Corners = d.Corners.Reverse().ToArray() };
                return GpcDrafts.Reidentify(d, identity);
            }
            var start = Unit(axis == 0 ? axes1 : axes2);
            if (Math.Abs(Dot(axes3, dir)) > 0.99999) return GpcDrafts.Reidentify(d, identity);
            var orto = Unit(Cross(axes3, dir));
            var newDir = Unit(Cross(orto, axes3));
            if (Dot(newDir, dir) < 0) newDir = (-newDir.X, -newDir.Y, -newDir.Z);
            orto = Cross(axes3, start);
            double increment = Math.Atan2(Dot(newDir, orto), Dot(newDir, start)) * 180.0 / Math.PI;
            if (increment != 0) throw new NotSupportedException("LegacyPlateAngleNotMappedYet");
            return GpcDrafts.Reidentify(d, identity);
        }

        /// <summary>The local axes of a former plate of 3 or 4 corners (<c>PlateModel.GetLocalAxes</c>, Straus7 orientation), same expressions.</summary>
        private static void LocalAxes((double X, double Y, double Z)[] p, out (double X, double Y, double Z) axes1, out (double X, double Y, double Z) axes2, out (double X, double Y, double Z) axes3)
        {
            if (p.Length == 3)
            {
                var from2To3 = Sub(p[2], p[1]);
                var mid23 = Add(p[1], Scale(0.5, from2To3));
                axes1 = Unit(Sub(mid23, p[0]));
                axes3 = Unit(Cross(Sub(p[1], p[0]), Sub(p[2], p[1])));
                axes2 = Unit(Cross(axes3, axes1));
                return;
            }
            var from1To4 = Sub(p[3], p[0]);
            var mid14 = Add(p[0], Scale(0.5, from1To4));
            var mid23q = Add(p[1], Scale(0.5, Sub(p[2], p[1])));
            axes1 = Unit(Sub(mid23q, mid14));
            var dir0 = Unit(Sub(p[1], p[0]));
            var dir1 = Unit(Sub(p[2], p[1]));
            if (Dot(dir0, dir1) > 0.99999) dir1 = Unit(from1To4);
            axes3 = Cross(dir0, dir1);
            axes2 = Unit(Cross(axes3, axes1));
            axes3 = Unit(Cross(axes1, axes2));
        }

        private static (double X, double Y, double Z) Sub((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        private static (double X, double Y, double Z) Add((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        private static (double X, double Y, double Z) Scale(double s, (double X, double Y, double Z) a) => (s * a.X, s * a.Y, s * a.Z);
        private static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        private static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
            (a.Y * b.Z - b.Y * a.Z, a.Z * b.X - b.Z * a.X, a.X * b.Y - b.X * a.Y);

        /// <summary>A unit vector as Rhino's <c>Unitize</c> (a zero vector stays as is).</summary>
        private static (double X, double Y, double Z) Unit((double X, double Y, double Z) a)
        {
            double length = Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
            return length > 0 ? (a.X / length, a.Y / length, a.Z / length) : a;
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

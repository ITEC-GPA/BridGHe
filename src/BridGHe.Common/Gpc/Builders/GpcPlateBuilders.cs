using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;
using GPC.Utilities.Extensions;
using R = Rhino.Geometry;

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
        /// <summary>PAR (A07, user decision of 8/11): the error of a plate with more than four nodes (Plate component, builders and the reader
        /// of the former files): BridGHe 0.4.0 makes plates of 3 or 4 nodes only.</summary>
        public const string MoreThanFourNodes = "LegacyPlateWithMoreThanFourNodes: a plate takes one face of 3 or 4 vertices (BridGHe 0.4.0 has no plates of 6, 8 or 9 nodes); " +
            "give one triangle or quadrangle per plate (for example, split the mesh into its faces).";

        /// <summary>Plate (and the plates of Bridge Deck Mesh): the draft the reader gives a former plate with these corners (definition units, in
        /// order: 3 or 4), property (the GPC property of the Plate Property datum, copied), groups (each a chain of names from the root; the plate
        /// takes the first, the records all), local axis angle (P2X: mapped as the reader maps it, <see cref="WithAngle"/>), offset and plate ID;
        /// number 1, the identity as GUID and owner.</summary>
        public static LegacyPlateDraft Create(IReadOnlyList<(double X, double Y, double Z)> corners, GPC.Model.ElementProperties.PlateProperty property,
            IReadOnlyList<IReadOnlyList<string>> groups, double angleDeg, double offset, string plateId, Guid identity)
        {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (property == null) throw new ArgumentNullException(nameof(property));
            var units = GpcBeamDrafts.DraftUnits;
            var report = new LegacyImportReport { Units = units, Owner = identity, Bridge = new GPC.Checkers.CompositeBridge.BridgeModelData() };
            if (corners.Count != 3 && corners.Count != 4) throw new NotSupportedException(MoreThanFourNodes);
            var points = corners.Select(c => new GPC.Geometry.Point3d(units.ToMm(c.X), units.ToMm(c.Y), units.ToMm(c.Z))).ToArray();
            var records = new LegacyElementRecords();
            var plate = LegacyModelImport.NewPlate(identity, GPC.Model.Core.ModelValues.CopyValue(property), plateId ?? "", 1, identity);
            if (offset != 0) plate.Assignments.Offset = units.ToMm(offset);
            groups ??= Array.Empty<IReadOnlyList<string>>();
            if (groups.Count > 0) plate.AddGroup(new DraftGroups().Of(groups[0]));
            foreach (var chain in groups) records.GroupList.Add(DraftGroups.FullName(chain));
            return WithAngle(new LegacyPlateDraft(plate, points, report) { Records = records }, angleDeg);
        }

        /// <summary>P2X (user decision of 8/11, «Mappalo come nella 0.3.0»): the draft with this local axis angle (degrees, as the former
        /// <c>PlateModel.AngleDeg</c>): <see cref="LegacyPlateDraft.AngleDeg"/> and the local axes of the plate turned by it in
        /// <c>Assignments.LayerAxes</c> (<see cref="LayerAxes"/> on the corners of the draft); 0: no layer axes (the plate keeps the Straus7 axes
        /// of its nodes, as the former exporter wrote no angle). It changes the draft given (a builder calls it on its own copy).</summary>
        public static LegacyPlateDraft WithAngle(LegacyPlateDraft draft, double angleDeg)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (!double.IsFinite(angleDeg)) throw new ArgumentException("LegacyPlateAngleNotFinite");
            draft.Plate.Assignments.LayerAxes = LayerAxes(draft.Corners, angleDeg);
            return draft with { AngleDeg = angleDeg };
        }

        /// <summary>P2X: the GPC layer axes of a plate of 3 or 4 corners turned by its local axis angle: origin at the first corner; the initial
        /// axes Straus7 gives the plate (<see cref="StrausAxes"/>) turned by the angle about their normal, V1 = cos·x + sin·y, V2 = V3 × V1, V3 the
        /// normal; null for the angle 0. The Straus7 writer of the Converter gives them to Straus7 as the angle of V1 from the initial axis 1 of
        /// the plate (<c>SetPlateAxes</c>: the normal must be the initial one within 1e-8, then <c>St7SetPlateXAngle1</c>), which is the angle the
        /// former exporter wrote. On a planar plate these are the turned local axes of the former plate (<see cref="LocalAxes"/>) to the last
        /// digits; on a quadrangle slightly out of plane (within the planarity of GPC) only the Straus7 normal passes the check of the writer.</summary>
        public static GPC.Geometry.CoordinateSystem LayerAxes(IReadOnlyList<GPC.Geometry.Point3d> corners, double angleDeg)
        {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (angleDeg == 0) return null;
            StrausAxes(corners, out var x, out var y, out var z);
            double radAngle = angleDeg * Math.PI / 180d, cos = Math.Cos(radAngle), sin = Math.Sin(radAngle);
            var v1 = new GPC.Geometry.Vector3d(cos * x.X + sin * y.X, cos * x.Y + sin * y.Y, cos * x.Z + sin * y.Z);
            var v2 = new GPC.Geometry.Vector3d(z.Y * v1.Z - z.Z * v1.Y, z.Z * v1.X - z.X * v1.Z, z.X * v1.Y - z.Y * v1.X);
            var o = corners[0];
            return new GPC.Geometry.CoordinateSystem(new GPC.Geometry.Point3d(o.X, o.Y, o.Z), v1, v2, z);
        }

        /// <summary>P2X: the initial axes Straus7 gives a plate of 3 or 4 corners (<c>St7GetPlateAxisSystemInitial</c>, before any angle), measured
        /// natively in P2X (passo 74) on a quadrangle with a corner out of plane: a triangle x from corner 1 to the middle of edge 2-3, z normal
        /// to edges 1-2 and 2-3; a quadrangle x from the middle of edge 4-1 to the middle of edge 2-3 (as the former plate), z = x × (middle of
        /// edge 1-2 → middle of edge 3-4), which is the normal of the diagonals (the former plate took edges 1-2 and 2-3: the same on a planar
        /// plate, 2.5e-6 apart on that quadrangle); y = z × x. Unit vectors.</summary>
        public static void StrausAxes(IReadOnlyList<GPC.Geometry.Point3d> corners, out GPC.Geometry.Vector3d x, out GPC.Geometry.Vector3d y, out GPC.Geometry.Vector3d z)
        {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            static GPC.Geometry.Vector3d V(double a, double b, double c) => new GPC.Geometry.Vector3d(a, b, c);
            static GPC.Geometry.Vector3d Unit(GPC.Geometry.Vector3d v) { double l = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z); return V(v.X / l, v.Y / l, v.Z / l); }
            static GPC.Geometry.Vector3d Cross(GPC.Geometry.Vector3d a, GPC.Geometry.Vector3d b) => V(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
            static GPC.Geometry.Vector3d Mid(GPC.Geometry.Point3d a, GPC.Geometry.Point3d b) => V((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);
            static GPC.Geometry.Vector3d Sub(GPC.Geometry.Vector3d a, GPC.Geometry.Vector3d b) => V(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            static GPC.Geometry.Vector3d P(GPC.Geometry.Point3d a) => V(a.X, a.Y, a.Z);
            var c = corners;
            if (c.Count == 3)
            {
                x = Unit(Sub(Mid(c[1], c[2]), P(c[0])));
                z = Unit(Cross(Sub(P(c[1]), P(c[0])), Sub(P(c[2]), P(c[1]))));
            }
            else if (c.Count == 4)
            {
                x = Unit(Sub(Mid(c[1], c[2]), Mid(c[3], c[0])));
                z = Unit(Cross(x, Sub(Mid(c[2], c[3]), Mid(c[0], c[1]))));
            }
            else throw new NotSupportedException("LegacyPlateWithMoreThanFourNodes");
            y = Cross(z, x);
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

        /// <summary>Align Plate Axes: the draft the reader gives the former plate after the component, decided on the corners and the local axis
        /// angle of the draft with the expressions of the former component on the former local axes (<see cref="LocalAxes"/>, turned by the
        /// angle): axis 2 (Blue) with a normal against the direction reverses the corners (the angle stays; the layer axes follow the reversed
        /// corners, as the former local axes did); axes 0 (Red) and 1 (Green) turn the local axis angle by the increment that aligns the turned axis
        /// with the direction projected on the plane (P2X: mapped as the reader maps the angle of the former output, <see cref="WithAngle"/>); a
        /// direction along the normal leaves the plate unchanged (the warning of the component).</summary>
        public static LegacyPlateDraft AlignAxes(LegacyPlateDraft plate, int axis, double dx, double dy, double dz, Guid identity)
        {
            var d = GpcDrafts.Copy(plate ?? throw new ArgumentNullException(nameof(plate)));
            if (axis < 0 || axis > 2) throw new ArgumentException("GpcPlateBuilderAlignAxisOutOfRange");
            var dir = new R.Vector3d(dx, dy, dz); dir.Unitize();
            LocalAxes(d.Corners, d.AngleDeg, out var axes1, out var axes2, out var axes3);
            if (axis == 2)
            {
                if (axes3 * dir < 0) d = WithAngle(d with { Corners = d.Corners.Reverse().ToArray() }, d.AngleDeg);
                return GpcDrafts.Reidentify(d, identity);
            }
            var start = axis == 0 ? axes1 : axes2;
            start.Unitize();
            if (Math.Abs(axes3 * dir) > 0.99999) return GpcDrafts.Reidentify(d, identity);
            var orto = R.Vector3d.CrossProduct(axes3, dir);
            orto.Unitize();
            var newDir = R.Vector3d.CrossProduct(orto, axes3);
            newDir.Unitize();
            if (newDir * dir < 0) newDir.Reverse();
            orto = R.Vector3d.CrossProduct(axes3, start);
            double x = newDir * start;
            double y = newDir * orto;
            double increment = Math.Atan2(y, x).ToDegrees();
            return GpcDrafts.Reidentify(WithAngle(d, d.AngleDeg + increment), identity);
        }

        /// <summary>R6.5 P1 lane 1a: whether Align Plate Axes on axis 0 or 1 leaves the plate unchanged because the direction is along its
        /// normal (the warning of the component), decided on the corners of the draft with the expressions of <see cref="AlignAxes"/>.</summary>
        public static bool AlignAxesAlongNormal(LegacyPlateDraft plate, double dx, double dy, double dz)
        {
            if (plate == null) throw new ArgumentNullException(nameof(plate));
            var dir = new R.Vector3d(dx, dy, dz); dir.Unitize();
            LocalAxes(plate.Corners, plate.AngleDeg, out _, out _, out var axes3);
            return Math.Abs(axes3 * dir) > 0.99999;
        }

        /// <summary>The local axes of a former plate of 3 or 4 corners (<c>PlateModel.GetLocalAxes</c>: Straus7 orientation, then turned by the local
        /// axis angle about axis 3), the same Rhino and GPC operations one for one on the corners (definition units for a draft, mm for the nodes of a
        /// model: the axes do not depend on the unit). P2X: also the axes of the preview of a plate draft.</summary>
        public static void LocalAxes(IReadOnlyList<GPC.Geometry.Point3d> corners, double angleDeg, out R.Vector3d axes1, out R.Vector3d axes2, out R.Vector3d axes3)
        {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            var p = corners.Select(c => new R.Point3d(c.X, c.Y, c.Z)).ToArray();
            if (p.Length == 3)
            {
                //   3
                //1     2: x from 1 to mid 23
                R.Vector3d from2To3 = p[2] - p[1];
                R.Point3d mid23 = p[1] + 0.5 * from2To3;
                axes1 = mid23 - p[0];
                axes1.Unitize();
                R.Vector3d dir0 = p[1] - p[0];
                R.Vector3d dir1 = p[2] - p[1];
                axes3 = R.Vector3d.CrossProduct(dir0, dir1);
                axes3.Unitize();
                axes2 = R.Vector3d.CrossProduct(axes3, axes1);
                axes2.Unitize();
            }
            else if (p.Length == 4)
            {
                //4      3
                //1      2: x from mid 14 to mid 23
                R.Vector3d from1To4 = p[3] - p[0];
                R.Vector3d from2To3 = p[2] - p[1];
                R.Point3d mid14 = p[0] + 0.5 * from1To4;
                R.Point3d mid23 = p[1] + 0.5 * from2To3;
                axes1 = mid23 - mid14;
                axes1.Unitize();
                R.Vector3d dir0 = p[1] - p[0];
                dir0.Unitize();
                R.Vector3d dir1 = p[2] - p[1];
                dir1.Unitize();
                if (R.Vector3d.Multiply(dir0, dir1) > 0.99999)
                {
                    from1To4.Unitize();
                    dir1 = from1To4;
                }
                axes3 = R.Vector3d.CrossProduct(dir0, dir1);
                axes2 = R.Vector3d.CrossProduct(axes3, axes1);
                axes2.Unitize();
                // need a recalculation of zdir, points could not be on a plane
                axes3 = R.Vector3d.CrossProduct(axes1, axes2);
                axes3.Unitize();
            }
            else throw new NotSupportedException("LegacyPlateWithMoreThanFourNodes");
            // Rotating of angle
            if (angleDeg != 0)
            {
                double radAngle = angleDeg * System.Math.PI / 180d;
                GPC.Geometry.Point3d pOnX = new GPC.Geometry.Point3d(axes1.X, axes1.Y, axes1.Z);
                GPC.Geometry.Point3d pOnY = new GPC.Geometry.Point3d(axes2.X, axes2.Y, axes2.Z);
                GPC.Geometry.CoordinateSystem trans = new GPC.Geometry.CoordinateSystem(GPC.Geometry.Point3d.Origin, pOnX, pOnY);
                GPC.Geometry.Point3d newDirX = trans.ToGlobal(new GPC.Geometry.Point3d(Math.Cos(radAngle), Math.Sin(radAngle), 0));
                axes1 = new R.Vector3d(newDirX.X, newDirX.Y, newDirX.Z);
                axes2 = R.Vector3d.CrossProduct(axes3, axes1);
            }
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

#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// R6.2, lane A (R62A, nodes and plates): the runs of the regression <c>gpc-builders-a</c> (entry <see cref="GpcBuilderChecksA"/> in the common
/// harness). Every producer component of the lane runs headless on the variants of input it distinguishes; every output datum must carry a GPC
/// draft built on GPC, which <see cref="GpcDraftCheckHost"/> compares with the conversion of its FEM value (same identity, canonical texts:
/// element, records, report). The refusals of the reader are runs too: the component gives the same output as before with no draft attached
/// and its conversion refuses it with the message of the builder. The filters decide on the GPC drafts and the host checks the decision on the
/// FEM value (<c>GpcBuilderAttach.Decide</c>). Node Displacement (user decision of 6/11): refused with
/// <see cref="LegacyModelImport.PrescribedDisplacementNotSupported"/>, no output; the reader refuses a former node with a displacement with the
/// same message. Counts per component on the PASS line.
/// </summary>
internal static class GpcBuilderChecksARuns
{
    private static readonly SortedDictionary<string, (int Runs, int Attached)> Counts = new SortedDictionary<string, (int, int)>(StringComparer.Ordinal);
    private static int TurnedPlates;

    private static List<IGH_Goo>[] Run(GpcBuilderChecks h, string type, int[] attached, params (int Input, object Value)[] inputs)
    {
        int before = h.Attached;
        var outputs = h.Run(type, attached, inputs);
        Counts.TryGetValue(type, out var c); Counts[type] = (c.Runs + 1, c.Attached + h.Attached - before);
        return outputs;
    }

    private static IGH_Goo One(GpcBuilderChecks h, string type, params (int Input, object Value)[] inputs)
    {
        var outputs = Run(h, type, new[] { 0 }, inputs);
        if (outputs[0].Count == 0) throw new Exception("GPC BUILDERS A FAIL: no output of " + type + Environment.NewLine + string.Join(Environment.NewLine, h.Failures));
        return GpcBuilderChecks.Output(outputs, 0);
    }

    /// <summary>A run that must fail with <paramref name="message"/> (a runtime error of the component) and give no output.</summary>
    private static void Error(GpcBuilderChecks h, string type, string message, params (int Input, object Value)[] inputs)
    {
        int failures = h.Failures.Count;
        var outputs = Run(h, type, new int[0], inputs);
        var added = h.Failures.Skip(failures).ToList();
        h.Failures.RemoveRange(failures, added.Count);
        if (added.Count != 1 || !added[0].Contains(message)) h.Failures.Add($"A {type}: expected the error \"{message}\", got: {string.Join(" | ", added)}");
        if (outputs.Any(o => o.Count != 0)) h.Failures.Add($"A {type}: an output after the error \"{message}\"");
    }

    /// <summary>A run without an error and without outputs (a warning of the component).</summary>
    private static void NoOutput(GpcBuilderChecks h, string type, params (int Input, object Value)[] inputs)
    {
        var outputs = Run(h, type, new int[0], inputs);
        if (outputs.Any(o => o.Count != 0)) h.Failures.Add($"A {type}: an output where the component gives none");
    }

    /// <summary>A run whose output the reader refuses: the output as before, no draft attached, its conversion refused with <paramref name="message"/>.</summary>
    private static IGH_Goo Refused(GpcBuilderChecks h, string type, string message, params (int Input, object Value)[] inputs)
    {
        var outputs = Run(h, type, new int[0], inputs);
        var goo = outputs[0].FirstOrDefault();
        if (goo == null) { h.Failures.Add($"A {type}: no output for a refused value"); return null; }
        if ((bool)goo.GetType().GetProperty("HasAttachedGpc").GetValue(goo)) h.Failures.Add($"A {type}: a draft attached to a value the reader refuses");
        string got;
        try { goo.GetType().GetProperty("Gpc").GetValue(goo); got = "converted"; }
        catch (System.Reflection.TargetInvocationException x) { got = x.InnerException?.Message; }
        if (got != message) h.Failures.Add($"A {type}: the conversion of the refused output gives \"{got}\", the builder \"{message}\"");
        return goo;
    }

    private static object Gpc(IGH_Goo goo) => goo.GetType().GetProperty("Gpc").GetValue(goo);

    private static Mesh Face(params Point3d[] points)
    {
        var mesh = new Mesh();
        foreach (var p in points) mesh.Vertices.Add(p);
        if (points.Length == 3) mesh.Faces.AddFace(0, 1, 2);
        else if (points.Length == 4) mesh.Faces.AddFace(0, 1, 2, 3);
        else for (int i = 1; i + 1 < points.Length; i++) mesh.Faces.AddFace(0, i, i + 1);
        return mesh;
    }

    internal static void Run(GpcBuilderChecks h)
    {
        Counts.Clear(); TurnedPlates = 0;
        var p = new Point3d(1000, 2000, 3000);
        var dead = GpcBuilderChecks.Output(h.Run("LoadCaseComponent", new int[0], (0, "Dead")), 0);
        var live = GpcBuilderChecks.Output(h.Run("LoadCaseComponent", new int[0], (0, "Live"), (1, true)), 0);
        var wind = GpcBuilderChecks.Output(h.Run("LoadCaseComponent", new int[0], (0, "Wind")), 0);
        var node = h.Goo("GH_Node", p);

        // R6.1 worked example (P2N), kept: Node Load, one force, a second one in the same case and one in another case (the case instance shared
        // by name), and on a restrained node (the restraint before the loads in the reader).
        var one = One(h, "NodeLoadComponent", (0, node), (1, dead), (2, new Vector3d(0, 0, -10000)));
        var two = One(h, "NodeLoadComponent", (0, one), (1, dead), (2, new Vector3d(1.5, 0, 0)));
        Run(h, "NodeLoadComponent", new[] { 0 }, (0, two), (1, live), (2, new Vector3d(0, 3e-7, 1e9)));
        var restrained = One(h, "NodeRestraintsComponent", (0, h.Goo("GH_Node", p)), (1, new[] { true, true, true }), (2, new[] { false, false, false }));
        Run(h, "NodeLoadComponent", new[] { 0 }, (0, restrained), (1, live), (2, new Vector3d(-1, -2, -3)));

        // Node (R62A-2, coordinator 6/11): a new node at a point (no ID, no groups): positive, negative and fractional coordinates, the origin,
        // large values; its output carries the draft into Node Restraints and Node Load.
        var made = One(h, "NodeComponent", (0, p));
        One(h, "NodeComponent", (0, new Point3d(-12.5, 0.1, 1e-7)));
        One(h, "NodeComponent", (0, Point3d.Origin));
        One(h, "NodeComponent", (0, new Point3d(1.5e6, -2.25e6, 987654.321)));
        One(h, "NodeLoadComponent", (0, One(h, "NodeRestraintsComponent", (0, made), (1, new[] { true, true, true }))), (1, dead), (2, new Vector3d(0, 0, -1)));

        // Node Restraints: translations, partial lists, rotations, text values, a second restraint on a restrained node, after loads.
        One(h, "NodeRestraintsComponent", (0, node), (1, new[] { false, true }), (2, new[] { true, false, true }));
        One(h, "NodeRestraintsComponent", (0, node), (1, new[] { "true", "False", "TRUE" }), (2, new[] { "false" }));
        One(h, "NodeRestraintsComponent", (0, node), (2, new[] { true, true, true }));
        var twice = One(h, "NodeRestraintsComponent", (0, restrained), (1, new[] { false, false, true }), (2, new[] { true }));
        One(h, "NodeRestraintsComponent", (0, two), (1, new[] { true, true, true }), (2, new[] { true, true, true }));

        // Node Stiffness: both vectors, translations only, rotations only (the unset vector: zero springs, free DOFs), on a restrained node,
        // twice; no stiffness: the warning, no output.
        var spring = One(h, "NodeStiffnessComponent", (0, node), (1, new Vector3d(10, 20, 30)), (2, new Vector3d(1e9, 0, 3.5)));
        One(h, "NodeStiffnessComponent", (0, node), (1, new Vector3d(0, 0, 5)));
        One(h, "NodeStiffnessComponent", (0, node), (2, new Vector3d(7, 8.25, 9e-3)));
        One(h, "NodeStiffnessComponent", (0, twice), (1, new Vector3d(1, 2, 3)));
        One(h, "NodeStiffnessComponent", (0, spring), (1, new Vector3d(4, 5, 6)), (2, new Vector3d(1, 1, 1)));
        NoOutput(h, "NodeStiffnessComponent", (0, node));

        // Node Moment: one, a second in the same case, another case, on a node with forces and a restraint.
        var m1 = One(h, "NodeMomentComponent", (0, node), (1, dead), (2, new Vector3d(0, 0, -5e6)));
        var m2 = One(h, "NodeMomentComponent", (0, m1), (1, dead), (2, new Vector3d(1, 2, 3)));
        One(h, "NodeMomentComponent", (0, m2), (1, live), (2, new Vector3d(-0.5, 1e-9, 7)));
        var loaded = One(h, "NodeMomentComponent", (0, two), (1, dead), (2, new Vector3d(12, 0, 0)));
        One(h, "NodeMomentComponent", (0, twice), (1, wind), (2, new Vector3d(0, 3, 0)));

        // Node Temperature: the three former types, in one and in two cases, on a node with loads and restraints.
        var t0 = One(h, "NodeTemperatureComponent", (0, node), (1, dead), (2, 0), (3, 20.5));
        var t1 = One(h, "NodeTemperatureComponent", (0, t0), (1, live), (2, 1), (3, -5.0));
        One(h, "NodeTemperatureComponent", (0, t1), (1, live), (2, 2), (3, 12.0));
        var hot = One(h, "NodeTemperatureComponent", (0, loaded), (1, wind), (2, 1), (3, 3.25));
        One(h, "NodeTemperatureComponent", (0, twice), (1, dead), (2, 2), (3, -40.0));

        // Node Displacement (user decision of 6/11): an error with the message of 0.4.0 and no output; no displacement: the warning of before.
        Error(h, "NodeDisplacementLoadComponent", LegacyModelImport.PrescribedDisplacementNotSupported, (0, node), (1, dead), (2, new Vector3d(0, 0, -10)));
        Error(h, "NodeDisplacementLoadComponent", LegacyModelImport.PrescribedDisplacementNotSupported, (0, restrained), (1, live), (3, new Vector3d(0.01, 0, 0)));
        NoOutput(h, "NodeDisplacementLoadComponent", (0, node), (1, dead));

        // Plates (Plate component): quadrangle, triangle, groups with a parent (the first on the plate, all in the records), offset, a load
        // patch property, a local axis angle (P2X: on the layer axes of the draft, Turned); refused as by the reader: more than four corners
        // (the output as before, no draft).
        var shell = h.Goo("GH_PlateProperty", BridgeMeshTests.DeckProperty());
        var patchProperty = h.Goo("GH_PlateProperty", new PlatePropertyModel("LP") { Name = "LP", PropertyType = PlatePropertyType.LoadPatch, MembraneThickness = 250, BendingThickness = 250, Material = new MaterialModel("C") { Modulus = 34000, PoissonRatio = .2 } });
        var deck = GpcBuilderChecks.Output(h.Run("GroupComponent", new int[0], (0, "Deck")), 0);
        var pour = GpcBuilderChecks.Output(h.Run("GroupComponent", new int[0], (0, "Pour1"), (1, deck)), 0);
        var other = GpcBuilderChecks.Output(h.Run("GroupComponent", new int[0], (0, "Other")), 0);
        Point3d a = new Point3d(0, 0, 100), b = new Point3d(2000, 0, 100), c = new Point3d(2000, 1500.5, 110), d = new Point3d(-3.25, 1500, 100);
        var quad = One(h, "PlateComponent", (0, Face(a, b, c, d)), (1, shell));
        var tri = One(h, "PlateComponent", (0, Face(a, b, c)), (1, shell), (2, new[] { pour, other }));
        var patch = One(h, "PlateComponent", (0, Face(a, b, c, d)), (1, patchProperty), (2, new[] { deck }), (4, 12.5));
        One(h, "PlateComponent", (0, Face(b, c, d)), (1, shell), (2, new[] { other, pour, deck }), (4, -0.75));
        var angledQuad = Turned(h, One(h, "PlateComponent", (0, Face(a, b, c, d)), (1, shell), (3, 30.0)));
        var angledTri = Turned(h, One(h, "PlateComponent", (0, Face(a, b, c)), (1, shell), (2, new[] { pour }), (3, -45.0), (4, 2.5)));
        // A quadrangle 0.005 mm out of plane (within the planarity of GPC): the layer axes carry the Straus7 normal, not the former one.
        var warped = Turned(h, One(h, "PlateComponent", (0, Face(new Point3d(5000, 0, 0), new Point3d(6000, 0, 0), new Point3d(6000, 1000, 0.005), new Point3d(5000, 1000, 0))), (1, shell), (3, 30.0)));
        // PAR (A07, user decision of 8/11): more than four vertices: the error and no output (0.3.0 warned and made a plate of 6, 8 or 9 nodes).
        Error(h, "PlateComponent", "LegacyPlateWithMoreThanFourNodes", (0, Face(a, b, new Point3d(2500, 700, 100), c, d)), (1, shell));

        // Plate Support: top and bottom faces (carried), compression only or not, the same support twice (carried twice), after loads.
        var s1 = One(h, "PlateSupportComponent", (0, quad), (1, 1.5e-3), (2, false), (3, 0));
        var s2 = One(h, "PlateSupportComponent", (0, s1), (1, 2.0), (2, true), (3, 1));
        One(h, "PlateSupportComponent", (0, s2), (1, 2.0), (2, true), (3, 1));
        One(h, "PlateSupportComponent", (0, tri), (1, 0.0), (2, false), (3, 1));

        // Load Patch Type: one edge (Auto1), two adjacent edges (Angle Split), manual edges and weights, a second type (the last one wins),
        // on a load patch plate and on a structural one; one edge missing: the error of before.
        var lp1 = One(h, "LoadPatchTypeComponent", (0, patch), (1, 0), (2, new[] { 2 }));
        One(h, "LoadPatchTypeComponent", (0, quad), (1, 4), (2, new[] { 0, 1 }));
        One(h, "LoadPatchTypeComponent", (0, quad), (1, 5), (2, new[] { 0, 2 }), (3, new[] { 0.25, 0.75 }));
        One(h, "LoadPatchTypeComponent", (0, lp1), (1, 3));
        One(h, "LoadPatchTypeComponent", (0, tri), (1, 1), (2, new[] { 1 }));
        Error(h, "LoadPatchTypeComponent", "The selected load patch type requires one edge", (0, quad), (1, 0));

        // Plate NS Mass, Plate Pre-Stress, Plate Pressure: one, two in a case, another case; pressure local by number, text and vector (the Z
        // value), global with a coordinate system on the top and on the bottom face, projected or not.
        var cs = h.Goo("GH_CoordinateSystem", new CoordinateSystemModel("1", "Global"));
        var n1 = One(h, "PlateNSMassComponent", (0, quad), (1, dead), (2, 0.5e-9), (3, 1.2));
        var n2 = One(h, "PlateNSMassComponent", (0, n1), (1, dead), (2, 1e-9), (3, 0.0), (4, new Vector3d(0, 0, -125)));
        One(h, "PlateNSMassComponent", (0, n2), (1, live), (2, 2e-9), (3, 1.0), (4, new Vector3d(5, 0, 0)));
        var ps1 = One(h, "PlatePreStressComponent", (0, quad), (1, dead), (2, 1.5), (3, -2.0), (4, 0.0));
        var ps2 = One(h, "PlatePreStressComponent", (0, ps1), (1, live), (2, 0.0), (3, 0.0), (4, 3.0));
        One(h, "PlatePreStressComponent", (0, ps2), (1, live), (2, 7.0), (3, 8.0), (4, 9.0));
        var pr1 = One(h, "PlatePressureComponent", (0, quad), (1, dead), (4, new GH_Number(0.01)));
        var pr2 = One(h, "PlatePressureComponent", (0, pr1), (1, dead), (4, new GH_String("0.025")));
        var pr3 = One(h, "PlatePressureComponent", (0, pr2), (1, live), (4, new GH_Vector(new Vector3d(0.1, 0.2, 0.3))));
        var pr4 = One(h, "PlatePressureComponent", (0, pr3), (1, wind), (2, cs), (3, 0), (4, new GH_Vector(new Vector3d(0, 0, -0.01))), (5, true));
        var pr5 = One(h, "PlatePressureComponent", (0, pr4), (1, live), (2, cs), (3, 1), (4, new GH_Vector(new Vector3d(1e-3, -2e-3, 0))));
        // Everything on one plate: supports, load patch, masses, prestresses and pressures in several cases, then an Element ID.
        var full = One(h, "PlateSupportComponent", (0, pr5), (1, 3.0), (2, false), (3, 0));
        full = One(h, "PlatePreStressComponent", (0, full), (1, wind), (2, 1.0), (3, 1.0), (4, 1.0));
        full = One(h, "LoadPatchTypeComponent", (0, full), (1, 1), (2, new[] { 3 }));
        full = One(h, "PlateNSMassComponent", (0, full), (1, wind), (2, 3e-9), (3, 2.0));

        // Element ID: on nodes (plain, with loads and restraints, with a rigid connection: the label before the connections as the reader),
        // plates and beams; refused as before: a second ID (the message with the first), an empty one.
        var idNode = One(h, "ElementIdComponent", (0, node), (1, "N1"));
        One(h, "ElementIdComponent", (0, hot), (1, "N2"));
        var rigidModel = new NodeModel("R1", p); rigidModel.Attributes.Add(new RigidConnectionAttributeModel(new Point3d(1000, 2000, 2500)));
        rigidModel.Attributes.Add(new RigidConnectionAttributeModel(new Point3d(1500, 2000, 3000)));
        var rigid = h.Goo("GH_Node", rigidModel, null);
        var rigidId = One(h, "ElementIdComponent", (0, rigid), (1, "R1-ID"));
        One(h, "NodeRestraintsComponent", (0, rigidId), (1, new[] { true, true, true }));
        Error(h, "ElementIdComponent", "Node N1 has already an ID", (0, idNode), (1, "N1b"));
        Error(h, "ElementIdComponent", "Node R1-ID has already an ID", (0, rigidId), (1, "X"));
        Error(h, "ElementIdComponent", "Empty string not admitted", (0, node), (1, ""));
        var idPlate = One(h, "ElementIdComponent", (0, full), (1, "P1"));
        One(h, "ElementIdComponent", (0, tri), (1, "P2"));
        Error(h, "ElementIdComponent", "Plate P1 has already an ID", (0, idPlate), (1, "P3"));
        One(h, "PlatePressureComponent", (0, idPlate), (1, dead), (4, new GH_Number(-0.5)));
        var property = new BeamPropertyModel("P") { Name = "P", PropertyType = BeamPropertyType.Beam, Material = new MaterialModel("S") { Name = "S355", Modulus = 210000, PoissonRatio = .3 },
            Section = new SectionModel { SectionType = SectionModel.SectionTypes.Generic, SectionArea = 10000, I11 = 1e8, I22 = 1e8, J = 1e7 } };
        var beam = h.Goo("GH_Beam", new BeamModel(new Point3d(0, 0, 0), new Point3d(5000, 0, 0), property), null);
        var idBeam = One(h, "ElementIdComponent", (0, beam), (1, "B1"));
        Error(h, "ElementIdComponent", "Beam B1 has already an ID", (0, idBeam), (1, "B2"));
        var releasedModel = new BeamModel(new Point3d(0, 0, 0), new Point3d(0, 4000, 0), property);
        var release = new BeamReleasesAttributeModel.AttributeData(); release.RotNode2[1] = true; releasedModel.Attributes.Add(new BeamReleasesAttributeModel(release));
        One(h, "ElementIdComponent", (0, h.Goo("GH_Beam", releasedModel, null)), (1, "B3"));

        // Align Plate Axes (R62A-2, coordinator 6/11): axis 3 (Blue) against the normal reverses the corners (quadrangle, triangle, the plate with
        // everything and an ID: attributes and loads unchanged), along the normal nothing changes; axes 1 (Red) and 2 (Green) already aligned
        // (an increment of 0, also with a direction out of the plane) keep the plate; along the normal: the warning, the plate unchanged; P2X: a
        // turn by an increment (axis 1 or 2, also on a turned plate) and axis 3 on a plate with an angle (the corners reversed, the angle kept)
        // give drafts with the angle on their layer axes (Turned).
        Point3d e = new Point3d(0, 0, 0), f = new Point3d(1000, 0, 0), g = new Point3d(1000, 500, 0), k = new Point3d(0, 500, 0);
        // The directions come from the local axes of each input (the corners of the Plate component follow the naked edge of the mesh).
        var flat = One(h, "PlateComponent", (0, Face(e, f, g, k)), (1, shell), (2, new[] { pour }));
        var (f1, f2, f3) = Axes(flat);
        var flipped = Align(h, true, flat, 2, -f3);
        Align(h, true, flipped, 2, f3 + 0.3 * f1 - 0.2 * f2);
        Align(h, false, flat, 2, 5 * f3);
        Align(h, true, tri, 2, -Axes(tri).A3);
        var (q1, q2, q3) = Axes(idPlate);
        Align(h, true, idPlate, 2, q1 + q2 - q3);
        Align(h, false, quad, 2, 0.1 * Axes(quad).A2 + Axes(quad).A3);
        Align(h, false, flat, 0, f1);
        Align(h, false, flat, 0, 5 * f1 + 3 * f3);
        Align(h, false, flat, 1, 2 * f2);
        var (g1, g2, g3) = Axes(flipped);
        Align(h, false, flipped, 1, 2 * g2 + 7 * g3);
        Align(h, false, flat, 0, f3);
        var turned = Turned(h, One(h, "AlignPlateAxesComponent", (0, flat), (1, 0), (2, f1 + f2)));
        Turned(h, One(h, "AlignPlateAxesComponent", (0, quad), (1, 1), (2, Axes(quad).A1 - 2 * Axes(quad).A2)));
        var (r1, r2, _) = Axes(turned);
        Turned(h, One(h, "AlignPlateAxesComponent", (0, turned), (1, 0), (2, r1 + 0.5 * r2)));
        Turned(h, One(h, "AlignPlateAxesComponent", (0, turned), (1, 1), (2, f1)));
        var angled = new PlateModel(new[] { e, f, g, k }, BridgeMeshTests.DeckProperty()) { AngleDeg = 30 };
        var reversedAngled = Turned(h, Align(h, true, h.Goo("GH_Plate", angled, null), 2, new Vector3d(0, 0, -1)));
        // P2X: an internalized 0.3.0 plate with an angle (its BGHA1 payload read by value) and a whole 0.3.0 model with it: the layer axes of the
        // turned local axes, on the corners of the value and on the nodes of the model.
        var byValue = GpcLegacyPayloads.Plate(BridGHe.Common.Helpers.ModelArchive.Write(angled));
        angled.GetLocalAxes(out var v1, out _, out var v3); v1.Unitize(); v3.Unitize();
        if (byValue.AngleDeg != 30 || byValue.Plate.Assignments.LayerAxes is not { } payloadAxes || (payloadAxes.V1 - new GPC.Geometry.Vector3d(v1.X, v1.Y, v1.Z)).Length > 1e-12
            || (payloadAxes.V3 - new GPC.Geometry.Vector3d(v3.X, v3.Y, v3.Z)).Length > 1e-12) h.Failures.Add("A BGHA1 plate payload with an angle: not the turned local axes");
        var former = new ModelModel(ModelModel.ModelUnits.Nmm);
        former.AddElements(new ElementModel[] { new NodeModel("1", e), new NodeModel("2", f), new NodeModel("3", g), new NodeModel("4", k), new PlateModel(new[] { e, f, g, k }, BridgeMeshTests.DeckProperty()) { AngleDeg = 30 } });
        var imported = LegacyModelImport.Import(LegacyArchive.Read(BridGHe.Common.Helpers.ModelArchive.Write(former)), Guid.NewGuid(), 1e-3, new LegacyImportReport());
        PlateAngles(h, "a whole 0.3.0 model", imported, new[] { 30.0 });
        // P2X, Build Model on turned plates (and one without an angle): every plate of the GPC model has the layer axes of its nodes turned by its
        // angle, which the Straus7 writer of the Converter gives Straus7 as the angle of V1 from the initial axis 1 (SetPlateAxes).
        var built = GpcBuilderChecks.Output(h.Run("BuildModelComponent", new int[0], (0, new[] { angledQuad, angledTri, quad, turned, reversedAngled, warped })), 0);
        var conversion = built.GetType().GetProperty("GpcInput").GetValue(built);
        PlateAngles(h, "Build Model", (GPC.Model.Models.Model)conversion.GetType().GetProperty("Model").GetValue(conversion),
            new[] { angledQuad, angledTri, quad, turned, reversedAngled, warped }.Select(x => ((PlateModel)x.GetType().GetProperty("Value").GetValue(x)).AngleDeg).ToArray());

        // Node Filter and Plate Filter: by ID, group, position, property name; a node whose draft the reader refuses (a former displacement,
        // decided on the FEM value); the host checks each decision on the FEM value. Element Filter: unchanged (GH GUIDs only), one run.
        var grouped = new NodeModel("G", new Point3d(10, 20, 30)); grouped.Groups.Add(new GroupModel("Pour1", new GroupModel("Deck")));
        var refusedNode = new NodeModel("D", new Point3d(-1, -1, -1)); refusedNode.Loads.Add(new NodeDisplacementLoadModel(new LoadCaseModel("Dead") { Name = "Dead" }, new NodeDisplacementLoadModel.LoadData(new Vector3d(0, 0, -1), Vector3d.Zero)));
        var nodes = new IGH_Goo[] { idNode, node, h.Goo("GH_Node", grouped, null), h.Goo("GH_Node", refusedNode, null), rigidId, hot };
        Partition(h, "NodeFilterComponent", 2, 4, (0, nodes), (1, new[] { "N1", "nobody" }), (3, 2), (4, new[] { pour }));
        Partition(h, "NodeFilterComponent", 5, 1, (0, nodes), (2, new[] { new Point3d(-1, -1, -1), new Point3d(1000, 2000, 3000) }), (3, 2));
        Partition(h, "NodeFilterComponent", 1, 5, (0, nodes), (1, new[] { "R1-ID" }), (3, 2), (4, new[] { other }));
        var plates = new IGH_Goo[] { quad, tri, patch, idPlate, full };
        Partition(h, "PlateFilterComponent", 2, 3, (0, plates), (1, new[] { "P1" }), (3, 2), (4, new[] { "LP" }));
        Partition(h, "PlateFilterComponent", 1, 4, (0, plates), (3, 2), (5, new[] { pour }));
        Partition(h, "PlateFilterComponent", 5, 0, (0, plates), (2, new[] { new Point3d(2000, 0, 100) }), (3, 2));
        Partition(h, "PlateFilterComponent", 1, 4, (0, plates), (2, new[] { new Point3d(9999, 0, 0) }), (3, 2), (5, new[] { other }));
        // The copy of a GH datum keeps its GH GUID: the node and its copy with an ID are both found.
        Partition(h, "ElementFilterComponent", 3, 1, (0, new IGH_Goo[] { idNode, quad, idBeam, node }), (1, new IGH_Goo[] { idNode, quad }));

        // Bridge Bearings and Bridge Deck Mesh (generators): every node and plate on GPC, the variants of the generators.
        var curved = h.Goo("BridgeGeometryGoo", BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(SimpleBridgeTests.Input(curved: true)));
        var straight = h.Goo("BridgeGeometryGoo", BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()));
        Run(h, "BridgeBearingsComponent", new[] { 0 }, (0, curved), (1, new[] { 2 }), (3, new[] { 0, 1, 2 }), (4, 23.0));
        Run(h, "BridgeBearingsComponent", new[] { 0 }, (0, curved), (1, new[] { 1 }), (2, new[] { 1 }), (3, new[] { 3 }), (5, new Vector3d(10, 20, 30)), (6, new Vector3d(40, 50, 60)));
        Run(h, "BridgeBearingsComponent", new[] { 0 }, (0, straight), (3, new[] { 2 }), (7, new Plane(new Point3d(5, 5, 5), new Vector3d(1, 1, 0), new Vector3d(-1, 1, 0))));
        Run(h, "BridgeBearingsComponent", new[] { 0 }, (0, straight), (3, new[] { 3 }), (4, -12.5), (6, new Vector3d(1e12, 0, 1e12)));
        Run(h, "BridgeDeckMeshComponent", new[] { 2 }, (0, curved), (1, shell), (2, 5000.0), (3, 2500.0), (5, true));
        Run(h, "BridgeDeckMeshComponent", new[] { 2 }, (0, straight), (1, shell), (2, 7000.0), (3, 3000.0), (4, new[] { 20000.0 }), (5, false));
        Run(h, "BridgeDeckMeshComponent", new[] { 2 }, (0, straight), (1, patchProperty), (2, 9000.0), (3, 4000.0), (5, true));

        // The reader of a former node with a displacement (an internalized 0.3.0 value, a whole former model): the same message as the component.
        var payload = BridGHe.Common.Helpers.ModelArchive.Write(refusedNode);
        Expect(h, "BGHA1 node payload", () => GpcLegacyPayloads.Node(payload), LegacyModelImport.PrescribedDisplacementNotSupported);
        Expect(h, "FEM node conversion", () => GpcBeamDrafts.FromFemm(refusedNode), LegacyModelImport.PrescribedDisplacementNotSupported);
        var restrainedDisplaced = new NodeModel("E", new Point3d(0, 0, 0)); restrainedDisplaced.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, false, false, false));
        restrainedDisplaced.Loads.Add(new NodeDisplacementLoadModel(new LoadCaseModel("Settlement") { Name = "Settlement" }, new NodeDisplacementLoadModel.LoadData(new Vector3d(0, 0, -5), Vector3d.Zero)));
        var model = new ModelModel(ModelModel.ModelUnits.Nmm); model.AddElements(new ElementModel[] { restrainedDisplaced });
        Expect(h, "BGHA1 model payload", () => LegacyModelImport.Import(LegacyArchive.Read(BridGHe.Common.Helpers.ModelArchive.Write(model)), Guid.NewGuid(), 1e-3, new LegacyImportReport()),
            LegacyModelImport.PrescribedDisplacementNotSupported);
        if (Environment.GetEnvironmentVariable("BRIDGHE_R62A_DISPLACEMENT_SCAN") == "1") DisplacementScan(h);

        h.Finish(Counts.Values.Sum(v => v.Runs));
        Console.WriteLine("GPC BUILDERS A COUNTS: " + string.Join(", ", Counts.Select(c => $"{c.Key} {c.Value.Runs} runs/{c.Value.Attached} drafts")) + $"; turned plates (P2X) {TurnedPlates}");
    }

    /// <summary>An Align Plate Axes run with a draft: the corners of the output draft are those of the input draft, reversed when
    /// <paramref name="reversed"/> (the host compares the rest with the conversion of the FEM output).</summary>
    private static IGH_Goo Align(GpcBuilderChecks h, bool reversed, IGH_Goo plate, int axis, Vector3d direction)
    {
        var output = One(h, "AlignPlateAxesComponent", (0, plate), (1, axis), (2, direction));
        var angle = ((PlateModel)output.GetType().GetProperty("Value").GetValue(output)).AngleDeg;
        var given = ((PlateModel)plate.GetType().GetProperty("Value").GetValue(plate)).AngleDeg;
        if (angle != given) { h.Failures.Add($"A AlignPlateAxesComponent: axis {axis}, direction {direction}: angle {angle:R} where {given:R}"); return output; }
        var before = ((LegacyPlateDraft)Gpc(plate)).Corners; var after = ((LegacyPlateDraft)Gpc(output)).Corners;
        var expected = reversed ? before.Reverse().ToList() : before.ToList();
        if (!after.SequenceEqual(expected)) h.Failures.Add($"A AlignPlateAxesComponent: axis {axis}, direction {direction}: corners {(reversed ? "not reversed" : "changed")}");
        return output;
    }

    /// <summary>P2X: a plate datum with a local axis angle: the draft has the angle of the FEM value (bit for bit) and, for an angle other than 0,
    /// the layer axes V1, V2, V3 of the turned local axes of the FEM value (<c>PlateModel.GetLocalAxes</c>, the axes 0.3.0 drew and gave Straus7
    /// by <c>St7SetPlateXAngle1</c>) within 1e-12; no layer axes for the angle 0.</summary>
    private static IGH_Goo Turned(GpcBuilderChecks h, IGH_Goo plate)
    {
        var fem = (PlateModel)plate.GetType().GetProperty("Value").GetValue(plate);
        var draft = (LegacyPlateDraft)Gpc(plate);
        if (!(bool)plate.GetType().GetProperty("HasAttachedGpc").GetValue(plate)) h.Failures.Add($"A turned plate {fem.AngleDeg:R}: no draft attached");
        if (BitConverter.DoubleToInt64Bits(draft.AngleDeg) != BitConverter.DoubleToInt64Bits(fem.AngleDeg)) h.Failures.Add($"A turned plate: angle {draft.AngleDeg:R} where {fem.AngleDeg:R}");
        var layer = draft.Plate.Assignments.LayerAxes;
        if (fem.AngleDeg == 0) { if (layer != null) h.Failures.Add("A turned plate: layer axes for the angle 0"); return plate; }
        if (layer == null) { h.Failures.Add($"A turned plate {fem.AngleDeg:R}: no layer axes"); return plate; }
        fem.GetLocalAxes(out var a1, out var a2, out var a3);
        a1.Unitize(); a2.Unitize(); a3.Unitize();
        double Far(GPC.Geometry.Vector3d v, Vector3d w) => Math.Max(Math.Abs(v.X - w.X), Math.Max(Math.Abs(v.Y - w.Y), Math.Abs(v.Z - w.Z)));
        // The Straus7 frame of the corners: the normal of the layer axes and the angle of V1 from its axis 1 (what the writer checks and gives).
        BridGHe.Common.Gpc.Builders.GpcPlateBuilders.StrausAxes(draft.Corners, out var sx, out var sy, out var sz);
        double normal = Math.Max(Math.Abs(layer.V3.X - sz.X), Math.Max(Math.Abs(layer.V3.Y - sz.Y), Math.Abs(layer.V3.Z - sz.Z)));
        double angle = Math.Atan2(layer.V1.X * sy.X + layer.V1.Y * sy.Y + layer.V1.Z * sy.Z, layer.V1.X * sx.X + layer.V1.Y * sx.Y + layer.V1.Z * sx.Z) * 180 / Math.PI;
        if (!(normal <= 1e-12) || !(Math.Abs(Math.IEEERemainder(angle - fem.AngleDeg, 360)) <= 1e-9)) h.Failures.Add($"A turned plate {fem.AngleDeg:R}: layer axes at {angle:R}°, normal {normal:R} from the Straus7 one");
        // A planar plate: the layer axes are the turned local axes of the former plate (the axes 0.3.0 drew), to the last digits.
        if (Far(sz, a3) <= 1e-12)
        {
            double far = Math.Max(Far(layer.V1, a1), Math.Max(Far(layer.V2, a2), Far(layer.V3, a3)));
            if (!(far <= 1e-12)) h.Failures.Add($"A turned plate {fem.AngleDeg:R}: layer axes {far:R} from the former local axes");
        }
        TurnedPlates++;
        return plate;
    }

    /// <summary>P2X: the plates of a GPC model, in order, with these local axis angles: no layer axes for 0; otherwise layer axes whose V3 is the
    /// normal of the initial axes Straus7 gives the nodes (<c>GpcPlateBuilders.StrausAxes</c>; the writer requires the normal within 1e-8) and
    /// whose V1 makes with the initial axis 1 the angle of the plate (the angle the writer gives St7SetPlateXAngle1).</summary>
    private static void PlateAngles(GpcBuilderChecks h, string what, GPC.Model.Models.Model model, double[] angles)
    {
        var plates = model.AreaElements.Values.OrderBy(p => p.Id).ToArray();
        if (plates.Length != angles.Length) { h.Failures.Add($"A {what}: {plates.Length} plates where {angles.Length}"); return; }
        for (int i = 0; i < plates.Length; i++)
        {
            var layer = plates[i].Assignments.LayerAxes;
            if (angles[i] == 0) { if (layer != null) h.Failures.Add($"A {what}: plate {i + 1}: layer axes for the angle 0"); continue; }
            if (layer == null) { h.Failures.Add($"A {what}: plate {i + 1}: no layer axes for the angle {angles[i]:R}"); continue; }
            BridGHe.Common.Gpc.Builders.GpcPlateBuilders.StrausAxes(plates[i].Points, out var e1, out var e2, out var e3);
            double x = layer.V1.X * e1.X + layer.V1.Y * e1.Y + layer.V1.Z * e1.Z, y = layer.V1.X * e2.X + layer.V1.Y * e2.Y + layer.V1.Z * e2.Z;
            double angle = Math.Atan2(y, x) * 180 / Math.PI, turn = Math.IEEERemainder(angle - angles[i], 360);
            double normal = Math.Max(Math.Abs(layer.V3.X - e3.X), Math.Max(Math.Abs(layer.V3.Y - e3.Y), Math.Abs(layer.V3.Z - e3.Z)));
            if (!(Math.Abs(turn) <= 1e-9) || !(normal <= 1e-12)) h.Failures.Add($"A {what}: plate {i + 1}: angle {angle:R} where {angles[i]:R}, normal {normal:R} from the nodes");
        }
    }

    /// <summary>The local axes of the FEM value of a plate datum (the expressions of the former component).</summary>
    private static (Vector3d A1, Vector3d A2, Vector3d A3) Axes(IGH_Goo plate)
    {
        ((PlateModel)plate.GetType().GetProperty("Value").GetValue(plate)).GetLocalAxes(out var a1, out var a2, out var a3);
        return (a1, a2, a3);
    }

    /// <summary>A filter run: the outputs are the inputs partitioned (each decision taken on the GPC draft and checked on the FEM value by the host).</summary>
    private static void Partition(GpcBuilderChecks h, string type, int found, int others, params (int Input, object Value)[] inputs)
    {
        var outputs = Run(h, type, new int[0], inputs);
        if (outputs[0].Count != found || outputs[1].Count != others) h.Failures.Add($"A {type}: {outputs[0].Count}/{outputs[1].Count} where {found}/{others}");
    }

    private static void Expect(GpcBuilderChecks h, string what, Func<object> read, string message)
    {
        string got;
        try { read(); got = "read"; } catch (Exception x) when (x is ArgumentException || x is NotSupportedException) { got = x.Message; }
        if (got != message) h.Failures.Add($"A {what}: \"{got}\" where \"{message}\"");
        Counts.TryGetValue("reader refusal", out var c); Counts["reader refusal"] = (c.Runs + 1, c.Attached);
    }

    /// <summary>The former node displacements in the examples (every BGHA1 payload of every definition, archives included; read only): the user
    /// expects none. Env BRIDGHE_R62A_DISPLACEMENT_SCAN=1.</summary>
    private static void DisplacementScan(GpcBuilderChecks h)
    {
        var files = Directory.GetFiles(ExamplePaths.Root, "*.gh*", SearchOption.AllDirectories).Where(f => (f.EndsWith(".gh") || f.EndsWith(".ghx"))
            && !f.Contains(Path.DirectorySeparatorChar + "calcoli" + Path.DirectorySeparatorChar)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        int payloads = 0, displacements = 0, nodes = 0;
        foreach (var file in files)
        {
            var archive = new GH_IO.Serialization.GH_Archive();
            if (!archive.ReadFromFile(file)) { h.Failures.Add("A displacement scan: not readable " + file); continue; }
            void Walk(GH_IO.Serialization.GH_IChunk chunk)
            {
                foreach (var item in chunk.Items)
                {
                    if (item.Type != GH_IO.Types.GH_Types.gh_bytearray || !LegacyArchive.IsLegacy(item._bytearray)) continue;
                    payloads++;
                    var seen = new HashSet<LegacyValue>(ReferenceEqualityComparer.Instance);
                    void Visit(LegacyValue v)
                    {
                        if (v == null || v.IsNull || !seen.Add(v)) return;
                        if (v.Is("NodeModel")) nodes++;
                        if (v.Is("NodeDisplacementLoadModel")) { displacements++; Console.WriteLine("R62A DISPLACEMENT " + Path.GetRelativePath(ExamplePaths.Root, file)); }
                        foreach (var f in v.Fields) Visit(f.Value);
                        if (v.IsCollection) foreach (var i in v.Items) Visit(i);
                    }
                    try { Visit(LegacyArchive.Read(item._bytearray)); } catch (Exception x) { Console.WriteLine("R62A DISPLACEMENT SCAN unreadable payload in " + file + ": " + x.Message); }
                }
                foreach (var c in chunk.Chunks) Walk(c);
            }
            Walk(archive.GetRootNode);
        }
        Console.WriteLine($"R62A DISPLACEMENT SCAN: {files.Count} files, {payloads} BGHA1 payloads, {nodes} former nodes, {displacements} former node displacements");
    }
}
#endif

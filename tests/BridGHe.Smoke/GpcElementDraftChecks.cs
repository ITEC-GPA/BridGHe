#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using GPC.Model.Attributes;
using GPC.Model.Elements;
using GPC.Model.Models;

/// <summary>
/// Chain E (e1, P2J): the drafts of single former node, beam and plate values (<see cref="LegacyModelImport.NodeDraft"/>,
/// <see cref="LegacyModelImport.BeamDraft"/>, <see cref="LegacyModelImport.PlateDraft"/>, with the units of the model) against the elements of
/// the conversion of the whole model (<see cref="LegacyModelImport.Import"/>), element by element: everything but the identity (GUID, source,
/// number: the model gives them) and the connectivity (the model finds the nodes): position or ends or corners, name, loads, assignments,
/// attributes, physical data, axes, offsets, formulation, groups, property, bridge data; the rigid connections of the node drafts are the
/// rigid links of the model. Regression <c>gpc-element-drafts</c> (not native): the examples 01–10 built in memory and the 77 FEM inputs
/// captured from the former exporter (<c>results/capture-1</c>), when present.
/// </summary>
internal static class GpcElementDraftChecks
{
    private static string Fp(params object[] values) => GPC.Model.Core.ModelValues.Fingerprint(values);
    private const string Captures = @"C:\Users\g.pacini\Documents\Repository\gpc-f6-claude\P2B\results\capture-1";

    internal static void Regression()
    {
        int models = 0, nodes = 0, beams = 0, plates = 0, links = 0, snapped = 0;
        static bool Coincide(GPC.Geometry.Point3d a, GPC.Geometry.Point3d b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        void Model(string id, ModelModel fem, double tolerance)
        {
            if (fem.Elements.Any(e => e.Results.Count != 0)) { fem = new ModelModel(fem); foreach (var e in fem.Elements) e.Results.Clear(); }
            var legacy = LegacyArchive.Read(BridGHe.Common.Helpers.ModelArchive.Write(fem));
            var report = new LegacyImportReport();
            var model = LegacyModelImport.Import(legacy, GpcIdentity.Derive(GpcIdentity.NameSpace, "drafts:" + id), tolerance, report, GpcModels.SectionStateSignatures(fem));
            var units = report.Units; var elements = legacy.Field("Elements").Items; var signature = GpcModels.SectionStateSignatures(fem);
            int n = 0, b = 0, p = 0, rigid = 0;
            foreach (var e in elements)
            {
                if (e.Is("NodeModel"))
                {
                    var draft = LegacyModelImport.NodeDraft(e, units, Guid.NewGuid()); var x = model.NodesElements[++n];
                    // The draft keeps one record per former attribute and load; Build Model resolves them (e3).
                    LegacyAssembly.ResolveNode(draft.Node, new LegacyImportReport());
                    rigid += draft.Node.Attributes.Values.OfType<RigidConnectionAttribute>().Count();
                    Node(id, draft.Node, x);
                }
                else if (e.Is("BeamModel"))
                {
                    var draft = LegacyModelImport.BeamDraft(e, units, Guid.NewGuid(), signature(b)); var x = model.BeamElements[++b];
                    // e3: the draft keeps the groups, Element IDs and bridge records at list level; Build Model resolves them.
                    LegacyAssembly.ResolveBeam(draft.Beam, draft.Records, new LegacyImportReport());
                    // A beam whose former property was shadowed by an earlier one with the same name takes that one in the model.
                    // An end merged onto a node at another position within the tolerance: the model builds the beam on the node (its axes and
                    // loads follow), the draft on its own ends; counted, not compared.
                    if (!Coincide(draft.Beam.StartPoint, x.StartPoint) || !Coincide(draft.Beam.EndPoint, x.EndPoint)) snapped++;
                    else if (!report.ShadowedPropertyBeams.Contains(b)) Beam(id, draft, x, report.Bridge.Beam(x.Guid));
                }
                else if (e.Is("PlateModel"))
                {
                    var draft = LegacyModelImport.PlateDraft(e, units, Guid.NewGuid()); var x = model.AreaElements[++p];
                    LegacyAssembly.ResolvePlate(draft.Plate, draft.Records, new LegacyImportReport());
                    if (draft.Corners.Where((c, q) => !Coincide(c, x.Nodes[q].Position)).Any()) snapped++;
                    else Plate(id, draft, x);
                }
            }
            if (rigid != model.Costrains.Count) throw new Exception($"GPC ELEMENT DRAFTS FAIL ({id}): {rigid} rigid connections on the node drafts, {model.Costrains.Count} rigid links in the model");
            models++; nodes += n; beams += b; plates += p; links += rigid;
        }
        foreach (var example in NativeExamples.Create()) Model("memory-" + example.Id, example.Model, 1e-6);
        if (Directory.Exists(Captures))
            foreach (var file in Directory.GetFiles(Captures, "*.bgmodel").OrderBy(f => f, StringComparer.Ordinal))
            {
                string info = Path.ChangeExtension(file, ".txt");
                double tolerance = File.Exists(info) ? double.Parse(File.ReadAllLines(info)[1], System.Globalization.CultureInfo.InvariantCulture) : 1e-6;
                Model("capture-" + Path.GetFileNameWithoutExtension(file), BridGHe.Common.Helpers.ModelArchive.Read<ModelModel>(File.ReadAllBytes(file)), tolerance);
            }
        if (models < 10) throw new Exception("GPC ELEMENT DRAFTS FAIL: only " + models + " models");
        Console.WriteLine($"GPC ELEMENT DRAFTS PASS: {models} models, {nodes} node, {beams} beam and {plates} plate drafts equal to the elements of the conversion of the whole model ({links} rigid connections = rigid links; {snapped} beams and plates with an end merged onto a node at another position, not compared)");
    }

    private static void Same(string id, Element element, string what, object x, object y)
    {
        if (Fp(x) != Fp(y)) throw new Exception($"GPC ELEMENT DRAFTS FAIL ({id}, {element.GetType().Name} {element.Id} '{element.Name}'): {what} differs from the conversion of the whole model");
    }

    private static object[] Groups(Element e) => e.Groups.Values.Select(g => new object[] { g.Guid, g.Name, g.Parent?.Guid }).ToArray();
    private static object[] Restraints(NodeElement n) => n.Assignments.Restrains.Select(r => new object[] { r.Case, r.FreedomCase, r.Phase, r.SourceRecord, r.Restrain?.Name, r.Restrain?.CoordinateSystem, r.Restrain?.Restrains }).ToArray();

    private static void Node(string id, NodeElement draft, NodeElement x)
    {
        Same(id, x, "name and position", new object[] { draft.Name, draft.Position }, new object[] { x.Name, x.Position });
        Same(id, x, "loads", draft.Loads.Values.ToArray(), x.Loads.Values.ToArray());
        Same(id, x, "restraints", Restraints(draft), Restraints(x));
        Same(id, x, "assignments", new object[] { draft.Assignments.Physical, draft.Assignments.Dofs, draft.Assignments.Mass, draft.Assignments.GroundSprings },
            new object[] { x.Assignments.Physical, x.Assignments.Dofs, x.Assignments.Mass, x.Assignments.GroundSprings });
        Same(id, x, "attributes", draft.Attributes.Values.Where(a => !(a is RigidConnectionAttribute)).ToArray(), x.Attributes.Values.ToArray());
        Same(id, x, "groups", Groups(draft), Groups(x));
    }

    private static void Beam(string id, LegacyBeamDraft draft, BeamElement x, GPC.Checkers.CompositeBridge.BridgeBeamData bridge)
    {
        var a = draft.Beam;
        Same(id, x, "name and ends", new object[] { a.Name, a.StartPoint, a.EndPoint }, new object[] { x.Name, x.StartPoint, x.EndPoint });
        Same(id, x, "loads", new object[] { a.Loads.Values.ToArray(), a.Assignments.Loads.Select(Load).ToArray() }, new object[] { x.Loads.Values.ToArray(), x.Assignments.Loads.Select(Load).ToArray() });
        Same(id, x, "attributes", a.Attributes.Values.ToArray(), x.Attributes.Values.ToArray());
        Same(id, x, "physical data", new object[] { a.Assignments.Physical }, new object[] { x.Assignments.Physical });
        Same(id, x, "axes and offsets", new object[] { a.Assignments.SectionAxes, a.Assignments.OffsetAxes, a.Assignments.OffsetI, a.Assignments.OffsetJ, (int)a.Assignments.Formulation },
            new object[] { x.Assignments.SectionAxes, x.Assignments.OffsetAxes, x.Assignments.OffsetI, x.Assignments.OffsetJ, (int)x.Assignments.Formulation });
        Same(id, x, "groups", Groups(a), Groups(x));
        Same(id, x, "property", new object[] { a.BeamProperty }, new object[] { x.BeamProperty });
        string Bridge(GPC.Checkers.CompositeBridge.BridgeBeamData d) => d == null ? "none" : string.Join("|", d.Member == null ? "-" : Fp(d.Member), d.SuperElement ?? "-",
            d.SectionState == null ? "-" : $"{d.SectionState.Behavior} {d.SectionState.ConcreteExcluded} {d.SectionState.ModularRatio:R} {d.SectionState.ClassificationCase ?? "-"} {d.SectionState.PropertyFingerprint}");
        if (Bridge(draft.Bridge) != Bridge(bridge)) throw new Exception($"GPC ELEMENT DRAFTS FAIL ({id}, beam {x.Id}): bridge data '{Bridge(draft.Bridge)}' / '{Bridge(bridge)}'");
    }

    /// <summary>A beam load without its source record, which names the beam by number (the draft is number 1).</summary>
    private static object Load(GPC.Model.Loads.Assignments.BeamLoadAssignment l) => new object[] { l.Start, l.End, l.Concentrated, l.StartIntensity, l.EndIntensity, l.Eccentricity,
        l.EccentricityCoordinateSystem, (int)l.LengthConvention, l.ProjectionPlaneNormal, l.StationDomain };

    private static void Plate(string id, LegacyPlateDraft draft, AreaElement x)
    {
        var a = draft.Plate;
        Same(id, x, "name and corners", new object[] { a.Name, draft.Corners.ToArray() }, new object[] { x.Name, x.Nodes.Select(n => n.Position).ToArray() });
        Same(id, x, "loads", a.Loads.Values.ToArray(), x.Loads.Values.ToArray());
        Same(id, x, "assignments", new object[] { a.Assignments.Offset, a.Assignments.Physical }, new object[] { x.Assignments.Offset, x.Assignments.Physical });
        Same(id, x, "attributes", a.Attributes.Values.ToArray(), x.Attributes.Values.ToArray());
        Same(id, x, "groups", Groups(a), Groups(x));
        Same(id, x, "property", new object[] { a.PlateProperty }, new object[] { x.PlateProperty });
    }
}
#endif

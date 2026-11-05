#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using GPC.Converter;
using GPC.Geometry;
using GPC.Model.Analysis;
using GPC.Model.Core.Identity;
using GPC.Model.Core.Units;
using GPC.Model.ElementProperties;
using GPC.Model.Elements;
using GPC.Model.LoadCases;
using GPC.Model.Materials;
using GPC.Model.Models;
using GPC.Model.Results.Locations;
using GPC.Model.Results.State;
using GPC.Model.Structure.Members;

/// <summary>
/// P2T (F6, Smoke tests on GPC): an analysed GPC model built directly from GPC objects, without any FEM model, for the tests of the
/// result consumers that read GPC (envelopes, result cases, fatigue histories, deflection and camber, support reactions, bridge actions).
/// It is what a BridGHe analysis gives the consumers: a GPC model in N-mm with a result dataset and the results catalog of the Results
/// components (<see cref="GpcResultCatalog"/>: one entry per case, label = case, in the order of the first stored result; the BridGHe angle
/// of every beam).
/// <para>Values are given as the Results components show them, in the definition units: node displacements and reactions in global axes;
/// beam actions at a station (distance from the start) in the former BridGHe principal frame of the beam angle (N, SF1, SF2, T, BM1, BM2).
/// They are converted once with the inverse of the <see cref="GpcResults"/> conventions (as the TESTS ONLY LegacyResultsReader does for the
/// former archives), so the consumers read them back bit for bit (exact for beams along the global axes, as in these tests).</para>
/// <para>Structure: nodes and beams in the definition units (converted to mm), one generic elastic beam property unless another is given,
/// bridge members as bridge data by beam GUID (<see cref="Bridge"/>, L12: outside the generic model), as the legacy conversion and the
/// future Build Model write them. Identities are derived from the model name (a rebuild gives the same GUIDs).</para>
/// </summary>
internal sealed class GpcTestModel
{
    internal const string Dataset = "BridGHe test results", Program = "BridGHe";

    private readonly Guid owner;
    private readonly BridgeUnits units;
    private readonly ResultImportBatch batch;
    private readonly AnalysisSource source;
    private readonly List<string> entries = new();
    private readonly Dictionary<int, double> angles = new();
    /// <summary>L12: the bridge data of the model (member metadata of the beams by GUID).</summary>
    internal GPC.Checkers.CompositeBridge.BridgeModelData Bridge { get; } = new GPC.Checkers.CompositeBridge.BridgeModelData();
    private ElasticBeamProperty generic;
    private bool analysed;

    public Model Model { get; }

    internal GpcTestModel(string name, DefinitionUnits definitionUnits = DefinitionUnits.Nmm)
    {
        owner = GpcIdentity.Derive(GpcIdentity.NameSpace, "p2t-test:" + name);
        units = new BridgeUnits(definitionUnits);
        Model = new Model(owner, name);
        source = new AnalysisSource { Program = Program, SolverVersion = "P2T synthetic results", ModelRevision = owner.ToString("D"), AnalysisId = Dataset };
        batch = new ResultImportBatch
        {
            Source = source, DatasetId = Dataset, SourceHash = "p2t-test/" + name,
            Units = new ResultUnits(units.Force, units.Length, units.Force * units.Length), ShellDenominatorLengthToMm = units.Length,
            ResolvedConvention = "BridGHe Results components: nodal values global; beam actions in the former principal frame, SF1 = -V1, SF2 = -V2, BM1 = M2, BM2 = -M1.",
            ReaderVersion = "BridGHe.Smoke.GpcTestModel/1", Semantics = AnalysisSemantics.LinearStatic, IsSynthetic = true,
        };
    }

    /// <summary>A primary load case (GUID by name, as every BridGHe case).</summary>
    internal LoadCaseBase Case(string name)
    {
        if (Model.LoadCases.TryGetValue(name, out var known)) return known;
        var c = new LoadCaseBase(GpcIdentity.Case(name), name);
        Model.LoadCases.Add(c.Name, c); return c;
    }

    /// <summary>A node at a point in the definition units; its BridGHe ID is <paramref name="id"/> (GPC name and source identity).</summary>
    internal NodeElement Node(string id, double x, double y, double z)
    {
        int number = Model.NodesElements.Count + 1;
        var node = new NodeElement(GpcIdentity.Derive(owner, "node:" + number), new Point3d(units.ToMm(x), units.ToMm(y), units.ToMm(z)), null, id, number)
        { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Node, id) };
        Model.NodesElements.Add(node); return node;
    }

    /// <summary>The generic elastic beam property of these tests (S355 steel, explicit section), or <paramref name="property"/>.</summary>
    internal ElasticBeamProperty Property(string name = "P", double area = 10000, double i11 = 1e8, double i22 = 1e8, double j = 1e7)
    {
        if (Model.BeamProperties.TryGetValue(name, out var known) && known is ElasticBeamProperty elastic) return elastic;
        var material = new Material("S355", 210000, 0.3, 7.85e-9, 1.2e-5);
        var property = new ElasticBeamProperty(name, material, GpcSections.Explicit(name, area, i11, i22, j, 0, 0, 0, 0, 0), area / 2, area / 2);
        Model.BeamProperties.Add(property.Name, property); return property;
    }

    /// <summary>A beam between two nodes, BridGHe ID <paramref name="id"/>, BridGHe angle in degrees (principal frame of the actions).</summary>
    internal BeamElement Beam(string id, NodeElement from, NodeElement to, double angleDeg = 0, ElasticBeamProperty property = null)
    {
        var beam = Beam(id, from.Position, to.Position, angleDeg, property);
        beam.ConnectNodes(from, to);
        return beam;
    }

    /// <summary>A beam between two points (definition units) without node elements, as the drafts upstream of Build Model (§1bis):
    /// for the consumers that read only the beams (the FEM fixtures of these tests have beams without nodes).</summary>
    internal BeamElement Beam(string id, double x0, double y0, double z0, double x1, double y1, double z1, double angleDeg = 0, ElasticBeamProperty property = null) =>
        Beam(id, new Point3d(units.ToMm(x0), units.ToMm(y0), units.ToMm(z0)), new Point3d(units.ToMm(x1), units.ToMm(y1), units.ToMm(z1)), angleDeg, property);

    private BeamElement Beam(string id, Point3d from, Point3d to, double angleDeg, ElasticBeamProperty property)
    {
        property ??= generic ??= Property();
        int number = Model.BeamElements.Count + 1;
        var beam = new BeamElement(GpcIdentity.Derive(owner, "beam:" + number), from, to, property, null, id, number)
        { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Beam, id) };
        beam.Assignments.Formulation = GPC.Model.Structure.Assignments.BeamFormulation.StraightTwoNode;
        beam.Assignments.SectionAxes = GpcBeamAxes.Section(beam.StartPoint, beam.EndPoint, angleDeg, 0);
        angles[number] = angleDeg;
        Model.BeamElements.Add(beam); return beam;
    }

    /// <summary>A beam that is part of a physical bridge member (stations in the definition units), in the order of the calls.</summary>
    internal void Member(BeamElement beam, GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily family, int group, int segment, int span, double stationFrom, double stationTo, string geometryId = "")
    {
        string id = $"{family}:{group}:{segment}";
        var metadata = new GPC.Checkers.CompositeBridge.Layout.BridgeMemberMetadata(id, family, group, segment, span, units.ToMm(stationFrom), units.ToMm(stationTo), new Vector3d(0, 0, 1), geometryId);
        Bridge.SetMember(beam.Guid, metadata);
    }

    /// <summary>The group of an element by its full BridGHe name ("Family\Group1\Segment1"), with its ancestors, as the legacy reader
    /// registers them (GPC name = full name, GUID by name).</summary>
    internal void Group(Element element, string fullName)
    {
        GPC.Model.Attributes.Group Of(string full)
        {
            if (Model.Groups.TryGetValue(full, out var known)) return known;
            int cut = full.LastIndexOf('\\');
            var parent = cut < 0 ? null : Of(full.Substring(0, cut));
            var group = GPC.Model.Attributes.Group.Create(GpcIdentity.Group(full), full);
            parent?.AddChild(group);
            Model.Groups.Add(group.Name, group); return group;
        }
        element.AddGroup(Of(fullName));
    }

    /// <summary>A global restraint of the node (translations and rotations restrained or free), as Node Restraint.</summary>
    internal void Restrain(NodeElement node, params bool[] dofs)
    {
        var restrains = Enumerable.Range(0, 6).Select(k => new GPC.Model.Restraints.DofRestrain((GPC.Model.Restraints.GeometryRestrain.DOF)k, dofs[k])).ToList();
        node.Assignments.Restrains.Add(new GPC.Model.Structure.Assignments.RestrainAssignment
            { Restrain = new GPC.Model.Restraints.NodeRestrain(node, CoordinateSystem.Global, restrains), SourceRecord = "P2T test restraint" });
    }

    private void Entry(string caseName)
    {
        if (!Model.LoadCases.ContainsKey(caseName)) throw new ArgumentException("GpcTestModel: unknown case " + caseName);
        if (!entries.Contains(caseName)) entries.Add(caseName);
    }

    private static ResultState State(string caseName) => new ResultState
    {
        Semantics = AnalysisSemantics.LinearStatic, ConcomitantStateId = "test case " + caseName, IsCombined = false, IsCumulative = true,
        Components = Enumerable.Repeat(ComponentAvailability.Available, 6).ToArray(), Coverage = "Synthetic results of a Smoke test.",
    };

    private CoordinateSystem At(Point3d mm) => new CoordinateSystem(new Point3d(mm.X / units.Length, mm.Y / units.Length, mm.Z / units.Length),
        new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1));

    /// <summary>Node translations (definition length) and rotations (rad), global axes.</summary>
    internal GpcTestModel Displacement(NodeElement node, string caseName, double ux, double uy, double uz, double rx = 0, double ry = 0, double rz = 0)
    {
        Entry(caseName);
        batch.NodeDisplacements.Add(new NodeDisplacementRecord { ElementId = node.Source.OriginalId, Case = caseName, State = State(caseName), Axes = At(node.Position),
            Record = "test node " + node.Name + " displacement " + caseName, Values = new double?[] { ux, uy, uz, rx, ry, rz } });
        return this;
    }

    /// <summary>Support reaction force and moment (definition units), global axes.</summary>
    internal GpcTestModel Reaction(NodeElement node, string caseName, double fx, double fy, double fz, double mx = 0, double my = 0, double mz = 0)
    {
        Entry(caseName);
        batch.NodeForces.Add(new NodeForceRecord { ElementId = node.Source.OriginalId, Case = caseName, State = State(caseName), Axes = At(node.Position),
            Record = "test node " + node.Name + " reaction " + caseName, Kind = NodalForceKind.SupportReaction, Body = ActionBody.OnNode,
            Values = new double?[] { fx, fy, fz, mx, my, mz } });
        return this;
    }

    /// <summary>Beam actions at <paramref name="station"/> (distance from the start, definition length) as the Results components show them:
    /// N, SF1 (Vx), SF2 (Vy), T, BM1 (Mx), BM2 (My) in the BridGHe principal frame of the beam angle.</summary>
    internal GpcTestModel Forces(BeamElement beam, string caseName, double station, double axial, double sf1 = 0, double sf2 = 0, double torque = 0, double bm1 = 0, double bm2 = 0)
    {
        Entry(caseName);
        double length = beam.Length / units.Length, t = station / length;
        var axes = beam.Assignments.SectionAxes; var principal = GpcBeamAxes.Principal(beam.StartPoint, beam.EndPoint, angles[beam.Id]);
        // Inverse of GpcResults.BeamForces (the former principal frame back to the section axes).
        var shear = principal.V1 * -sf1 + principal.V2 * -sf2;
        var bending = principal.V2 * bm1 + principal.V1 * -bm2;
        static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        var point = beam.StartPoint + (beam.EndPoint - beam.StartPoint) * t;
        var at = new CoordinateSystem(new Point3d(point.X / units.Length, point.Y / units.Length, point.Z / units.Length),
            new Vector3d(axes.V1.X, axes.V1.Y, axes.V1.Z), new Vector3d(axes.V2.X, axes.V2.Y, axes.V2.Z), new Vector3d(axes.V3.X, axes.V3.Y, axes.V3.Z));
        batch.Beams.Add(new BeamForceRecord { ElementId = beam.Source.OriginalId, Case = caseName, State = State(caseName), Axes = at, Station = t, PhysicalDistance = station,
            Body = ActionBody.PositiveSectionFace, StationDomain = "NodeToNode", Record = "test beam " + beam.Name + " station " + station.ToString("R") + " " + caseName,
            Values = new double?[] { axial, Dot(shear, axes.V1), Dot(shear, axes.V2), torque, Dot(bending, axes.V1), Dot(bending, axes.V2) } });
        return this;
    }

    /// <summary>The analysed model: physical members, the result dataset (Converter result mapper) and the results catalog. Once.</summary>
    internal Model Analyse()
    {
        Members();
        Model.AnalysisSource = source;
        batch.ExpectedInputFingerprint = ModelRevisions.AnalysisFingerprint(Model);
        if (batch.NodeDisplacements.Count + batch.NodeForces.Count + batch.Beams.Count > 0)
        {
            var report = ResultMapper.Import(Model, batch);
            if (report.Status != ImportStatus.Completed)
                throw new InvalidOperationException("GpcTestModel results not imported: " + report.Status + " " + string.Join("; ", report.Diagnostics.Select(d => d.Code + " " + d.Message)));
        }
        GpcResultCatalog.Attach(Model, units.Units, entries.Select(c => new GpcResultCatalog.Entry { Label = c, Case = c }).ToArray(), angles);
        return Model;
    }

    /// <summary>The input model (structure with the physical members, no result, no catalog): the reference of Build BridGHe Model, as the
    /// input GPC model of a GH model. The same calls on a second builder with the same name give its analysed copy (same identities and
    /// numbering). Once.</summary>
    internal Model Structure()
    {
        Members();
        return Model;
    }

    /// <summary>The BridGHe angle (degrees) of every beam, by its GPC number.</summary>
    internal IReadOnlyDictionary<int, double> BeamAngles => angles;

    private void Members()
    {
        if (analysed) throw new InvalidOperationException("GpcTestModel already analysed.");
        analysed = true;
    }
}
#endif

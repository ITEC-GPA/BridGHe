using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;
using R = Rhino.Geometry;

namespace BridGHe.Common.Gpc.Builders
{
    /// <summary>
    /// R6.5 P1 lane 3c: Merge Model on the element drafts, the steps of the former <c>ModelHelper.MergeModel</c> (definition units, the RTree of
    /// Rhino): a node within the tolerance of a kept node is merged into the nearest, on a tie the first (<see cref="LegacyBuildModel.MergeCoincident"/>,
    /// the former node merge of Build Model and of Merge Model); a beam end or plate corner moves onto the nearest node or creates one; a beam of
    /// zero length is removed; a plate whose corners coincide keeps the distinct ones, and with fewer than three it is removed. Never changes its
    /// inputs. The former branch of the plates of more than four nodes is gone with them (A07).
    /// <para>Identity (decision 1 of passo 73): by content, <c>identity(output, parameters)</c> with the input draft GUIDs (themselves by
    /// content), the tolerance and the final position, so two equal elements share a GUID.</para>
    /// </summary>
    public static class GpcDraftMerge
    {
        public sealed record Result(IReadOnlyList<LegacyNodeDraft> Nodes, IReadOnlyList<LegacyBeamDraft> Beams, IReadOnlyList<LegacyPlateDraft> Plates);

        public static Result Merge(IReadOnlyList<LegacyNodeDraft> nodes, IReadOnlyList<LegacyBeamDraft> beams, IReadOnlyList<LegacyPlateDraft> plates, double tolerance,
            Func<int, object[], Guid> identity)
        {
            if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            static R.Point3d P(GPC.Geometry.Point3d p) => new R.Point3d(p.X, p.Y, p.Z);
            static GPC.Geometry.Point3d G(R.Point3d p) => new GPC.Geometry.Point3d(p.X, p.Y, p.Z);
            var kept = new List<(LegacyNodeDraft Draft, R.Point3d Position, List<Guid> Sources)>();
            using var tree = new R.RTree();

            int Nearest(R.Point3d point)
            {
                int found = -1; double nearest = double.PositiveInfinity;
                tree.Search(new R.Sphere(point, tolerance), (s, e) =>
                {
                    double distance = kept[e.Id].Position.DistanceToSquared(point);
                    if (distance < nearest || distance == nearest && e.Id < found) { found = e.Id; nearest = distance; }
                });
                return found;
            }

            foreach (var draft in nodes)
            {
                var point = P(draft.Node.Position);
                int found = Nearest(point);
                if (found < 0)
                {
                    kept.Add((draft, point, new List<Guid> { draft.Node.Guid }));
                    tree.Insert(point, kept.Count - 1);
                }
                else
                {
                    var target = kept[found];
                    target.Sources.Add(draft.Node.Guid);
                    kept[found] = (LegacyBuildModel.MergeCoincident(target.Draft, draft), target.Position, target.Sources);
                }
            }

            R.Point3d Resolve(R.Point3d point)
            {
                int found = Nearest(point);
                if (found >= 0) return kept[found].Position;
                kept.Add((null, point, null));
                tree.Insert(point, kept.Count - 1);
                return point;
            }

            var outBeams = new List<LegacyBeamDraft>();
            foreach (var draft in beams)
            {
                var start = Resolve(P(draft.Beam.StartPoint)); var end = Resolve(P(draft.Beam.EndPoint));
                if (start == end) continue;
                var placed = GpcBeamBuilders.Placed(draft, draft.Beam.Name ?? "", G(start), G(end));
                outBeams.Add(GpcDrafts.Reidentify(placed, identity(1, new object[] { tolerance, draft.Beam.Guid, G(start), G(end) })));
            }

            var outPlates = new List<LegacyPlateDraft>();
            foreach (var draft in plates)
            {
                var corners = draft.Corners.Select(c => Resolve(P(c))).Distinct().ToArray();
                if (corners.Length < 3) continue;
                var moved = GpcDrafts.Copy(draft) with { Corners = corners.Select(G).ToArray() };
                var placed = GpcPlateBuilders.WithAngle(moved, moved.AngleDeg);
                outPlates.Add(GpcDrafts.Reidentify(placed, identity(2, new object[] { tolerance, draft.Plate.Guid, placed.Corners })));
            }

            // The nodes last: the ends and the corners may have created some, in the order they were met (the former list of the nodes).
            foreach (var n in kept.Where(n => n.Sources?.Count > 1)) LegacyBuildModel.ShareCases(n.Draft.Node); // one case instance per name, as Build Model after a merge
            var outNodes = kept.Select(n => n.Draft != null
                ? GpcDrafts.Reidentify(n.Draft, identity(0, new object[] { tolerance, n.Sources, G(n.Position) }))
                : GpcNodeBuilders.Create(n.Position.X, n.Position.Y, n.Position.Z, "", null, identity(0, new object[] { tolerance, G(n.Position) }))).ToList();
            return new Result(outNodes, outBeams, outPlates);
        }
    }
}

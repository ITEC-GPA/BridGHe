using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GPC.Model.Elements;
using GPC.Model.Models;
using GPC.Model.Structure.Assignments;

namespace BridGHe.Common.Gpc
{
    /// <summary>F6 C4: what the Results components show from an analysed GPC model. The requested results in the order of the analysis
    /// (label shown in the "Load cases" outputs, GPC case, step, phase), the units of the definition and the BridGHe angle of every beam
    /// (principal axes of the former export). Kept in the analysed model as a BridGHe record of <see cref="Model.PreservedSourceData"/>,
    /// so the results file next to the .st7 (BGHR2) carries it too. Never an input of a solver: <see cref="Remove"/> before writing.</summary>
    public sealed class GpcResultCatalog
    {
        public const string Kind = "BridGHe", Record = "AnalysisResults/1";
        /// <summary>F6 R7: the message of a Results component whose element has no analysed GPC model with a results catalog (not analysed,
        /// analysed by a version before F6, or an element of the bridge model of Build BridGHe Model, whose merged actions have no catalog).</summary>
        public const string NoAnalysedResultsMessage = "Nessun risultato di un'analisi BridGHe per questo elemento (modello non analizzato, analizzato con una versione precedente o modello di ponte): ricalcolare l'analisi.";
        /// <summary>R6.4 (6): the message of a result utility (envelopes, result cases, deflection and camber, fatigue history) whose model has no
        /// analysed GPC model with a results catalog; a model of a former file shows its own notice instead.</summary>
        public const string NoAnalysedModelMessage = "Nessun risultato di un'analisi BridGHe per questo modello (modello non analizzato, analizzato con una versione precedente o modello di ponte): collegare l'uscita di Straus R3 Analysis o di Moving Static Analysis e premere Run.";

        public sealed class Entry
        {
            /// <summary>The name shown in the "Load cases" outputs (load case, combination, generated moving case, increment).</summary>
            public string Label { get; set; }
            /// <summary>The GPC case holding the values: load case, combination (superposition of its cases) or output case.</summary>
            public string Case { get; set; }
            public string Step { get; set; }
            public string Phase { get; set; }
        }

        private sealed class Data
        {
            public string Units { get; set; }
            public Entry[] Entries { get; set; }
            public Dictionary<int, double> BeamAngles { get; set; }
        }

        private static readonly ConditionalWeakTable<Model, GpcResultCatalog> cache = new ConditionalWeakTable<Model, GpcResultCatalog>();
        private readonly Dictionary<int, double> angles;

        public BridgeUnits Units { get; }
        public IReadOnlyList<Entry> Entries { get; }

        private GpcResultCatalog(Data data)
        {
            Units = new BridgeUnits(BridgeUnits.Parse(data.Units));
            Entries = data.Entries ?? throw new ArgumentException("GpcResultCatalogWithoutEntries");
            angles = data.BeamAngles ?? new Dictionary<int, double>();
        }

        /// <summary>The BridGHe angle (degrees) of an analysed beam, by its GPC number.</summary>
        public double BeamAngle(BeamElement beam) =>
            angles.TryGetValue(beam.Id, out var angle) ? angle : throw new KeyNotFoundException("GpcResultCatalogBeamAngleMissing: " + beam.Id);

        /// <summary>Writes the catalog into the analysed model (replacing a previous one) and returns it.</summary>
        public static GpcResultCatalog Attach(Model analysed, DefinitionUnits units, IReadOnlyList<Entry> entries, IReadOnlyDictionary<int, double> beamAngles)
        {
            if (analysed == null) throw new ArgumentNullException(nameof(analysed));
            var data = new Data { Units = units.ToString(), Entries = entries.ToArray(), BeamAngles = beamAngles.ToDictionary(p => p.Key, p => p.Value) };
            Remove(analysed);
            analysed.PreservedSourceData.Add(new PreservedAssignment
            {
                Kind = Kind, SourceRecord = Record, RawData = JsonSerializer.Serialize(data),
                UnitsAndAxes = "Values N-mm in the model; shown in the definition units (" + data.Units + "); beam principal axes from the BridGHe angle (degrees).",
                UnsupportedReason = "BridGHe results catalog of the Results components; not a solver input.",
            });
            var catalog = new GpcResultCatalog(data);
            cache.AddOrUpdate(analysed, catalog);
            return catalog;
        }

        /// <summary>The catalog of an analysed model, or null when the model has none (not analysed by BridGHe).</summary>
        public static GpcResultCatalog Of(Model analysed)
        {
            if (analysed == null) return null;
            if (cache.TryGetValue(analysed, out var known)) return known;
            var record = analysed.PreservedSourceData.LastOrDefault(r => r != null && r.Kind == Kind && r.SourceRecord == Record);
            if (record == null) return null;
            var catalog = new GpcResultCatalog(JsonSerializer.Deserialize<Data>(record.RawData) ?? throw new ArgumentException("GpcResultCatalogUnreadable"));
            cache.AddOrUpdate(analysed, catalog);
            return catalog;
        }

        /// <summary>C6: the static cases generated by a moving-only analysis, by native number (the "number: name" step of the moving case),
        /// as the former GeneratedMovingCases of the FEM result; empty for any other analysis.</summary>
        public IReadOnlyList<(int Number, string Name)> GeneratedMovingCases() => Entries
            .Where(e => e.Step != null && e.Phase == null)
            .Select(e => (Number: int.Parse(e.Step.Substring(0, e.Step.IndexOf(':')), NumberStyles.None, CultureInfo.InvariantCulture), Name: e.Label))
            .OrderBy(e => e.Number).ToArray();

        /// <summary>C6, model audit: a result without case identity (an entry with an empty label or case, or a stored location of the
        /// analysed model without case), the GPC form of the former check on the FEM result objects.</summary>
        public static bool HasInvalidResult(Model analysed, GpcResultCatalog catalog) =>
            catalog.Entries.Any(e => string.IsNullOrWhiteSpace(e.Label) || string.IsNullOrWhiteSpace(e.Case))
            || analysed.AllElements.Any(el => el.Results.SelectMany(r => r.Results).Any(l => string.IsNullOrWhiteSpace(l.Case?.Name)));

        /// <summary>Removes the catalog (a model analysed again: the writer accepts only solver records).</summary>
        public static void Remove(Model model)
        {
            model.PreservedSourceData.RemoveAll(r => r != null && r.Kind == Kind && r.SourceRecord == Record);
            cache.Remove(model);
        }
    }

    /// <summary>F6 C4: the rows of Node, Beam and Plate Results from an analysed GPC model, in the order of the catalog entries, in the
    /// definition units, with the conventions of <see cref="GpcResults"/>. The analysis writes the former FEM result objects with the same
    /// functions, so both give the same numbers bit for bit.</summary>
    public static class GpcResultRows
    {
        public sealed class NodeRow
        {
            public int Entry;
            /// <summary>Translations (definition length) and rotations (rad), global axes; null when absent.</summary>
            public double[] Displacement;
            /// <summary>Reaction force and moment, global axes; null when the node has no reaction.</summary>
            public double[] Reaction;
        }

        public sealed class BeamRow
        {
            public int Entry;
            /// <summary>Distance from the start of the beam (definition length).</summary>
            public double Station;
            public double Axial, Shear1, Shear2, Torque, Bending1, Bending2;
        }

        public sealed class PlateRow
        {
            public int Entry;
            /// <summary>Node number of the result (plate node, no averaging).</summary>
            public string Node;
            /// <summary>{Fxx, Fyy, Fxy, Fxz, Fyz, Mxx, Myy, Mxy} per unit width, plate local axes.</summary>
            public double[] Values;
            public double ForceMax, ForceMin, ForceAngle, ForceVonMises, MomentMax, MomentMin, MomentAngle;
        }

        /// <summary>A value as the Results components showed it from the FEM results, whose copies (the BGHA1 archive of the GH data)
        /// write −0 as 0: zero without sign, every other value unchanged bit for bit. Only for the component outputs; the analysis writes
        /// the rows unchanged into the FEM results.</summary>
        public static double Shown(double value) => value == 0 ? 0.0 : value;

        private static bool IsCombination(Model solved, GpcResultCatalog.Entry e) => e.Step == null && e.Phase == null && solved.Combinations.ContainsKey(e.Case);

        /// <summary>C2 (P26-07, user decision of 11/11: as 0.3.0): a combination analysed since C2 carries the results Straus7 computed for it (the LSA
        /// combination case, read as StrausResultsReader of 0.3.0 read it: displacements, reactions, beam forces at its own stations, plate forces and
        /// moments): those are the rows. A model analysed before C2 has none: its rows stay the superposition of the cases (at the stations common to
        /// its cases for the beams), as before.</summary>
        private static bool Stored(IEnumerable<GPC.Model.Results.Locations.ResultLocation> locations) => locations.Any(l => l.State?.IsCombined == true);

        public static IEnumerable<NodeRow> Node(GpcResultCatalog catalog, Model solved, NodeElement node)
        {
            var u = catalog.Units;
            for (int i = 0; i < catalog.Entries.Count; i++)
            {
                var e = catalog.Entries[i]; bool combination = IsCombination(solved, e) && !Stored(GpcResults.Locations<GPC.Model.Results.Locations.ResultLocation>(node, e.Case));
                var d = combination ? GpcResults.Combined(solved, e.Case, c => GpcResults.NodeDisplacement(node, c)) : GpcResults.NodeDisplacement(node, e.Case, e.Step, e.Phase);
                var r = combination ? GpcResults.Combined(solved, e.Case, c => GpcResults.NodeReaction(node, c)) : GpcResults.NodeReaction(node, e.Case, e.Step, e.Phase);
                if (d == null && r == null) continue;
                yield return new NodeRow
                {
                    Entry = i,
                    Displacement = d == null ? null : new[] { u.FromMm(d[0]), u.FromMm(d[1]), u.FromMm(d[2]), d[3], d[4], d[5] },
                    Reaction = r == null ? null : new[] { u.FromN(r[0]), u.FromN(r[1]), u.FromN(r[2]), u.FromNmm(r[3]), u.FromNmm(r[4]), u.FromNmm(r[5]) },
                };
            }
        }

        public static IEnumerable<BeamRow> Beam(GpcResultCatalog catalog, Model solved, BeamElement beam)
        {
            var u = catalog.Units;
            var principal = GpcBeamAxes.Principal(beam.StartPoint, beam.EndPoint, catalog.BeamAngle(beam));
            double length = beam.Length;
            for (int i = 0; i < catalog.Entries.Count; i++)
            {
                var e = catalog.Entries[i];
                var rows = IsCombination(solved, e) && !Stored(GpcResults.Locations<GPC.Model.Results.Locations.ResultLocation>(beam, e.Case))
                    ? CombinedBeam(solved, beam, e.Case, principal) : GpcResults.BeamForces(beam, e.Case, principal, e.Step, e.Phase);
                foreach (var row in rows)
                    yield return new BeamRow
                    {
                        Entry = i, Station = u.FromMm(row[0] * length), Axial = u.FromN(row[1]), Shear1 = u.FromN(row[2]), Shear2 = u.FromN(row[3]),
                        Torque = u.FromNmm(row[4]), Bending1 = u.FromNmm(row[5]), Bending2 = u.FromNmm(row[6]),
                    };
            }
        }

        public static IEnumerable<PlateRow> Plate(GpcResultCatalog catalog, Model solved, AreaElement plate)
        {
            var u = catalog.Units;
            for (int i = 0; i < catalog.Entries.Count; i++)
            {
                var e = catalog.Entries[i];
                var rows = IsCombination(solved, e) && !Stored(GpcResults.Locations<GPC.Model.Results.Locations.ResultLocation>(plate, e.Case))
                    ? CombinedPlate(solved, plate, e.Case) : GpcResults.PlateNodeForces(plate, e.Case, e.Step, e.Phase);
                foreach (var row in rows)
                {
                    var w = row.Values;
                    var v = new[] { u.FromNPerMm(w[0]), u.FromNPerMm(w[1]), u.FromNPerMm(w[2]), u.FromNPerMm(w[3]), u.FromNPerMm(w[4]), u.FromN(w[5]), u.FromN(w[6]), u.FromN(w[7]) };
                    var (fMax, fMin, fAngle) = Principal(v[0], v[1], v[2]); var (mMax, mMin, mAngle) = Principal(v[5], v[6], v[7]);
                    yield return new PlateRow
                    {
                        Entry = i, Node = row.Node.ToString(CultureInfo.InvariantCulture), Values = v,
                        ForceMax = fMax, ForceMin = fMin, ForceAngle = fAngle, ForceVonMises = VonMises(v[0], v[1], v[2], v[3], v[4]),
                        MomentMax = mMax, MomentMin = mMin, MomentAngle = mAngle,
                    };
                }
            }
        }

        /// <summary>The Von Mises force per length that BridGHe 0.3.0 showed (Straus7 combined plate force, <c>stPlateCombined</c>
        /// <c>ipPlateCombVonMises</c>): the membrane forces and the transverse shears, √(Nxx² − Nxx·Nyy + Nyy² + 3(Nxy² + Qxz² + Qyz²)).
        /// Equal to the in-plane value where Qxz = Qyz = 0.</summary>
        private static double VonMises(double xx, double yy, double xy, double xz, double yz) =>
            Math.Sqrt(xx * xx - xx * yy + yy * yy + 3 * (xy * xy + xz * xz + yz * yz));

        /// <summary>Principal values and angle (degrees from local x) of an in-plane tensor, as Straus7 combined them before.</summary>
        private static (double Max, double Min, double AngleDeg) Principal(double xx, double yy, double xy)
        {
            double c = (xx + yy) / 2, r = Math.Sqrt((xx - yy) * (xx - yy) / 4 + xy * xy);
            return (c + r, c - r, 0.5 * Math.Atan2(2 * xy, xx - yy) * 180 / Math.PI);
        }

        /// <summary>Combination at the stations common to its cases (Straus7 adds result-dependent stations per case).</summary>
        private static IReadOnlyList<double[]> CombinedBeam(Model solved, BeamElement beam, string name, GPC.Geometry.CoordinateSystem principal)
        {
            var pairs = solved.Combinations[name].GetLoadCaseCoefficientsPair();
            var perCase = pairs.Select(p => (p.Value, Rows: GpcResults.BeamForces(beam, p.Key.Name, principal))).ToArray();
            if (perCase.Length == 0) return Array.Empty<double[]>();
            var stations = perCase[0].Rows.Select(r => r[0]).Where(s => perCase.All(c => c.Rows.Any(r => Math.Abs(r[0] - s) < 1e-9))).ToArray();
            return stations.Select(s =>
            {
                var sum = new double[7]; sum[0] = s;
                foreach (var c in perCase) { var row = c.Rows.First(r => Math.Abs(r[0] - s) < 1e-9); for (int k = 1; k < 7; k++) sum[k] += c.Value * row[k]; }
                return sum;
            }).ToArray();
        }

        private static IReadOnlyList<(int Node, double[] Values)> CombinedPlate(Model solved, AreaElement plate, string name)
        {
            var pairs = solved.Combinations[name].GetLoadCaseCoefficientsPair();
            var perCase = pairs.Select(p => (p.Value, Rows: GpcResults.PlateNodeForces(plate, p.Key.Name).ToDictionary(r => r.Node, r => r.Values))).ToArray();
            if (perCase.Length == 0) return Array.Empty<(int, double[])>();
            return plate.Nodes.Where(n => perCase.All(c => c.Rows.ContainsKey(n.Id))).Select(n =>
            {
                var sum = new double[8];
                foreach (var c in perCase) for (int k = 0; k < 8; k++) sum[k] += c.Value * c.Rows[n.Id][k];
                return (n.Id, sum);
            }).ToArray();
        }
    }
}

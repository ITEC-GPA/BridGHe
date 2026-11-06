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
    /// <summary>R6.1: the reader helpers shared by the element families (cases and groups of one element, Element ID); of P2N, the lanes of R6.2 call them.</summary>
    public static partial class LegacyModelImport
    {
        /// <summary>The cases of the loads of one former element by name (the rules of the case set of a whole model: a name, one definition
        /// per name), and the groups of a draft by full name with their parents (no model: the draft keeps them). e3: the definitions of the
        /// cases (<see cref="LegacyElementRecords.Cases"/>) and the first Element ID (<see cref="LegacyElementRecords.FirstLabel"/>) go to the
        /// records of the draft.</summary>
        private static Dictionary<string, LoadCaseBase> ElementCases(LegacyValue element, out Func<LegacyValue, Group> groupOf, LegacyElementRecords records)
        {
            var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
            Group GroupOf(LegacyValue g)
            {
                string full = FullName(g);
                if (groups.TryGetValue(full, out var known)) return known;
                var parent = g["Parent"].IsNull ? null : GroupOf(g["Parent"]);
                var group = Group.Create(GpcIdentity.Group(full), full);
                parent?.AddChild(group);
                groups.Add(full, group);
                return group;
            }
            groupOf = GroupOf;
            var first = new Dictionary<string, LegacyCaseData>(StringComparer.Ordinal);
            foreach (var c in element.Field("Loads").Items.Select(l => LegacyCaseData.From(l.Field("LoadCase"))))
            {
                if (string.IsNullOrWhiteSpace(c.Name)) throw new ArgumentException("Caso di carico nullo o senza nome.");
                if (!first.TryGetValue(c.Name, out var known)) first.Add(c.Name, c);
                else if (!known.Same(c)) throw new ArgumentException("Definizioni incompatibili per il caso di carico " + c.Name);
            }
            foreach (var l in element.Field("Loads").Items) { records.LoadList.Add((l.ShortType, l.Field("LoadCase").String("Name"))); records.LoadCaseList.Add(LegacyCaseData.From(l.Field("LoadCase"))); }
            var label = element.Field("Attributes").Items.FirstOrDefault(a => a.Is("ElementIdAttributeModel"));
            records.FirstLabel = label == null ? null : label.String("Value") ?? "";
            return first.ToDictionary(pair => pair.Key, pair => Case(pair.Key, pair.Value), StringComparer.Ordinal);
        }

        /// <summary>Former releases: per end and kind, a released DOF with zero partial value is released, with a value it is a partial
        /// (absolute) stiffness; the others stay continuous. Beam principal axes, as St7SetBeamTRelease3/RRelease3.</summary>
        /// <summary>C1: the former Element ID (ElementIdComponent: the user's identifier of a node, beam or plate) as the GPC element label,
        /// descriptive (the Straus7 writer does not write it; the former exporter numbered the native elements itself).</summary>
        /// <remarks>e3: every value at list level (<see cref="LegacyElementRecords.Labels"/>); the element takes the first as its label, the
        /// warning for each other comes with the resolution.</remarks>
        private static void Label(Element element, LegacyValue a, LegacyImportReport report, LegacyElementRecords records)
        {
            string value = a.String("Value");
            if (string.IsNullOrEmpty(value)) { report.Carry("Empty Element ID attribute"); records.EmptyLabels++; return; }
            records.LabelList.Add(value);
            if (records.LabelList.Count == 1) element.Attributes.Add(new ElementLabelAttribute(value));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;

namespace BridGHe.Grasshopper.Helpers;

/// <summary>0.7.2 (proposal 8 of 21/11/2026): the member families by name. The values stay the integer codes (0 girder … 8 transverse slab,
/// -1 all steel families in Bridge Family Property); only the way of choosing changes: the named values of the input (right click) and a
/// connected Value List filled with the names, keeping the value it had selected.</summary>
internal static class FamilyChoices
{
    internal const string AllSteel = "All steel families";

    /// <summary>The choices in the order of the menu: «All steel families» (-1) first when allowed, then the families of the enumeration.</summary>
    internal static (string Name, int Value)[] Of(bool allSteel) =>
        (allSteel ? new[] { (AllSteel, -1) } : Array.Empty<(string, int)>())
        .Concat(Enum.GetValues<BridgeMemberFamily>().Select(f => (f.ToString(), (int)f))).ToArray();

    internal static void AddNamedValues(Param_Integer parameter, bool allSteel)
    { foreach (var (name, value) in Of(allSteel)) parameter.AddNamedValue(name, value); }

    /// <summary>Fills every Value List connected to the input with the family names. A list is changed only when each of its items is a valid
    /// family code (a list of anything else is left as it is); the value selected before stays selected. Lists already filled are untouched.</summary>
    internal static void Configure(GH_Component component, int port, bool allSteel)
    {
        if (port >= component.Params.Input.Count) return;
        var choices = Of(allSteel);
        foreach (var list in component.Params.Input[port].Sources.OfType<GH_ValueList>().ToArray())
        {
            if (list.ListItems.Count == 0) continue;
            var values = new List<int>();
            foreach (var item in list.ListItems)
            {
                if (!int.TryParse(item.Expression?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || !choices.Any(c => c.Value == v)) { values = null; break; }
                values.Add(v);
            }
            if (values == null) continue;
            if (list.ListMode == GH_ValueListMode.DropDown && list.ListItems.Count == choices.Length
                && list.ListItems.Select((item, i) => item.Name == choices[i].Name && item.Expression == choices[i].Value.ToString(CultureInfo.InvariantCulture)).All(v => v)) continue;
            int selected = list.ListItems.Select((item, i) => (item, i)).Where(x => x.item.Selected).Select(x => values[x.i]).DefaultIfEmpty(values[0]).First();
            list.ListMode = GH_ValueListMode.DropDown; list.ListItems.Clear();
            foreach (var (name, value) in choices)
                list.ListItems.Add(new GH_ValueListItem(name, value.ToString(CultureInfo.InvariantCulture)) { Selected = value == selected });
            list.ExpireSolution(false); component.Params.Input[port].ExpireSolution(false);
        }
    }
}

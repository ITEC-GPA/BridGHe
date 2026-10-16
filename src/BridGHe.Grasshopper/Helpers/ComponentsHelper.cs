using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel.Special;

namespace BridGHe.Grasshopper.Helpers
{
    public static class ComponentsHelper
    {
        /// <summary>
        /// Setup a Value List componet with the given keys
        /// </summary>
        /// <param name="vallist">The Value List component</param>
        /// <param name="keys">The keys to putting in</param>
        public static void SetValueList(GH_ValueList vallist, IEnumerable<string> keys)
        {
            /*IEnumerable<GH_ValueListItem> ex_keys = vallist.ListItems.Where(i => keys.Contains(i.Name));
            //if (ex_keys.Count() != vallist.ListItems.Count)
            {
                List<GH_ValueListItem> selected = vallist.SelectedItems;

                vallist.ListItems.Clear();
                foreach (string k in keys)
                    vallist.ListItems.Add(new GH_ValueListItem(k, $"\"{k}\""));

                foreach (GH_ValueListItem sv in selected)
                {
                    GH_ValueListItem ev = vallist.ListItems.SingleOrDefault(i => i.Name == sv.Name);
                    if (ev != null)
                    {
                        int i = vallist.ListItems.IndexOf(ev);
                        if (i != -1)
                            vallist.SelectItem(i);
                    }
                }
            }*/
            //IEnumerable<GH_ValueListItem> ex_keys = vallist.ListItems.Where(i => keys.Contains(i.Name));

            GH_ValueListItem[] to_remove = vallist.ListItems.Where(i => !keys.Contains(i.Name)).ToArray();
            foreach (GH_ValueListItem item in to_remove)
                vallist.ListItems.Remove(item);
            bool changed = to_remove.Length == 0 ? false : true;

            foreach (string k in keys)
            {
                if (vallist.ListItems.Count(i => i.Name == k) == 0)
                {
                    vallist.ListItems.Add(new GH_ValueListItem(k, $"\"{k}\""));
                    changed = true;
                }
            }

            if (changed)
                vallist.ExpireSolution(true);
        }
    }
}

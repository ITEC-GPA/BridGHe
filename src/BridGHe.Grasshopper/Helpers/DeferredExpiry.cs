using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// P2CG (13/11/2026, review of ChatGPT task 51, CE-03): an object expired after the current solution instead of <c>ExpireSolution(true)</c>
    /// from inside it. The requests of one document are merged: one schedule (<see cref="GH_Document.ScheduleSolution(int, GH_Document.GH_ScheduleDelegate)"/>,
    /// 1 ms) and one callback for every object asked for until it runs; the callback expires them (<c>ExpireSolution(false)</c>), and the scheduled
    /// solution solves them.
    /// <para>When the callback runs (Grasshopper 8, <c>GH_Document</c>): a schedule asked for during a solution starts its timer when that solution
    /// ends; the timer reaches the solution through the Grasshopper editor (no editor, as in a headless host: no timer solution). Every new solution,
    /// whatever starts it (the timer, a manual one before the timer, a host), first runs the pending callbacks; so a manual solution never drops the
    /// request, and no re-arm at <c>SolutionEnd</c> is needed (proposal of task 82, not taken: it would add a second callback per request).</para>
    /// </summary>
    internal static class DeferredExpiry
    {
        private static readonly object Sync = new object();
        private static readonly ConditionalWeakTable<GH_Document, HashSet<IGH_ActiveObject>> Pending = new ConditionalWeakTable<GH_Document, HashSet<IGH_ActiveObject>>();

        /// <summary>The number of schedules asked for since the start (merged requests ask for none): read by the Smoke checks of P2P-01.</summary>
        internal static int Schedules { get; private set; }

        /// <summary>Expires <paramref name="owner"/> before the next solution of its document (nothing outside a document).</summary>
        internal static void Request(IGH_ActiveObject owner)
        {
            var document = owner?.OnPingDocument();
            if (document == null) return;
            lock (Sync)
            {
                var owners = Pending.GetValue(document, _ => new HashSet<IGH_ActiveObject>());
                bool first = owners.Count == 0;
                owners.Add(owner);
                if (!first) return;
                Schedules++;
            }
            document.ScheduleSolution(1, Expire);
        }

        /// <summary>The objects of <paramref name="document"/> waiting for the callback: read by the Smoke checks of P2P-01.</summary>
        internal static int Waiting(GH_Document document)
        {
            lock (Sync) return document != null && Pending.TryGetValue(document, out var owners) ? owners.Count : 0;
        }

        private static void Expire(GH_Document document)
        {
            IGH_ActiveObject[] owners;
            lock (Sync)
            {
                if (!Pending.TryGetValue(document, out var set)) return;
                owners = set.ToArray();
                set.Clear();
            }
            // An object removed from the document (or moved to another) is left alone.
            foreach (var owner in owners)
                if (ReferenceEquals(owner.OnPingDocument(), document)) owner.ExpireSolution(false);
        }
    }
}

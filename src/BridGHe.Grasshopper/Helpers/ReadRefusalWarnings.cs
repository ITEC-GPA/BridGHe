using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GH_IO.Serialization;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>A file-reader message is an archive log, not a canvas runtime message. Keep refusals against the
    /// owning parameter and reapply them after each solution while it has no valid value. Installed lazily by
    /// a refusal during Read; no assembly-load callback, UI dialog, file IO or scheduled solution.</summary>
    internal static class ReadRefusalWarnings
    {
        private sealed class Pending
        {
            internal readonly Dictionary<Guid, HashSet<string>> Messages = new();
            internal Guid DocumentId;
        }
        private sealed class State
        {
            internal readonly Dictionary<IGH_Param, Entry> Messages = new();
        }
        private sealed class Entry
        {
            internal readonly HashSet<string> Messages = new(StringComparer.Ordinal);
            internal bool PersistentChanged;
        }
        private static readonly ConditionalWeakTable<GH_Archive, Pending> Archives = new();
        // Keep only the small diagnostic record alive until the document is registered, not its archive or goo.
        // GH_DocumentIO can release the archive before DocumentAdded, including across a GC.
        private static readonly List<Pending> PendingReads = new();
        private static readonly ConditionalWeakTable<GH_Document, State> Documents = new();
        private static bool installed;

        internal static void Record(GH_IReader reader, string message)
        {
            var archive = reader?.Archive;
            if (archive == null) return; // A loose goo read has no parameter to colour; its archive log remains available.
            Guid owner = Owner(archive.GetRootNode, reader, Guid.Empty);
            if (owner == Guid.Empty) return;
            if (!Archives.TryGetValue(archive, out var pending))
            {
                pending = new Pending(); Archives.Add(archive, pending);
                var header = archive.GetRootNode.FindChunk("Definition")?.FindChunk("DocumentHeader");
                if (header?.ItemExists("DocumentID") == true) pending.DocumentId = header.GetGuid("DocumentID");
            }
            if (!PendingReads.Contains(pending)) PendingReads.Add(pending);
            if (!pending.Messages.TryGetValue(owner, out var messages)) pending.Messages.Add(owner, messages = new HashSet<string>(StringComparer.Ordinal));
            messages.Add(message);
            if (!installed)
            {
                installed = true;
                var server = global::Grasshopper.Instances.DocumentServer;
                foreach (GH_Document document in server) Attach(document, false);
                server.DocumentAdded += (_, document) => Attach(document, true);
            }
        }

        // GH persistent data may be on a floating parameter or a component input, including Param_GenericObject.
        // The nearest InstanceGuid on the path is the parameter's, never the component type GUID.
        private static Guid Owner(GH_IReader chunk, GH_IReader target, Guid inherited)
        {
            if (chunk.ItemExists("InstanceGuid")) inherited = chunk.GetGuid("InstanceGuid");
            if (ReferenceEquals(chunk, target)) return inherited;
            foreach (var child in chunk.Chunks)
            {
                var found = Owner((GH_IReader)child, target, inherited);
                if (found != Guid.Empty) return found;
            }
            return Guid.Empty;
        }

        private static IEnumerable<IGH_Param> Parameters(GH_Document document) =>
            document.Objects.OfType<IGH_Param>().Concat(document.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Input.Concat(c.Params.Output)));

        private static void Attach(GH_Document document, bool bind)
        {
            if (document == null || Documents.TryGetValue(document, out _)) return;
            var state = new State(); Documents.Add(document, state);
            if (bind) Bind(document, state);
            document.SolutionStart += (_, _) => Bind(document, state); // Also supports internalise, paste and undo in an open document.
            document.SolutionEnd += (_, _) => Show(document, state);
        }

        private static void Bind(GH_Document document, State state)
        {
            var parameters = Parameters(document).ToDictionary(p => p.InstanceGuid);
            foreach (var pending in PendingReads.ToArray())
            {
                if (pending.DocumentId != Guid.Empty && pending.DocumentId != document.DocumentID) continue;
                bool claimed = false;
                foreach (var id in pending.Messages.Keys.ToArray())
                    if (parameters.TryGetValue(id, out var parameter))
                    {
                        if (state.Messages.TryGetValue(parameter, out var existing) && !existing.PersistentChanged) continue;
                        if (!state.Messages.TryGetValue(parameter, out var entry))
                        {
                            state.Messages.Add(parameter, entry = new Entry());
                            parameter.ObjectChanged += (_, e) =>
                            {
                                if (e.Type == GH_ObjectEventType.PersistentData && state.Messages.TryGetValue(parameter, out var current))
                                    current.PersistentChanged = true;
                            };
                        }
                        entry.Messages.UnionWith(pending.Messages[id]); entry.PersistentChanged = false;
                        pending.Messages.Remove(id);
                        claimed = true;
                    }
                if (pending.Messages.Count == 0) PendingReads.Remove(pending);
                // Two copies opened before registration can have identical saved instance IDs. Claim one read per document.
                if (claimed) break;
            }
        }

        private static void Show(GH_Document document, State state)
        {
            var parameters = new HashSet<IGH_Param>(Parameters(document));
            foreach (var pair in state.Messages.ToArray())
            {
                var parameter = pair.Key;
                if (!parameters.Contains(parameter)) { state.Messages.Remove(parameter); continue; }
                bool valid = parameter.VolatileData.AllData(true).Any(g => g != null && g.IsValid);
                if (valid && pair.Value.PersistentChanged) { state.Messages.Remove(parameter); continue; }
                if (valid && (parameter.SourceCount > 0 || parameter.Kind == GH_ParamKind.output)) continue;
                // A partially read list is still incomplete: an unrelated valid item must not hide its refusal.
                foreach (string message in pair.Value.Messages)
                    if (!parameter.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Contains(message))
                        parameter.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, message);
            }
        }
    }
}

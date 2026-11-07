using System;
using BridGHe.Common.Gpc;
using BridGHe.Grasshopper.DataTypes.Fem;
using GH_IO.Serialization;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// R6.4 (2), option 1 of P2Q (coordinator, 7/11): the drafts of the elements of the model an analysis component keeps (Straus R3 Analysis,
    /// Moving Static Analysis) are those of its input (§1bis, point 10), so the document does not save them with that model
    /// (<see cref="GH_Model.Write(GH_IWriter, bool)"/>): it keeps only the fingerprint of the input they belong to
    /// (<see cref="GpcResultSelection.InputHash"/>, item "DraftsInputHash"). At the first solution after opening the drafts of the live input
    /// are attached to the output again when its fingerprint is the same, as a Run would attach them. With another input the output carries
    /// no drafts (its elements keep the conversion of their FEM values until R6.5) and the component warns: Run. A document saved before
    /// (0.3.0, or the integration branch with the drafts in the model) has no fingerprint: nothing is attached, the model keeps what it read.
    /// </summary>
    internal sealed class AnalysisDrafts
    {
        internal const string HashItem = "DraftsInputHash";
        internal const string ChangedMessage = "Il modello d'ingresso è cambiato dopo l'analisi: l'uscita tiene i risultati salvati, senza i dati GPC degli elementi d'ingresso. Rilanciare Run.";
        private string _hash;
        private bool _pending;

        /// <summary>True between reading a document with a fingerprint and the first solution that attaches the drafts again.</summary>
        public bool Pending => _pending;

        /// <summary>After a Run: the output keeps the elements of the input, so it carries the drafts of the input (when it carries some);
        /// the fingerprint of the input is kept for the document (<paramref name="hash"/> when the component has it already).</summary>
        public void Record(GH_Model input, GH_Model output, string hash = null)
        {
            _pending = false; _hash = null;
            var drafts = input?.GpcDrafts;
            if (drafts == null || output?.Value == null) return;
            output.AttachDrafts(drafts);
            _hash = hash ?? Fingerprint(input);
        }

        /// <summary>No analysed model any more (Reset, an error, an input without results).</summary>
        public void Clear() { _hash = null; _pending = false; }

        public void Write(GH_IWriter writer) { if (_hash != null) writer.SetString(HashItem, _hash); }

        public void Read(GH_IReader reader)
        {
            _hash = null; _pending = false;
            if (!reader.ItemExists(HashItem)) return;
            _hash = reader.GetString(HashItem); _pending = _hash != null;
        }

        /// <summary>At the first solution after opening: the drafts of <paramref name="input"/> attached to <paramref name="output"/> when the
        /// fingerprint of the input is the saved one (<paramref name="hash"/> when the component has it already). Returns null, or the
        /// warning to show (the input changed: no drafts).</summary>
        public string Restore(GH_Model input, GH_Model output, string hash = null)
        {
            if (!_pending) return null;
            _pending = false;
            if (output?.Value == null) { _hash = null; return null; }
            string current = input?.Value == null ? null : hash ?? Fingerprint(input);
            if (current == null || current != _hash) { _hash = null; return ChangedMessage; }
            var drafts = input.GpcDrafts;
            if (drafts != null) output.AttachDrafts(drafts);
            return null;
        }

        /// <summary>The fingerprint of the GPC input of a model, or null when it has none (an input that cannot be converted).</summary>
        private static string Fingerprint(GH_Model model)
        {
            try { var input = model.GpcInput; return input == null ? null : GpcResultSelection.InputHash(input); }
            catch (Exception ex) when (ex is NotSupportedException || ex is ArgumentException || ex is InvalidOperationException || ex is System.Runtime.Serialization.SerializationException)
            { return null; }
        }
    }
}

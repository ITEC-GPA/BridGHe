using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Grasshopper.DataTypes.Fem;
using GH_IO.Serialization;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// F6 phase (c), C4: where an analysis component keeps its analysed GPC model between sessions, as master's results file (d7e4092):
    /// in <c>&lt;output&gt;.bghr</c> next to the .st7 (<see cref="GpcResultsFile"/>, BGHR2), not in the Grasshopper document, which keeps only
    /// "ResultsFile" and "ResultsHash" (SHA-256). After a run the file is written; at the first solution after opening it is read, from
    /// the current output path first and then from the stored one, and attached to the output only when its hash matches; otherwise
    /// the component warns and asks for Run. A results file of master (BGHR1, FeMM payload) gives the previous-version message.
    /// Until the FeMM results leave the document (phase b), the former results stay embedded as before, so FeMM consumers are unchanged.
    /// </summary>
    internal sealed class SavedGpcResults
    {
        private const string FileItem = "ResultsFile", HashItem = "ResultsHash";
        private string _path, _hash;
        private bool _pending;

        /// <summary>After an analysis: writes the analysed GPC model of <paramref name="model"/> next to its native output. Returns a warning
        /// when there is no analysed model or the file cannot be written (the GPC results then need a new Run after reopening).</summary>
        public string Record(GH_Model model, string outputPath)
        {
            _pending = false; _path = null; _hash = null;
            if (model == null || !model.HasAnalysedGpc) return null;
            try
            {
                string path = GpcResultsFile.PathFor(outputPath);
                _hash = GpcResultsFile.Write(path, model.Gpc); _path = path;
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is ArgumentException || ex is NotSupportedException)
            {
                return "The GPC results were not written beside the model (" + ex.Message + "): after the file is reopened, press Run.";
            }
        }

        /// <summary>True between reading a document with a results file and the first solution that restores it.</summary>
        public bool Pending => _pending;

        /// <summary>At the first solution after opening: reads the stored results next to the current <paramref name="outputPath"/> or where
        /// they were written, and attaches them to <paramref name="model"/>. Returns null, or the warning to show (Run required).</summary>
        public string Restore(GH_Model model, string outputPath)
        {
            if (!_pending) return null;
            _pending = false;
            string current = null, error = null;
            try { if (!string.IsNullOrWhiteSpace(outputPath)) current = GpcResultsFile.PathFor(outputPath); } catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { error = ex.Message; }
            foreach (string path in new[] { current, _path }.Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path)) continue;
                try
                {
                    if (GpcResultsFile.IsPreviousVersion(path)) { error = GpcResultsFile.PreviousVersionMessage; continue; }
                    var analysed = GpcResultsFile.Read(path, _hash);
                    if (model?.Value == null && !GpcOnlyModels.WithoutFem(model)) return "GPC results without their model in the document: press Run again.";
                    model.AttachGpc(analysed); _path = path;
                    return null;
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Runtime.Serialization.SerializationException)
                { error = ex.Message; }
            }
            error ??= "File dei risultati non trovato: " + (current ?? _path) + ".";
            _path = null; _hash = null;
            return error.Contains("Run") ? error : error + " Rilanciare Run per i risultati GPC.";
        }

        public void Write(GH_IWriter writer)
        {
            if (_path == null || _hash == null) return;
            writer.SetString(FileItem, _path);
            writer.SetString(HashItem, _hash);
        }

        public void Read(GH_IReader reader)
        {
            _path = null; _hash = null; _pending = false;
            if (!reader.ItemExists(FileItem) || !reader.ItemExists(HashItem)) return;
            _path = reader.GetString(FileItem); _hash = reader.GetString(HashItem); _pending = true;
        }
    }
}

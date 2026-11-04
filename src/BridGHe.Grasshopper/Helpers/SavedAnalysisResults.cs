using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Grasshopper.DataTypes.Fem;
using GH_IO.Serialization;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// Where an analysis component keeps its results between sessions (4/11/2026): in the file next to the native output
    /// (<see cref="AnalysisResultsFile"/>), not in the Grasshopper document. The document keeps the file and its SHA-256, and
    /// the file is read at the first solution after opening.
    /// A document saved before keeps its results inside and opens as before; at its next save the results move to the file.
    /// A model without results (an empty or reset component) is still written in the document, as before.
    /// </summary>
    internal sealed class SavedAnalysisResults
    {
        private const string FileItem = "ResultsFile", HashItem = "ResultsHash";
        private string _path, _hash;
        // The model whose results are in the file, and the empty model of a document just opened, until the file is read.
        private GH_Model _saved, _placeholder;

        /// <summary>After an analysis: writes the results next to its native output. Returns a warning when the file cannot be written.</summary>
        public string Record(GH_Model model, string outputPath)
        {
            _placeholder = null;
            try
            {
                _path = AnalysisResultsFile.PathFor(outputPath);
                _hash = AnalysisResultsFile.Write(_path, model.Value, model.ElementGuids());
                _saved = model;
                return null;
            }
            catch (Exception ex)
            {
                _saved = null; _hash = null;
                return "Risultati non scritti accanto al modello (" + ex.Message + "): restano nel documento.";
            }
        }

        /// <summary>True while <paramref name="current"/> stands for results saved in a file that has not been read yet.</summary>
        public bool Pending(GH_Model current) => _placeholder != null && ReferenceEquals(current, _placeholder);

        /// <summary>
        /// Reads the results saved with the document: next to the current <paramref name="outputPath"/>, or where they were written.
        /// Returns null, with the reason in <paramref name="error"/>, when the file is missing or no longer the one saved.
        /// </summary>
        public GH_Model Load(string outputPath, out string error)
        {
            error = null;
            _placeholder = null;
            string current = null;
            try { if (!string.IsNullOrWhiteSpace(outputPath)) current = AnalysisResultsFile.PathFor(outputPath); }
            catch (Exception ex) { error = ex.Message; }
            foreach (string path in new[] { current, _path }.Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var value = AnalysisResultsFile.Read(path, _hash, out var guids);
                    _path = path;
                    return _saved = GH_Model.FromSavedResults(value, guids);
                }
                catch (Exception ex) { error = ex.Message; }
            }
            error ??= "File dei risultati non trovato: " + (current ?? _path) + ".";
            _saved = null; _path = null; _hash = null;
            return null;
        }

        /// <summary>
        /// Writes the results of <paramref name="current"/> as a reference to their file. Results not yet in a file (a document saved
        /// before) are written to the file first. A model without results, or one that no file can hold, goes to
        /// <paramref name="embed"/>, which writes it in the document as before.
        /// </summary>
        public void Write(GH_IWriter writer, GH_Model current, string outputPath, Action<GH_IWriter> embed)
        {
            bool inFile = _hash != null && (ReferenceEquals(current, _saved) || Pending(current));
            if (!inFile && !string.IsNullOrWhiteSpace(outputPath) && AnalysisResultsFile.HasResults(current?.Value))
                inFile = Record(current, outputPath) == null;
            if (!inFile) { embed(writer); return; }
            writer.SetString(FileItem, _path);
            writer.SetString(HashItem, _hash);
        }

        /// <summary>Reads what <see cref="Write"/> wrote. Returns the placeholder of results in a file, or null when the document holds them.</summary>
        public GH_Model Read(GH_IReader reader)
        {
            _saved = null; _placeholder = null; _path = null; _hash = null;
            if (!reader.ItemExists(HashItem) || !reader.ItemExists(FileItem)) return null;
            _path = reader.GetString(FileItem);
            _hash = reader.GetString(HashItem);
            return _placeholder = new GH_Model();
        }
    }
}

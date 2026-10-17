using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using St7API;
using static BridGHe.Common.Straus.StrausRuntime;

namespace BridGHe.Common.Straus
{
    public static class StrausAnalysis
    {
        public static ModelModel Run(ModelModel input, string outputPath, int solver, IReadOnlyCollection<string> requested, double tolerance, bool movingOnly, out List<string> warnings)
        {
            // The native solver accepts only canonical Windows paths when opening
            // its result file, although St7NewFile also accepts forward slashes.
            outputPath = Path.GetFullPath(outputPath);
            if (solver < 0 || solver > 2) throw new ArgumentOutOfRangeException(nameof(solver));
            if (!string.Equals(Path.GetExtension(outputPath), ".st7", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Estensione richiesta: .st7");
            if (movingOnly && solver != 0) throw new ArgumentException("Moving Static Analysis richiede il solutore lineare.");
            if (movingOnly && !input.MovingPaths.Any(p => p.ConvertToStaticCases)) throw new ArgumentException("Nessun percorso configurato per generare casi statici.");
            if (movingOnly && input.MovingPaths.Any(p => p.Template.Vehicles.Any(v => v.Loads.Any(l => l is MovingLoads.MovingHeatSource))))
                throw new NotSupportedException("Moving Static Analysis è strutturale. Le sorgenti termiche vanno esportate e risolte con il solutore termico Straus.");
            var model = ModelArchive.Clone(input);
            var oldMovingNames = new HashSet<string>(model.GeneratedMovingCases.Values);
            model.LoadCases.RemoveAll(c => oldMovingNames.Contains(c.Name));
            foreach (var element in model.Elements) element.Results.Clear();
            ModelHelper.GetLoadCases(model, out var cases);
            var selection = movingOnly ? Array.Empty<StrausResultSelection.Entry>() : StrausResultSelection.Select(model, solver, requested);
            var names = new HashSet<string>(selection.Select(e => e.Case.Name), StringComparer.Ordinal);
            var selectedCases = selection.Select(e => e.Case).OfType<LoadCaseModel>().ToList();
            var selectedCombinations = selection.Select(e => e.Case).OfType<LoadCombinationModel>().ToList();
            var enabled = new HashSet<string>(selectedCases.Select(c => c.Name));
            foreach (var combination in selectedCombinations)
                foreach (var item in combination.Values) if (item.Value != 0) enabled.Add(item.Key.Name);
            lock (Gate)
            {
                if (!StrausR3Helper.CreateModelR3(model, outputPath, tolerance, out _, out warnings, out var errors))
                    throw new InvalidOperationException(string.Join(Environment.NewLine, errors.Concat(warnings)));
                int uid = AcquireModelId(); bool fileOpen = false, resultOpen = false;
                try
                {
                    Check(St7.St7OpenFile(uid, outputPath, Path.GetTempPath()), "St7OpenFile"); fileOpen = true;
                    var read = new List<(int Index, CaseModel Case)>();
                    int primary = 0;
                    if (solver == 0)
                    {
                        // Export numbers ordinary load cases in this order, starting at one.
                        for (int i = 0; i < cases.Count; i++)
                        {
                            bool active = !movingOnly && enabled.Contains(cases[i].Name);
                            Check(active ? St7.St7EnableLSALoadCase(uid, i + 1, 1) : St7.St7DisableLSALoadCase(uid, i + 1, 1), "Configure LSA load case");
                            if (active) { primary++; if (names.Contains(cases[i].Name)) read.Add((primary, cases[i])); }
                        }
                        foreach (var entry in model.GeneratedMovingCases.OrderBy(e => e.Key))
                        {
                            Check(movingOnly ? St7.St7EnableLSALoadCase(uid, entry.Key, 1) : St7.St7DisableLSALoadCase(uid, entry.Key, 1), "Configure moving LSA case");
                            if (movingOnly) { var c = new LoadCaseModel(entry.Key.ToString(), entry.Value); model.LoadCases.Add(c); read.Add((++primary, c)); }
                        }
                        if (!movingOnly)
                            for (int i = 0; i < model.LoadCombinations.Count; i++) if (names.Contains(model.LoadCombinations[i].Name)) read.Add((primary + i + 1, model.LoadCombinations[i]));
                    }
                    else if (solver == 1)
                    {
                        read.AddRange(selection.Select(e => (e.Increment, e.Case)));
                    }
                    else
                    {
                        Check(St7.St7SetNLAStagedAnalysis(uid, St7.btTrue), "St7SetNLAStagedAnalysis");
                        read.AddRange(selection.Select(e => (e.Increment, e.Case)));
                    }
                    if (read.Count == 0) throw new InvalidOperationException("Nessun caso di risultato da leggere.");
                    Check(St7.St7SaveFile(uid), "St7SaveFile");
                    string resultPath = Path.ChangeExtension(outputPath, solver == 0 ? "lsa" : "nla");
                    if (File.Exists(resultPath)) File.Delete(resultPath);
                    Check(St7.St7RunSolver(uid, solver == 0 ? St7.stLinearStatic : St7.stNonlinearStatic, St7.smNormalCloseRun, St7.btTrue), "St7RunSolver");
                    int countPrimary = 0, countSecondary = 0;
                    Check(St7.St7OpenResultFile(uid, resultPath, null, St7.btTrue, ref countPrimary, ref countSecondary), "St7OpenResultFile"); resultOpen = true;
                    if (solver == 0 && countPrimary != primary) throw new InvalidOperationException($"Mappatura risultati inattesa: {countPrimary} casi primari, attesi {primary}. Nessun risultato associato agli elementi.");
                    if (solver != 0)
                    {
                        int expected = StrausResultSelection.Catalog(model, solver).Count;
                        if (countPrimary != expected)
                            throw new InvalidOperationException($"Analisi NLA incompleta: {countPrimary} incrementi salvati, attesi {expected}.");
                        // A later selected increment may depend on an earlier failed step.
                        // Validate the entire history before associating any results.
                        var stages = model.StagedConstruction.SelectMany((s, i) => s.LoadCombinations.Select(_ => i + 1)).ToArray();
                        for (int index = 1; index <= countPrimary; index++)
                        {
                            byte converged = St7.btFalse;
                            Check(St7.St7GetResultCaseConvergence(uid, index, ref converged), "St7GetResultCaseConvergence");
                            if (converged == St7.btFalse) throw new InvalidOperationException($"Incremento NLA {index} non convergente: risultati non accettati.");
                            int actualStage = 0;
                            Check(St7.St7GetResultCaseStage(uid, index, ref actualStage), "St7GetResultCaseStage");
                            int expectedStage = solver == 2 ? stages[index - 1] : 0;
                            if (actualStage != expectedStage)
                                throw new InvalidOperationException($"Incremento {index}: fase Straus {actualStage}, attesa {expectedStage}.");
                        }
                    }
                    foreach (var item in read)
                    {
                        if (item.Index > countPrimary + countSecondary) throw new InvalidOperationException("Indice risultato oltre i casi disponibili.");
                        StrausResultsReader.Read(uid, item.Index, item.Case, model);
                    }
                    if (string.IsNullOrEmpty(model.Name)) model.Name = Path.GetFileNameWithoutExtension(outputPath);
                    return model;
                }
                finally
                {
                    if (resultOpen) St7.St7CloseResultFile(uid);
                    if (fileOpen) St7.St7CloseFile(uid);
                    ReleaseModelId(uid);
                }
            }
        }
    }
}

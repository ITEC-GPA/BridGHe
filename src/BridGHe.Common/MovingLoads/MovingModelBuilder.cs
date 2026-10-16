using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.MovingLoads
{
    public static class MovingModelBuilder
    {
        public static ModelModel ReplacePaths(ModelModel input, IEnumerable<MovingPath> paths)
        {
            var model = ModelArchive.Clone(input);
            var generated = new HashSet<string>(model.GeneratedMovingCases.Values, StringComparer.Ordinal);
            if (model.LoadCombinations.Any(c => c.Values.Keys.Any(k => generated.Contains(k.Name))))
                throw new InvalidOperationException("Le combinazioni contengono casi mobili generati dalla precedente analisi. Rimuoverli prima di sostituire i percorsi.");
            model.LoadCases.RemoveAll(c => generated.Contains(c.Name));
            foreach (var element in model.Elements)
                foreach (var result in element.Results.Where(r => generated.Contains(r.LoadCase.Name)).ToArray())
                    element.Results.Remove(result);
            model.MovingPaths.Clear();
            model.GeneratedMovingCases.Clear();
            foreach (var path in paths)
            {
                path.Validate();
                if (!model.LoadCases.Any(c => c.Name == path.LoadCaseName))
                    model.LoadCases.Add(new LoadCaseModel("", path.LoadCaseName));
                model.MovingPaths.Add((MovingPath)path.Clone());
            }
            return model;
        }
    }
}

using BridGHe.Common.Models;
using GPC.Utilities.Units;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Common.Helpers
{
    public static class ModelHelper
    {
        /// <summary>
        /// Setup the conversion parameter for converting measures through the Utilities library
        /// </summary>
        /// <param name="units">The model units</param>
        /// <param name="lengthUnits">The output lenght conversion param</param>
        /// <param name="forceUnits">The output force conversion param</param>
        /// <param name="massUnits">The output mass conversion param</param>
        /// <param name="pressureUnits">The output pressure conversion param</param>
        /// <param name="temperatureUnits">The output temperature conversion param</param>
        public static void GetUtilitiesConversionParams(
            ModelModel.ModelUnits units,
            out UnitsConvert.LengthUnits lengthUnits,
            out UnitsConvert.ForceUnits forceUnits,
            out UnitsConvert.MassUnits massUnits,
            out UnitsConvert.PressureUnits pressureUnits,
            out UnitsConvert.TemperatureUnits temperatureUnits)
        {
            switch (units)
            {
                case ModelModel.ModelUnits.Knm:
                    lengthUnits = UnitsConvert.LengthUnits.m;
                    forceUnits = UnitsConvert.ForceUnits.kN;
                    massUnits = UnitsConvert.MassUnits.ton;
                    pressureUnits = UnitsConvert.PressureUnits.kPa;
                    temperatureUnits = UnitsConvert.TemperatureUnits.C;
                    break;
                case ModelModel.ModelUnits.Nmm:
                    lengthUnits = UnitsConvert.LengthUnits.mm;
                    forceUnits = UnitsConvert.ForceUnits.N;
                    massUnits = UnitsConvert.MassUnits.ton;
                    pressureUnits = UnitsConvert.PressureUnits.MPa;
                    temperatureUnits = UnitsConvert.TemperatureUnits.C;
                    break;
                case ModelModel.ModelUnits.SI:
                    lengthUnits = UnitsConvert.LengthUnits.m;
                    forceUnits = UnitsConvert.ForceUnits.N;
                    massUnits = UnitsConvert.MassUnits.kg;
                    pressureUnits = UnitsConvert.PressureUnits.Pa;
                    temperatureUnits = UnitsConvert.TemperatureUnits.C;
                    break;
                case ModelModel.ModelUnits.IPS:
                    lengthUnits = UnitsConvert.LengthUnits.inch;
                    forceUnits = UnitsConvert.ForceUnits.lbf;
                    massUnits = UnitsConvert.MassUnits.lb;
                    pressureUnits = UnitsConvert.PressureUnits.psi;
                    temperatureUnits = UnitsConvert.TemperatureUnits.F;
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        public static void GetLoadCases(ModelModel model, out List<LoadCaseModel> loadCases)
        {
            loadCases = new List<LoadCaseModel>();// { LoadCaseModel.Dead };
            var generatedNames = new HashSet<string>(model.GeneratedMovingCases.Values);
            var candidates = model.LoadCases.Where(c => !generatedNames.Contains(c.Name))
                .Concat(model.Elements.SelectMany(e => e.Loads).Select(l => l.LoadCase))
                .Concat(model.LoadCombinations.SelectMany(c => c.Values.Keys))
                .Concat(model.StagedConstruction.SelectMany(s => s.LoadCombinations).SelectMany(c => c.Values.Keys)).ToList();
            foreach (var candidate in candidates)
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.Name))
                    throw new ArgumentException("Caso di carico nullo o senza nome.");
            foreach (var group in candidates.GroupBy(c => c.Name, StringComparer.Ordinal))
            {
                var first = group.First();
                if (group.Any(c => !SameLoadDefinition(first, c)))
                    throw new ArgumentException("Definizioni incompatibili per il caso di carico " + group.Key);
                loadCases.Add(first);
            }
            foreach (var path in model.MovingPaths)
                if (!loadCases.Any(c => c.Name == path.LoadCaseName))
                    loadCases.Add(new LoadCaseModel("", path.LoadCaseName));
            AddDeadLoadCase(ref loadCases);
            loadCases.Sort();
        }


        private static bool SameLoadDefinition(LoadCaseModel a, LoadCaseModel b) =>
            a.Temperature == b.Temperature && a.StructuralMassAcceleration == b.StructuralMassAcceleration &&
            a.NonStructuralMassAcceleration == b.NonStructuralMassAcceleration && a.GlobalInertiaLoad == b.GlobalInertiaLoad &&
            a.GravityDirection == b.GravityDirection && a.GravityValue == b.GravityValue &&
            a.A == b.A && a.V == b.V && a.W == b.W && a.Origin == b.Origin;

        public static void AddDeadLoadCase(ref List<LoadCaseModel> loadCases)
        {
            //To define stages, dead load case can be already present in the list, and must have right values. If not is added. 
            LoadCaseModel deadCase = loadCases.FirstOrDefault(lc => new LoadCaseModelsEqualityComparer().Equals(lc, LoadCaseModel.Dead));
            if (deadCase == null)
            {
                //add DeadLoadCase
                loadCases.Add(new LoadCaseModel(LoadCaseModel.Dead));
            }
            else if (deadCase.Name != LoadCaseModel.Dead.Name ||
                     deadCase.Id != LoadCaseModel.Dead.Id ||
                     deadCase.GlobalInertiaLoad != LoadCaseModel.Dead.GlobalInertiaLoad ||
                     deadCase.GravityDirection != LoadCaseModel.Dead.GravityDirection ||
                     deadCase.GravityValue != LoadCaseModel.Dead.GravityValue ||
                     deadCase.StructuralMassAcceleration != LoadCaseModel.Dead.StructuralMassAcceleration ||
                     deadCase.NonStructuralMassAcceleration != LoadCaseModel.Dead.NonStructuralMassAcceleration)
            {
                //NB: stages not checked
                throw new NotSupportedException("Dead load case error");
            }
        }




    }
}

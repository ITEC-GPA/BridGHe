using System;
using System.Linq;
using BridGHe.Common.Checks;
using GPC.Model.Materials;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Cement = GPC.Model.Materials.ConcreteMaterialEuropeanCommon.CementTypes;

internal static class ConcreteShrinkageTests
{
    internal static void Run()
    {
        int checks = 0;
        void Near(double actual, double expected)
        {
            if (Math.Abs(actual - expected) > 1e-12 * Math.Max(1, Math.Abs(expected))) throw new Exception($"Shrinkage {actual:R} != {expected:R}");
            checks++;
        }
        ConcreteShrinkageResult Calc(double h0, double age = double.PositiveInfinity, double reference = 0, double rh = 80, Cement cement = Cement.ClassN)
            => ConcreteShrinkage.Calculate(30, cement, rh, h0 * 1000, 2000, age, 7, reference);
        // Independent scalar references from the JRC annex formulas; GPC strengths are signed.
        Near(Calc(100, cement: Cement.ClassS).Total, -.00026577029797417693);
        Near(Calc(100).Total, -.00031895281715993315);
        Near(Calc(100, cement: Cement.ClassR).Total, -.0004224929218062434);
        foreach (var (h, kh) in new[] { (50d, 1d), (100d, 1d), (150d, .925), (200d, .85), (250d, .80), (300d, .75), (400d, .725), (500d, .7), (800d, .7) })
        { Near(Calc(h).Kh, kh); Near(Calc(h).Drying, -.0002689528171599331 * kh); }
        Near(Calc(100, rh: 100).Drying, 0); Near(Calc(100, rh: 100).Autogenous, -50e-6);
        Near(Calc(100, 0).Total, 0); Near(Calc(100, 7).Drying, 0);
        Near(Calc(100, 28, 28).Increment, 0);
        Near(Calc(100, 365, 7).Increment, Calc(100, 28, 7).Increment + Calc(100, 365, 28).Increment);
        Near(ConcreteShrinkage.Calculate(32, Cement.ClassN, 70, 750000, 5000, 365, 7, 7).Increment, -.000198939016744924);
        foreach (double invalid in new[] { -1d, 20, 101, double.NaN })
        { bool rejected = false; try { Calc(100, rh: invalid); } catch (ArgumentException) { rejected = true; } if (!rejected) throw new Exception("Invalid RH accepted"); checks++; }
        bool chronology = false; try { Calc(100, 7, 28); } catch (ArgumentException) { chronology = true; }
        if (!chronology) throw new Exception("Reverse chronology accepted"); checks++;

        var plugin = BridgeMergeTests.Initialize();
        using var doc = new GH_Document();
        var component = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "CreepAndShrinkageConcreteComponent"));
        component.CreateAttributes(); doc.AddObject(component, false);
        void Number(int i, double x) { var p = (Param_Number)component.Params.Input[i]; p.PersistentData.Clear(); p.SetPersistentData(x); }
        Number(0, 32); Number(2, 750000); Number(3, 70); Number(4, 5000); Number(6, 365); Number(8, 7); Number(9, 7);
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        if (component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count != 0) throw new Exception("Creep component failed");
        Near((double)component.Params.Output[3].VolatileData.AllData(true).Single().ScriptVariable(), -.000198939016744924);
        if (component.Params.Output.Any(p => p.VolatileDataCount != 1)) throw new Exception("Missing time-effect output"); checks++;
        Number(3, 101); component.ExpireSolution(false); doc.NewSolution(false, GH_SolutionMode.Silent);
        if (component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0) throw new Exception("Invalid input did not turn component red"); checks++;
        Console.WriteLine($"CONCRETE SHRINKAGE PASS: {checks} checks; three cements, kh boundaries, signed strengths, time increments and GH integration.");
    }
}

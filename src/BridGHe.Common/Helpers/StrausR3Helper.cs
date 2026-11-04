namespace BridGHe.Common.Helpers
{
    /// <summary>F6A: the former Straus7 exporter of BridGHe is removed (the plug-in writes, solves and reads Straus7 through the GPC
    /// Converter). Only the solver names remain here, because the registration of the analysis component refers to them.</summary>
    public static class StrausR3Helper
    {
        public static string[] Solvers => Straus.StrausAnalysis.Solvers;
    }
}

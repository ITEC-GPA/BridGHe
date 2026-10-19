namespace BridGHe.Grasshopper
{
    public sealed class Settings
    {
        public static Settings Instance { get; } = new();
        private Settings() { }
        public double Tolerance => global::Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
    }
}

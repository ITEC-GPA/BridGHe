using System;
using System.Text.Json;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Display
{
    public sealed record DisplayOptions
    {
        public bool Enabled { get; init; } = true;
        public bool Automatic { get; init; } = true;
        public bool Geometry { get; init; } = true;
        public bool Sections { get; init; } = true;
        public bool Shaded { get; init; } = true;
        public bool Reinforcement { get; init; } = true;
        public bool Loads { get; init; } = true;
        public bool Supports { get; init; } = true;
        public bool Releases { get; init; } = true;
        public bool OtherAttributes { get; init; } = true;
        public bool Axes { get; init; }
        public bool Labels { get; init; }
        public double SymbolScale { get; init; } = 1;
        public double LoadScale { get; init; } = 1;
        public double Transparency { get; init; } = 0.65;
        public string LoadCase { get; init; } = "";
        public void Validate()
        {
            if (!double.IsFinite(SymbolScale) || SymbolScale <= 0 || !double.IsFinite(LoadScale) || LoadScale <= 0)
                throw new ArgumentException("Le scale devono essere finite e positive.");
            if (!double.IsFinite(Transparency) || Transparency < 0 || Transparency > 1)
                throw new ArgumentException("La trasparenza deve essere compresa tra 0 e 1.");
        }
        public bool Matches(string name) => string.IsNullOrWhiteSpace(LoadCase) || string.Equals(name, LoadCase.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static class DisplayPreferences
    {
        private static readonly GH_SettingsServer server = new("BridGHe",true);
        public static DisplayOptions Current { get; private set; } = new();
        public static event Action Changed;
        public static void Set(DisplayOptions options, bool save = true)
        {
            options.Validate(); Current = options;
            if (save) { server.SetValue("Display",JsonSerializer.Serialize(options)); server.WritePersistentSettings(); }
            Changed?.Invoke(); Rhino.RhinoDoc.ActiveDoc?.Views.Redraw(); global::Grasshopper.Instances.ActiveCanvas?.Refresh();
        }
        public static void Load()
        {
            try { var json=server.GetValue("Display", ""); Set(json.Length>0 ? JsonSerializer.Deserialize<DisplayOptions>(json) : new DisplayOptions { Sections=server.GetValue("ShowSolids",true), Loads=server.GetValue("ShowAttributes",true), Supports=server.GetValue("ShowAttributes",true) },false); }
            catch { Current=new(); }
        }
    }
}

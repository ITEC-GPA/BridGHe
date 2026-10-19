using System;
using System.Drawing;

namespace BridGHe.Grasshopper.Properties
{
    /// <summary>One named, embedded 24 px asset for each registered component/parameter.
    /// Shared cache avoids allocating a bitmap for every object on the canvas.</summary>
    internal static class ComponentIcons
    {
        public static Bitmap For(Type componentType) => Resources.Load("Icons." + componentType.Name);
        public static Bitmap Plugin => Resources.Load("Icons.BridGHe");
        public static Bitmap ModelCategory => Resources.Load("Icons.BridGHeCategory");
    }
}

using System;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper
{
    public sealed class BridGHeInfo : GH_AssemblyInfo
    {
        public override System.Drawing.Bitmap Icon => Properties.ComponentIcons.Plugin;
        public override string Name => "BridGHe";
        public override string Description => "Modellazione parametrica, analisi Straus7 R3 e verifiche con librerie gPC.";
        public override Guid Id => new Guid("db9c536d-770b-4586-8c8c-1c7a4df232b4");
        public override string AuthorName => "gPC";
        public override string AuthorContact => "";
    }

    public sealed class BridGHeAssemblyPriority : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            var server = global::Grasshopper.Instances.ComponentServer;
            server.AddCategoryIcon(Helpers.CategoryNameConstants.CATEGORY_MODEL, Properties.ComponentIcons.ModelCategory);
            server.AddCategoryShortName(Helpers.CategoryNameConstants.CATEGORY_MODEL, "BGH");
            server.AddCategorySymbolName(Helpers.CategoryNameConstants.CATEGORY_MODEL, 'B');
            Display.DisplayMenu.Initialize();
            Helpers.GpcReleaseGuard.Start();
            return GH_LoadingInstruction.Proceed;
        }
    }
}

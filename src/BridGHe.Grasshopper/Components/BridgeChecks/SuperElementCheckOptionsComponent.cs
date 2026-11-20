using BridGHe.Common.Checks;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    /// <summary>The options of the Super Element Check (user decision of 20/11/2026): the inputs that were optional on the check (the three local
    /// instability toggles, Bridge Analysis Options, ULS Combinations) and the number of analysis stations per element, by default the 2 nodes.
    /// The output goes to the Options input of Super Element Check.</summary>
    public class SuperElementCheckOptionsComponent : GH_Component
    {
        public SuperElementCheckOptionsComponent()
          : base("Super Element Check Options", "SECOpt", "Options of Super Element Check: local instabilities, Bridge Analysis Options, ULS combinations and analysis stations per element.",
              CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_DESIGN) { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Web Local Instability", "WLI", "If true, the web is subjected to local instability. Not used when Bridge Analysis Options are connected.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Top Flange Local Instability", "TFLI", "If true, the top flange is subjected to local instability. Not used when Bridge Analysis Options are connected.", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Bottom Flange Local Instability", "BLI", "If true, the bottom flange is subjected to local instability. Not used when Bridge Analysis Options are connected.", GH_ParamAccess.item, true);
            pManager.AddGenericParameter("Analysis Options", "AO", "Optional Bridge Analysis Options. When connected they replace the defaults (Eurocode 4, rare SLS, Class 4, default partial factors) and the three local instability toggles.", GH_ParamAccess.item);
            pManager[3].Optional = true;
            pManager.AddTextParameter("ULS Combinations", "ULS", "Optional combinations checked at the ultimate limit state: exact names, or a prefix ending with * (e.g. SLU_*). The other combinations keep the limit state of the options (rare SLS by default).", GH_ParamAccess.list);
            pManager[4].Optional = true;
            pManager.AddIntegerParameter("Stations per Element", "St", "Analysis stations per element among the FEM output stations, evenly spread: 2 = the two nodes (default; with 1 m elements one check per metre on both sides of every node), 3 = nodes and middle, 0 = all.", GH_ParamAccess.item, BridgeCheckStations.DefaultPerElement);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Options", "O", "Super Element Check Options, to the Options input of Super Element Check.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool web = true, top = true, bottom = true; int stations = BridgeCheckStations.DefaultPerElement;
            DA.GetData(0, ref web); DA.GetData(1, ref top); DA.GetData(2, ref bottom);
            BridgeDetailGoo optionsGoo = null; GPC.Checkers.CompositeBridge.BridgeAnalysisOptions analysis = null;
            if (DA.GetData(3, ref optionsGoo) && optionsGoo != null)
            {
                analysis = optionsGoo.Value as GPC.Checkers.CompositeBridge.BridgeAnalysisOptions;
                if (analysis == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Analysis Options richiede Bridge Analysis Options."); return; }
            }
            var uls = new List<string>(); DA.GetDataList(4, uls);
            DA.GetData(5, ref stations);
            if (stations < 0 || stations == 1) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Stations per Element: 0 (tutte) oppure almeno 2."); return; }
            if (analysis != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Analysis Options collegate: valgono le loro scelte (norma, stato limite, Class4, coefficienti e instabilità locale); WLI, TFLI e BLI non sono usati.");
            var settings = new SuperElementCheckSettings
            {
                WebBuckling = web, TopFlangeBuckling = top, BottomFlangeBuckling = bottom, AnalysisOptions = analysis,
                UlsCombinations = uls.Where(u => !string.IsNullOrWhiteSpace(u)).ToArray(), StationsPerElement = stations
            };
            Message = stations == 0 ? "tutte le stazioni" : $"{stations} stazioni";
            DA.SetData(0, new BridgeDetailGoo(settings));
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
        public override Guid ComponentGuid => new Guid("3f7c2a91-5d84-4e6b-9a1f-0c2e8b7d4a63");
    }
}

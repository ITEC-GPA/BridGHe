using System;
using System.Collections.Generic;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_PlateResults : GH_Goo<object>
    {
        #region Properties

        // F6 C4: valid with FEM results or with the analysed GPC element (the Results components read it first).
        public override bool IsValid => AnalysedGpc != null; // P2CE phase 2b: no FEM results (an empty list before)

        public override string IsValidWhyNot => "Invalid or empty plate results";

        public override string TypeName => "Plate Result";

        public override string TypeDescription => "The plate" +
            " analysis results";

        #endregion

        #region Constructors

        public GH_PlateResults()
        {
        }

        public GH_PlateResults(GH_Plate plate)
            : this()
        {
            // R6.5 P0: an element without FEM value has no FEM results (its analysed GPC element is read).
            Value = null;
            AnalysedGpc = plate.AnalysedGpc; AnalysedGpcModel = plate.AnalysedGpcModel;
        }

        /// <summary>F6 C4: the analysed GPC element of this element and its analysed model, set by Decompose Model when its model was
        /// analysed (Straus R3 / Moving Static Analysis); null otherwise. Immutable by convention, so copies share it.</summary>
        public GPC.Model.Elements.Element AnalysedGpc { get; set; }
        public GPC.Model.Models.Model AnalysedGpcModel { get; set; }


        #endregion

        #region IGH_Goo implementation functions

        public override IGH_Goo Duplicate()
        {
            return new GH_PlateResults
            {
                Value = null,
                AnalysedGpc = AnalysedGpc, AnalysedGpcModel = AnalysedGpcModel,
            };
        }

        public override string ToString()
        {
            return "Null Plate results";
        }

        #endregion
    }
}

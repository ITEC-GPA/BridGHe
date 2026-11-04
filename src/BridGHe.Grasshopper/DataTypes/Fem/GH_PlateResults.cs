using System;
using System.Collections.Generic;
using Grasshopper.Kernel.Types;
using BridGHe.Common.Models;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_PlateResults : GH_Goo<List<ResultModel>>
    {
        #region Properties

        // F6 C4: valid with FEM results or with the analysed GPC element (the Results components read it first).
        public override bool IsValid => Value != null && Value.Count > 0 || AnalysedGpc != null;

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
            Value = new List<ResultModel>(plate.Value.Results);
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
                Value = Common.Helpers.ModelArchive.Clone(Value),
                AnalysedGpc = AnalysedGpc, AnalysedGpcModel = AnalysedGpcModel,
            };
        }

        public override string ToString()
        {
            return Value.ToString();
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
using BridGHe.Common.Models;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_BeamResults : GH_Goo<List<ResultModel>>
    {
        #region Properties

        public override bool IsValid => Value != null && Value.Count > 0;

        public override string IsValidWhyNot => "Invalid or empty beam results";

        public override string TypeName => "Beam Result";

        public override string TypeDescription => "The beam analysis results";

        #endregion

        #region Constructors

        public GH_BeamResults()
        {
        }

        public GH_BeamResults(GH_Beam beam)
            : this()
        {
            Value = new List<ResultModel>(beam.Value.Results);
            AnalysedGpc = beam.AnalysedGpc; AnalysedGpcModel = beam.AnalysedGpcModel;
        }

        /// <summary>F6 C4: the analysed GPC element of this element and its analysed model, set by Decompose Model when its model was
        /// analysed (Straus R3 / Moving Static Analysis); null otherwise. Immutable by convention, so copies share it.</summary>
        public GPC.Model.Elements.Element AnalysedGpc { get; set; }
        public GPC.Model.Models.Model AnalysedGpcModel { get; set; }


        #endregion

        #region IGH_Goo implementation functions

        public override IGH_Goo Duplicate()
        {
            return new GH_BeamResults
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

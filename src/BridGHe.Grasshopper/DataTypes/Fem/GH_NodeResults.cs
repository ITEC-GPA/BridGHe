using System;
using System.Collections.Generic;
using Grasshopper.Kernel.Types;
using BridGHe.Common.Models;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_NodeResults : GH_Goo<List<ResultModel>>
    {
        #region Properties

        public override bool IsValid => Value != null && Value.Count > 0;

        public override string IsValidWhyNot => "Invalid or empty node results";

        public override string TypeName => "Node Result";

        public override string TypeDescription => "The node analysis results";

        #endregion

        #region Constructors

        public GH_NodeResults()
        {
        }

        public GH_NodeResults(GH_Node node)
            : this()
        {
            Value = new List<ResultModel>(node.Value.Results);
            AnalysedGpc = node.AnalysedGpc; AnalysedGpcModel = node.AnalysedGpcModel;
        }

        /// <summary>F6 C4: the analysed GPC element of this element and its analysed model, set by Decompose Model when its model was
        /// analysed (Straus R3 / Moving Static Analysis); null otherwise. Immutable by convention, so copies share it.</summary>
        public GPC.Model.Elements.Element AnalysedGpc { get; set; }
        public GPC.Model.Models.Model AnalysedGpcModel { get; set; }


        #endregion

        #region GH_Goo implementation functions

        public override IGH_Goo Duplicate()
        {
            return new GH_NodeResults
            {
                Value = Common.Helpers.ModelArchive.Clone(Value),
                AnalysedGpc = AnalysedGpc, AnalysedGpcModel = AnalysedGpcModel,
            };
        }

        /*public bool CastFrom(object source)
        {
            return false; ;
        }

        public bool CastTo<T>(out T target)
        {
            target = default(T);
            return false;
        }*/

        public override string ToString()
        {
            return Value.ToString();
        }

        #endregion
    }
}

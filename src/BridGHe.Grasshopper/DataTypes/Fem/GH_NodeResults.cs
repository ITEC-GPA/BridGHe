using System;
using System.Collections.Generic;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_NodeResults : GH_Goo<object>
    {
        #region Properties

        // F6 C4: valid with FEM results or with the analysed GPC element (the Results components read it first).
        public override bool IsValid => AnalysedGpc != null; // P2CE phase 2b: no FEM results (an empty list before)

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
            // R6.5 P0: an element without FEM value has no FEM results (its analysed GPC element is read).
            Value = null;
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
                Value = null,
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
            return "Null Node results";
        }

        #endregion
    }
}

using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Text.Json;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_LoadCombination : GH_Goo<LoadCombinationModel>
    {
        public List<LoadCaseModel> LoadCases => Value.Values.Keys.ToList();

        public override bool IsValid => Value.Name != string.Empty && Value.Values.Count > 0;

        public override string IsValidWhyNot => "Not propely defined";

        public override string TypeName => "Load Combination";

        public override string TypeDescription => "The load cases combination";

        public GH_LoadCombination()
        {
        }

        public GH_LoadCombination(string name)
        {
            Value = new LoadCombinationModel(name);
        }

        public override bool CastFrom(object source)
        {
            // TODO: Commentata perchè per commutare i nomi dei load case on oggetti LoadCaseModel bisogna reperirli dal 
            //  componte FemExport o FemAnalysis
            /*if (source.GetType() == typeof(GH_String))
            {
                string[] toc = source.ToString().Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries).
                    Select(s => s.Trim()).ToArray();
                if (toc.Length != 2)
                    throw new FormatException("Invalid format. Must be like \"ComboName=LC1*LI1+LC2*LI2+...\"");
                Value = new LoadCombinationModel(toc[0]);
                string[] itms = toc[1].Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
                if (itms.Length <= 0)
                    throw new FormatException("Invalid format. Must be like \"ComboName=LC1*LI1+LC2*LI2+...\"");
                foreach (string itm in itms)
                {
                    string[] parts = itm.Trim().Split(new[] { '*' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2)
                        throw new FormatException("Invalid format. Must be like \"ComboName=LC1*LI1+LC2*LI2+...\"");
                    _loadCases.Add(parts[0].Trim());
                    if (!double.TryParse(parts[1].Trim(), out double f))
                        throw new InvalidCastException("Invalid load increment format");
                    _increments.Add(f);
                }
                return true;
            }*/
            return false;
        }

        public override bool CastTo<Q>(ref Q target)
        {
            target = default;
            return false;
        }

        /// <summary>
        /// R6.1 (frozen API for R6.2, §1bis): the GPC datum of this GH_LoadCombination, the combination by value (name, case records and factors in order): the one a GPC builder attached
        /// (<see cref="AttachGpc"/>), the one read from a file (BGHGPC1), the former payload read by value
        /// (<see cref="Common.Gpc.GpcLegacyPayloads"/>), otherwise the conversion of the FEM value, once per value. Immutable by convention.
        /// </summary>
        public Common.Gpc.Legacy.LegacyCombinationDraft Gpc
        {
            get
            {
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                if (_legacy != null && ReferenceEquals(_legacyOf, Value)) { _gpc = Common.Gpc.GpcLegacyPayloads.Combination(_legacy); _gpcOf = Value; HasAttachedGpc = false; return _gpc; }
                if (Value == null) return null;
                _gpc = Common.Gpc.GpcModels.CombinationDraft(Value); _gpcOf = Value; HasAttachedGpc = false;
                return _gpc;
            }
        }

        /// <summary>True when <see cref="Gpc"/> was attached by a GPC builder or read from a file (not converted from the FEM value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC datum built on GPC for the current FEM value (it must describe it: the gates compare the two, <see cref="Common.Gpc.GpcDraftCheck"/>).</summary>
        public void AttachGpc(Common.Gpc.Legacy.LegacyCombinationDraft datum)
        {
            _gpc = datum ?? throw new ArgumentNullException(nameof(datum)); _gpcOf = Value; HasAttachedGpc = true;
            if (Value != null) { var value = Value; Common.Gpc.GpcDraftCheck.Attached("GH_LoadCombination", datum, () => Common.Gpc.GpcModels.CombinationDraft(value)); }
        }

        private Common.Gpc.Legacy.LegacyCombinationDraft _gpc;
        private LoadCombinationModel _gpcOf;

        /// <summary>R6.1 (frozen API for R6.2): whoever changes the FEM value of this datum in place (Load Combinations parameter, UpdateLoadCases)
        /// calls this: the datum attached, read or read by value no longer describes it and is dropped (the conversion follows the value).</summary>
        public void ValueChanged() { _gpc = null; _gpcOf = null; _legacy = null; _legacyOf = null; HasAttachedGpc = false; }
        private byte[] _legacy;
        private LoadCombinationModel _legacyOf;

        /// <summary>R6.1: a copy (<c>Duplicate</c>) keeps the datum attached or read and the former payload, for its copy of the same value.</summary>
        private void CarryGpcTo(GH_LoadCombination copy)
        {
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
            if (_legacy != null && ReferenceEquals(_legacyOf, Value)) { copy._legacy = _legacy; copy._legacyOf = copy.Value; }
        }

        public override IGH_Goo Duplicate()
        {
            var copy = DuplicateValue();
            CarryGpcTo(copy);
            return copy;
        }

        private GH_LoadCombination DuplicateValue()
        {
            GH_LoadCombination value = new GH_LoadCombination(Value.Name);
            foreach (KeyValuePair<LoadCaseModel, double> item in Value.Values)
                value.Value.Values.Add(item.Key, item.Value);
            return value;
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                switch (GpcGooPersistence.Read(reader, out var data, out var records, out var femm))
                {
                    case GpcGooPersistence.Kind.Gpc:
                        var datum = Common.Gpc.GpcDraftArchive.ReadCombination(records);
                        Value = femm == null ? null : Common.Helpers.ModelArchive.Read<LoadCombinationModel>(femm);
                        _gpc = datum; _gpcOf = Value; HasAttachedGpc = true; return true;
                    case GpcGooPersistence.Kind.Legacy:
                        Value = Common.Helpers.ModelArchive.Read<LoadCombinationModel>(femm); _legacy = femm; _legacyOf = Value; return true;
                    case GpcGooPersistence.Kind.FemmOnly:
                        Value = Common.Helpers.ModelArchive.Read<LoadCombinationModel>(femm); return true;
                    default: return false;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Write(GH_IWriter writer)
        {
            try { GpcGooPersistence.Write(writer, () => ((byte[])null, Common.Gpc.GpcDraftArchive.Write(Gpc)), Value == null ? null : Common.Helpers.ModelArchive.Write(Value)); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            string str = $"{Value.Name} = ";

            bool first = true;
            foreach (KeyValuePair<LoadCaseModel, double> item in Value.Values)
            {
                if (!first)
                    str += " + ";
                str += $"{item.Key.Name}*{item.Value}";
                first = false;
            }

            return str;
        }
    }
}

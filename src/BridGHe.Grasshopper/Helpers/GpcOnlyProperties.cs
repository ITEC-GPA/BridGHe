using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Grasshopper.DataTypes.Fem;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// R6.5 P1, lane 2a (passi 75–76): properties and data given by their GPC data alone, with no FEM value: sections, beam and plate
    /// properties, materials, load cases, combinations, stages and groups.
    /// <list type="bullet">
    /// <item>A producer gives its output by the GPC datum alone with <see cref="GpcOnlyElements.PropertiesWithoutFem"/> (the path that stays in
    /// P2; on only in the lane test <c>gpc-only-properties</c>) and whenever an input of these kinds is given by its GPC datum alone.</item>
    /// <item>The rule of passo 75 for every component that makes or changes an output: an input of these kinds given by its GPC datum alone
    /// gives an output by its GPC datum alone (an element output too: <see cref="GpcOnlyElements"/>); no FEM value is made from a null
    /// <c>Value</c>. R65-C2-04 (review of lane 2): the selectors and filters (Beam, Node and Plate Filter by group) pass their input objects
    /// through unchanged (an input with a FEM value stays one).</item>
    /// <item>The datum is the one the GPC builder of the component builds, the one the dual rule of R6.2 attaches to the FEM value
    /// (<see cref="GpcBuilderAttach"/>). A builder that refuses gives an error on the component and no output: there is no conversion of a FEM
    /// value to fall back on (decision 2 of passo 73).</item>
    /// <item>Identity: properties and data have no GUIDs of their own (a beam property is known by its signature, the GPC fingerprint); the
    /// elements built from them keep the rules of lanes 1a and 1b (<see cref="GpcOnlyElements.Identity"/>,
    /// <see cref="GpcOnlyElements.ContentIdentity"/>).</item>
    /// </list>
    /// With the inputs of today and the switch off nothing changes. Removed in P2 with the FEM path (this path then is the only one).
    /// </summary>
    internal static class GpcOnlyProperties
    {
        /// <summary>True when a producer gives its output by its GPC datum alone: the switch is on, or an input of the lane kinds (a goo or a
        /// list of them) has no FEM value.</summary>
        internal static bool On(params object[] inputs) => GpcOnlyElements.PropertiesWithoutFem || inputs.Any(WithoutFem);

        /// <summary>An input of the lane kinds given by its GPC datum alone (no FEM value), or a list with one.</summary>
        internal static bool WithoutFem(object input) => input switch
        {
            null => false,
            GH_BeamSection s => s.Value == null,
            GH_BeamProperty p => p.Value == null,
            GH_PlateProperty p => p.Value == null,
            GH_Material m => m.Value == null,
            GH_LoadCase c => c.Value == null,
            GH_LoadCombination c => c.Value == null,
            GH_StagedConstruction s => s.Value == null,
            GH_Group g => g.Value == null,
            // R6.5 P1 lane 2b: the composite properties and the coordinate systems of the loads.
            DataTypes.GH_CompositeBeamProperty c => c.Value == null,
            GH_CoordinateSystem c => c.Value == null,
            string _ => false,
            IEnumerable list => list.Cast<object>().Any(WithoutFem),
            _ => false,
        };

        /// <summary>The «Non-structural mass» flag of a load case, from its record when it has no FEM value (the warnings of the load
        /// components).</summary>
        internal static bool NonStructuralMass(GH_LoadCase lc) => lc.Value != null ? lc.Value.NonStructuralMassAcceleration : lc.Gpc.NonStructuralMass;

        /// <summary>For a beam property given by its GPC datum alone: whether it is a valid Beam or Truss property (the check of the deformable
        /// connections, <c>BridgeConnections.Create</c>), read from the datum; null for a property with a FEM value (the check reads it).</summary>
        internal static bool? BeamOrTrussWithoutFem(GH_BeamProperty p) => !WithoutFem(p) ? null
            : p.Gpc?.Property != null && p.Gpc.Data?.Formulation is GPC.Model.Structure.Assignments.BeamFormulation.StraightTwoNode or GPC.Model.Structure.Assignments.BeamFormulation.Truss;

        /// <summary>P2K (P1): whether a beam property is a valid Beam or Truss property (the check of the deformable connections,
        /// <c>BridgeConnections.Check</c>), read from its GPC datum (false without a property or a datum).</summary>
        internal static bool BeamOrTruss(GH_BeamProperty p) => p?.Gpc?.Property != null
            && p.Gpc.Data?.Formulation is GPC.Model.Structure.Assignments.BeamFormulation.StraightTwoNode or GPC.Model.Structure.Assignments.BeamFormulation.Truss;

        /// <summary>P2K (P1): the units of <c>GPC.Utilities.Units.UnitsConvert</c> of the definition units of a component menu (Material Custom,
        /// Beam Section Param): the table of the FEM <c>ModelHelper.GetUtilitiesConversionParams</c> (same integers as the former
        /// <c>ModelUnits</c>), without the FEM type.</summary>
        internal static void UtilitiesUnits(Common.Gpc.DefinitionUnits units, out GPC.Utilities.Units.UnitsConvert.LengthUnits length,
            out GPC.Utilities.Units.UnitsConvert.ForceUnits force, out GPC.Utilities.Units.UnitsConvert.MassUnits mass,
            out GPC.Utilities.Units.UnitsConvert.PressureUnits pressure, out GPC.Utilities.Units.UnitsConvert.TemperatureUnits temperature)
        {
            switch (units)
            {
                case Common.Gpc.DefinitionUnits.Knm:
                    length = GPC.Utilities.Units.UnitsConvert.LengthUnits.m; force = GPC.Utilities.Units.UnitsConvert.ForceUnits.kN; mass = GPC.Utilities.Units.UnitsConvert.MassUnits.ton;
                    pressure = GPC.Utilities.Units.UnitsConvert.PressureUnits.kPa; temperature = GPC.Utilities.Units.UnitsConvert.TemperatureUnits.C; break;
                case Common.Gpc.DefinitionUnits.Nmm:
                    length = GPC.Utilities.Units.UnitsConvert.LengthUnits.mm; force = GPC.Utilities.Units.UnitsConvert.ForceUnits.N; mass = GPC.Utilities.Units.UnitsConvert.MassUnits.ton;
                    pressure = GPC.Utilities.Units.UnitsConvert.PressureUnits.MPa; temperature = GPC.Utilities.Units.UnitsConvert.TemperatureUnits.C; break;
                case Common.Gpc.DefinitionUnits.SI:
                    length = GPC.Utilities.Units.UnitsConvert.LengthUnits.m; force = GPC.Utilities.Units.UnitsConvert.ForceUnits.N; mass = GPC.Utilities.Units.UnitsConvert.MassUnits.kg;
                    pressure = GPC.Utilities.Units.UnitsConvert.PressureUnits.Pa; temperature = GPC.Utilities.Units.UnitsConvert.TemperatureUnits.C; break;
                case Common.Gpc.DefinitionUnits.IPS:
                    length = GPC.Utilities.Units.UnitsConvert.LengthUnits.inch; force = GPC.Utilities.Units.UnitsConvert.ForceUnits.lbf; mass = GPC.Utilities.Units.UnitsConvert.MassUnits.lb;
                    pressure = GPC.Utilities.Units.UnitsConvert.PressureUnits.psi; temperature = GPC.Utilities.Units.UnitsConvert.TemperatureUnits.F; break;
                default: throw new NotSupportedException();
            }
        }

        /// <summary>The datum of a builder, or null after an error on the component (a refusal of the reader or of a builder).</summary>
        internal static T Datum<T>(GH_Component component, Func<T> build) where T : class
        {
            try { return build() ?? throw new ArgumentException("no datum"); }
            catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, Refused + x.Message);
                return null;
            }
        }

        internal const string Refused = "The GPC datum of the output is refused: ";

        internal static GH_BeamSection Section(GH_Component c, Func<LegacySectionRecipe> build) => Datum(c, build) is { } d ? GH_BeamSection.FromGpc(d) : null;
        internal static GH_BeamProperty BeamProperty(GH_Component c, Func<LegacyBeamProperty> build) => Datum(c, build) is { } d ? GH_BeamProperty.FromGpc(d) : null;
        internal static GH_PlateProperty PlateProperty(GH_Component c, Func<GPC.Model.ElementProperties.PlateProperty> build, double membraneBending = 0) =>
            Datum(c, build) is { } d ? GH_PlateProperty.FromGpc(d).WithMembraneBending(membraneBending) : null; // L2C: with the bending of a membrane
        internal static GH_Material Material(GH_Component c, Func<GPC.Model.Materials.Material> build) => Datum(c, build) is { } d ? GH_Material.FromGpc(d) : null;
        internal static GH_LoadCase Case(GH_Component c, Func<LegacyCaseData> build) => Datum(c, build) is { } d ? GH_LoadCase.FromGpc(d) : null;
        internal static GH_LoadCombination Combination(GH_Component c, Func<LegacyCombinationDraft> build) => Datum(c, build) is { } d ? GH_LoadCombination.FromGpc(d) : null;
        internal static GH_StagedConstruction Stage(GH_Component c, Func<LegacyStageDraft> build) => Datum(c, build) is { } d ? GH_StagedConstruction.FromGpc(d) : null;
        internal static GH_Group Group(GH_Component c, Func<IReadOnlyList<string>> build) => Datum(c, build) is { } d ? GH_Group.FromGpc(d) : null;
    }
}

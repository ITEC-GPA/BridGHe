using BridGHe.Common.Straus;
using BridGHe.Common.Models;
using GPC.Geometry.Meshes;
using GPC.Geometry.Meshes.DelaunayMesh;
using GPC.Utilities.Units;
using St7API;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace BridGHe.Common.Helpers
{
    public static class StrausR3Helper
    {
        public static string[] Solvers = { "Linear Static", "Nonlinear Static", "Staged analysis" };

        public static bool CreateModelR3(ModelModel model, string outputPath, double tolerance, out int modelId, out List<string> warnings, out List<string> errors)
        {
            lock (StrausRuntime.Gate) return CreateModelCore(model, outputPath, tolerance, out modelId, out warnings, out errors);
        }

        private static bool CreateModelCore(ModelModel model, string outputPath, double tolerance, out int modelId, out List<string> warnings, out List<string> errors)
        {
            string scratchPath = Path.GetTempPath();
            warnings = new List<string>();
            errors = new List<string>();
            modelId = 0;

            bool st7FileOpened = false;
            //scratchPath = Path.GetDirectoryName(outputPath) + "\\";
            try
            {
                outputPath = Path.GetFullPath(outputPath);
                modelId = StrausRuntime.AcquireModelId();

                if (!Directory.Exists(Path.GetDirectoryName(outputPath)))
                    throw new Exception("Directory does not exist");

                // Create a new model
                if (HandleError(St7.St7NewFile(modelId, outputPath, scratchPath)))
                    throw new Exception("Failed to create new model");
                else
                    st7FileOpened = true;

                // Set the measure units
                int[] st7_units = new int[St7.kLastUnit];
                // Conversion factors to convert the material data from Nmm to the model units
                ModelHelper.GetUtilitiesConversionParams(model.Units, out UnitsConvert.LengthUnits lengthUnits, out UnitsConvert.ForceUnits forceUnits,
                    out UnitsConvert.MassUnits massUnits, out UnitsConvert.PressureUnits pressureUnits, out UnitsConvert.TemperatureUnits temperatureUnits);

                switch (model.Units)
                {
                    case ModelModel.ModelUnits.Knm:
                        st7_units[St7.ipLENGTHU] = St7.luMETRE;
                        st7_units[St7.ipFORCEU] = St7.fuKILONEWTON;
                        st7_units[St7.ipSTRESSU] = St7.suKILOPASCAL;
                        st7_units[St7.ipMASSU] = St7.muTONNE;
                        st7_units[St7.ipTEMPERU] = St7.tuCELSIUS;
                        st7_units[St7.ipENERGYU] = St7.euJOULE;
                        break;
                    case ModelModel.ModelUnits.SI:
                        st7_units[St7.ipLENGTHU] = St7.luMETRE;
                        st7_units[St7.ipFORCEU] = St7.fuNEWTON;
                        st7_units[St7.ipSTRESSU] = St7.suPASCAL;
                        st7_units[St7.ipMASSU] = St7.muKILOGRAM;
                        st7_units[St7.ipTEMPERU] = St7.tuCELSIUS;
                        st7_units[St7.ipENERGYU] = St7.euJOULE;
                        break;
                    case ModelModel.ModelUnits.Nmm:
                        st7_units[St7.ipLENGTHU] = St7.luMILLIMETRE;
                        st7_units[St7.ipFORCEU] = St7.fuNEWTON;
                        st7_units[St7.ipSTRESSU] = St7.suMEGAPASCAL;
                        st7_units[St7.ipMASSU] = St7.muTONNE;
                        st7_units[St7.ipTEMPERU] = St7.tuCELSIUS;
                        st7_units[St7.ipENERGYU] = St7.euJOULE;
                        break;
                    case ModelModel.ModelUnits.IPS:
                        st7_units[St7.ipLENGTHU] = St7.luINCH;
                        st7_units[St7.ipFORCEU] = St7.fuPOUNDFORCE;
                        st7_units[St7.ipSTRESSU] = St7.suPSI;
                        st7_units[St7.ipMASSU] = St7.muPOUND;
                        st7_units[St7.ipTEMPERU] = St7.tuFAHRENHEIT;
                        st7_units[St7.ipENERGYU] = St7.euBTU;
                        break;
                    default:
                        return false;
                }
                if (HandleError(St7.St7SetUnits(modelId, st7_units)))
                    throw new Exception("Failed to set the units");

                // Groups
                GroupModel[] groups = model.Elements.SelectMany(e => e.Groups).Distinct().ToArray();
                Dictionary<GroupModel, int> groups_ids = new Dictionary<GroupModel, int>();
                for (int k = 0; k < groups.Length; k++)
                {
                    GroupModel group = groups[k];
                    int parent_id = 1;
                    if (group.Parent != null)
                    {
                        /*List<GroupModel> parents = new List<GroupModel>();
						GroupModel curr_parent = group.Parent;
						while (curr_parent != null)
						{
							parents.Insert(0, curr_parent);
							curr_parent = curr_parent.Parent;
						}*/
                        List<GroupModel> parents = GroupModel.GetBranch(group);
                        parents.RemoveAt(parents.Count - 1);
                        for (int j = 0; j < parents.Count; j++)
                        {
                            GroupModel parent = parents[j];
                            // Check in the group already exists
                            bool found = false;
                            int numGroups = 0;
                            StrausRuntime.Check(St7.St7GetNumGroups(modelId, ref numGroups), "St7GetNumGroups");
                            for (int i = 1; i <= numGroups; i++)
                            {
                                StringBuilder gname = new StringBuilder(St7.kMaxStrLen);
                                int gid = 0;
                                StrausRuntime.Check(St7.St7GetGroupByIndex(modelId, i, gname, St7.kMaxStrLen, ref gid), "St7GetGroupByIndex");
                                string sname = gname.ToString().Replace("Model\\", "");
                                if (parent.FullName == sname)
                                {
                                    found = true;
                                    parent_id = gid;
                                    break;
                                }
                            }
                            if (!found) // Create it if not existing
                            {
                                if (HandleError(St7.St7NewChildGroup(modelId, parent_id, parent.Name, ref parent_id)))
                                {
                                    errors.Add(string.Format("Failed to create parent group {0}", parent.Name));
                                    continue;
                                }
                            }
                        }
                    }
                    int group_id = 0;
                    if (HandleError(St7.St7NewChildGroup(modelId, parent_id, group.Name, ref group_id)))
                    {
                        errors.Add(string.Format("Failed to create group {0}", group.Name));
                        continue;
                    }
                    groups_ids.Add(group, group_id);
                }

                // Beam properties
                int id = 1;
                Dictionary<string, int> beamProperties = new Dictionary<string, int>();

                BeamPropertyModel[] beamProps = model.Beams.Select(b => b.BeamProperty).Distinct().ToArray();
                for (int k = 0; k < beamProps.Length; k++)
                {
                    BeamPropertyModel bp = beamProps[k];
                    if (beamProperties.ContainsKey(bp.Name))
                    {
                        warnings.Add(string.Format("The beam property {0} already exists", bp.Name));
                        continue;
                    }

                    // Property type
                    int beamType = St7.btNull;
                    if (bp.PropertyType == BeamPropertyType.Cable)
                        beamType = St7.btCable;
                    else if (bp.PropertyType == BeamPropertyType.Truss)
                        beamType = St7.btTruss;
                    else if (bp.PropertyType == BeamPropertyType.CutoffBar)
                        beamType = St7.btCutoff;
                    else if (bp.PropertyType == BeamPropertyType.PointContact)
                        beamType = St7.btContact;
                    else if (bp.PropertyType == BeamPropertyType.Beam)
                        beamType = St7.btBeam;
                    else if (bp.PropertyType == BeamPropertyType.Pipe)
                        beamType = St7.btPipe;
                    else if (bp.PropertyType == BeamPropertyType.Connection)
                        beamType = St7.btConnection;

                    if (HandleError(St7.St7NewBeamProperty(modelId, id, beamType, bp.Name)))
                    {
                        errors.Add(string.Format("Failed to create beam property {0}\r\n", bp.Name));
                        continue;
                    }
                    // CutoffBar specific properties
                    if (bp.PropertyType == BeamPropertyType.CutoffBar)
                    {
                        int[] co_ints = new int[2];
                        co_ints[St7.ipCutoffType] = bp.CutoffType == CutoffType.Brittle ? St7.cbBrittle : St7.cbDuctile;
                        co_ints[St7.ipKeepMass] = bp.KeepMass ? St7.btTrue : St7.btFalse;
                        double[] co_doubles = new double[2];
                        co_doubles[St7.ipCutoffTension] = bp.MaxTension;
                        co_doubles[St7.ipCutoffCompression] = bp.MaxCompression;
                        StrausRuntime.Check(St7.St7SetCutoffBarData(modelId, id, co_ints, co_doubles), "St7SetCutoffBarData");
                    }

                    // Section Geometry
                    if (bp.Section.NativeSection != null)
                        bp.Section.NativeSection.Assign(modelId, id);
                    else if (bp.SectionType != SectionModel.SectionTypes.Generic)
                    {
                        if (bp.SectionType == SectionModel.SectionTypes.GenericShapes)
                        {
                            StrausBxsGenerator.Assign(modelId, id, bp.GenericShapes, st7_units);
                        }
                        else
                        {
                            int sectionType = St7.bsNullSection;
                            double[] st_doubles = new double[6];
                            switch (bp.SectionType)
                            {
                                case SectionModel.SectionTypes.SolidCircle:
                                    StrausRuntime.Check(St7.St7SetBeamSectionName(modelId, id, "SolidRound"), "St7SetBeamSectionName");
                                    sectionType = St7.bsCircularSolid;
                                    st_doubles[0] = bp.D;
                                    break;
                                case SectionModel.SectionTypes.HollowCircle:
                                    StrausRuntime.Check(St7.St7SetBeamSectionName(modelId, id, "HollowRound"), "St7SetBeamSectionName");
                                    sectionType = St7.bsCircularHollow;
                                    st_doubles[0] = bp.D;
                                    st_doubles[3] = bp.T;
                                    break;
                                case SectionModel.SectionTypes.SolidRectangle:
                                    StrausRuntime.Check(St7.St7SetBeamSectionName(modelId, id, "SolidRect"), "St7SetBeamSectionName");
                                    sectionType = St7.bsSquareSolid;
                                    st_doubles[0] = bp.B;
                                    st_doubles[1] = bp.D;
                                    break;
                                case SectionModel.SectionTypes.HollowRectangle:
                                    StrausRuntime.Check(St7.St7SetBeamSectionName(modelId, id, "HollowRect"), "St7SetBeamSectionName");
                                    sectionType = St7.bsSquareHollow;
                                    st_doubles[0] = bp.B;
                                    st_doubles[1] = bp.D;
                                    st_doubles[3] = bp.T1;
                                    st_doubles[4] = bp.T2;
                                    break;
                                case SectionModel.SectionTypes.I:
                                    StrausRuntime.Check(St7.St7SetBeamSectionName(modelId, id, "IBeam"), "St7SetBeamSectionName");
                                    sectionType = St7.bsISection;
                                    st_doubles[0] = bp.B1;
                                    st_doubles[1] = bp.B2;
                                    st_doubles[2] = bp.D;
                                    st_doubles[3] = bp.T1;
                                    st_doubles[4] = bp.T2;
                                    st_doubles[5] = bp.T3;
                                    break;
                                case SectionModel.SectionTypes.T:
                                    StrausRuntime.Check(St7.St7SetBeamSectionName(modelId, id, "TBeam"), "St7SetBeamSectionName");
                                    sectionType = St7.bsTSection;
                                    st_doubles[0] = bp.B;
                                    st_doubles[1] = bp.D;
                                    st_doubles[2] = bp.L;
                                    st_doubles[3] = bp.T1;
                                    st_doubles[4] = bp.T2;
                                    st_doubles[5] = bp.T3;
                                    break;
                                case SectionModel.SectionTypes.C:
                                    StrausRuntime.Check(St7.St7SetBeamSectionName(modelId, id, "Lipped Channel"), "St7SetBeamSectionName");
                                    sectionType = St7.bsLipChannel;
                                    st_doubles[0] = bp.B;
                                    st_doubles[1] = bp.D;
                                    st_doubles[2] = bp.L;
                                    st_doubles[3] = bp.T1;
                                    st_doubles[4] = bp.T2;
                                    st_doubles[5] = bp.T3;
                                    break;
                                case SectionModel.SectionTypes.Omega:
                                    sectionType = St7.bsTopHatChannel;
                                    st_doubles = new[] { bp.B, bp.D, bp.L, bp.T1, bp.T2, bp.T3 };
                                    break;
                                case SectionModel.SectionTypes.Z:
                                    sectionType = St7.bsZSection;
                                    st_doubles = new[] { bp.B1, bp.B2, bp.D, bp.T1, bp.T2, bp.T3 };
                                    break;
                                case SectionModel.SectionTypes.Angle:
                                    StrausRuntime.Check(St7.St7SetBeamSectionName(modelId, id, "Angle"), "St7SetBeamSectionName");
                                    sectionType = St7.bsLSection;
                                    st_doubles[0] = bp.B;
                                    st_doubles[1] = bp.D;
                                    st_doubles[3] = bp.T1;
                                    st_doubles[4] = bp.T2;
                                    break;
                                case SectionModel.SectionTypes.Generic:
                                    break;
                                default:
                                    errors.Add(string.Format("Unable to det the section geometry {0} of the beam property {1}",
                                        bp.SectionType, bp.Id));
                                    break;
                            }
                            StrausRuntime.Check(St7.St7SetBeamSectionGeometry(modelId, id, sectionType, st_doubles), "St7SetBeamSectionGeometry");
                        }
                    }

                    // Section data
                    if (bp.Section.NativeSection == null && bp.SectionType != SectionModel.SectionTypes.GenericShapes)
                    {
                        int[] ints = new int[1];
                        ints[0] = 0;
                        double[] doubles = new double[11];
                        doubles[St7.ipAREA] = bp.SectionArea;
                        doubles[St7.ipI11] = bp.I11;
                        doubles[St7.ipI22] = bp.I22;
                        doubles[St7.ipJ] = bp.J;
                        doubles[St7.ipSL1] = bp.ShearL1;
                        doubles[St7.ipSL2] = bp.ShearL2;
                        doubles[St7.ipSA1] = bp.ShearA1;
                        doubles[St7.ipSA2] = bp.ShearA2;
                        doubles[St7.ipXBAR] = bp.Centroid.X;
                        doubles[St7.ipYBAR] = bp.Centroid.Y;
                        doubles[St7.ipANGLE] = bp.AngleX1Rad;
                        StrausRuntime.Check(St7.St7SetBeamSectionPropertyData(modelId, id, ints, doubles), "St7SetBeamSectionPropertyData");
                    }

                    if (bp.Mirror != SectionModel.MirrorTypes.None)
                    {
                        int mirrorType = St7.mtLeft;
                        int compatibleTwist = St7.btFalse;
                        StrausRuntime.Check(St7.St7SetBeamMirrorOption(modelId, id, mirrorType, compatibleTwist, new double[] { bp.MirrorGapA, bp.MirrorGapB }), "St7SetBeamMirrorOption");
                    }

                    // Material
                    StrausRuntime.Check(St7.St7SetMaterialName(modelId, St7.ptBEAMPROP, id, bp.Material.Name), "St7SetMaterialName");

                    double[] doubles1 = new double[9];
                    doubles1[St7.ipBeamModulus] = UnitsConvert.ConvertFromDefaultUnits(bp.Material.Modulus, pressureUnits, 1);// * c_f * Math.Pow(c_l, 2);
                    doubles1[St7.ipBeamDensity] = UnitsConvert.ConvertFromDefaultUnits(bp.Material.Density, massUnits, 1, lengthUnits, -3); //  * c_f * Math.Pow(c_l, 3);
                    doubles1[St7.ipBeamAlpha] = bp.Material.ThermalExpansion;
                    doubles1[St7.ipBeamViscosity] = bp.Material.ViscousDamping;
                    doubles1[St7.ipBeamDampingRatio] = bp.Material.DampingRatio;
                    doubles1[St7.ipBeamConductivity] = UnitsConvert.ConvertFromDefaultUnits(bp.Material.Conductivity, lengthUnits, -1);  /// c_l;
					doubles1[St7.ipBeamSpecificHeat] = UnitsConvert.ConvertFromDefaultUnits(bp.Material.SpecificHeat, massUnits, -1); //* Math.Pow(c_l, 3);
                    doubles1[St7.ipBeamShear] = UnitsConvert.ConvertFromDefaultUnits(bp.Material.ShearModulus, pressureUnits, 1);
                    doubles1[St7.ipBeamPoisson] = bp.Material.PoissonRatio;
                    StrausRuntime.Check(St7.St7SetBeamMaterialData(modelId, id, doubles1), "St7SetBeamMaterialData");
                    if (bp.Material.UsePoisson)
                        StrausRuntime.Check(St7.St7SetBeamShearModulusMode(modelId, id, St7.smUsePoissonsRatio), "St7SetBeamShearModulusMode");
                    else
                        StrausRuntime.Check(St7.St7SetBeamShearModulusMode(modelId, id, St7.smUseShearModulus), "St7SetBeamShearModulusMode");

                    StrausRuntime.Check(St7.St7UpdateElementPropertyData(modelId, St7.ptBEAMPROP, id), "St7UpdateElementPropertyData");
                    if (bp.SectionType != SectionModel.SectionTypes.Generic && bp.SectionType != SectionModel.SectionTypes.GenericShapes)
                    {
                        StrausRuntime.Check(St7.St7CalculateBeamSectionProperties(modelId, id, St7.btTrue), "St7CalculateBeamSectionProperties");
                    }

                    if (bp.Material.StressStrainTable != null && (bp.PropertyType == BeamPropertyType.Beam || bp.PropertyType == BeamPropertyType.Truss))
                    {
                        int maxTableNum = 0;
                        int tables = 0;

                        StrausRuntime.Check(St7.St7GetNumTables(modelId, St7.ttStressStrain, ref tables, ref maxTableNum), "St7GetNumTables");

                        var strain = bp.Material.StressStrainTable.Select(i => i.strain).ToArray();
                        var stress = bp.Material.StressStrainTable.Select(i => i.stress).ToArray();

                        double[] stressStrainTable = new double[strain.Length * 2];
                        for (int i = 0; i < strain.Length; i++)
                        {
                            stressStrainTable[i * 2] = strain[i];
                            stressStrainTable[i * 2 + 1] = stress[i];
                        }
                        maxTableNum++;

                        StrausRuntime.Check(St7.St7NewTableType(modelId, St7.ttStressStrain, maxTableNum, bp.Material.StressStrainTable.Length, bp.Material.Name, stressStrainTable), "St7NewTableType");

                        if (bp.PropertyType == BeamPropertyType.Beam)
                            StrausRuntime.Check(St7.St7SetPropertyTable(modelId, St7.ptBeamStressVsStrain, id, maxTableNum), "St7SetPropertyTable");
                        else if (bp.PropertyType == BeamPropertyType.Truss)
                            StrausRuntime.Check(St7.St7SetPropertyTable(modelId, St7.ptBeamStressVsStrain, id, maxTableNum), "St7SetPropertyTable");
                    }

                    beamProperties.Add(bp.Name, id++);
                    //St7.St7SaveFile(modelId);
                }

                // Plate properties
                id = 1;
                Dictionary<string, int> plateProperties = new Dictionary<string, int>();
                var plateProps = model.Plates.Select(p => p.PlateProperty).Distinct().ToArray();
                for (int j = 0; j < plateProps.Length; j++)
                {
                    PlatePropertyModel pp = plateProps[j];
                    if (plateProperties.ContainsKey(pp.Name))
                    {
                        warnings.Add(string.Format("The plate property {0} already exists", pp.Name));
                        continue;
                    }

                    int plateType = St7.ptNull;
                    int materialType = St7.mtNull;
                    if (pp.PropertyType == PlatePropertyType.ShellThin ||
                        pp.PropertyType == PlatePropertyType.ShellThick ||
                        pp.PropertyType == PlatePropertyType.PlateThin ||
                        pp.PropertyType == PlatePropertyType.PlateThick)
                    {
                        plateType = St7.ptPlateShell;
                        materialType = St7.mtIsotropic;
                    }
                    else if (pp.PropertyType == PlatePropertyType.ShearPanel)
                    {
                        //plateType = ro.Const("kPlateTypeShearPanel");
                        //materialType = ro.Const("mtIsotropic");
                        plateType = St7.ptPlateShell;
                        materialType = St7.mtOrthotropic;
                    }
                    else if (pp.PropertyType == PlatePropertyType.Membrane)
                    {
                        plateType = St7.ptMembrane;
                        materialType = St7.mtIsotropic;
                    }
                    else if (pp.PropertyType == PlatePropertyType.LoadPatch)
                    {
                        plateType = St7.ptLoadPatch;
                        materialType = St7.mtIsotropic;
                    }

                    if (HandleError(St7.St7NewPlateProperty(modelId, id, plateType, materialType, pp.Name)))
                    {
                        errors.Add(string.Format("Failed to create beam property {0}\r\n", pp.Name));
                        continue;
                    }

                    if (pp.PropertyType != PlatePropertyType.LoadPatch)
                    {
                        StrausRuntime.Check(St7.St7SetMaterialName(modelId, St7.ptPLATEPROP, id, pp.Material.Name), "St7SetMaterialName");

                        if (pp.PropertyType == PlatePropertyType.ShellThin ||
                            pp.PropertyType == PlatePropertyType.ShellThick ||
                            pp.PropertyType == PlatePropertyType.PlateThin ||
                            pp.PropertyType == PlatePropertyType.PlateThick ||
                            pp.PropertyType == PlatePropertyType.ShearPanel)
                        {
                            double[] thickness = new double[2];
                            thickness[0] = pp.MembraneThickness;
                            thickness[1] = pp.BendingThickness;
                            StrausRuntime.Check(St7.St7SetPlateThickness(modelId, id, thickness), "St7SetPlateThickness");
                        }
                        else if (pp.PropertyType == PlatePropertyType.Membrane)
                        {
                            double[] thickness = new double[2];
                            thickness[0] = pp.MembraneThickness;
                            thickness[1] = 0;
                            StrausRuntime.Check(St7.St7SetPlateThickness(modelId, id, thickness), "St7SetPlateThickness");
                        }

                        double[] doubles;
                        //switch (pp.Material)
                        //{
                        //    case Model.PlateProperty.MaterialType.Isotropic:
                        // Workarround for the API bug that it doesn't set the shear modulus in the ShearPanels
                        if (pp.PropertyType == PlatePropertyType.ShearPanel)
                        {
                            // temporarilly set the property as Shell Orthotropic
                            doubles = new double[18];
                            doubles[St7.ipPlateOrthoModulus1] = UnitsConvert.ConvertFromDefaultUnits(pp.Material.Modulus, pressureUnits, 1);
                            doubles[St7.ipPlateOrthoModulus1] = doubles[St7.ipPlateOrthoModulus1];
                            doubles[St7.ipPlateOrthoModulus1] = doubles[St7.ipPlateOrthoModulus1];
                            doubles[St7.ipPlateOrthoShear12] = doubles[St7.ipPlateOrthoModulus1];
                            doubles[St7.ipPlateOrthoPoisson12] = pp.Material.PoissonRatio;
                            doubles[St7.ipPlateOrthoPoisson23] = doubles[St7.ipPlateOrthoPoisson12];
                            doubles[St7.ipPlateOrthoPoisson31] = doubles[St7.ipPlateOrthoPoisson12];
                            doubles[St7.ipPlateOrthoDensity] = UnitsConvert.ConvertFromDefaultUnits(pp.Material.Density, massUnits, 1, lengthUnits, -3); //* c_f * Math.Pow(c_l, 3);
                            doubles[St7.ipPlateOrthoAlpha1] = pp.Material.ThermalExpansion;
                            doubles[St7.ipPlateOrthoAlpha2] = doubles[St7.ipPlateOrthoAlpha1];
                            doubles[St7.ipPlateOrthoAlpha3] = doubles[St7.ipPlateOrthoAlpha1];
                            doubles[St7.ipPlateOrthoViscosity] = pp.Material.ViscousDamping;
                            doubles[St7.ipPlateOrthoDampingRatio] = pp.Material.DampingRatio;
                            doubles[St7.ipPlateOrthoConductivity1] = UnitsConvert.ConvertFromDefaultUnits(pp.Material.Conductivity, lengthUnits, -1);// pp.Material.Conductivity / c_l;
                            doubles[St7.ipPlateOrthoConductivity2] = doubles[St7.ipPlateOrthoConductivity1];
                            doubles[St7.ipPlateOrthoSpecificHeat] = UnitsConvert.ConvertFromDefaultUnits(pp.Material.SpecificHeat, massUnits, -1);// pp.Material.SpecificHeat * Math.Pow(c_l, 3);
                            StrausRuntime.Check(St7.St7SetPlateOrthotropicMaterial(modelId, id, doubles), "St7SetPlateOrthotropicMaterial");
                            // Then change the property in ShearPanel Isotropic
                            StrausRuntime.Check(St7.St7SetPlatePropertyType(modelId, id, St7.ptShearPanel, St7.mtIsotropic), "St7SetPlatePropertyType");
                        }
                        doubles = new double[8];
                        doubles[St7.ipPlateIsoModulus] = UnitsConvert.ConvertFromDefaultUnits(pp.Material.Modulus, pressureUnits, 1);// * c_f * Math.Pow(c_l, 2);
                        doubles[St7.ipPlateIsoPoisson] = pp.Material.PoissonRatio;
                        doubles[St7.ipPlateIsoDensity] = UnitsConvert.ConvertFromDefaultUnits(pp.Material.Density, massUnits, 1, lengthUnits, -3); //* c_f * Math.Pow(c_l, 3);
                        doubles[St7.ipPlateIsoAlpha] = pp.Material.ThermalExpansion;
                        doubles[St7.ipPlateIsoViscosity] = pp.Material.ViscousDamping;
                        doubles[St7.ipPlateIsoDampingRatio] = pp.Material.DampingRatio;
                        doubles[St7.ipPlateIsoConductivity] = UnitsConvert.ConvertFromDefaultUnits(pp.Material.Conductivity, lengthUnits, -1);// pp.Material.Conductivity / c_l;
                        doubles[St7.ipPlateIsoSpecificHeat] = UnitsConvert.ConvertFromDefaultUnits(pp.Material.SpecificHeat, massUnits, -1);// pp.Material.SpecificHeat * Math.Pow(c_l, 3);
                        StrausRuntime.Check(St7.St7SetPlateIsotropicMaterial(modelId, id, doubles), "St7SetPlateIsotropicMaterial");

                        if (pp.Material.StressStrainTable != null && (pp.PropertyType == PlatePropertyType.ShellThin || pp.PropertyType == PlatePropertyType.ShellThick ||
                            pp.PropertyType == PlatePropertyType.PlateThin || pp.PropertyType == PlatePropertyType.PlateThick))
                        {
                            int maxTableNum = 0;
                            int tables = 0;

                            StrausRuntime.Check(St7.St7GetNumTables(modelId, St7.ttStressStrain, ref tables, ref maxTableNum), "St7GetNumTables");

                            double[] strain = pp.Material.StressStrainTable.Select(i => i.strain).ToArray();
                            double[] stress = pp.Material.StressStrainTable.Select(i => i.stress).ToArray();
                            double[] stressStrainTable = new double[strain.Length * 2];

                            for (int i = 0; i < strain.Length; i++)
                            {
                                stressStrainTable[(i) * 2] = strain[i];
                                stressStrainTable[(i) * 2 + 1] = stress[i];
                            }
                            maxTableNum++;
                            StrausRuntime.Check(St7.St7NewTableType(modelId, St7.ttStressStrain, maxTableNum, pp.Material.StressStrainTable.Length, pp.Material.Name, stressStrainTable), "St7NewTableType");
                            StrausRuntime.Check(St7.St7SetPropertyTable(modelId, St7.ptPlateStressVsStrain, id, maxTableNum), "St7SetPropertyTable");
                        }

                        /*break;
					case Model.PlateProperty.MaterialType.Orthotropic:
						doubles = new double[18];
						doubles[ro.Const("ipPlateOrthoModulus1")] = pp.Modulus1;
						doubles[ro.Const("ipPlateOrthoModulus2")] = pp.Modulus2;
						doubles[ro.Const("ipPlateOrthoModulus3")] = pp.Modulus3;
						doubles[ro.Const("ipPlateOrthoShear12")] = pp.ShearMod12;
						if (pp.GetType() == typeof(Model.PlateShellProp))
						{
							Model.PlateShellProp psp = pp as Model.PlateShellProp;
							doubles[ro.Const("ipPlateOrthoShear23")] = psp.ShearMod23;
							doubles[ro.Const("ipPlateOrthoShear31")] = psp.ShearMod31;
						}
						doubles[ro.Const("ipPlateOrthoPoisson12")] = pp.Poisson12;
						doubles[ro.Const("ipPlateOrthoPoisson23")] = pp.Poisson23;
						doubles[ro.Const("ipPlateOrthoPoisson31")] = pp.Poisson31;
						doubles[ro.Const("ipPlateOrthoDensity")] = pp.Density;
						doubles[ro.Const("ipPlateOrthoAlpha1")] = pp.Expansion1;
						doubles[ro.Const("ipPlateOrthoAlpha2")] = pp.Expansion2;
						doubles[ro.Const("ipPlateOrthoAlpha3")] = pp.Expansion3;
						doubles[ro.Const("ipPlateOrthoViscosity")] = pp.Damping;
						doubles[ro.Const("ipPlateOrthoDampingRatio")] = pp.DampingRatio;
						doubles[ro.Const("ipPlateOrthoConductivity1")] = pp.ThermalCond1;
						doubles[ro.Const("ipPlateOrthoConductivity2")] = pp.ThermalCond2;
						doubles[ro.Const("ipPlateOrthoSpecificHeat")] = pp.SpecificHeat;
						ro.SetPlateOrthotropicMaterial(modelId, pp.Id, doubles);
						break;
					default:
						_output += string.Format("Unable to set the material of type {0}\r\n", pp.Material.ToString());
						break;
				}*/
                    }

                    plateProperties.Add(pp.Name, id++);
                }

                // Load cases
                ModelHelper.GetLoadCases(model, out List<LoadCaseModel> loadCases);
                Dictionary<string, int> loadCaseNameIdMap = ModelHelper.GetLoadCaseNumbers(loadCases);

                id = 1;
                for (int i = 0; i < loadCases.Count; i++)
                {
                    LoadCaseModel loadCase = loadCases[i];
                    if (loadCase.Name == LoadCaseModel.Dead.Name)
                    {
                        if (HandleError(St7.St7SetLoadCaseName(modelId, id, loadCase.Name)))
                            errors.Add(string.Format("Failed to rename the default load case in {0}", loadCase.Name));
                    }
                    else
                    {
                        if (HandleError(St7.St7NewLoadCase(modelId, loadCase.Name)))
                            errors.Add(string.Format("Failed to define the load case {0}", loadCase.Name));
                    }

                    int caseType = St7.lcNoInertia;

                    if (loadCase.GlobalInertiaLoad == LoadCaseModel.GlobalInertiaLoads.Gravity)
                        caseType = St7.lcGravity;

                    if (HandleError(St7.St7SetLoadCaseType(modelId, id, caseType)))
                        errors.Add(string.Format("Failed to set load case type of the load case {0}", loadCase.Name));

                    if (HandleError(St7.St7SetLoadCaseGravityDir(modelId, id, (int)loadCase.GravityDirection + 1)))
                        errors.Add(string.Format("Failed to set gravity direction of the load case {0}", loadCase.Name));

                    if (HandleError(St7.St7SetLoadCaseMassOption(modelId, id, Convert.ToByte(loadCase.StructuralMassAcceleration),
                        Convert.ToByte(loadCase.NonStructuralMassAcceleration))))
                        errors.Add(string.Format("Failed to set the mass options of the {0} load case", loadCase.Name));

                    if (HandleError(St7.St7SetLoadCaseDefaults(modelId, id, new[] { 0, 0, 0, 0, 0, 0,
                        UnitsConvert.ConvertFromDefaultUnits(loadCase.GravityValue, lengthUnits, 1), 0, 0, 0, 0, 0, 0 })))
                        errors.Add(string.Format("Failed to set the gravity value of the {0} load case", loadCase.Name));
                    id++;
                }


                #region Multithread preprocessing

                // Checking only a single load case for node displacements. This load case will give coefficients to apply in all
                // combinations at the freedom case
                LoadCaseModel displLoadCase = null;
                Dictionary<int, int[]> beamsNodeNumberMap = new Dictionary<int, int[]>();
                Dictionary<int, int[]> plateNodeNumberMap = new Dictionary<int, int[]>();
                List<string> warningPreprocessingList = new List<string>();
                using var nodeIndex = new StrausNodeIndex(model.Nodes, tolerance);

                try
                {
                    for (int j = 0; j < model.Nodes.Count; j++)
                    {
                        var node = model.Nodes[j];
                        for (int i = 0; i < node.Loads.Count; i++)
                        {
                            LoadModel load = node.Loads[i];
                            if (load is NodeDisplacementLoadModel dl)
                            {
                                if (displLoadCase == null)
                                {
                                    displLoadCase = dl.LoadCase;
                                }
                                else
                                {
                                    if (displLoadCase.Name != dl.LoadCase.Name)
                                    {
                                        throw new NotSupportedException("Only one displacement load case is supported; multiple cases cannot be exported without changing the model.");
                                    }
                                }
                            }
                        }
                    }

                    for (int i = 0; i < model.Beams.Count; i++)
                    {
                        BeamModel beam = model.Beams[i];

                        var from = nodeIndex.Find(beam.PointFrom);
                        var to = nodeIndex.Find(beam.PointTo);
                        if (from.Matches > 1) warningPreprocessingList.Add($"Collapsing {from.Matches} nodes at coordinates {beam.PointFrom}");
                        if (to.Matches > 1) warningPreprocessingList.Add($"Collapsing {to.Matches} nodes at coordinates {beam.PointTo}");
                        int[] connections = new int[St7.kMaxElementNode + 1];
                        connections[0] = 2;
                        connections[1] = from.Number;
                        connections[2] = to.Number;

                        beamsNodeNumberMap[i] = connections;
                    }

                    for (int j = 0; j < model.Plates.Count; j++)
                    {
                        var plate = model.Plates[j];

                        int[] connections = new int[St7.kMaxElementNode + 1];
                        connections[0] = plate.Points.Count;

                        for (int i = 0; i < plate.Points.Count; i++)
                        {
                            var match = nodeIndex.Find(plate.Points[i]);
                            if (match.Matches > 1) warningPreprocessingList.Add($"Collapsing {match.Matches} nodes at coordinates {plate.Points[i]}");
                            connections[i + 1] = match.Number;
                        }

                        plateNodeNumberMap[j] = connections;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(string.Format($"Failed to preprocess the model. {ex.Message}"));
                }

                warnings.AddRange(warningPreprocessingList);
                if (errors.Count > 0) throw new InvalidOperationException("Preprocessing incompleto: esportazione interrotta.");

                #endregion

                // Load Combinations
                int comboNumber = 1;
                LoadCombinationModel[] loadCombModelArray = model.LoadCombinations.ToArray();
                for (int i = 0; i < loadCombModelArray.Length; i++)
                {
                    LoadCombinationModel combo = loadCombModelArray[i];
                    // For linear static analysis
                    if (HandleError(St7.St7AddLSACombination(modelId, combo.Name)))
                    {
                        errors.Add($"Failed to add the LSA combination: {combo.Name}");
                    }
                    else
                    {
                        foreach (KeyValuePair<LoadCaseModel, double> item in combo.Values)
                        {
                            int lc = loadCaseNameIdMap[item.Key.Name];
                            if (HandleError(St7.St7SetLSACombinationFactor(modelId, St7.ltLoadCase, comboNumber, lc, 1, item.Value)))
                                errors.Add($"Failed to set the LSA combination factor: {combo.Name}");
                        }
                    }

                    // For nonlinear static analysis
                    if (HandleError(St7.St7AddNLAIncrement(modelId, 0, combo.Name)))
                    {
                        errors.Add($"Failed to add the NLA increment: {combo.Name}");
                    }
                    else
                    {
                        foreach (KeyValuePair<LoadCaseModel, double> item in combo.Values)
                        {
                            //int lc = Convert.ToInt32(loadCases.Single(l => l.Name == item.Key.Name).Id);
                            if (HandleError(St7.St7SetNLALoadIncrementFactor(modelId, 0, comboNumber, loadCaseNameIdMap[item.Key.Name], item.Value)))
                                errors.Add($"Failed to set the NLA increment factor: {combo.Name}");
                            if (displLoadCase != null && displLoadCase.Name == item.Key.Name)
                            {
                                if (HandleError(St7.St7SetNLAFreedomIncrementFactor(modelId, 0, comboNumber, 1, item.Value)))
                                {
                                    errors.Add($"Failed to set the NLA freedom case factor: {combo.Name}");
                                }
                            }
                        }
                    }
                    comboNumber++;
                }

                #region Export nodi ed elementi

                // Nodes
                //List<int> addedNodes = new List<int>();
                HashSet<int> addedNodesNumber = new HashSet<int>();
                for (int i = 0; i < model.Nodes.Count; i++)
                {
                    NodeModel node = model.Nodes[i];
                    try
                    {
                        int nodeNumber = i + 1;

                        if (!addedNodesNumber.Contains(nodeNumber))
                        {
                            StrausRuntime.Check(St7.St7SetNodeXYZ(modelId, nodeNumber, new double[] { node.Position.X, node.Position.Y, node.Position.Z }), "St7SetNodeXYZ");
                            addedNodesNumber.Add(nodeNumber);
                        }

                        if (node.HasAttribute(typeof(NodeRestraintAttributeModel)))
                        {
                            try
                            {
                                IEnumerable<NodeRestraintAttributeModel.AttributeData> restraints = node.Attributes.Where(n => n.GetType() == typeof(NodeRestraintAttributeModel))
                                    .Cast<NodeRestraintAttributeModel>().Select(r => r.Value);

                                int[] sts = new int[6];
                                foreach (NodeRestraintAttributeModel.AttributeData restraint in restraints)
                                {
                                    sts[0] |= restraint.Tx ? St7.btTrue : St7.btFalse;
                                    sts[1] |= restraint.Ty ? St7.btTrue : St7.btFalse;
                                    sts[2] |= restraint.Tz ? St7.btTrue : St7.btFalse;
                                    sts[3] |= restraint.Rx ? St7.btTrue : St7.btFalse;
                                    sts[4] |= restraint.Ry ? St7.btTrue : St7.btFalse;
                                    sts[5] |= restraint.Rz ? St7.btTrue : St7.btFalse;
                                }

                                double[] doubles = new double[6];
                                //searching for displacements on the node
                                for (int j = 0; j < node.Loads.Count; j++)
                                {
                                    LoadModel l = node.Loads[j];
                                    if (l is NodeDisplacementLoadModel ndl)
                                    {
                                        doubles[0] = ndl.Value.Translation.X;
                                        doubles[1] = ndl.Value.Translation.Y;
                                        doubles[2] = ndl.Value.Translation.Z;
                                        doubles[3] = ndl.Value.Rotation.X;
                                        doubles[4] = ndl.Value.Rotation.Y;
                                        doubles[5] = ndl.Value.Rotation.Z;
                                        break;
                                    }
                                }
                                StrausRuntime.Check(St7.St7SetNodeRestraint6(modelId, nodeNumber, 1, 1, sts, doubles), "St7SetNodeRestraint6");
                            }
                            catch (Exception ex)
                            {
                                errors.Add(string.Format($"Failed to set Node Restraint on {node.NodeId}. {ex.Message}"));
                                continue;
                            }
                        }

                        if (node.HasAttribute(typeof(NodeStiffnessAttributeModel)))
                        {
                            try
                            {
                                NodeStiffnessAttributeModel.AttributeData stiffness = NodeStiffnessAttributeModel.GetStiffness(node);
                                int[] sts = new int[6];
                                StrausRuntime.Check(St7.St7SetNodeKTranslation3F(modelId, nodeNumber, 1, 1, new double[] { stiffness.Tx, stiffness.Ty, stiffness.Tz }), "St7SetNodeKTranslation3F");
                                StrausRuntime.Check(St7.St7SetNodeKRotation3F(modelId, nodeNumber, 1, 1, new double[] { stiffness.Rx, stiffness.Ry, stiffness.Rz }), "St7SetNodeKRotation3F");
                            }
                            catch (Exception ex)
                            {
                                errors.Add(string.Format($"Failed to set node stiffness on node {node.NodeId}. {ex.Message}"));
                                continue;
                            }
                        }

                        if (GetElementID(node, ref errors, out int elementId))
                        {
                            StrausRuntime.Check(St7.St7SetNodeID(modelId, nodeNumber, elementId), "St7SetNodeID");
                        }

                        foreach (var action in NodalLoadAccumulator.Sum(node.Loads))
                        {
                            int lc = loadCaseNameIdMap[action.Key];
                            var f = action.Value.Force;
                            var m = action.Value.Moment;
                            StrausRuntime.Check(St7.St7SetNodeForce3(modelId, nodeNumber, lc, new[] { f.X, f.Y, f.Z }), "St7SetNodeForce3");
                            StrausRuntime.Check(St7.St7SetNodeMoment3(modelId, nodeNumber, lc, new[] { m.X, m.Y, m.Z }), "St7SetNodeMoment3");
                        }
                        for (int j = 0; j < node.Loads.Count; j++)
                        {
                            LoadModel load = node.Loads[j];
                            int lc = loadCaseNameIdMap[load.LoadCase.Name];
                            if (load.GetType() == typeof(NodeTemperatureLoadModel))
                            {
                                NodeTemperatureLoadModel t = (NodeTemperatureLoadModel)load;
                                StrausRuntime.Check(St7.St7SetNodeTemperature1(modelId, nodeNumber, lc, new double[] { t.Value }), "St7SetNodeTemperature1");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(string.Format($"Failed to create node {node.NodeId}. {ex.Message}"));
                        continue;
                    }
                }

                // Beams
                HashSet<int> addedBeamsId = new HashSet<int>();
                //var ordered = model.Beams.OrderBy(k => k.BeamId).ToArray();
                for (int k = 0; k < model.Beams.Count; k++)
                {
                    BeamModel beam = model.Beams[k];
                    try
                    {
                        int beamNumber = k + 1;
                        int propNumber = beamProperties[beam.BeamProperty.Name];

                        if (!addedBeamsId.Contains(beamNumber))
                        {
                            try
                            {
                                //NodeModel node1 = model.Nodes.Single(n => n.Position.DistanceToSquared(beam.PointFrom) < tol);
                                //NodeModel node2 = model.Nodes.Single(n => n.Position.DistanceToSquared(beam.PointTo) < tol);
                                //IEnumerable<NodeModel> fromNodes = model.Nodes.Where(n => n.Position.DistanceToSquared(beam.PointFrom) < squaredTol);
                                //IEnumerable<NodeModel> toNodes = model.Nodes.Where(n => n.Position.DistanceToSquared(beam.PointTo) < squaredTol);
                                //if (fromNodes.Count() > 1)
                                //    warnings.Add(string.Format("Collapsing {0} nodes at coordinates {1}", fromNodes.Count(), beam.PointFrom));
                                //if (toNodes.Count() > 1)
                                //    warnings.Add(string.Format("Collapsing {0} nodes at coordinates {1}", toNodes.Count(), beam.PointTo));
                                //NodeModel node1 = fromNodes.First();
                                //NodeModel node2 = toNodes.First();
                                //if (node1 == null || node2 == null)
                                //{
                                //    errors.Add(string.Format("Unreconized node(s) at coordinates {0} and/or {1}", beam.PointFrom, beam.PointTo));
                                //    return false;
                                //}
                                //connections[0] = 2;
                                //connections[1] = Convert.ToInt32(node1.NodeId);
                                //connections[2] = Convert.ToInt32(node2.NodeId);


                                StrausRuntime.Check(St7.St7SetElementConnection(modelId, St7.tyBEAM, beamNumber, propNumber, beamsNodeNumberMap[k]), "St7SetElementConnection");

                                if (beam.Groups.Count > 0)
                                {
                                    if (HandleError(St7.St7SetEntityGroup(modelId, St7.tyBEAM, beamNumber, groups_ids[beam.Groups[0]])))
                                        errors.Add(string.Format("Failed to set the group {0} to the beam {1}\r\n", beam.Groups[0], id));
                                    if (beam.Groups.Count > 1)
                                        warnings.Add(string.Format("Straus7 allows assigning only one single group to the beam {0}\r\n", id));
                                }

                                if (GetElementID(beam, ref errors, out int elementId))
                                {
                                    StrausRuntime.Check(St7.St7SetBeamID(modelId, beamNumber, elementId), "St7SetBeamID");
                                }

                                double angle = 90;
                                if (beam.AngleDeg != 0)
                                    angle += beam.AngleDeg;
                                if (RotateOf90Deg(beam))
                                    angle += 90;
                                else if (RotateOf180Deg(beam))
                                    angle += 180;

                                StrausRuntime.Check(St7.St7SetBeamReferenceAngle1(modelId, beamNumber, new double[] { angle }), "St7SetBeamReferenceAngle1");

                                if (beam.Offset != Rhino.Geometry.Vector2d.Unset && beam.Offset.Length > 0) //Rhino.Geometry.Vector2d or GPC.Geometry.Vector2d?
                                    StrausRuntime.Check(St7.St7SetBeamOffset2(modelId, beamNumber, new double[] { beam.Offset.X, beam.Offset.Y }), "St7SetBeamOffset2");

                                addedBeamsId.Add(beamNumber);
                            }
                            catch (Exception ex)
                            {
                                errors.Add(string.Format($"Failed to create geometry of beam {beam.BeamId}. {ex.Message}"));
                                continue;
                            }
                        }

                        // Releases
                        if (beam.HasAttribute(typeof(BeamReleasesAttributeModel)))
                        {
                            try
                            {
                                if (beam.Attributes.Where(x => x is BeamReleasesAttributeModel).Count() > 1)
                                {
                                    errors.Add($"Cannot assign more than 1 set of releases for beam {beamNumber}");
                                }
                                BeamReleasesAttributeModel.AttributeData rel = BeamReleasesAttributeModel.GetRelease(beam);
                                if (rel.TransNode1.Count(b => b == true) > 0)
                                {
                                    int[] rel_status = new[]
                                    {
                                        rel.TransNode1[0] == false ? St7.brFixed :  (rel.TransN1Partial.X == 0 ? St7.brReleased : St7.brPartial),
                                        rel.TransNode1[1] == false ? St7.brFixed :  (rel.TransN1Partial.Y == 0 ? St7.brReleased : St7.brPartial),
                                        rel.TransNode1[2] == false ? St7.brFixed :  (rel.TransN1Partial.Z == 0 ? St7.brReleased : St7.brPartial),
                                    };
                                    double[] rel_values = new[] { rel.TransN1Partial.X, rel.TransN1Partial.Y, rel.TransN1Partial.Z };
                                    StrausRuntime.Check(St7.St7SetBeamTRelease3(modelId, beamNumber, 1, rel_status, rel_values), "St7SetBeamTRelease3");
                                }
                                if (rel.RotNode1.Count(b => b == true) > 0)
                                {
                                    int[] rel_status = new[]
                                    {
                                        rel.RotNode1[0] == false ? St7.brFixed : (rel.RotN1Partial.X == 0 ? St7.brReleased : St7.brPartial),
                                        rel.RotNode1[1] == false ? St7.brFixed : (rel.RotN1Partial.Y == 0 ? St7.brReleased : St7.brPartial),
                                        rel.RotNode1[2] == false ? St7.brFixed : (rel.RotN1Partial.Z == 0 ? St7.brReleased : St7.brPartial),
                                    };
                                    double[] rel_values = new[] { rel.RotN1Partial.X, rel.RotN1Partial.Y, rel.RotN1Partial.Z };
                                    StrausRuntime.Check(St7.St7SetBeamRRelease3(modelId, beamNumber, 1, rel_status, rel_values), "St7SetBeamRRelease3");
                                }
                                if (rel.TransNode2.Count(b => b == true) > 0)
                                {
                                    int[] rel_status = new[]
                                    {
                                        rel.TransNode2[0] == false ? St7.brFixed :  (rel.TransN2Partial.X == 0 ? St7.brReleased : St7.brPartial),
                                        rel.TransNode2[1] == false ? St7.brFixed :  (rel.TransN2Partial.Y == 0 ? St7.brReleased : St7.brPartial),
                                        rel.TransNode2[2] == false ? St7.brFixed :  (rel.TransN2Partial.Z == 0 ? St7.brReleased : St7.brPartial),
                                    };
                                    double[] rel_values = new[] { rel.TransN2Partial.X, rel.TransN2Partial.Y, rel.TransN2Partial.Z };
                                    StrausRuntime.Check(St7.St7SetBeamTRelease3(modelId, beamNumber, 2, rel_status, rel_values), "St7SetBeamTRelease3");
                                }
                                if (rel.RotNode2.Count(b => b == true) > 0)
                                {
                                    int[] rel_status = new[]
                                    {
                                        rel.RotNode2[0] == false ? St7.brFixed : (rel.RotN2Partial.X == 0 ? St7.brReleased : St7.brPartial),
                                        rel.RotNode2[1] == false ? St7.brFixed : (rel.RotN2Partial.Y == 0 ? St7.brReleased : St7.brPartial),
                                        rel.RotNode2[2] == false ? St7.brFixed : (rel.RotN2Partial.Z == 0 ? St7.brReleased : St7.brPartial),
                                    };
                                    double[] rel_values = new[] { rel.RotN2Partial.X, rel.RotN2Partial.Y, rel.RotN2Partial.Z };
                                    StrausRuntime.Check(St7.St7SetBeamRRelease3(modelId, beamNumber, 2, rel_status, rel_values), "St7SetBeamRRelease3");
                                }
                            }
                            catch (Exception ex)
                            {
                                errors.Add(string.Format($"Failed to set release to beam {beam.BeamId}. {ex.Message}"));
                                continue;
                            }
                        }

                        // Linear Support
                        if (beam.HasAttribute(typeof(BeamSupportAttributeModel)))
                        {
                            try
                            {
                                bool[] compressionOnly = new bool[3];
                                double[] supportStiffness = new double[3];

                                int directionIndex = 0;
                                bool reverseCompressionOnly = false;
                                for (int j = 0; j < beam.Attributes.Count; j++)
                                {
                                    if (beam.Attributes[j].GetType() == typeof(BeamSupportAttributeModel))
                                    {
                                        BeamSupportAttributeModel support = (BeamSupportAttributeModel)beam.Attributes[j];

                                        // Manuale Straus:
                                        // The direction of compression is in towards the negative beam axis direction.
                                        // Tension is towards the positive beam axis direction

                                        // supportato solo le molle con direzione degli assi locali
                                        if (support.Value.SpringTensionOrientation == BeamSupportAttributeModel.SpringTensionOrientation.BeamLocalAxis)
                                        {

                                            if (support.Value.SpringType == BeamSupportAttributeModel.SpringType.Simple)
                                            {
                                                switch (support.Value.LocalAxisDirection)
                                                {
                                                    case BeamSupportAttributeModel.LocalAxisDirection.negative1Axis:
                                                        directionIndex = 0;
                                                        break;

                                                    case BeamSupportAttributeModel.LocalAxisDirection.negative2Axis:
                                                        directionIndex = 1;
                                                        break;

                                                    case BeamSupportAttributeModel.LocalAxisDirection.negative3Axis:
                                                        directionIndex = 2;
                                                        break;

                                                    case BeamSupportAttributeModel.LocalAxisDirection.positive1Axis:
                                                        directionIndex = 0;
                                                        reverseCompressionOnly = true;
                                                        break;

                                                    case BeamSupportAttributeModel.LocalAxisDirection.positive2Axis:
                                                        directionIndex = 1;
                                                        reverseCompressionOnly = true;
                                                        break;

                                                    case BeamSupportAttributeModel.LocalAxisDirection.positive3Axis:
                                                        directionIndex = 2;
                                                        reverseCompressionOnly = true;
                                                        break;

                                                    default:
                                                        throw new NotSupportedException();
                                                }

                                                if (support.Value.SimpleSpringType == BeamSupportAttributeModel.SimpleSpringTypes.CompressionOnly)
                                                {
                                                    if (!reverseCompressionOnly)
                                                    {
                                                        compressionOnly[directionIndex] = true;
                                                    }
                                                }

                                                supportStiffness[directionIndex] = support.Value.Stiffness;
                                            }
                                            else
                                            {
                                                errors.Add($"Only simple spring type is supported for beam support attribute. Beam {beamNumber}");
                                            }
                                        }
                                        else
                                        {
                                            errors.Add($"Only local axis spring direction is supported for beam support attribute. Beam {beamNumber}");
                                        }
                                    }
                                }

                                if (compressionOnly.Distinct().Count() > 1)
                                {
                                    errors.Add($"Compression only flag must be the equal for all supports attribute directions. Beam {beamNumber}");
                                }
                                else
                                {
                                    //TODO: implementare
                                    for (int i = 0; i < loadCases.Count; i++)
                                        StrausRuntime.Check(St7.St7SetBeamSupport2(modelId, beamNumber, directionIndex, loadCaseNameIdMap[loadCases[i].Name],
                                            compressionOnly.First() ? St7.btTrue : St7.btFalse, new double[] { supportStiffness[0], supportStiffness[1] }), "St7SetBeamSupport2");
                                }
                            }
                            catch (Exception ex)
                            {
                                errors.Add(string.Format($"Failed to set support attribute to beam {beam.BeamId}. {ex.Message}"));
                                continue;
                            }
                        }

                        // Taper
                        if (beam.HasAttribute(typeof(BeamTaperAttributeModel)))
                        {
                            try
                            {
                                BeamTaperAttributeModel.AttributeData tap = BeamTaperAttributeModel.GetTaper(beam);
                                if (tap.NFromX != 1 || tap.NToX != 1)
                                {
                                    int code;
                                    switch (tap.AlignmentX)
                                    {
                                        case BeamTaperAttributeModel.AlignmentCodes.Bottom:
                                            code = St7.btBottom;
                                            break;
                                        case BeamTaperAttributeModel.AlignmentCodes.Center:
                                            code = St7.btSymm;
                                            break;
                                        case BeamTaperAttributeModel.AlignmentCodes.Top:
                                            code = St7.btTop;
                                            break;
                                        default:
                                            throw new NotSupportedException();
                                    }
                                    StrausRuntime.Check(St7.St7SetBeamTaper2(modelId, beamNumber, St7.axLocalX, code, new double[2] { tap.NFromX, tap.NToX }), "St7SetBeamTaper2");
                                }
                                if (tap.NFromY != 1 || tap.NToY != 1)
                                {
                                    int code;
                                    switch (tap.AlignmentY)
                                    {
                                        case BeamTaperAttributeModel.AlignmentCodes.Bottom:
                                            code = St7.btBottom;
                                            break;
                                        case BeamTaperAttributeModel.AlignmentCodes.Center:
                                            code = St7.btSymm;
                                            break;
                                        case BeamTaperAttributeModel.AlignmentCodes.Top:
                                            code = St7.btTop;
                                            break;
                                        default:
                                            throw new NotSupportedException();
                                    }
                                    StrausRuntime.Check(St7.St7SetBeamTaper2(modelId, beamNumber, St7.axLocalY, code, new double[2] { tap.NFromY, tap.NToY }), "St7SetBeamTaper2");
                                }
                            }
                            catch (Exception ex)
                            {
                                errors.Add(string.Format($"Failed to set taper attribute to beam {beam.BeamId}. {ex.Message}"));
                                continue;
                            }
                        }

                        // Loads
                        List<int> preLoadLC = new List<int>();
                        Dictionary<int, int> pointFPId = new Dictionary<int, int>();
                        Dictionary<int, int> pointFGId = new Dictionary<int, int>();
                        Dictionary<int, int> pointMPId = new Dictionary<int, int>();
                        Dictionary<int, int> pointMGId = new Dictionary<int, int>();
                        Dictionary<int, int> distrFPId = new Dictionary<int, int>();
                        Dictionary<int, int> distrFGId = new Dictionary<int, int>();

                        for (int j = 0; j < beam.Loads.Count; j++)
                        {
                            LoadModel load = beam.Loads[j];
                            try
                            {
                                int lc = loadCaseNameIdMap[load.LoadCase.Name];
                                if (load.GetType() == typeof(BeamPreLoadModel))
                                {
                                    BeamPreLoadModel pl = (BeamPreLoadModel)load;
                                    int type = pl.PreLoadType == BeamPreLoadModel.LoadType.Strain ? St7.plBeamPreStrain : St7.plBeamPreTension;
                                    StrausRuntime.Check(St7.St7SetBeamPreLoad1(modelId, beamNumber, lc, type, new double[] { pl.Value }), "St7SetBeamPreLoad1");
                                    if (preLoadLC.Contains(lc))
                                    {
                                        errors.Add("Beam " + beamNumber + " has multiple prestrain in loadcase " + load.LoadCase.Name);
                                    }
                                    else
                                    {
                                        preLoadLC.Add(lc);
                                    }
                                }
                                else if (load.GetType() == typeof(BeamDistributedLoadModel))
                                {
                                    BeamDistributedLoadModel dl = (BeamDistributedLoadModel)load;
                                    int type = 0;
                                    int loadId;
                                    switch (dl.Value.LoadSchema)
                                    {
                                        case BeamDistributedLoadModel.LoadSchema.Uniform:
                                            type = St7.dlConstant;
                                            break;
                                        case BeamDistributedLoadModel.LoadSchema.Keystone:
                                            type = St7.dlLinear;
                                            break;
                                        case BeamDistributedLoadModel.LoadSchema.Triangular:
                                            type = St7.dlTriangular;
                                            break;
                                        case BeamDistributedLoadModel.LoadSchema.TriangularEnd1_Keystone:
                                            type = St7.dlThreePoint0;
                                            break;
                                        case BeamDistributedLoadModel.LoadSchema.Keystone_TriangularEnd2:
                                            type = St7.dlThreePoint1;
                                            break;
                                        case BeamDistributedLoadModel.LoadSchema.TriangularEnds_Keystone:
                                            type = St7.dlTrapezoidal;
                                            break;
                                    }
                                    if (dl.Value.CoordinateSystem == null)
                                    {
                                        int dir = (int)dl.Value.LoadDirection + 1;
                                        if (distrFPId.TryGetValue(lc, out loadId))
                                        {
                                            loadId++;
                                            distrFPId[lc] = loadId;
                                        }
                                        else
                                        {
                                            loadId = 1;
                                            distrFPId.Add(lc, loadId);
                                        }
                                        if (dl.DistributedLoadType == BeamDistributedLoadModel.LoadType.Force)
                                            StrausRuntime.Check(St7.St7SetBeamDistributedForcePrincipal6ID(modelId, beamNumber, dir, lc, type, loadId, new[] { dl.Value.PA, dl.Value.PB,
                                        dl.Value.P1, dl.Value.P2, dl.Value.A, dl.Value.B }), "St7SetBeamDistributedForcePrincipal6ID");
                                        else
                                            StrausRuntime.Check(St7.St7SetBeamDistributedMomentPrincipal6ID(modelId, beamNumber, dir, lc, type, loadId, new[] { dl.Value.PA, dl.Value.PB,
                                        dl.Value.P1, dl.Value.P2, dl.Value.A, dl.Value.B }), "St7SetBeamDistributedMomentPrincipal6ID");
                                    }
                                    else
                                    {
                                        int dir = 0;
                                        switch (dl.Value.LoadDirection)
                                        {
                                            case BeamDistributedLoadModel.LoadDirection.X:
                                            case BeamDistributedLoadModel.LoadDirection.X_Projected:
                                                dir = 1;
                                                break;
                                            case BeamDistributedLoadModel.LoadDirection.Y:
                                            case BeamDistributedLoadModel.LoadDirection.Y_Projected:
                                                dir = 2;
                                                break;
                                            case BeamDistributedLoadModel.LoadDirection.Z:
                                            case BeamDistributedLoadModel.LoadDirection.Z_Projected:
                                            case BeamDistributedLoadModel.LoadDirection.Gravity:
                                            case BeamDistributedLoadModel.LoadDirection.Gravity_Projected:
                                                dir = 3;
                                                break;
                                        }
                                        int projectFlag = 0;
                                        switch (dl.Value.LoadDirection)
                                        {
                                            case BeamDistributedLoadModel.LoadDirection.X:
                                            case BeamDistributedLoadModel.LoadDirection.Y:
                                            case BeamDistributedLoadModel.LoadDirection.Z:
                                            case BeamDistributedLoadModel.LoadDirection.Gravity:
                                                projectFlag = St7.btFalse;
                                                break;
                                            case BeamDistributedLoadModel.LoadDirection.X_Projected:
                                            case BeamDistributedLoadModel.LoadDirection.Y_Projected:
                                            case BeamDistributedLoadModel.LoadDirection.Z_Projected:
                                            case BeamDistributedLoadModel.LoadDirection.Gravity_Projected:
                                                projectFlag = St7.btTrue;
                                                break;
                                        }
                                        double[] values = new[] { dl.Value.PA, dl.Value.PB, dl.Value.P1, dl.Value.P2, dl.Value.A, dl.Value.B };
                                        if (dl.Value.LoadDirection == BeamDistributedLoadModel.LoadDirection.Gravity ||
                                            dl.Value.LoadDirection == BeamDistributedLoadModel.LoadDirection.Gravity_Projected)
                                        {
                                            values[0] = -dl.Value.PA != 0 ? -dl.Value.PA : 0;
                                            values[1] = -dl.Value.PB != 0 ? -dl.Value.PB : 0;
                                            values[2] = -dl.Value.P1 != 0 ? -dl.Value.P1 : 0;
                                            values[3] = -dl.Value.P2 != 0 ? -dl.Value.P2 : 0;
                                        }

                                        if (distrFGId.TryGetValue(lc, out loadId))
                                        {
                                            loadId++;
                                            distrFGId[lc] = loadId;
                                        }
                                        else
                                        {
                                            loadId = 1;
                                            distrFGId.Add(lc, loadId);
                                        }

                                        StrausRuntime.Check(St7.St7SetBeamDistributedForceGlobal6ID(modelId, beamNumber, dir, projectFlag, lc, type, loadId, values), "St7SetBeamDistributedForceGlobal6ID");
                                    }
                                }
                                else if (load.GetType() == typeof(BeamPointLoadModel))
                                {
                                    BeamPointLoadModel pl = (BeamPointLoadModel)load;
                                    double[] values = new[] { pl.Value.P.X, pl.Value.P.Y, pl.Value.P.Z, pl.Value.A };
                                    int loadId;
                                    if (pl.PointLoadType == BeamPointLoadModel.LoadType.Force)
                                    {
                                        if (pl.Value.CoordinateSystem == null)
                                        {
                                            if (pointFPId.TryGetValue(lc, out loadId))
                                            {
                                                loadId++;
                                                pointFPId[lc] = loadId;
                                            }
                                            else
                                            {
                                                loadId = 1;
                                                pointFPId.Add(lc, loadId);
                                            }
                                            StrausRuntime.Check(St7.St7SetBeamPointForcePrincipal4ID(modelId, beamNumber, lc, loadId, values), "St7SetBeamPointForcePrincipal4ID");
                                        }
                                        else
                                        {
                                            if (pointFGId.TryGetValue(lc, out loadId))
                                            {
                                                loadId++;
                                                pointFGId[lc] = loadId;
                                            }
                                            else
                                            {
                                                loadId = 1;
                                                pointFGId.Add(lc, loadId);
                                            }
                                            StrausRuntime.Check(St7.St7SetBeamPointForceGlobal4ID(modelId, beamNumber, lc, loadId, values), "St7SetBeamPointForceGlobal4ID");
                                        }
                                    }
                                    else
                                    {
                                        if (pl.Value.CoordinateSystem == null)
                                        {
                                            if (pointMPId.TryGetValue(lc, out loadId))
                                            {
                                                loadId++;
                                                pointMPId[lc] = loadId;
                                            }
                                            else
                                            {
                                                loadId = 1;
                                                pointMPId.Add(lc, loadId);
                                            }
                                            StrausRuntime.Check(St7.St7SetBeamPointMomentPrincipal4ID(modelId, beamNumber, lc, loadId, values), "St7SetBeamPointMomentPrincipal4ID");
                                        }
                                        else
                                        {
                                            if (pointMGId.TryGetValue(lc, out loadId))
                                            {
                                                loadId++;
                                                pointMGId[lc] = loadId;
                                            }
                                            else
                                            {
                                                loadId = 1;
                                                pointMGId.Add(lc, loadId);
                                            }
                                            StrausRuntime.Check(St7.St7SetBeamPointMomentGlobal4ID(modelId, beamNumber, lc, loadId, values), "St7SetBeamPointMomentGlobal4ID");
                                        }
                                    }
                                }
                                else if (load.GetType() == typeof(BeamGradientLoadModel))
                                {
                                    try
                                    {
                                        BeamGradientLoadModel gl = (BeamGradientLoadModel)load;
                                        StrausRuntime.Check(St7.St7SetBeamTempGradient2(modelId, beamNumber, lc, new double[] { gl.Value.Axis1, gl.Value.Axis2 }), "St7SetBeamTempGradient2");
                                    }
                                    catch (Exception ex)
                                    {
                                        errors.Add(string.Format($"Failed to set string group to beam {beam.BeamId}. {ex.Message}"));
                                        continue;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                errors.Add(string.Format($"Failed to set load case {load.LoadCase.Id} to beam {beam.BeamId}. {ex.Message}"));
                                continue;
                            }
                        }

                        //StringGroup
                        if (beam.HasAttribute(typeof(BeamStringGroupAttributeModel)))
                        {
                            BeamStringGroupAttributeModel.AttributeData sg = BeamStringGroupAttributeModel.GetStringGroup(beam);
                            int idSG = sg.ID;
                            StrausRuntime.Check(St7.St7SetBeamStringGroup1(modelId, beamNumber, idSG), "St7SetBeamStringGroup1");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(string.Format($"Failed to create beam {beam.BeamId}. {ex.Message}"));
                        continue;
                    }
                }

                // Plates
                HashSet<int> addedPlatesId = new HashSet<int>();
                for (int k = 0; k < model.Plates.Count; k++)
                {
                    PlateModel plate = model.Plates[k];
                    try
                    {
                        int plateId = k + 1;

                        if (!addedPlatesId.Contains(plateId))
                        {
                            //connections[0] = plate.Points.Count;
                            //for (int i = 0; i < plate.Points.Count; i++)
                            //    connections[i + 1] = Convert.ToInt32(model.Nodes.Single(n => n.Position.DistanceToSquared(plate.Points[i]) < squaredTol).NodeId);

                            try
                            {
                                int propId = plateProperties[plate.PlateProperty.Name];
                                if (HandleError(St7.St7SetElementConnection(modelId, St7.tyPLATE, plateId, propId, plateNodeNumberMap[k])))
                                {
                                    errors.Add($"Failed to define the plate {id}");
                                    continue;
                                }
                                if (plate.Groups.Count > 0) // We consider only the first group as Straus7 allows
                                {
                                    if (HandleError(St7.St7SetEntityGroup(modelId, St7.tyPLATE, plateId, groups_ids[plate.Groups[0]])))
                                        errors.Add($"Failed to set the group {plate.Groups[0]} to the plate {id}");
                                    if (plate.Groups.Count > 1)
                                        warnings.Add($"Straus7 allows assigning only one single group to the beam {id}");
                                }
                                if (plate.AngleDeg != 0)
                                {
                                    StrausRuntime.Check(St7.St7SetPlateXAngle1(modelId, plateId, new[] { plate.AngleDeg }), "St7SetPlateXAngle1");
                                }
                                if (plate.Offset != 0)
                                {
                                    if (HandleError(St7.St7SetPlateOffset1(modelId, plateId, new double[] { plate.Offset })))
                                        errors.Add($"Failed to set the offset to the plate {id}");
                                }

                                if (GetElementID(plate, ref errors, out int elementId))
                                {
                                    StrausRuntime.Check(St7.St7SetPlateID(modelId, plateId, elementId), "St7SetPlateID");
                                }

                                addedPlatesId.Add(plateId);
                            }
                            catch (Exception ex)
                            {
                                errors.Add(string.Format($"Failed to create geometry of plate {plate.PlateId}. {ex.Message}"));
                                continue;
                            }
                        }

                        // Attributes
                        for (int j = 0; j < plate.Attributes.Count; j++)
                        {
                            AttributeModel attribute = plate.Attributes[j];
                            // Load Patch                       
                            if (attribute is LoadPatchTypeAttributeModel lp)
                            {
                                try
                                {
                                    int patchType = 0;
                                    switch (lp.Value.PatchType)
                                    {
                                        case LoadPatchTypeAttributeModel.PatchType.Auto1:
                                            patchType = St7.ptAuto1;
                                            break;
                                        case LoadPatchTypeAttributeModel.PatchType.Auto2:
                                            patchType = St7.ptAuto2;
                                            break;
                                        case LoadPatchTypeAttributeModel.PatchType.Auto3:
                                            patchType = St7.ptAuto3;
                                            break;
                                        case LoadPatchTypeAttributeModel.PatchType.Auto4:
                                            patchType = St7.ptAuto4;
                                            break;
                                        case LoadPatchTypeAttributeModel.PatchType.AngleSplit:
                                            patchType = St7.ptAngleSplit;
                                            break;
                                        case LoadPatchTypeAttributeModel.PatchType.Manual:
                                            patchType = St7.ptManual;
                                            break;
                                    }
                                    string edgesString = "";
                                    for (int e = 1; e <= 4; e++)
                                    {
                                        if (lp.Value.SelectedEdges.Contains(e))
                                            edgesString = "1" + edgesString;
                                        else
                                            edgesString = "0" + edgesString;
                                    }
                                    int edgesBits = Convert.ToInt32(edgesString, 2);
                                    double[] doubles = lp.Value.Weights.ToArray();
                                    StrausRuntime.Check(St7.St7SetPlateLoadPatch4(modelId, plateId, patchType, edgesBits, doubles), "St7SetPlateLoadPatch4");
                                }
                                catch (Exception ex)
                                {
                                    errors.Add(string.Format($"Failed to set load patch {lp.Value.PatchType} on plate {plate.PlateId}. {ex.Message}"));
                                    continue;
                                }
                            }
                            // Face support
                            else if (attribute is PlateSupportAttributeModel support)
                            {
                                try
                                {
                                    int cOnly;
                                    if (support.Value.CompressionOnly)
                                    {
                                        cOnly = St7.btTrue;
                                    }
                                    else
                                    {
                                        cOnly = St7.btFalse;
                                    }
                                    for (int i = 0; i < loadCases.Count; i++)
                                    {
                                        StrausRuntime.Check(St7.St7SetPlateFaceSupport4(modelId, plateId, St7.psPlateMinusZ, loadCaseNameIdMap[loadCases[i].Name],
                                            new int[] { cOnly, St7.btFalse }, new double[] { support.Value.Stiffness, 0, 0, 0 }), "St7SetPlateFaceSupport4");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    errors.Add(string.Format($"Failed to set support on plate {plate.PlateId}. {ex.Message}"));
                                    continue;
                                }
                            }
                        }

                        // Loads
                        for (int i = 0; i < plate.Loads.Count; i++)
                        {
                            LoadModel load = plate.Loads[i];
                            {
                                int lc = loadCaseNameIdMap[load.LoadCase.Name];
                                if (load is PlatePressureModel pp)
                                {
                                    try
                                    {
                                        if (pp.Value.CoordinateSystem == null)
                                        {
                                            StrausRuntime.Check(St7.St7SetPlateNormalPressure2(modelId, plateId, lc, new double[] { pp.Value.P.Z }), "St7SetPlateNormalPressure2");
                                        }
                                        else
                                        {
                                            int projectFlag = pp.Value.Projected ? St7.btTrue : St7.btFalse;
                                            int face = pp.Value.Face == PlatePressureModel.LoadFace.Top ? St7.psPlatePlusZ : St7.psPlateMinusZ;
                                            StrausRuntime.Check(St7.St7SetPlateGlobalPressure3S(modelId, plateId, face, projectFlag, lc, new double[] { pp.Value.P.X, pp.Value.P.Y, pp.Value.P.Z }), "St7SetPlateGlobalPressure3S");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        errors.Add(string.Format($"Failed to set load case {lc} on plate {plate.PlateId}. {ex.Message}"));
                                        continue;
                                    }
                                }
                                else if (load is PlatePreStressModel pps)
                                {
                                    try
                                    {
                                        StrausRuntime.Check(St7.St7SetPlatePreLoad3(modelId, plateId, lc, St7.plPlatePreStress, new double[] { pps.Value.LocalPressure.X, pps.Value.LocalPressure.Y, pps.Value.LocalPressure.Z }), "St7SetPlatePreLoad3");
                                    }
                                    catch (Exception ex)
                                    {
                                        errors.Add(string.Format($"Failed to set load patch {lc} on plate {plate.PlateId}. {ex.Message}"));
                                        continue;
                                    }
                                }
                                else if (load is PlateNSMassModel ns)
                                {
                                    try
                                    {
                                        StrausRuntime.Check(St7.St7SetPlateNSMass5ID(modelId, plateId, lc, 1, new double[5] { ns.Value.Mass, ns.Value.DynFactor, ns.Value.Offset.X, ns.Value.Offset.Y, ns.Value.Offset.Z }), "St7SetPlateNSMass5ID");
                                    }
                                    catch (Exception ex)
                                    {
                                        errors.Add(string.Format($"Failed to set load patch {lc} on plate {plate.PlateId}. {ex.Message}"));
                                        continue;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add(string.Format($"Failed to create plate {plate.PlateId}. {ex.Message}"));
                        continue;
                    }
                }

                #endregion

                // StagedLoadCase
                for (int i = 0; i < loadCases.Count; i++)
                {
                    LoadCaseModel lc = loadCases[i];
                    if (lc.Stages != null)
                    {
                        if (lc.Stages.Any(k => k.BridgePhase != LoadCaseModel.Stage.BridgePhases.Unassigned))
                        {
                            warnings.Add("Stages on load cases not supported");
                            break;
                        }
                    }
                }

                // Defines the Stages for the Staged Analysis
                if (model.StagedConstruction.Count() > 0)
                {
                    int stage = 1;
                    StagedConstructionModel[] stagedConstArr = model.StagedConstruction.ToArray();
                    for (int i = 0; i < stagedConstArr.Length; i++)
                    {
                        try
                        {
                            StagedConstructionModel stagedConstruction = stagedConstArr[i];
                            int[] integers = new[]
                            {
                            stagedConstruction.Marph ? St7.btTrue : St7.btFalse, St7.btFalse,
                            stagedConstruction.Rotate ? St7.btTrue : St7.btFalse
                        };
                            StrausRuntime.Check(St7.St7AddStage(modelId, stagedConstruction.Name, integers), "St7AddStage");
                            StrausRuntime.Check(St7.St7EnableNLAStage(modelId, stage), "St7EnableNLAStage");

                            for (int k = 0; k < groups.Length; k++)
                            {
                                GroupModel group = groups[k];
                                if (stagedConstruction.Groups.Contains(group))
                                    StrausRuntime.Check(St7.St7EnableStageGroup(modelId, stage, groups_ids[group]), "St7EnableStageGroup");
                                else
                                    StrausRuntime.Check(St7.St7DisableStageGroup(modelId, stage, groups_ids[group]), "St7DisableStageGroup");
                            }

                            comboNumber = 1;
                            LoadCombinationModel[] stageConArray = stagedConstruction.LoadCombinations.ToArray();
                            for (int kk = 0; kk < stageConArray.Length; kk++)
                            {
                                LoadCombinationModel combo = stageConArray[kk];
                                if (HandleError(St7.St7AddNLAIncrement(modelId, stage, combo.Name)))
                                {
                                    errors.Add($"Failed to add the NLA increment {combo.Name}");
                                }
                                else
                                {
                                    foreach (KeyValuePair<LoadCaseModel, double> item in combo.Values)
                                    {
                                        //int lc = Convert.ToInt32(loadCases.Single(l => l.Name == item.Key.Name).Id);

                                        if (HandleError(St7.St7SetNLALoadIncrementFactor(modelId, stage, comboNumber, loadCaseNameIdMap[item.Key.Name], item.Value)))
                                            errors.Add($"Failed to set the NLA increment factor {combo.Name}");

                                        if (displLoadCase != null && displLoadCase.Name == item.Key.Name)
                                        {
                                            if (HandleError(St7.St7SetNLAFreedomIncrementFactor(modelId, stage, comboNumber, 1, item.Value)))
                                            {
                                                errors.Add($"Failed to set the NLA freedom case factor {combo.Name}");
                                            }
                                        }
                                    }
                                }
                                comboNumber++;
                            }

                            stage++;
                        }
                        catch (Exception ex)
                        {
                            errors.Add(string.Format($"Failed to create stage {stagedConstArr[i].Name}. {ex.Message}"));
                            continue;
                        }
                    }
                }

                model.GeneratedMovingCases = new Dictionary<int, string>(MovingLoadExporter.Write(modelId, model.MovingPaths, loadCaseNameIdMap));
                if (errors.Count > 0)
                    throw new InvalidOperationException("Esportazione incompleta: correggere gli errori riportati prima dell'analisi.");
                StrausRuntime.Check(St7.St7SaveFile(modelId), "St7SaveFile");
                return true;
            }
            catch (Exception e)
            {
                errors.Add(e.Message);
                return false;
            }
            finally
            {
                if (st7FileOpened) St7.St7CloseFile(modelId);
                StrausRuntime.ReleaseModelId(modelId);
            }
        }

        private static bool HandleError(int errorCode)
        {
            StringBuilder sbErrorString;

            if (errorCode != St7.ERR7_NoError) // Returned an error
            {
                Exception exception = null;

                sbErrorString = new StringBuilder(St7.kMaxStrLen);
                if (St7.ERR7_NoError == St7.St7GetAPIErrorString(errorCode, sbErrorString, sbErrorString.Capacity))
                {
                    exception = new Exception(sbErrorString.ToString());
                }
                else if (St7.ERR7_NoError == St7.St7GetSolverErrorString(errorCode, sbErrorString, sbErrorString.Capacity))
                {
                    exception = new Exception(sbErrorString.ToString());
                }
                if (exception != null)
                {
                    exception.Data.Add("ErrorCode", errorCode);
                    throw exception;
                }
                return true;
            }
            else // No errors
            {
                return false;
            }
        }

        public static bool RotateOf90Deg(BeamModel beam)
        {
            return beam.BeamProperty.SectionType == SectionModel.SectionTypes.Angle && beam.BeamProperty.Mirror != SectionModel.MirrorTypes.None;
        }

        public static bool RotateOf180Deg(BeamModel beam)
        {
            return beam.BeamProperty.AngleX1Rad < -0.000001;
        }

        private static bool GetElementID(ElementModel el, ref List<string> errors, out int eId)
        {
            ElementIdAttributeModel id = (ElementIdAttributeModel)el.Attributes.FirstOrDefault(at => at is ElementIdAttributeModel);
            eId = 0;
            if (id != null)
            {
                if (int.TryParse(id.Value, out eId))
                {
                    return true;
                }
                else
                {
                    errors.Add("Unable to assign id " + id.Value + ". Id for st7 must be an integer value");
                }
            }
            return false;
        }

        public static void ReleaseModelId(int modelId) => StrausRuntime.ReleaseModelId(modelId);
    }
}

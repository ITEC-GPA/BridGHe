using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using GPC.Utilities.Units;

namespace BridGHe.Grasshopper.Components.Checks
{
    public class RCFrameChecksComponent : GH_Component
    {

        /// <summary>
        /// Initializes a new instance of the BeamComponent class.
        /// </summary>
        public RCFrameChecksComponent()
          : base("RC frame checks", "RCFC", "Check RC frames", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_SECTION_CHECKS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            int n;
            pManager.AddGenericParameter("RC Beams", "B", "Beams", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("B,Ascissas", "B,Ls", "Location of sections of beams", GH_ParamAccess.tree);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("RC Pillars", "P", "Pillars", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("P,Ascissas", "P,Ls", "Location of sections of pillars", GH_ParamAccess.tree);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("Long As", "LAs", "Longitudinal reinforcement area of pillars in all sections", GH_ParamAccess.tree);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter(char.ConvertFromUtf32(0x3B3) + "s", char.ConvertFromUtf32(0x3B3) + "s", "Partial safety factor of rebar", GH_ParamAccess.item, 1.15);
            pManager.AddNumberParameter(char.ConvertFromUtf32(0x3B3) + "c", char.ConvertFromUtf32(0x3B3) + "c", "Partial safety factor of concrete", GH_ParamAccess.item, 1.5);
            pManager.AddNumberParameter(char.ConvertFromUtf32(0x3B1) + "cc", char.ConvertFromUtf32(0x3B1) + "cc", char.ConvertFromUtf32(0x3B1) + "cc factor of concrete", GH_ParamAccess.item, 0.85);
            pManager.AddNumberParameter("fyk", "fyk", "Caratheristic yield resistance of rebar", GH_ParamAccess.item);
            n = pManager.AddIntegerParameter("Length unit", "LU", "Units of the length", GH_ParamAccess.item);
            Param_Integer length_param = (Param_Integer)pManager[n];
            foreach (SteelFrameChecksComponent.LengthUnits value in Enum.GetValues(typeof(SteelFrameChecksComponent.LengthUnits)))
                length_param.AddNamedValue(value.GetDescription(), (int)value);
            n = pManager.AddIntegerParameter("Force unit", "FU", "Units of the forces", GH_ParamAccess.item);
            Param_Integer force_param = (Param_Integer)pManager[n];
            foreach (SteelFrameChecksComponent.ForceUnits value in Enum.GetValues(typeof(SteelFrameChecksComponent.ForceUnits)))
                force_param.AddNamedValue(value.GetDescription(), (int)value);
            //pManager.AddNumberParameter("MinDeflectionFactor", "MDF", "Minimum deflection factor (L/factor) for deflection checks", GH_ParamAccess.item, 400);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Beams", "B", "Beams", GH_ParamAccess.list);
            pManager.AddNumberParameter("Beams ascissas", "BAsc", "Ascissas of RC beams", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Beam resistance URs", "BRUR", "The maximum resistance utilization ratios, one for each beam", GH_ParamAccess.tree);
            pManager.AddGenericParameter("Pillars", "P", "Pillars", GH_ParamAccess.list);
            pManager.AddNumberParameter("Pillar ascissas", "PAsc", "Ascissas of RC pillars", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Pillar resistance URs", "PRUR", "The maximum resistance utilization ratios, one for each pillar", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<GH_Beam> beams = new List<GH_Beam>();
            GH_Structure<GH_Number> beamAscissas = null;
            List<GH_Beam> pillars = new List<GH_Beam>();
            GH_Structure<GH_Number> pillarAscissas = null;
            GH_Structure<GH_Number> pillarLongAs = null;
            double gammaS = 0;
            double gammaC = 0;
            double alfaCC = 0;
            double fyk = 0;
            int lengthValue = -1;
            int forceValue = -1;
            //double minDeflectionFactor = 0;

            DA.GetDataList(0, beams);
            DA.GetDataTree(1, out beamAscissas);
            DA.GetDataList(2, pillars);
            DA.GetDataTree(3, out pillarAscissas);
            DA.GetDataTree(4, out pillarLongAs);
            if (!DA.GetData(5, ref gammaS))
                return;
            if (!DA.GetData(6, ref gammaC))
                return;
            if (!DA.GetData(7, ref alfaCC))
                return;
            if (!DA.GetData(8, ref fyk))
                return;
            if (!DA.GetData(9, ref lengthValue))
                return;
            if (!DA.GetData(10, ref forceValue))
                return;

            if (pillars.Count == 0 && beams.Count == 0)
            {
                return;
            }


            if (pillars.Count > 0)
            {
                if (pillars.Count != pillarAscissas.Branches.Count ||
                    pillars.Count != pillarLongAs.Branches.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data dimension");
                    return;
                }
                for (int i = 0; i < pillars.Count; i++)
                {
                    if (pillarAscissas[i].Count != pillarLongAs[i].Count)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data dimension");
                        return;
                    }
                }
            }
            if (beams.Count > 0)
            {
                if (beams.Count != beamAscissas.Branches.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data dimension");
                    return;
                }
            }

            Dictionary<BeamModel, List<double>> beamAscissasList, pillarAscissasList;
            Dictionary<BeamModel, List<double>> pillarLongAsList;
            Dictionary<BeamModel, List<double>> beamURs = new Dictionary<BeamModel, List<double>>();
            Dictionary<BeamModel, List<double>> pillarURs = new Dictionary<BeamModel, List<double>>();
            SteelFrameChecksComponent.LengthUnits lengthUnits;
            SteelFrameChecksComponent.ForceUnits forceUnits;
            double fyd;
            double toll;
            List<double> bufferURs;

            lengthUnits = (SteelFrameChecksComponent.LengthUnits)lengthValue;
            forceUnits = (SteelFrameChecksComponent.ForceUnits)forceValue;

            UnitsConvert.ForceUnits utilsForceCode;
            UnitsConvert.LengthUnits utilsLengthCode;
            switch (forceUnits)
            {
                case SteelFrameChecksComponent.ForceUnits.N:
                    utilsForceCode = UnitsConvert.ForceUnits.N;
                    break;
                case SteelFrameChecksComponent.ForceUnits.daN:
                    utilsForceCode = UnitsConvert.ForceUnits.daN;
                    break;
                case SteelFrameChecksComponent.ForceUnits.kN:
                    utilsForceCode = UnitsConvert.ForceUnits.kN;
                    break;
                default:
                    throw new NotSupportedException();
            }

            switch (lengthUnits)
            {
                case SteelFrameChecksComponent.LengthUnits.mm:
                    utilsLengthCode = UnitsConvert.LengthUnits.mm;
                    break;
                case SteelFrameChecksComponent.LengthUnits.cm:
                    utilsLengthCode = UnitsConvert.LengthUnits.cm;
                    break;
                case SteelFrameChecksComponent.LengthUnits.m:
                    utilsLengthCode = UnitsConvert.LengthUnits.m;
                    break;
                default:
                    throw new NotSupportedException();
            }

            beamAscissasList = new Dictionary<BeamModel, List<double>>();
            for (int i = 0; i < beams.Count; i++)
            {
                List<double> ascissas;
                ascissas = new List<double>();
                for (int j = 0; j < beamAscissas[i].Count; j++)
                {
                    ascissas.Add(beamAscissas[i][j].Value);
                }
                beamAscissasList.Add(beams[i].Value, ascissas);
            }

            pillarAscissasList = new Dictionary<BeamModel, List<double>>();
            pillarLongAsList = new Dictionary<BeamModel, List<double>>();
            for (int i = 0; i < pillars.Count; i++)
            {
                List<double> ascissas, longAs;
                ascissas = new List<double>();
                for (int j = 0; j < pillarAscissas[i].Count; j++)
                {
                    ascissas.Add(pillarAscissas[i][j].Value);
                }
                pillarAscissasList.Add(pillars[i].Value, ascissas);

                longAs = new List<double>();
                for (int j = 0; j < pillarLongAs[i].Count; j++)
                {
                    longAs.Add(pillarLongAs[i][j].Value);
                }
                pillarLongAsList.Add(pillars[i].Value, longAs);
            }


            switch (lengthUnits)
            {
                case SteelFrameChecksComponent.LengthUnits.cm:
                    toll = 0.01;
                    break;
                case SteelFrameChecksComponent.LengthUnits.m:
                    toll = 0.0001;
                    break;
                case SteelFrameChecksComponent.LengthUnits.mm:
                    toll = 0.1;
                    break;
                default:
                    throw new NotSupportedException();
            }

            fyd = fyk / gammaS;

            //Beam Resistance
            for (int counter = 0; counter < beams.Count; counter++)
            {
                List<double> ascissas;
                GH_Beam beam;
                BeamPropertyModel pp;
                double aCls, w11Cls, w22Cls, fck, fcd;

                beam = beams[counter];
                ascissas = beamAscissasList[beam.Value];
                pp = beam.Value.BeamProperty;

                aCls = pp.SectionArea;
                w11Cls = pp.W11;
                w22Cls = pp.W22;

                fck = UnitsConvert.ConvertFromDefaultUnits(pp.Material.SpecificCompressiveStrength, utilsForceCode, 1, utilsLengthCode, -2);
                //switch (lengthCode)
                //{
                //    case SteelFrameChecksComponent.LengthUnits.cm:
                //        fck /= 10000;
                //        break;
                //    case SteelFrameChecksComponent.LengthUnits.m:
                //        break;
                //    case SteelFrameChecksComponent.LengthUnits.mm:
                //        fck /= 1000000;
                //        break;
                //    default:
                //        throw new NotSupportedException();
                //}
                //switch (forceCode)
                //{
                //    case SteelFrameChecksComponent.ForceUnits.daN:
                //        fck /= 10;
                //        break;
                //    case SteelFrameChecksComponent.ForceUnits.kN:
                //        fck /= 1000;
                //        break;
                //    case SteelFrameChecksComponent.ForceUnits.N:
                //        break;
                //    default:
                //        throw new NotSupportedException();
                //}
                fcd = fck * alfaCC / gammaC;

                if (!beamURs.TryGetValue(beam.Value, out bufferURs))
                {
                    bufferURs = new List<double>();
                    for (int ascCount = 0; ascCount < ascissas.Count; ascCount++)
                    {
                        bufferURs.Add(0);
                    }
                    beamURs.Add(beam.Value, bufferURs);
                }

                BeamForceResultModel[] results = beam.Value.Results.
                        Where(r => r.GetType() == typeof(BeamForceResultModel)).Cast<BeamForceResultModel>().ToArray();
                for (int resCounter = 0; resCounter < results.Length; resCounter++)
                {
                    double n, m11, m22;
                    int ascissaIndex;
                    BeamForceResultModel res;
                    double sigmaC;

                    res = results[resCounter];

                    n = res.AxialForce;
                    m11 = res.BendingMoment.Y;
                    m22 = res.BendingMoment.X;

                    ascissaIndex = -1;
                    for (int i = 0; i < ascissas.Count; i++)
                    {
                        if (Math.Abs(ascissas[i] - res.Station) < toll)
                        {
                            ascissaIndex = i;
                            break;
                        }
                    }
                    if (ascissaIndex < 0)
                    {
                        throw new NotSupportedException("Ascissa not found");
                    }

                    //determining max sigma compression on cls
                    sigmaC = -Math.Abs(m11) / w11Cls - Math.Abs(m22) / w22Cls + n / aCls;

                    if (sigmaC < 0)
                    {
                        bufferURs[ascissaIndex] = Math.Max(bufferURs[ascissaIndex], Math.Abs(sigmaC) / fcd);
                    }
                }
            }

            //Pillars Resistance
            for (int counter = 0; counter < pillars.Count; counter++)
            {
                List<double> ascissas;
                GH_Beam pillar;
                double dimension11, dimension22;
                BeamPropertyModel pp;
                double aCls, fck, fcd;

                pillar = pillars[counter];
                ascissas = pillarAscissasList[pillar.Value];
                pp = pillar.Value.BeamProperty;

                switch (pp.SectionType)
                {
                    case SectionModel.SectionTypes.SolidRectangle:
                        {
                            dimension11 = pp.B;
                            dimension22 = pp.D;
                            break;
                        }
                    default:
                        throw new NotImplementedException("Section not supported");
                }

                aCls = dimension11 * dimension22;
                fck = UnitsConvert.ConvertFromDefaultUnits(pp.Material.SpecificCompressiveStrength, utilsForceCode, 1, utilsLengthCode, -2);

                fcd = fck * alfaCC / gammaC;

                if (!pillarURs.TryGetValue(pillar.Value, out bufferURs))
                {
                    bufferURs = new List<double>();
                    for (int ascCount = 0; ascCount < ascissas.Count; ascCount++)
                    {
                        bufferURs.Add(0);
                    }
                    pillarURs.Add(pillar.Value, bufferURs);
                }

                BeamForceResultModel[] results = pillar.Value.Results.
                        Where(r => r.GetType() == typeof(BeamForceResultModel)).Cast<BeamForceResultModel>().ToArray();
                for (int resCounter = 0; resCounter < results.Length; resCounter++)
                {
                    double n;
                    double aS;
                    double nRd;
                    int ascissaIndex;
                    BeamForceResultModel res;

                    res = results[resCounter];

                    n = res.AxialForce;

                    ascissaIndex = -1;
                    for (int i = 0; i < ascissas.Count; i++)
                    {
                        if (Math.Abs(ascissas[i] - res.Station) < toll)
                        {
                            ascissaIndex = i;
                            break;
                        }
                    }
                    if (ascissaIndex < 0)
                    {
                        throw new NotSupportedException("Ascissa not found");
                    }

                    aS = pillarLongAsList[pillar.Value][ascissaIndex];

                    nRd = aCls * fcd + aS * fyd;

                    //Longitudinal URs
                    bufferURs[ascissaIndex] = Math.Max(bufferURs[ascissaIndex], Math.Abs(n) / nRd);
                }
            }

            ////Beams Deflection
            //List<CreateBeamReinforcementComponent.BeamData> disjoinedBeams;
            //disjoinedBeams = new List<CreateBeamReinforcementComponent.BeamData>();
            //foreach (GH_Beam beam in beams)
            //{
            //    disjoinedBeams.Add(new CreateBeamReinforcementComponent.BeamData(beam.Value));
            //}
            ////attaching subsequent beams
            //List<CreateBeamReinforcementComponent.BeamDataSequence> seqs;
            //seqs = CreateBeamReinforcementComponent.BeamDataSequence.GetSequences(disjoinedBeams, model.Value);
            //foreach (int beam in beamList)
            //{
            //    beamDeformationURs.Add(beam, 0);
            //}
            //if (cases == 2 || cases == 0)
            //{
            //    //LoadCases
            //    for (int caseCounter = 1; caseCounter <= primary; caseCounter++)
            //    {
            //        if (model.Value.LoadCases[caseCounter - 1].Name.StartsWith(CombinationComponent.SLS_PREFIX))
            //        {
            //            Straus7St7SteelFrameCheckComponent.GetDeformationChecks(seqs, model, modelId, caseCounter, minDeflectionFactor,
            //                                                                    beamAscissasList, ref beamDeformationURs);
            //        }
            //    }
            //}

            //if (cases == 2 || cases == 1)
            //{
            //    //Combs
            //    for (int caseCounter = primary + 1; caseCounter <= primary + secondary; caseCounter++)
            //    {
            //        int combinationNumber;
            //        combinationNumber = caseCounter - primary - 1;
            //        if (model.Value.LoadCaseCombinations[combinationNumber].Name.StartsWith(CombinationComponent.SLS_PREFIX))
            //        {
            //            Straus7St7SteelFrameCheckComponent.GetDeformationChecks(seqs, model, modelId, caseCounter, minDeflectionFactor,
            //                                                                    beamAscissasList, ref beamDeformationURs);
            //        }
            //    }
            //}

            List<GH_Beam> beamList, pillarList;

            beamList = new List<GH_Beam>();
            foreach (KeyValuePair<BeamModel, List<double>> kvp in beamURs)
            {
                beamList.Add(new GH_Beam(kvp.Key));
            }
            pillarList = new List<GH_Beam>();
            foreach (KeyValuePair<BeamModel, List<double>> kvp in pillarURs)
            {
                pillarList.Add(new GH_Beam(kvp.Key));
            }

            DA.SetDataList(0, beamList);
            DA.SetDataTree(1, beamAscissas);
            DA.SetDataTree(2, SteelFrameChecksComponent.GetStructure(beamURs));
            DA.SetDataList(3, pillarList);
            DA.SetDataTree(4, pillarAscissas);
            DA.SetDataTree(5, SteelFrameChecksComponent.GetStructure(pillarURs));
        }


        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("fe7f2ad0-e6db-488c-befd-6077043c839d");
    }
}
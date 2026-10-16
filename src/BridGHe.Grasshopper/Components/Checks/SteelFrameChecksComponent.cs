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
    public class SteelFrameChecksComponent : GH_Component
    {
        public enum LengthUnits
        {
            mm,
            cm,
            m,
            inch
        }

        public enum ForceUnits
        {
            N,
            daN,
            kN,
            lbf
        }

        /// <summary>
        /// Initializes a new instance of the BeamComponent class.
        /// </summary>
        public SteelFrameChecksComponent()
          : base("Steel frame checks", "SFC", "Check steel frames", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_STEELCHECKS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            int n;
            pManager.AddGenericParameter("Frames", "Bs", "The frames containing results", GH_ParamAccess.list);
            pManager.AddNumberParameter(char.ConvertFromUtf32(0x3B3) + "M0", char.ConvertFromUtf32(0x3B3) + "M0", "Partial safety factor on resistance checks", GH_ParamAccess.item, 1.05);
            pManager.AddNumberParameter(char.ConvertFromUtf32(0x3B3) + "M1", char.ConvertFromUtf32(0x3B3) + "M1", "Partial safety factor on buckling checks", GH_ParamAccess.item, 1.05);
            //pManager.AddNumberParameter("Min deflection factor", "MinDF", "Minimum deflection factor allowed for beam", GH_ParamAccess.item);
            //pManager.AddNumberParameter("Max allowed deflection", "MaxD", "Maximum deflection admitted", GH_ParamAccess.item);
            n = pManager.AddIntegerParameter("Length unit", "LU", "Units of the length", GH_ParamAccess.item);
            Param_Integer length_param = (Param_Integer)pManager[n];
            foreach (LengthUnits value in Enum.GetValues(typeof(LengthUnits)))
                length_param.AddNamedValue(value.GetDescription(), (int)value);
            n = pManager.AddIntegerParameter("Force unit", "FU", "Units of the forces", GH_ParamAccess.item);
            Param_Integer force_param = (Param_Integer)pManager[n];
            foreach (ForceUnits value in Enum.GetValues(typeof(ForceUnits)))
                force_param.AddNamedValue(value.GetDescription(), (int)value);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Steel Frames", "SFs", "Steel frames where results are calculated", GH_ParamAccess.list);
            pManager.AddNumberParameter("Ascissas", "As", "Ascissas of Frames where the UR are calculated", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Resistance URs", "RUR", "The maximum resistance utilization ratios, one for each beam", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Buckling URs", "BUR", "The maximum buckling utilization ratios, one for each beam", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<GH_Beam> frames = new List<GH_Beam>();
            double gammaM0 = 0;
            double gammaM1 = 0;
            //double minDeflectionFactor = 0;
            //double maxDeflectionAdmitted = 0;
            int lengthValue = -1;
            int forceValue = -1;
            //double toll;
            LengthUnits lengthUnits;
            ForceUnits forceUnits;
            if (!DA.GetDataList(0, frames))
                return;
            if (!DA.GetData(1, ref gammaM0))
                return;
            if (!DA.GetData(2, ref gammaM1))
                return;
            //if (!DA.GetData("fyk", ref fyk))
            //    return;
            //if (!DA.GetData("Min deflection factor", ref minDeflectionFactor))
            //    return;
            //if (!DA.GetData("Max allowed deflection", ref maxDeflectionAdmitted))
            //    return;
            if (!DA.GetData(3, ref lengthValue))
                return;
            if (!DA.GetData(4, ref forceValue))
                return;

            Dictionary<BeamModel, List<double>> ascissas, resistanceUrs, bucklingUrs;
            ascissas = new Dictionary<BeamModel, List<double>>();
            resistanceUrs = new Dictionary<BeamModel, List<double>>();
            bucklingUrs = new Dictionary<BeamModel, List<double>>();
            lengthUnits = (LengthUnits)lengthValue;
            forceUnits = (ForceUnits)forceValue;
            foreach (GH_Beam beam in frames)
            {
                if (beam.Value.BeamProperty.Material.Kind == MaterialModel.MaterialKind.Steel)
                {
                    double area, w11, w22, e, l0, j11, j22;
                    List<double> beamAscissas, beamResistanceUrs, beamBucklingUrs;
                    double nCr11, lambdaAdim11, nCr22, lambdaAdim22;
                    double chi11, chi22, chi;
                    BeamPropertyModel bp;
                    double nbRd;
                    double sigmaRd;
                    double fyk;
                    double angleXTo1;

                    //props
                    bp = beam.Value.BeamProperty;
                    area = bp.SectionArea;
                    w11 = bp.W11;
                    w22 = bp.W22;
                    l0 = (beam.Value.PointFrom - beam.Value.PointTo).Length;
                    j11 = bp.I11;
                    j22 = bp.I22;
                    angleXTo1 = beam.Value.BeamProperty.AngleX1Rad;

                    //Getting E,fyk in units. In material E,fyk is on N mm
                    UnitsConvert.ForceUnits utilsForceUnits;
                    UnitsConvert.LengthUnits utilsLengthUnits;
                    switch (forceUnits)
                    {
                        case ForceUnits.N:
                            utilsForceUnits = UnitsConvert.ForceUnits.N;
                            break;
                        case ForceUnits.daN:
                            utilsForceUnits = UnitsConvert.ForceUnits.daN;
                            break;
                        case ForceUnits.kN:
                            utilsForceUnits = UnitsConvert.ForceUnits.kN;
                            break;
                        case ForceUnits.lbf:
                            utilsForceUnits = UnitsConvert.ForceUnits.lbf;
                            break;
                        default:
                            throw new NotSupportedException();
                    }

                    switch (lengthUnits)
                    {
                        case LengthUnits.mm:
                            utilsLengthUnits = UnitsConvert.LengthUnits.mm;
                            break;
                        case LengthUnits.cm:
                            utilsLengthUnits = UnitsConvert.LengthUnits.cm;
                            break;
                        case LengthUnits.m:
                            utilsLengthUnits = UnitsConvert.LengthUnits.m;
                            break;
                        case LengthUnits.inch:
                            utilsLengthUnits = UnitsConvert.LengthUnits.inch;
                            break;
                        default:
                            throw new NotSupportedException();
                    }

                    e = UnitsConvert.ConvertFromDefaultUnits(bp.Material.Modulus, utilsForceUnits, 1, utilsLengthUnits, -2);
                    fyk = UnitsConvert.ConvertFromDefaultUnits(bp.Material.MinimumYieldStress, utilsForceUnits, 1, utilsLengthUnits, -2);
                    //e = bp.Material.Modulus;
                    //fyk = bp.Material.MinimumYieldStress;
                    //switch (forceCode)
                    //{
                    //    case ForceCodes.N:
                    //        break;
                    //    case ForceCodes.daN:
                    //        e /= 10d;
                    //        fyk /= 10d;
                    //        break;
                    //    case ForceCodes.kN:
                    //        e /= 1000d;
                    //        fyk /= 1000d;
                    //        break;
                    //    default:
                    //        throw new NotSupportedException();
                    //}
                    //switch (lengthCode)
                    //{
                    //    case LengthCodes.mm:
                    //        e /= Math.Pow(10d, 6d);
                    //        fyk /= Math.Pow(10d, 6d);
                    //        break;
                    //    case LengthCodes.cm:
                    //        e /= Math.Pow(10d, 4d);
                    //        fyk /= Math.Pow(10d, 4d);
                    //        break;
                    //    case LengthCodes.m:
                    //        break;
                    //    default:
                    //        throw new NotSupportedException();
                    //}
                    sigmaRd = fyk / gammaM0;

                    //buckling resistance
                    nCr11 = Math.PI * Math.PI * e * j11 / (l0 * l0);
                    lambdaAdim11 = Math.Sqrt(area * fyk / nCr11);

                    if (lambdaAdim11 > 0.2)
                    {
                        double alfa;
                        double fi;
                        switch (bp.SectionType)
                        {
                            case SectionModel.SectionTypes.SolidCircle:
                                alfa = 0.49;
                                break;
                            case SectionModel.SectionTypes.Undefined:
                                alfa = 0.76;
                                break;
                            case SectionModel.SectionTypes.HollowCircle:
                                alfa = 0.49;
                                break;
                            case SectionModel.SectionTypes.SolidRectangle:
                                alfa = 0.49;
                                break;
                            case SectionModel.SectionTypes.HollowRectangle:
                                alfa = 0.49;
                                break;
                            case SectionModel.SectionTypes.I:
                                if (Math.Max(bp.B1, bp.B2) * 1.2 > bp.D)
                                {
                                    alfa = 0.34;
                                }
                                else
                                {
                                    alfa = 0.21;
                                }
                                break;
                            case SectionModel.SectionTypes.T:
                                alfa = 0.49;
                                break;
                            case SectionModel.SectionTypes.C:
                                alfa = 0.49;
                                break;
                            case SectionModel.SectionTypes.Omega:
                                alfa = 0.76;
                                break;
                            case SectionModel.SectionTypes.Angle:
                                alfa = 0.34;
                                break;
                            case SectionModel.SectionTypes.Z:
                                alfa = 0.49;
                                break;
                            default:
                                throw new NotSupportedException();
                        }

                        fi = 0.5 * (1 + alfa * (lambdaAdim11 - 0.2) + lambdaAdim11 * lambdaAdim11);
                        chi11 = Math.Min(1, 1 / (fi + Math.Sqrt(fi * fi - lambdaAdim11 * lambdaAdim11)));
                    }
                    else
                    {
                        chi11 = 1;
                    }

                    nCr22 = Math.PI * Math.PI * e * j22 / (l0 * l0);
                    lambdaAdim22 = Math.Sqrt(area * fyk / nCr22);
                    if (lambdaAdim22 > 0.2)
                    {
                        double alfa;
                        double fi;
                        switch (bp.SectionType)
                        {
                            case SectionModel.SectionTypes.SolidCircle:
                                alfa = 0.49;
                                break;
                            case SectionModel.SectionTypes.Undefined:
                                alfa = 0.76;
                                break;
                            case SectionModel.SectionTypes.HollowCircle:
                                alfa = 0.49;
                                break;
                            case SectionModel.SectionTypes.SolidRectangle:
                                alfa = 0.49;
                                break;
                            case SectionModel.SectionTypes.HollowRectangle:
                                alfa = 0.49;
                                break;
                            case SectionModel.SectionTypes.I:
                                if (Math.Max(bp.B1, bp.B2) * 1.2 > bp.D)
                                {
                                    alfa = 0.49;
                                }
                                else
                                {
                                    alfa = 0.34;
                                }
                                break;
                            case SectionModel.SectionTypes.T:
                                alfa = 0.49;
                                break;
                            case SectionModel.SectionTypes.C:
                                alfa = 0.49;
                                break;
                            case SectionModel.SectionTypes.Omega:
                                alfa = 0.76;
                                break;
                            case SectionModel.SectionTypes.Angle:
                                alfa = 0.34;
                                break;
                            case SectionModel.SectionTypes.Z:
                                alfa = 0.49;
                                break;
                            default:
                                throw new NotSupportedException();
                        }
                        fi = 0.5 * (1 + alfa * (lambdaAdim22 - 0.2) + lambdaAdim22 * lambdaAdim22);
                        chi22 = Math.Min(1, 1 / (fi + Math.Sqrt(fi * fi - lambdaAdim22 * lambdaAdim22)));
                    }
                    else
                    {
                        chi22 = 1;
                    }
                    chi = Math.Min(chi11, chi22);

                    nbRd = chi * area * fyk / gammaM1;

                    beamAscissas = new List<double>();
                    beamResistanceUrs = new List<double>();
                    beamBucklingUrs = new List<double>();

                    BeamForceResultModel[] results = beam.Value.Results.
                        Where(r => r.GetType() == typeof(BeamForceResultModel)).Cast<BeamForceResultModel>().ToArray();
                    for (int resCounter = 0; resCounter < results.Length; resCounter++)
                    {
                        double n, mx, my, m1, m2, sigmaEd;
                        int index;
                        double resUr, buckUr;
                        n = beam.Value.Results.Cast<BeamForceResultModel>().ElementAt(resCounter).AxialForce;
                        mx = results[resCounter].BendingMoment.Y;//asse 3 di sap e 1 di st7
                        my = results[resCounter].BendingMoment.X;//asse 2 di sap e st7

                        //ruoto i momenti nel sdr principale
                        m1 = mx * Math.Cos(angleXTo1) + my * Math.Sin(angleXTo1);
                        m2 = -mx * Math.Sin(angleXTo1) + my * Math.Cos(angleXTo1);

                        //resistance
                        sigmaEd = Math.Abs(n / area) + Math.Abs(m1 / w11) + Math.Abs(m2 / w22);
                        resUr = sigmaEd / sigmaRd;

                        //buckling
                        if (n >= 0)
                        {
                            buckUr = 0;
                        }
                        else
                        {
                            buckUr = Math.Abs(n / nbRd);
                        }

                        index = beamAscissas.IndexOf(results[resCounter].Station);
                        if (index < 0)
                        {
                            beamAscissas.Add(results[resCounter].Station);
                            beamResistanceUrs.Add(resUr);
                            beamBucklingUrs.Add(buckUr);
                        }
                        else
                        {
                            beamResistanceUrs[index] = Math.Max(beamResistanceUrs[index], resUr);
                            beamBucklingUrs[index] = Math.Max(beamBucklingUrs[index], buckUr);
                        }
                    }
                    ascissas.Add(beam.Value, beamAscissas);
                    resistanceUrs.Add(beam.Value, beamResistanceUrs);
                    bucklingUrs.Add(beam.Value, beamBucklingUrs);
                }
            }



            //deformation checks TODO Emanuele mancano le deformate nelle beam
            //switch (lengthCode)
            //{
            //    case LengthCodes.mm:
            //        toll = 0.1;
            //        break;
            //    case LengthCodes.cm:
            //        toll = 0.01;
            //        break;
            //    case LengthCodes.m:
            //        toll = 0.0001;
            //        break;
            //    default:
            //        throw new NotSupportedException();
            //} //List<BeamResultsSequence> seqs;
            //seqs = BeamResultsSequence.GetSequences(beams, toll);

            List<GH_Beam> steelFrames;
            steelFrames = new List<GH_Beam>();
            foreach (BeamModel bm in ascissas.Keys)
            {
                steelFrames.Add(new GH_Beam(bm));
            }
            DA.SetDataList(0, steelFrames);
            DA.SetDataTree(1, GetStructure(ascissas));
            DA.SetDataTree(2, GetStructure(resistanceUrs));
            DA.SetDataTree(3, GetStructure(bucklingUrs));

        }
        public static GH_Structure<GH_Number> GetStructure(Dictionary<BeamModel, List<double>> dic)
        {
            GH_Structure<GH_Number> result;
            result = new GH_Structure<GH_Number>();
            int i = 0;
            foreach (KeyValuePair<BeamModel, List<double>> kvp in dic)
            {
                GH_Path path;
                List<GH_Number> bufferNum;
                path = new GH_Path(new int[] { i, 0 });
                result.EnsurePath(path);
                bufferNum = new List<GH_Number>();
                foreach (double value in kvp.Value)
                {
                    bufferNum.Add(new GH_Number(value));
                }
                result.AppendRange(bufferNum, path);
                i++;
            }
            return result;
        }

        //public static void GetDeformationChecks(List<BeamResultsSequence> seqs,
        //                                        double minDeflectionFactor,
        //                                        Dictionary<BeamModel, List<double>> beamAscissasList,
        //                                        ref Dictionary<int, double> deformationURs)
        //{            //deformation checks
        //    foreach (BeamResultsSequence seq in seqs)
        //    {
        //        double unCm;
        //        double lengthFromStart;
        //        List<double> bufferAscissas, bufferDisps;
        //        bool isStartRestrained, isEndRestrained;
        //        List<double> pillarPoss;
        //        lengthFromStart = 0;
        //        bufferAscissas = new List<double>();
        //        bufferDisps = new List<double>();
        //        lengthFromStart = 0;
        //        pillarPoss = new List<double>();
        //        foreach (GH_BeamResults data in seq.Sequence)
        //        {
        //            double[] beamRes;
        //            int numColumns = 0;
        //            List<double> ascissas;
        //            List<double> fzs;
        //            Point3d startP, endP;
        //            double dataLength;

        //            //start = model.Value.GetNode(data.Beam.StartNode);
        //            //end = model.Value.GetNode(data.Beam.EndNode);
        //            startP = data.Beam.PointFrom;// new Rhino.Geometry.Point3d(start.X, start.Y, start.Z);
        //            endP = data.Beam.PointTo; //new Rhino.Geometry.Point3d(end.X, end.Y, end.Z);
        //            dataLength = (startP - endP).Length;

        //            ascissas = beamAscissasList[data.Beam];
        //            for (int counter = 0; counter < ascissas.Count; counter++)
        //            {
        //                fzs.Add(beamRes[counter * numColumns + 2]);
        //            }
        //            if (seq.IsEquiverseTo(data))
        //            {
        //                for (int i = 0; i < ascissas.Count; i++)
        //                {
        //                    ascissas[i] += lengthFromStart;
        //                }
        //                CreateBeamReinforcementComponent.AddPillar(model, data, lengthFromStart, data.Beam.StartNode, ref pillarPoss);
        //                lengthFromStart += dataLength;
        //                CreateBeamReinforcementComponent.AddPillar(model, data, lengthFromStart, data.Beam.EndNode, ref pillarPoss);
        //            }
        //            else
        //            {
        //                CreateBeamReinforcementComponent.AddPillar(model, data, lengthFromStart, data.Beam.EndNode, ref pillarPoss);
        //                lengthFromStart += dataLength;
        //                for (int i = ascissas.Count - 1; i >= 0; i--)
        //                {
        //                    ascissas[i] = lengthFromStart - ascissas[i];
        //                }
        //                CreateBeamReinforcementComponent.AddPillar(model, data, lengthFromStart, data.Beam.StartNode, ref pillarPoss);
        //                fzs.Reverse();
        //            }
        //            bufferAscissas.AddRange(ascissas);
        //            bufferDisps.AddRange(fzs);
        //        }

        //        List<double> modifiedPillarPoss;
        //        pillarPoss.Sort();
        //        if (pillarPoss.Count == 0)
        //        {
        //            //beam sostained from other beams. Adding a restrain at start and at end
        //            isStartRestrained = true;
        //            isEndRestrained = true;
        //            modifiedPillarPoss = new List<double>();
        //            modifiedPillarPoss.Add(0);
        //            modifiedPillarPoss.Add(lengthFromStart);
        //        }
        //        else
        //        {
        //            unCm = CreateBeamReinforcementComponent.ConvertLengthTo(1, "cm", model.Value.LengthUnit, 1);
        //            isStartRestrained = Math.Abs(pillarPoss[0]) < unCm;
        //            isEndRestrained = Math.Abs(pillarPoss[pillarPoss.Count - 1] - lengthFromStart) < unCm;
        //            modifiedPillarPoss = new List<double>(pillarPoss);
        //            if (!isStartRestrained)
        //            {
        //                modifiedPillarPoss.Insert(0, 0);
        //            }
        //            if (!isEndRestrained)
        //            {
        //                modifiedPillarPoss.Add(lengthFromStart);
        //            }
        //        }
        //        for (int i = 0; i < modifiedPillarPoss.Count - 1; i++)
        //        {
        //            bool bufferIsStartRestrained, bufferIsEndRestrained;
        //            List<double> ascissas;
        //            List<double> fzs;
        //            double prevPillar, nextPillar;
        //            if ((i == 0) && (!isStartRestrained))
        //            {
        //                bufferIsStartRestrained = false;
        //            }
        //            else
        //            {
        //                bufferIsStartRestrained = true;
        //            }
        //            if ((i == pillarPoss.Count - 2) && (!isEndRestrained))
        //            {
        //                bufferIsEndRestrained = false;
        //            }
        //            else
        //            {
        //                bufferIsEndRestrained = true;
        //            }
        //            prevPillar = modifiedPillarPoss[i];
        //            nextPillar = modifiedPillarPoss[i + 1];
        //            ascissas = new List<double>();
        //            fzs = new List<double>();
        //            for (int index = 0; index < bufferAscissas.Count; index++)
        //            {
        //                if ((bufferAscissas[index] >= prevPillar) &&
        //                    (bufferAscissas[index] <= nextPillar))
        //                {
        //                    //inside pillars
        //                    ascissas.Add(bufferAscissas[index]);
        //                    fzs.Add(bufferDisps[index]);
        //                }
        //            }
        //            if (ascissas.Count > 0)
        //            {
        //                double maxDz;
        //                double length;
        //                double lengthCm;
        //                maxDz = 0;
        //                length = nextPillar - prevPillar;
        //                lengthCm = CreateBeamReinforcementComponent.ConvertLengthTo(length, model.Value.LengthUnit, "cm", 1);
        //                if (lengthCm > 100)
        //                {
        //                    if (!bufferIsStartRestrained)
        //                    {
        //                        //cantilever start. Net displacement depure at end
        //                        foreach (double dispZ in fzs)
        //                        {
        //                            maxDz = Math.Max(maxDz, Math.Abs(dispZ - fzs[fzs.Count - 1]));
        //                        }
        //                        length *= 2;
        //                    }
        //                    else if (!bufferIsEndRestrained)
        //                    {
        //                        //cantilever end. Net displacement depure at start
        //                        foreach (double dispZ in fzs)
        //                        {
        //                            maxDz = Math.Max(maxDz, Math.Abs(dispZ - fzs[0]));
        //                        }
        //                        length *= 2;
        //                    }
        //                    else
        //                    {
        //                        double firstDisp, lastDisp;
        //                        //simply supported beam
        //                        firstDisp = fzs[0];
        //                        lastDisp = fzs[fzs.Count - 1];
        //                        for (int index = 0; index < ascissas.Count; index++)
        //                        {
        //                            double rigidDisp;
        //                            rigidDisp = fzs[0] + (fzs[fzs.Count - 1] - fzs[0]) / length * (ascissas[index] - ascissas[0]);
        //                            maxDz = Math.Max(maxDz, Math.Abs(fzs[index] - rigidDisp));
        //                        }
        //                    }
        //                    double ur;
        //                    double limitDeflection;
        //                    limitDeflection = length / minDeflectionFactor;
        //                    ur = maxDz / limitDeflection;

        //                    foreach (CreateBeamReinforcementComponent.BeamData data in seq.Sequence)
        //                    {
        //                        deformationURs[data.Beam.Id] = Math.Max(deformationURs[data.Beam.Id], ur);
        //                    }
        //                }
        //            }
        //        }
        //    }
        //}



        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("c42d7f54-7567-4f8a-8835-887f27af69eb");
    }
}
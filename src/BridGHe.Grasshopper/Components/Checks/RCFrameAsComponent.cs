using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using GPC.Utilities.Units;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Checks
{
    public class RCFrameAsComponent : GH_Component
    {

        /// <summary>
        /// Initializes a new instance of the BeamComponent class.
        /// </summary>
        public RCFrameAsComponent()
          : base("RC frames As", "RCFAs", "Calculate the reinforcement area of RC frames", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_MECHECK)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            int n;
            pManager.AddGenericParameter("Frames", "Fs", "The frames containing results", GH_ParamAccess.list);
            pManager.AddNumberParameter("Top beam cover", "TBC", "Top cover of beams", GH_ParamAccess.item);
            pManager.AddNumberParameter("Bottom beam cover", "BBC", "Bottom cover of beams", GH_ParamAccess.item);
            pManager.AddNumberParameter("Pillar cover", "PC", "Cover of pillars", GH_ParamAccess.item);
            pManager.AddNumberParameter(char.ConvertFromUtf32(0x3B3) + "s", char.ConvertFromUtf32(0x3B3) + "s", "Partial safety factor of rebar", GH_ParamAccess.item, 1.15);
            pManager.AddNumberParameter("fyk", "fyk", "Caratheristic yield resistance of rebar", GH_ParamAccess.item);
            n = pManager.AddIntegerParameter("Length unit", "LU", "Units of the length", GH_ParamAccess.item);
            Param_Integer length_param = (Param_Integer)pManager[n];
            foreach (SteelFrameChecksComponent.LengthUnits value in Enum.GetValues(typeof(SteelFrameChecksComponent.LengthUnits)))
                length_param.AddNamedValue(value.GetDescription(), (int)value);
            n = pManager.AddIntegerParameter("Force unit", "FU", "Units of the forces", GH_ParamAccess.item);
            Param_Integer force_param = (Param_Integer)pManager[n];
            foreach (SteelFrameChecksComponent.ForceUnits value in Enum.GetValues(typeof(SteelFrameChecksComponent.ForceUnits)))
                force_param.AddNamedValue(value.GetDescription(), (int)value);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("RC Beams", "Bs", "Beams", GH_ParamAccess.list);
            pManager.AddNumberParameter("Beam ascissas", "BAs", "Ascissas of beams", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Beam top As", "BTAs", "Beams required top reinforcement area", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Beam bottom As", "BBAs", "Beams required bottom reinforcement area", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Beam shear As", "BSAs", "Beams required shear reinforcement area", GH_ParamAccess.tree);

            pManager.AddGenericParameter("RC Pillars", "Ps", "Pillars", GH_ParamAccess.list);
            pManager.AddNumberParameter("Pillar ascissas", "PAs", "Ascissas of pillars", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Pillar As", "PAs", "Pillar required longitudinal reinforcement area", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Pillar shear As", "PSAs", "Pillars required shear reinforcement area", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<GH_Beam> frames = new List<GH_Beam>();
            double topBeamCover = 0;
            double bottomBeamCover = 0;
            double pillarCover = 0;
            double gammaS = 0;
            double fyk = 0;
            int lengthValue = -1;
            int forceValue = -1;
            if (!DA.GetDataList(0, frames))
                return;
            if (!DA.GetData(1, ref topBeamCover))
                return;
            if (!DA.GetData(2, ref bottomBeamCover))
                return;
            if (!DA.GetData(3, ref pillarCover))
                return;
            if (!DA.GetData(4, ref gammaS))
                return;
            if (!DA.GetData(5, ref fyk))
                return;
            if (!DA.GetData(6, ref lengthValue))
                return;
            if (!DA.GetData(7, ref forceValue))
                return;

            SteelFrameChecksComponent.LengthUnits lengthUnits;
            SteelFrameChecksComponent.ForceUnits forceUnits;
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

            List<GH_Beam> beams, rcBeams;
            List<GH_Beam> pillars, rcPillars;
            Dictionary<BeamModel, List<double>> beamTopAs = new Dictionary<BeamModel, List<double>>();
            Dictionary<BeamModel, List<double>> beamBottomAs = new Dictionary<BeamModel, List<double>>();
            Dictionary<BeamModel, List<double>> beamShearAs = new Dictionary<BeamModel, List<double>>();
            Dictionary<BeamModel, List<double>> pillarLongAs = new Dictionary<BeamModel, List<double>>();
            Dictionary<BeamModel, List<double>> pillarShearAs = new Dictionary<BeamModel, List<double>>();
            double fyd, fykMPa;
            Dictionary<BeamModel, List<double>> beamsAscissas, pillarAscissas, rcBeamsAscissas, rcPillarAscissas;

            GetBeamPillars(frames, out beams, out pillars, out beamsAscissas, out pillarAscissas);

            rcBeamsAscissas = new Dictionary<BeamModel, List<double>>();
            rcPillarAscissas = new Dictionary<BeamModel, List<double>>();

            fyd = fyk / gammaS;
            //fykMPa = fyk;
            fykMPa = UnitsConvert.ConvertToDefaultUnits(fyk, utilsForceCode, 1, utilsLengthCode, -2);

            //switch (lengthCode)
            //{
            //    case SteelFrameChecksComponent.LengthUnits.cm:
            //        fykMPa /= 100;
            //        break;
            //    case SteelFrameChecksComponent.LengthUnits.m:
            //        fykMPa /= 1000000;
            //        break;
            //    case SteelFrameChecksComponent.LengthUnits.mm:
            //        break;
            //    default:
            //        throw new NotSupportedException();
            //}
            //switch (forceCode)
            //{
            //    case SteelFrameChecksComponent.ForceUnits.daN:
            //        fykMPa *= 10;
            //        break;
            //    case SteelFrameChecksComponent.ForceUnits.kN:
            //        fykMPa *= 1000;
            //        break;
            //    case SteelFrameChecksComponent.ForceUnits.N:
            //        break;
            //    default:
            //        throw new NotSupportedException();
            //}

            //Beams
            rcBeams = new List<GH_Beam>();
            foreach (GH_Beam beam in beams)
            {
                if (beam.Value.BeamProperty.Material.Kind == MaterialModel.MaterialKind.Concrete)
                {
                    List<double> bufferTopAs, bufferBottomAs, bufferShearAs;
                    double fckMPa, fck;
                    double fctmMPa, fctm;
                    double vertDimension, horDimension;
                    double minAs, minShearAs;

                    rcBeams.Add(beam);
                    rcBeamsAscissas.Add(beam.Value, beamsAscissas[beam.Value]);
                    bufferTopAs = new List<double>();
                    bufferBottomAs = new List<double>();
                    bufferShearAs = new List<double>();
                    fckMPa = beam.Value.BeamProperty.Material.SpecificCompressiveStrength;
                    fck = UnitsConvert.ConvertFromDefaultUnits(beam.Value.BeamProperty.Material.SpecificCompressiveStrength,
                                                                  utilsForceCode, 1, utilsLengthCode, -2);
                    //switch (lengthCode)
                    //{
                    //    case SteelFrameChecksComponent.LengthUnits.cm:
                    //        fckMPa /= 100;
                    //        break;
                    //    case SteelFrameChecksComponent.LengthUnits.m:
                    //        fckMPa /= 1000000;
                    //        break;
                    //    case SteelFrameChecksComponent.LengthUnits.mm:
                    //        break;
                    //    default:
                    //        throw new NotSupportedException();
                    //}
                    //switch (forceCode)
                    //{
                    //    case SteelFrameChecksComponent.ForceUnits.daN:
                    //        fckMPa *= 10;
                    //        break;
                    //    case SteelFrameChecksComponent.ForceUnits.kN:
                    //        fckMPa *= 1000;
                    //        break;
                    //    case SteelFrameChecksComponent.ForceUnits.N:
                    //        break;
                    //    default:
                    //        throw new NotSupportedException();
                    //}

                    if (fckMPa <= 50)
                    {
                        fctmMPa = 0.3 * Math.Pow(fckMPa, 2d / 3d);
                    }
                    else
                    {
                        fctmMPa = 2.12 * Math.Log(1d + (fckMPa + 8) / 10d);
                    }

                    fctm = fctmMPa;//N su mm2
                    switch (lengthUnits)
                    {
                        case SteelFrameChecksComponent.LengthUnits.cm:
                            fctm *= 100;
                            break;
                        case SteelFrameChecksComponent.LengthUnits.m:
                            fctm *= 1000000;
                            break;
                        case SteelFrameChecksComponent.LengthUnits.mm:
                            break;
                        default:
                            throw new NotSupportedException();
                    }
                    switch (forceUnits)
                    {
                        case SteelFrameChecksComponent.ForceUnits.daN:
                            fctm /= 10;
                            break;
                        case SteelFrameChecksComponent.ForceUnits.kN:
                            fctm /= 1000;
                            break;
                        case SteelFrameChecksComponent.ForceUnits.N:
                            break;
                        default:
                            throw new NotSupportedException();
                    }

                    if (beam.Value.AngleDeg == 90)
                    {
                        switch (beam.Value.BeamProperty.SectionType)
                        {
                            case SectionModel.SectionTypes.SolidRectangle:
                                {
                                    vertDimension = beam.Value.BeamProperty.D;
                                    horDimension = beam.Value.BeamProperty.B;
                                    break;
                                }
                            default:
                                throw new NotImplementedException();
                        }
                    }
                    else if (beam.Value.AngleDeg == 0)
                    {
                        switch (beam.Value.BeamProperty.SectionType)
                        {
                            case SectionModel.SectionTypes.SolidRectangle:
                                {
                                    vertDimension = beam.Value.BeamProperty.B;
                                    horDimension = beam.Value.BeamProperty.D;
                                    break;
                                }
                            default:
                                throw new NotImplementedException();
                        }
                    }
                    else
                    {
                        throw new NotSupportedException();
                    }

                    minAs = 0.26 * fctm / fyk * vertDimension * horDimension;
                    minShearAs = 0.08 * Math.Sqrt(fckMPa) / fykMPa * horDimension;

                    if (!beamTopAs.TryGetValue(beam.Value, out bufferTopAs))
                    {
                        bufferTopAs = new List<double>();
                        for (int ascCount = 0; ascCount < beamsAscissas[beam.Value].Count; ascCount++)
                        {
                            bufferTopAs.Add(minAs);
                        }
                        beamTopAs.Add(beam.Value, bufferTopAs);
                    }
                    if (!beamBottomAs.TryGetValue(beam.Value, out bufferBottomAs))
                    {
                        bufferBottomAs = new List<double>();
                        for (int ascCount = 0; ascCount < beamsAscissas[beam.Value].Count; ascCount++)
                        {
                            bufferBottomAs.Add(minAs);
                        }
                        beamBottomAs.Add(beam.Value, bufferBottomAs);
                    }
                    if (!beamShearAs.TryGetValue(beam.Value, out bufferShearAs))
                    {
                        bufferShearAs = new List<double>();
                        for (int ascCount = 0; ascCount < beamsAscissas[beam.Value].Count; ascCount++)
                        {
                            bufferShearAs.Add(minShearAs);
                        }
                        beamShearAs.Add(beam.Value, bufferShearAs);
                    }

                    BeamForceResultModel[] results = beam.Value.Results.
                        Where(r => r.GetType() == typeof(BeamForceResultModel)).Cast<BeamForceResultModel>().ToArray();
                    foreach (BeamForceResultModel result in results)
                    {
                        double n, m11, m22, v11, v22;
                        double m, v;
                        double d;
                        double traction;
                        int index;

                        n = result.AxialForce;
                        //moment.X=m2,moment.Y=m3 con segni SAP
                        //shear.X=v2,shear.Y=v3 con segni SAP
                        //per segni SAP vedi manuale SAP

                        //per ora per me m>0 e v>0 se e'>0 il momento vettore
                        //TODO da rivedere tutto!!!!
                        m11 = result.BendingMoment.Y;
                        m22 = result.BendingMoment.X;
                        v11 = result.ShearForce.Y;
                        v22 = -result.ShearForce.X;

                        if (beam.Value.AngleDeg == 90)
                        {
                            //The vertical plane is the plane 2
                            m = m11;
                            v = v22;
                        }
                        else if (beam.Value.AngleDeg == 0)
                        {
                            //The vertical plane is the plane 1
                            m = m22;
                            v = v11;
                        }
                        else
                        {
                            throw new NotSupportedException();
                        }

                        if (m > 0)
                        {
                            //Bottom tensile
                            d = vertDimension - bottomBeamCover;
                        }
                        else
                        {
                            //Top tensile
                            d = vertDimension - topBeamCover;
                        }

                        traction = Math.Abs(m / (0.9 * d) + 0.5 * n);
                        index = beamsAscissas[beam.Value].IndexOf(result.Station);
                        if (index < 0)
                        {
                            throw new NotSupportedException();
                        }
                        if (traction > 0)
                        {
                            double aS;

                            aS = traction / fyd;
                            if (m > 0)
                            {
                                //Bottom tensile
                                bufferBottomAs[index] = Math.Max(bufferBottomAs[index], aS);
                            }
                            else
                            {
                                //Top tensile
                                bufferTopAs[index] = Math.Max(bufferTopAs[index], aS);
                            }
                        }

                        //ShearReinforcements
                        bufferShearAs[index] = Math.Max(bufferShearAs[index], Math.Abs(v) / (0.9 * d * fyd));
                    }
                }
            }

            //Pillars
            rcPillars = new List<GH_Beam>();
            foreach (GH_Beam pillar in pillars)
            {
                if (pillar.Value.BeamProperty.Material.Kind == MaterialModel.MaterialKind.Concrete)
                {
                    List<double> bufferLongAs, bufferShearAs;
                    double dimension11, dimension22, d11, d22;
                    double aCls;

                    bufferLongAs = new List<double>();
                    bufferShearAs = new List<double>();
                    rcPillars.Add(pillar);
                    rcPillarAscissas.Add(pillar.Value, pillarAscissas[pillar.Value]);

                    switch (pillar.Value.BeamProperty.SectionType)
                    {
                        case SectionModel.SectionTypes.SolidRectangle:
                            {
                                dimension11 = pillar.Value.BeamProperty.B;
                                dimension22 = pillar.Value.BeamProperty.D;
                                d11 = dimension11 - pillarCover;
                                d22 = dimension22 - pillarCover;
                                break;
                            }
                        default:
                            throw new NotImplementedException();
                    }

                    aCls = dimension11 * dimension22;

                    if (!pillarLongAs.TryGetValue(pillar.Value, out bufferLongAs))
                    {
                        bufferLongAs = new List<double>();
                        for (int ascCount = 0; ascCount < pillarAscissas[pillar.Value].Count; ascCount++)
                        {
                            bufferLongAs.Add(0);
                        }
                        pillarLongAs.Add(pillar.Value, bufferLongAs);
                    }
                    if (!pillarShearAs.TryGetValue(pillar.Value, out bufferShearAs))
                    {
                        bufferShearAs = new List<double>();
                        for (int ascCount = 0; ascCount < pillarAscissas[pillar.Value].Count; ascCount++)
                        {
                            bufferShearAs.Add(0);
                        }
                        pillarShearAs.Add(pillar.Value, bufferShearAs);
                    }
                    BeamForceResultModel[] results = pillar.Value.Results.
                        Where(r => r.GetType() == typeof(BeamForceResultModel)).Cast<BeamForceResultModel>().ToArray();
                    foreach (BeamForceResultModel result in results)
                    {
                        double m1;
                        double m2;
                        double n;
                        double v1;
                        double v2;
                        double minAs;
                        double n11, n22;
                        double as11, as22;
                        double aS;
                        double shearAs11, shearAs22;
                        int index;

                        //Longitudinal reinforcements
                        //See beams for sign convetions
                        m1 = result.BendingMoment.Y;
                        m2 = result.BendingMoment.X;
                        n = result.AxialForce;
                        v1 = result.ShearForce.Y;
                        v2 = -result.ShearForce.X;

                        minAs = Math.Max(0.1 * Math.Abs(n) / fyd, 0.002 * aCls);
                        n11 = Math.Abs(m1 / (0.9 * d22)) + 0.5 * n;
                        n22 = Math.Abs(m2 / (0.9 * d11)) + 0.5 * n;
                        as11 = Math.Max(0, n11 / fyd);
                        as22 = Math.Max(0, n22 / fyd);
                        aS = 2 * as11 + 2 * as22;

                        index = pillarAscissas[pillar.Value].IndexOf(result.Station);
                        if (index < 0)
                        {
                            throw new NotSupportedException();
                        }

                        bufferLongAs[index] = Math.Max(bufferLongAs[index], Math.Max(aS, minAs));

                        //ShearReinforcements
                        shearAs11 = Math.Abs(v1) / (0.9 * d11 * fyd);
                        shearAs22 = Math.Abs(v2) / (0.9 * d22 * fyd);

                        bufferShearAs[index] = Math.Max(bufferShearAs[index], Math.Max(shearAs11, shearAs22));
                    }
                }
            }

            DA.SetDataList(0, rcBeams);
            DA.SetDataTree(1, SteelFrameChecksComponent.GetStructure(rcBeamsAscissas));
            DA.SetDataTree(2, SteelFrameChecksComponent.GetStructure(beamTopAs));
            DA.SetDataTree(3, SteelFrameChecksComponent.GetStructure(beamBottomAs));
            DA.SetDataTree(4, SteelFrameChecksComponent.GetStructure(beamShearAs));

            DA.SetDataList(5, rcPillars);
            DA.SetDataTree(6, SteelFrameChecksComponent.GetStructure(rcPillarAscissas));
            DA.SetDataTree(7, SteelFrameChecksComponent.GetStructure(pillarLongAs));
            DA.SetDataTree(8, SteelFrameChecksComponent.GetStructure(pillarShearAs));
        }

        private static void GetBeamPillars(List<GH_Beam> frames,
                                           out List<GH_Beam> beams,
                                           out List<GH_Beam> pillars,
                                           out Dictionary<BeamModel, List<double>> beamsAscissas,
                                           out Dictionary<BeamModel, List<double>> pillarAscissas)
        {
            beams = new List<GH_Beam>();
            pillars = new List<GH_Beam>();
            beamsAscissas = new Dictionary<BeamModel, List<double>>();
            pillarAscissas = new Dictionary<BeamModel, List<double>>();
            foreach (GH_Beam frame in frames)
            {
                List<double> ascissas;
                Vector3d dir = frame.Value.PointTo - frame.Value.PointFrom;
                dir.Unitize();
                ascissas = new List<double>();
                BeamForceResultModel[] results = frame.Value.Results.
                        Where(r => r.GetType() == typeof(BeamForceResultModel)).Cast<BeamForceResultModel>().ToArray();
                foreach (BeamForceResultModel result in results)
                {
                    if (!ascissas.Contains(result.Station))
                    {
                        ascissas.Add(result.Station);
                    }
                }
                if (Math.Abs(dir * Vector3d.ZAxis) > 0.8)
                {
                    //pillar, pseudo vertical
                    pillars.Add(frame);
                    pillarAscissas.Add(frame.Value, ascissas);
                }
                else
                {
                    //beam
                    beams.Add(frame);
                    beamsAscissas.Add(frame.Value, ascissas);
                }
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("31e3225e-3154-4726-9e6b-67539de1b524");
    }
}
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using GPC.Checkers.Concrete.Attributes;
using GPC.Checkers.Concrete.Checkers;
using GPC.Checkers.Concrete.Results;
using GPC.Checkers.Concrete.SectionSolvers;
using GPC.Model.Materials;
using GPC.Model.Results;
using GPC.Model.Sections.Concrete;
using GPC.Model.Sections.Rebar;
using GPC.Model.Standards;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Grasshopper.Components.Checks
{
    public class MixedSectionTensionCheckComponent : GH_Component
    {
        protected Dictionary<int, string> _standardOptions = new Dictionary<int, string>()
        {
            { 0, "EC2" },
            { 1, "AASHTO" },
            { 2, "NTC2018" },
            { 3, "SBC 306-CR-18" }
        };

        /// <summary>
        /// Initializes a new instance of the MixedSectionBeamComponent class.
        /// </summary>
        public MixedSectionTensionCheckComponent()
          : base("Composite Section Tension Check", "CSTC", "Check the composite section with input forces", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_STEELCHECKS)

        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam Section", "BS", "Section to check", GH_ParamAccess.item);
            pManager.AddGenericParameter("Concrete Material", "CM", "Concrete Material", GH_ParamAccess.item);
            pManager.AddGenericParameter("Rebar Material", "RM", "Rebar Material", GH_ParamAccess.item);
            pManager.AddGenericParameter("Steel Material", "SM", "Steel Material", GH_ParamAccess.item);
            pManager.AddNumberParameter("Homogenized factor ", "N", "The homogenized factor", GH_ParamAccess.item, 15);
            pManager.AddGenericParameter("Loads", "L", "The loads to check", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Header", "H", "The result string header", GH_ParamAccess.item);
            pManager.AddTextParameter("Summary Results", "R", "The result string to save in .cvs", GH_ParamAccess.list);
            pManager.AddPointParameter("Steel section points", "SSP", "The boundary points of the steel section", GH_ParamAccess.list);
            pManager.AddTextParameter("Steel section value", "SSV", "The stress value associated to boundary points of the steel section", GH_ParamAccess.list);
            pManager.AddPointParameter("Concrete section points", "CSP", "The boundary points of the concrete section", GH_ParamAccess.list);
            pManager.AddTextParameter("Concrete section value", "CSV", "The stress value associated to boundary points of the concrete section", GH_ParamAccess.list);
            pManager.AddPointParameter("Rebar points", "RP", "The points of the concrete section rebars", GH_ParamAccess.list);
            pManager.AddTextParameter("Rebar value", "RV", "The stress value associated to points of the concrete section rebars", GH_ParamAccess.list);
            pManager.AddLineParameter("Neutral axis", "NA", "The neutral axis of the forces", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_MixedSection section = null;
            GH_Material concrete = null;
            GH_Material rebarMat = null;
            GH_Material steel = null;
            List<BeamForceResultModel> beamForceResultModel = new List<BeamForceResultModel>();
            double homogenizedFactor = 0;

            int count = 0;
            if (!DA.GetData(count++, ref section))
                return;
            if (!DA.GetData(count++, ref concrete))
                return;
            if (!DA.GetData(count++, ref rebarMat))
                return;
            if (!DA.GetData(count++, ref steel))
                return;
            if (!DA.GetData(count++, ref homogenizedFactor))
                return;
            if (!DA.GetDataList(count++, beamForceResultModel))
                return;

            double bfw = section.SteelSection.B1;       // bottom flange width
            double tfw = section.SteelSection.B2;       // top flange width
            double h = section.SteelSection.D;          // heigth
            double bft = section.SteelSection.T1;       // Bottom flange thickness
            double tft = section.SteelSection.T2;        // Top flange thickness
            double wt = section.SteelSection.T3;        // Web thickness

            double hh = section.ConcreteSection.D;      // heigth
            double b = section.ConcreteSection.B;       // base

            double verticalOffset = section.VerticalOffset;

            MaterialModel concreteModel = concrete.Value;
            MaterialModel steelModel = steel.Value;
            MaterialModel rebarModel = rebarMat.Value;

            double rebarsAreaTot = section.ReinforcementArea;
            double rebarsCover = section.ReinforcementCover;

            int numberRebars = 4;
            double rebarArea = rebarsAreaTot / numberRebars;
            double rebarDiameter = Math.Sqrt(rebarArea * 4 / Math.PI);

            GPC.Model.Sections.SectionH sectionH = new GPC.Model.Sections.SectionH(h, wt, tfw, tft, bfw, bft, section.Name);

            ConcreteMaterialACI318 concreteMaterialACI318 = new ConcreteMaterialACI318(concreteModel.Name, concreteModel.SpecificCompressiveStrength,
                ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
            SteelMaterialACI318 steelMaterialACI318 = new SteelMaterialACI318(steelModel.Name, steelModel.Modulus, steelModel.MinimumYieldStress, steelModel.MinimumTensileStress,
                0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
            SteelMaterialACI318 rebarMaterial = new SteelMaterialACI318(rebarModel.Name, rebarModel.Modulus, rebarModel.MinimumTensileStress, rebarModel.ExpectedTensileStress,
                0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);

            //var fakeRebar = new RebarSectionCircular("", 1.0, rebarMaterial);
            var rebar = new RebarSectionCircular("", rebarDiameter, rebarMaterial);
            ReinforcedConcreteSection reinforcedConcreteSection = new ReinforcedConcreteSection(b, hh, concreteMaterialACI318, rebar, b / numberRebars, rebarsCover, null, 1000,
                sectionH, steelMaterialACI318, 0, 0, section.Name);

            var vect = reinforcedConcreteSection.SteelSections[0].Traslation;
            reinforcedConcreteSection.SteelSections[0].Traslation = new GPC.Geometry.Vector2d(vect.X, vect.Y - verticalOffset);

            StandardACI318p14 standardACI318P14 = new StandardACI318p14();
            StandardAISC360p16 standardAISC360P16 = new StandardAISC360p16();

            double phi = CalculateHomogenizedFactorPhi(homogenizedFactor, steelMaterialACI318, concreteMaterialACI318);
            GPC.Geometry.CoordinateSystem coordinateSystem = GetLocalCoordinateSystem(reinforcedConcreteSection, phi);
            SectionCheckerACI318.SectionOptionsStandardACI318 options = new SectionCheckerACI318.SectionOptionsStandardACI318(coordinateSystem,
                SectionSolver.FailureAnalysisTypes.ConstantN, SectionSolver.FailureDomainTypes.Plastic, SectionSolver.StressAnalysisTypes.Linear, phi, 0, false, 64);

            ResultBeamForces[] resultBeamForces = new ResultBeamForces[beamForceResultModel.Count];
            for (int i = 0; i < beamForceResultModel.Count; i++)
            {
                var force = beamForceResultModel[i];
                resultBeamForces[i] = new ResultBeamForces(force.AxialForce, force.ShearForce.X, force.ShearForce.Y, force.Torque, force.BendingMoment.X, -force.BendingMoment.Y, coordinateSystem, i, force.LoadCase.Name);
            }

            SectionCheckerAttribute sectionCheckerAttribute = new SectionCheckerAttribute(reinforcedConcreteSection, resultBeamForces, null);
            SectionCheckerACI318 sectionCheckerACI318 = new SectionCheckerACI318(sectionCheckerAttribute, options, standardACI318P14, false, false, -1, standardAISC360P16);

            StressAnalysisResult[] results = sectionCheckerACI318.GetTensionAnalysisResult();

            string[] outCsv = new string[results.Length];
            string[] outConcreteTension = new string[results.Length];
            string[] outSteelTension = new string[results.Length];
            string[] outRebarTension = new string[results.Length];
            Line[] lines = new Line[results.Length];
            Point3d[] outConcretePoint = null;
            Point3d[] outSteelPoint = null;
            Point3d[] outRebarPoint = null;

            int cifreSignificativeForze = 2;
            int cifreSignificativeStress = 2;
            int cifreSignificativeStresss = 6;
            int cifreSignificativeStrain = 6;
            int cifreSignificativePlane = 2;

            for (int i = 0; i < results.Length; i++)
            {
                StressAnalysisResult result = results[i];

                string csv = $"{section.Name};{result.Force.Id};{result.Force.Name};" +
                    $"{Math.Round(beamForceResultModel[i].Station, cifreSignificativeForze)};" +
                    $"{Math.Round(result.Force.N / 1000, cifreSignificativeForze)};" +
                    $"{Math.Round(result.Force.M1 / 1000000, cifreSignificativeForze)};" +
                    $"{Math.Round(result.Force.M2 / 1000000, cifreSignificativeForze)};";

                GPC.Checker.Results.ResultType.StrainPlaneResult strainPlaneResult = result.CalculateStrainPlaneResult(true, phi);

                csv += $"{Math.Round(strainPlaneResult.SigmaCMin, cifreSignificativeStress)};" +
                    $"{Math.Round(strainPlaneResult.SigmaCMax, cifreSignificativeStress)};";
                csv += $"{Math.Round(strainPlaneResult.SigmaSMin, cifreSignificativeStress)};" +
                    $"{Math.Round(strainPlaneResult.SigmaSMax, cifreSignificativeStress)};";
                csv += "0;0;";
                csv += $"{Math.Round(strainPlaneResult.SigmaSSMin, cifreSignificativeStress)};" +
                    $"{Math.Round(strainPlaneResult.SigmaSSMax, cifreSignificativeStress)};";

                csv += $"{Math.Round(strainPlaneResult.EpsilonCMin, cifreSignificativeStrain)};" +
                    $"{Math.Round(strainPlaneResult.EpsilonCMax, cifreSignificativeStrain)};";
                csv += $"{Math.Round(strainPlaneResult.EpsilonSMin, cifreSignificativeStrain)};" +
                    $"{Math.Round(strainPlaneResult.EpsilonSMax, cifreSignificativeStrain)};";
                csv += "0;0;";
                csv += $"{Math.Round(strainPlaneResult.EpsilonSSMin, cifreSignificativeStrain)};" +
                    $"{Math.Round(strainPlaneResult.EpsilonSSMax, cifreSignificativeStrain)};";

                csv += $"{Math.Round(strainPlaneResult.NetHeight, cifreSignificativePlane)};" +
                    $"{Math.Round(strainPlaneResult.NeutralAxisDistance, cifreSignificativePlane)};" +
                    $"{Math.Round(strainPlaneResult.NeutralAxisAngle, cifreSignificativePlane)}";

                outCsv[i] = csv;

                (GPC.Geometry.Point2d point, double tension)[] concreteTens = result.GetConcreteVerticesTension(phi);
                string[] bufferConcrete = concreteTens.Select(k => Math.Round(k.tension, cifreSignificativeStresss).ToString() + ";").ToArray();
                outConcreteTension[i] = string.Concat(bufferConcrete);
                if (outConcretePoint == null)
                    outConcretePoint = concreteTens.Select(k => new Point3d(k.point.X, k.point.Y, 0)).ToArray();

                (GPC.Geometry.Point2d point, double tension)[] steelTens = result.GetStructuralSteelVerticesTension(phi);
                string[] bufferSteel = steelTens.Select(k => Math.Round(k.tension, cifreSignificativeStresss).ToString() + ";").ToArray();
                outSteelTension[i] = string.Concat(bufferSteel);
                if (outSteelPoint == null)
                    outSteelPoint = steelTens.Select(k => new Point3d(k.point.X, k.point.Y, 0)).ToArray();

                (ReinforcedConcreteRebar rebar, double tension)[] rebarTens = result.GetRebarsTension(phi, 0);
                string[] bufferRebar = rebarTens.Select(k => Math.Round(k.tension, cifreSignificativeStresss).ToString() + ";").ToArray();
                outRebarTension[i] = string.Concat(bufferRebar);
                if (outRebarPoint == null)
                    outRebarPoint = rebarTens.Select(k => new Point3d(k.rebar.Position.X, k.rebar.Position.Y, 0)).ToArray();


                //GPC.Geometry.Line2d neutralAxis = result.StrainPlane.GetNeutralAxis();
                //neutralAxis?.Move(result.StrainPlane.ReferencePoint.X, result.StrainPlane.ReferencePoint.Y);
                //GPC.Geometry.BoundingBox2d concreteBB = reinforcedConcreteSection.Shape.Fill2d.GetBoundingBox();
                //GPC.Geometry.BoundingBox2d steelBB = reinforcedConcreteSection.SteelSections[0].Section.Shape.Fill2d.GetBoundingBox();

                //GPC.Geometry.Point2d[] steelFill = reinforcedConcreteSection.SteelSections[0].Section.Shape.Fill2d.Points;
                //GPC.Geometry.Point2d[] steelFillGlobal = steelFill.Select(k => reinforcedConcreteSection.SteelSections[0].PositionToGlobal(k)).ToArray();

                //for (int k = 0; k < steelFillGlobal.Count(); k++)
                //	concreteBB.Update(steelFillGlobal);

                //concreteBB.Scale(1.1);

                //GPC.Geometry.Point2d point1 = new GPC.Geometry.Point2d(concreteBB.Min.X, concreteBB.Min.Y);
                //GPC.Geometry.Point2d point2 = new GPC.Geometry.Point2d(concreteBB.Max.X, concreteBB.Min.Y);
                //GPC.Geometry.Point2d point3 = new GPC.Geometry.Point2d(concreteBB.Max.X, concreteBB.Max.Y);
                //GPC.Geometry.Point2d point4 = new GPC.Geometry.Point2d(concreteBB.Min.X, concreteBB.Max.Y);

                //GPC.Geometry.Line2d line1 = new GPC.Geometry.Line2d(point1, point2);
                //GPC.Geometry.Line2d line2 = new GPC.Geometry.Line2d(point2, point3);
                //GPC.Geometry.Line2d line3 = new GPC.Geometry.Line2d(point3, point4);
                //GPC.Geometry.Line2d line4 = new GPC.Geometry.Line2d(point4, point1);

                //GPC.Geometry.Polygon2d polygon = new GPC.Geometry.Polygon2d(new GPC.Geometry.Point2d[] { point1, point2, point3, point4 });

                //bool int1 = neutralAxis.GetIntersectionWithInfiniteLine(line1, out var intersection1);
                //bool int2 = neutralAxis.GetIntersectionWithInfiniteLine(line2, out var intersection2);
                //bool int3 = neutralAxis.GetIntersectionWithInfiniteLine(line3, out var intersection3);
                //bool int4 = neutralAxis.GetIntersectionWithInfiniteLine(line4, out var intersection4);

                //List<GPC.Geometry.Point2d> ppp = new List<GPC.Geometry.Point2d>();
                //if (int1 && line1.IsPointOnLine(intersection1, 1))
                //	ppp.Add(intersection1);
                //if (int2 && line2.IsPointOnLine(intersection2, 1))
                //	ppp.Add(intersection2);
                //if (int3 && line3.IsPointOnLine(intersection3, 1))
                //	ppp.Add(intersection3);
                //if (int4 && line4.IsPointOnLine(intersection4, 1))
                //	ppp.Add(intersection4);

                //if (ppp.Count == 2)
                //{
                //	GPC.Geometry.Line2d outLine = new GPC.Geometry.Line2d(ppp[0], ppp[1]);
                //	var startPt = new Point3d(ppp[0].X, ppp[0].Y, 0); 
                //	var endPt = new Point3d(ppp[1].X, ppp[1].Y, 0);
                //	lines[i] = new Line(startPt, endPt);
                //	//Point3d p1 = new Point3d(neutralAxis.Start.X, neutralAxis.Start.Y, 0);
                //	//Point3d p2 = new Point3d(neutralAxis.End.X, neutralAxis.End.Y, 0);
                //	//lines[i] = new Line(p1, p2);
                //}
                //else
                //{
                //	lines[i] = new Line();
                //}
            }

            DA.SetDataList(1, outCsv);
            DA.SetDataList(2, outSteelPoint);
            DA.SetDataList(3, outSteelTension);
            DA.SetDataList(4, outConcretePoint);
            DA.SetDataList(5, outConcreteTension);
            DA.SetDataList(6, outRebarPoint);
            DA.SetDataList(7, outRebarTension);
            DA.SetDataList(8, lines);

            string header = "Beam Id;Force Id;Force Combo;Force station;N [kN];Mx [kNm];My [kNm];" +
                "σc min [Mpa];σc max [Mpa];σs min [Mpa];σs max [Mpa];σsp min [Mpa];σsp max [Mpa];σss min [Mpa];σss max [Mpa];" +
                "εc min;εc max;εs min;εs max;εsp min;εsp max;εss min;εss max;" +
                "d [mm];x [mm];θ [°]";

            DA.SetData(0, header);
        }

        protected GPC.Geometry.CoordinateSystem GetLocalCoordinateSystem(IConcreteSection section, double phi)
        {
            return new GPC.Geometry.CoordinateSystem(section.GetHomogenizedCentroid(phi, out double _, out double _), new GPC.Geometry.Vector3d(-1, 0, 0), new GPC.Geometry.Vector3d(0, -1, 0));
        }

        protected double CalculateHomogenizedFactorPhi(double n, SteelMaterial steelMaterial, ConcreteMaterial concreteMaterial)
        {
            return concreteMaterial.ElasticModulusCompression / steelMaterial.ElasticModulusTension * n - 1;
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("d370d023-bccf-4292-bed6-61858bcf71d8");
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;
using Rhino.Geometry;
using St7API;
namespace BridGHe.Common.Straus
{
    public static class StrausResultsReader
    {
        private static bool Success(int code) { StrausRuntime.Check(code, "Read element results"); return true; }
        public static void Read(int modelId, int resultCase, CaseModel caseModel, ModelModel model)
        {
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                NodeModel node = model.Nodes[i];
                double[] results = new double[6];
                if (Success(St7.St7GetNodeResult(modelId, St7.rtNodeDisp, i + 1, resultCase, results)))
                {
                    NodeDisplResultModel displ = new NodeDisplResultModel(caseModel)
                    {
                        Displacement = new Vector3d(results[0], results[1], results[2]),
                        Rotation = new Vector3d(results[3], results[4], results[5])
                    };
                    node.Results.Add(displ);
                }

                if (Success(St7.St7GetNodeResult(modelId, St7.rtNodeReact, i + 1, resultCase, results)))
                {
                    NodeReactResultModel react = new NodeReactResultModel(caseModel)
                    {
                        Force = new Vector3d(results[0], results[1], results[2]),
                        Moment = new Vector3d(results[3], results[4], results[5])
                    };
                    node.Results.Add(react);
                }
            }

            for (int i = 0; i < model.Beams.Count; i++)
            {
                BeamModel beam = model.Beams[i];
                int ns = 0, nc = 0;
                double[] pos = new double[St7.kMaxBeamResult];
                double[] res = new double[St7.kMaxBeamResult];

                BeamOutputStationsModel.AttributeData modifier = BeamOutputStationsModel.GetModifiers(beam);
                int stationNumber = 5;
                if (modifier != null)
                    stationNumber = modifier.MinimumNumberOfStations;

                if (Success(St7.St7GetBeamResultArray(modelId, St7.rtBeamForce, St7.stBeamPrincipal, i + 1, stationNumber, resultCase, ref ns, ref nc, pos, res)))
                {
                    List<BeamForceResultModel> listBuffer = new List<BeamForceResultModel>();
                    for (int s = 0; s < ns; s++)
                    {
                        double shear1, shear2;
                        double bending1, bending2;
                        shear1 = res[s * nc + St7.ipBeamSF1];
                        shear2 = res[s * nc + St7.ipBeamSF2];
                        bending1 = res[s * nc + St7.ipBeamBM1];
                        bending2 = res[s * nc + St7.ipBeamBM2];
                        if (Helpers.StrausR3Helper.RotateOf90Deg(beam))
                        {
                            double buffer;
                            buffer = shear2;
                            shear2 = shear1;
                            shear1 = -buffer;
                            buffer = bending2;
                            bending2 = bending1;
                            bending1 = -buffer;
                        }
                        BeamForceResultModel result = new BeamForceResultModel(caseModel, pos[s], 0, "")
                        {
                            ShearForce = new Vector2d(shear1, shear2),
                            BendingMoment = new Vector2d(bending1, bending2),
                            AxialForce = res[s * nc + St7.ipBeamAxialF],
                            Torque = res[s * nc + St7.ipBeamTorque]
                        };
                        listBuffer.Add(result);
                    }

                    var ll = listBuffer.OrderBy(f => f.Station).ToArray();
                    for (int j = 0; j < ll.Length; j++)
                        beam.Results.Add(ll[j]);
                }
            }

            for (int j = 0; j < model.Plates.Count; j++)
            {
                PlateModel plate = model.Plates[j];
                int numPoints = 0;
                int numColumns = 0;
                double[] results = new double[St7.kMaxPlateResult];
                int average = St7.spNodesAverageNever;
                int plane = St7.psPlateMidPlane;
                int plateId = j + 1;
                int[] buffer = new int[St7.kMaxElementNode];

                StrausRuntime.Check(St7.St7GetElementConnection(modelId, St7.tyPLATE, plateId, buffer), "St7GetElementConnection");
                int[] nodes = new int[buffer[0]];

                for (int i = 0; i < nodes.Length; i++)
                {
                    nodes[i] = buffer[i + 1];
                }

                //Plate moment
                Vector3d[] m = new Vector3d[nodes.Length];
                if (Success(St7.St7GetPlateResultArray(modelId, St7.rtPlateMoment, St7.stPlateLocal, plateId, resultCase, average, plane, 0, ref numPoints, ref numColumns, results)))
                {
                    for (int i = 0; i < numPoints; i++)
                    {
                        m[i] = new Vector3d(results[i * numColumns + St7.ipPlateLocalxx], results[i * numColumns + St7.ipPlateLocalyy], results[i * numColumns + St7.ipPlateLocalxy]);
                    }

                    //Principal moments
                    double[] mMax = new double[nodes.Length];
                    double[] mMin = new double[nodes.Length];
                    double[] mAngle = new double[nodes.Length];
                    results = new double[St7.kMaxPlateResult];

                    if (Success(St7.St7GetPlateResultArray(modelId, St7.rtPlateMoment, St7.stPlateCombined, plateId, resultCase, average, plane, 0, ref numPoints, ref numColumns, results)))
                    {
                        for (int i = 0; i < numPoints; i++)
                        {
                            mMax[i] = results[i * numColumns + St7.ipPlateCombPrincipal11];
                            mMin[i] = results[i * numColumns + St7.ipPlateCombPrincipal22];
                            mAngle[i] = results[i * numColumns + St7.ipPlateCombPrincipalAngle];
                        }

                        //Axial forces
                        Vector3d[] f = new Vector3d[nodes.Length];
                        Vector2d[] shear = new Vector2d[nodes.Length];
                        results = new double[St7.kMaxPlateResult];

                        if (Success(St7.St7GetPlateResultArray(modelId, St7.rtPlateForce, St7.stPlateLocal, plateId, resultCase, average, plane, 0, ref numPoints, ref numColumns, results)))
                        {
                            for (int i = 0; i < numPoints; i++)
                            {
                                f[i] = new Vector3d(results[i * numColumns + St7.ipPlateLocalxx], results[i * numColumns + St7.ipPlateLocalyy], results[i * numColumns + St7.ipPlateLocalxy]);
                                shear[i] = new Vector2d(results[i * numColumns + St7.ipPlateLocalxz], results[i * numColumns + St7.ipPlateLocalyz]);
                            }

                            //Principal axial forces
                            double[] fMax = new double[nodes.Length];
                            double[] fMin = new double[nodes.Length];
                            double[] fAngle = new double[nodes.Length];
                            double[] fVM = new double[nodes.Length];
                            results = new double[St7.kMaxPlateResult];

                            if (Success(St7.St7GetPlateResultArray(modelId, St7.rtPlateForce, St7.stPlateCombined, plateId, resultCase, average, plane, 0, ref numPoints, ref numColumns, results)))
                            {
                                for (int i = 0; i < numPoints; i++)
                                {
                                    fMax[i] = results[i * numColumns + St7.ipPlateCombPrincipal11];
                                    fMin[i] = results[i * numColumns + St7.ipPlateCombPrincipal22];
                                    fAngle[i] = results[i * numColumns + St7.ipPlateCombPrincipalAngle];
                                    fVM[i] = results[i * numColumns + St7.ipPlateCombVonMises];
                                }

                                //Setting results
                                for (int i = 0; i < numPoints; i++)
                                {
                                    PlateForceResultModel result = new PlateForceResultModel(caseModel, nodes[i].ToString(), 0, "")
                                    {
                                        Force = f[i],
                                        ForceMax = fMax[i],
                                        ForceMin = fMin[i],
                                        ForceAngle = fAngle[i],
                                        ForceVonMises = fVM[i],
                                        Moment = m[i],
                                        MomentMax = mMax[i],
                                        MomentMin = mMin[i],
                                        MomentAngle = mAngle[i],
                                        TransverseShearForce = shear[i],
                                        TransverseShearForceMax = double.NaN,
                                        TransverseShearForceAng = double.NaN
                                    };
                                    plate.Results.Add(result);
                                }
                            }
                        }
                    }
                }
            }
        }

    }
}

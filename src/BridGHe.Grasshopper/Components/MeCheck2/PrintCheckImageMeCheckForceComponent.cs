using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using GPC.Model.Results;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;

namespace BridGHe.Grasshopper.Components.MeCheck2
{
    public class PrintCheckImageMeCheckForceComponent : GH_Component
    {
        protected bool _run;

        private const double FROMNTOKN = 0.001;
        private const double FROMNMTOKNM = 0.000001;

        private Color[] ComboColors = new Color[]
        {
            Color.Red,
            Color.Green,
            Color.Blue,
            Color.Orange,
            Color.Magenta,
            Color.Yellow,
            Color.Purple,
            Color.Orchid,
            Color.Aquamarine,
        };

        private readonly List<string> _forceComponent = new List<string>()
        {
            "N",
            "Vx",
            "Vy",
            "T",
            "Mx",
            "My",
        };

        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public PrintCheckImageMeCheckForceComponent()
          : base("Print MeCheck Forces", "PI", "Create the check image of super element check", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_MECHECK2)
        {
        }

        public override void CreateAttributes()
        {
            ComponentAttributes.ComponentOneButtonAttributes attr = new ComponentAttributes.ComponentOneButtonAttributes(this, "Run");
            attr.ButtonPressed += () =>
            {
                _run = true;
                ExpireSolution(true);
            };
            m_attributes = attr;
        }


        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("FeMM Model", "FM", "The FEM models (more than one for stages)", GH_ParamAccess.item);
            pManager.AddGenericParameter("Super-Element Beam", "SE", "The Super-Element Beam", GH_ParamAccess.list);
            pManager.AddGenericParameter("Forces", "F", "The Forces", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("Force component", "FC", "The force component to print", GH_ParamAccess.item, 4);
            Param_Integer param = (Param_Integer)pManager[pManager.ParamCount - 1];
            for (int i = 0; i < _forceComponent.Count; i++)
                param.AddNamedValue(_forceComponent[i], i);
            int j = pManager.AddTextParameter("Combinations", "C", "The list of the combinations to draw", GH_ParamAccess.list);
            //pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddBooleanParameter("Show Min/Max Combo", "SMM", "If true, show the combinations that maximize and minimize the envelop", GH_ParamAccess.item, true);
            pManager.AddTextParameter("File Path", "P", "The path of the file", GH_ParamAccess.item);
            pManager.AddTextParameter("Graphic Title", "T", "The title of the graphic", GH_ParamAccess.item, "");
            pManager.AddBooleanParameter("Keep Running", "KR", "Run", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Message = "";
            GH_Model model = new GH_Model();
            List<GH_CompositeBeam> beams = new List<GH_CompositeBeam>();
            List<string> comboToDraw = new List<string>();
            int forceComponent = 0;
            string savePath = "";
            string title = "";
            bool showMinMax = true;
            bool keepRunning = false;

            int nnn = 0;
            if (!DA.GetData(nnn++, ref model))
                return;
            if (!DA.GetDataList(nnn++, beams))
                return;
            if (!DA.GetDataTree(nnn++, out GH_Structure<IGH_Goo> inputTree))
                return;
            if (!DA.GetData(nnn++, ref forceComponent))
                return;
            DA.GetDataList(nnn++, comboToDraw);
            DA.GetData(nnn++, ref showMinMax);
            DA.GetData(nnn++, ref savePath);
            DA.GetData(nnn++, ref title);
            if (!DA.GetData(nnn++, ref keepRunning))
                return;

            if (_run || keepRunning)
            {
                try
                {
                    List<int> n0List = new List<int>();
                    List<int> n1List = new List<int>();

                    int n0 = 0;
                    int n1 = 0;
                    for (int i = 0; i < inputTree.Branches.Count; i++)
                    {
                        GH_Path path = inputTree.Paths[i];
                        var indices = path.Indices;

                        if (i == 0)
                        {
                            n0 = indices[0];
                            n1 = indices[1];
                            continue;
                        }
                        if (i == inputTree.Branches.Count - 1)
                        {
                            n1 = indices[1];
                            n1List.Add(n1 + 1);
                        }
                        if (indices[0] > n0)
                        {
                            n1List.Add(n1 + 1);
                            n0 = indices[0];
                            n1 = indices[1];
                            continue;
                        }
                        if (indices[1] > n1)
                            n1 = indices[1];
                    }

                    ResultBeamForces[][][] forces = new ResultBeamForces[beams.Count][][];
                    for (int i = 0; i < beams.Count; i++)
                    {
                        forces[i] = new ResultBeamForces[n1List[i]][];
                        for (int j = 0; j < n1List[i]; j++)
                        {
                            List<IGH_Goo> row = (List<IGH_Goo>)inputTree.get_Branch(new GH_Path(new int[] { i, j }));

                            forces[i][j] = new ResultBeamForces[row.Count];
                            for (int k = 0; k < row.Count; k++)
                            {
                                forces[i][j][k] = ((GH_Goo<object>)row[k]).Value as ResultBeamForces;
                            }
                        }
                    }


                    GraphicCreator gc = new GraphicCreator(1500.0F, 900.0F, true);
                    gc.Title = title;

                    //HashSet<double> fyValue = new HashSet<double>();
                    //for (int i = 0; i < beams.Count; i++)
                    //    fyValue.Add(beams[i].Value.StartBeamProperty.SteelMaterial.MinimumYieldStress);

                    //List<KeyValuePair<double, System.Windows.Media.Color>> _referenceLines = new List<KeyValuePair<double, System.Windows.Media.Color>>();
                    //for (int i = 0; i < fyValue.Count; i++)
                    //    _referenceLines.Add(new KeyValuePair<double, System.Windows.Media.Color>(fyValue.ElementAt(i), System.Windows.Media.Color.FromRgb(255, 0, 0)));

                    //foreach (KeyValuePair<double, System.Windows.Media.Color> ref_line in _referenceLines)
                    //{
                    //    System.Drawing.Color color = System.Drawing.Color.FromArgb(ref_line.Value.A, ref_line.Value.R, ref_line.Value.G, ref_line.Value.B);
                    //    gc.AddReferenceLine(ref_line.Key, color);
                    //}

                    List<string> comboName = model.Value.LoadCombinations.Select(j => j.Name).ToList();
                    double x = 0;
                    int _graphicType = 0;

                    int absoluteMinIndex = 0;
                    int absoluteMaxIndex = 0;
                    double absoluteMinValue = 0;
                    double absoluteMaxValue = 0;

                    switch (_graphicType)
                    {
                        case 0:
                            x = 0;
                            // Set the minimum/maximum values
                            for (int i = 0; i < beams.Count; i++)
                            {
                                HashSet<double> stationBuffer = new HashSet<double>();
                                for (int k = 0; k < beams[i].Value.Results.Count; k++)
                                {
                                    BeamForceResultModel force = (BeamForceResultModel)beams[i].Value.Results[k];
                                    double dist = Math.Round(force.NormalizedLength ? force.Station * beams[i].Value.Length : force.Station, 15);
                                    stationBuffer.Add(dist);
                                }
                                double[] dists = stationBuffer.ToArray();

                                List<(double dist, double max, double min)> v = new List<(double dist, double max, double min)>();
                                for (int n = 0; n < dists.Length; n++)
                                {
                                    ResultBeamForces[] fff = forces[i][n];
                                    double min = 0;
                                    double max = 0;
                                    switch (forceComponent)
                                    {
                                        case 0:
                                            {
                                                for (int j = 0; j < fff.Length; j++)
                                                {
                                                    double combonentBuffer = fff[j].N;
                                                    MinMax(j, combonentBuffer, showMinMax, ref absoluteMaxValue, ref absoluteMinValue, ref absoluteMaxIndex, ref absoluteMinIndex, ref max, ref min);
                                                }
                                                min *= FROMNTOKN;
                                                max *= FROMNTOKN;
                                                break;
                                            }

                                        case 1:
                                            {
                                                for (int j = 0; j < fff.Length; j++)
                                                {
                                                    double combonentBuffer = fff[j].V1;
                                                    MinMax(j, combonentBuffer, showMinMax, ref absoluteMaxValue, ref absoluteMinValue, ref absoluteMaxIndex, ref absoluteMinIndex, ref max, ref min);
                                                }
                                                min *= FROMNTOKN;
                                                max *= FROMNTOKN;
                                                break;
                                            }

                                        case 2:
                                            {
                                                for (int j = 0; j < fff.Length; j++)
                                                {
                                                    double combonentBuffer = fff[j].V2;
                                                    MinMax(j, combonentBuffer, showMinMax, ref absoluteMaxValue, ref absoluteMinValue, ref absoluteMaxIndex, ref absoluteMinIndex, ref max, ref min);
                                                }
                                                min *= FROMNTOKN;
                                                max *= FROMNTOKN;
                                                break;
                                            }

                                        case 3:
                                            {
                                                for (int j = 0; j < fff.Length; j++)
                                                {
                                                    double combonentBuffer = fff[j].T;
                                                    MinMax(j, combonentBuffer, showMinMax, ref absoluteMaxValue, ref absoluteMinValue, ref absoluteMaxIndex, ref absoluteMinIndex, ref max, ref min);
                                                }
                                                min *= FROMNMTOKNM;
                                                max *= FROMNMTOKNM;
                                                break;
                                            }

                                        case 4:
                                            {
                                                for (int j = 0; j < fff.Length; j++)
                                                {
                                                    double combonentBuffer = fff[j].M1;
                                                    MinMax(j, combonentBuffer, showMinMax, ref absoluteMaxValue, ref absoluteMinValue, ref absoluteMaxIndex, ref absoluteMinIndex, ref max, ref min);
                                                }
                                                min *= FROMNMTOKNM;
                                                max *= FROMNMTOKNM;
                                                break;
                                            }

                                        case 5:
                                            {
                                                for (int j = 0; j < fff.Length; j++)
                                                {
                                                    double combonentBuffer = fff[j].M2;
                                                    MinMax(j, combonentBuffer, showMinMax, ref absoluteMaxValue, ref absoluteMinValue, ref absoluteMaxIndex, ref absoluteMinIndex, ref max, ref min);
                                                }
                                                min *= FROMNMTOKNM;
                                                max *= FROMNMTOKNM;
                                                break;
                                            }
                                    }

                                    double mod = 0;
                                    if (n == dists.Length - 1)
                                        mod = -1;
                                    v.Add((x + dists[n] - mod, max, min));
                                }

                                var sorted = v.OrderBy(a => a.dist).ToList();
                                for (int k = 0; k < sorted.Count; k++)
                                    gc.AddMaximumValues(sorted[k].dist, sorted[k].min, sorted[k].max);

                                x += dists.Max();
                                gc.FrameLenths.Add(dists.Max());
                            }

                            comboToDraw.Insert(0, comboName[absoluteMaxIndex]);
                            comboToDraw.Insert(1, comboName[absoluteMinIndex]);

                            for (int c = 0; c < comboToDraw.Count; c++)
                            {
                                int comboIndex = comboName.IndexOf(comboToDraw[c]);

                                Color color = ComboColors[c % ComboColors.Length];
                                gc.AddCurve(comboToDraw[c], color);

                                x = 0;

                                for (int i = 0; i < beams.Count; i++)
                                {
                                    HashSet<double> stationBuffer = new HashSet<double>();
                                    List<(string combo, double delta, double value)> cc = new List<(string combo, double delta, double value)>();

                                    for (int k = 0; k < beams[i].Value.Results.Count; k++)
                                    {
                                        BeamForceResultModel force = (BeamForceResultModel)beams[i].Value.Results[k];
                                        double dist = Math.Round(force.NormalizedLength ? force.Station * beams[i].Value.Length : force.Station, 15);
                                        stationBuffer.Add(dist);
                                    }
                                    double[] dists = stationBuffer.ToArray();

                                    for (int n = 0; n < dists.Length; n++)
                                    {
                                        ResultBeamForces[] fff = forces[i][n];
                                        double vv = 0;
                                        if (forceComponent == 0)
                                        {
                                            vv = fff[comboIndex].N * FROMNTOKN;
                                        }
                                        else if (forceComponent == 1)
                                        {
                                            vv = fff[comboIndex].V1 * FROMNTOKN;
                                        }
                                        else if (forceComponent == 2)
                                        {
                                            vv = fff[comboIndex].V2 * FROMNTOKN;
                                        }
                                        else if (forceComponent == 3)
                                        {
                                            vv = fff[comboIndex].T * FROMNMTOKNM;
                                        }
                                        else if (forceComponent == 4)
                                        {
                                            vv = fff[comboIndex].M1 * FROMNMTOKNM;
                                        }
                                        else if (forceComponent == 5)
                                        {
                                            vv = fff[comboIndex].M2 * FROMNMTOKNM;
                                        }

                                        double mod = 0;
                                        if (n == dists.Length - 1)
                                            mod = -1;
                                        cc.Add((comboToDraw[c], x + dists[n] - mod, vv));
                                    }

                                    var sorted = cc.OrderBy(a => a.delta).ToList();

                                    for (int k = 0; k < sorted.Count(); k++)
                                        gc.AddCurveValue(sorted[k].combo, sorted[k].delta, sorted[k].value);

                                    x += dists.Max();
                                }
                            }

                            switch (forceComponent)
                            {
                                case 0:
                                case 1:
                                case 2:
                                    gc.YAxisTitle = "kN";
                                    break;
                                case 3:
                                case 4:
                                case 5:
                                    gc.YAxisTitle = "kNm";
                                    break;
                            }

                            gc.XAxisTitle = "m";

                            break;
                    }

                    double _valuesScaleRatio = 1.0;
                    Bitmap _graphicImage = gc.Create(true, (float)_valuesScaleRatio);

                    _graphicImage.Save(savePath, ImageFormat.Png);
                    _run = false;
                }
                catch (Exception e)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Fail to create the image: {e}");
                    _run = false;
                }
            }
        }

        private void MinMax(int j, double combonentBuffer, bool showMinMax, ref double absoluteMaxValue, ref double absoluteMinValue, ref int absoluteMaxIndex,
            ref int absoluteMinIndex, ref double max, ref double min)
        {
            if (showMinMax)
            {
                if (combonentBuffer < absoluteMinValue)
                {
                    absoluteMinValue = combonentBuffer;
                    absoluteMinIndex = j;
                }
                if (combonentBuffer > absoluteMaxValue)
                {
                    absoluteMaxValue = combonentBuffer;
                    absoluteMaxIndex = j;
                }
            }
            if (combonentBuffer < min)
                min = combonentBuffer;
            if (combonentBuffer > max)
                max = combonentBuffer;
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("77e9f533-e4e8-41de-baaf-6bfece84def9");
    }
}
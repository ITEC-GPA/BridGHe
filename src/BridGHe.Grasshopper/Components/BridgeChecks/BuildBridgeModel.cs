using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using GPC.Utilities.Maths;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    public class BuildBridgeModel : GH_Component
    {
        protected bool _run;

        /// <summary>
        /// Initializes a new instance of the ChecksComponent class.
        /// </summary>
        public BuildBridgeModel()
          : base("Build BridGHe Model", "BMCM", "Merge the BridGHe models in to a single BridGHe model with input phases", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_MODEL)
        {
            _run = false;
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
            pManager.AddGenericParameter("BridGHe Models", "FM", "The FEM models (more than one for stages)", GH_ParamAccess.list);
            pManager.AddGenericParameter("Load Combinations", "LCB", "The load cases combinations", GH_ParamAccess.tree);
            pManager.AddTextParameter("Phase Inf Load Cases", "INFLC", "The load cases name to assign to the Inf phase", GH_ParamAccess.list);
            pManager[2].Optional = true;
            pManager.AddTextParameter("Phase Ninf Load Cases", "NINFLC", "The load cases name to assign to the NInf phase", GH_ParamAccess.list);
            pManager[3].Optional = true;
            pManager.AddTextParameter("Phase N0 Load Cases", "N0LC", "The load cases name to assign to the N0 phase", GH_ParamAccess.list);
            pManager[4].Optional = true;
            pManager.AddTextParameter("Phase Ninf strain Load Cases", "NINFSLC", "The load cases name to assign to the Phase Ninf strain phase", GH_ParamAccess.list);
            pManager[5].Optional = true;
            pManager.AddTextParameter("Phase N0 strain Load Cases", "N0SLC", "The load cases name to assign to the Phase N0 strain phase", GH_ParamAccess.list);
            pManager[6].Optional = true;
            pManager.AddTextParameter("Phase n ritiro FEM Load Cases", "NSHLC", "FEM forces calculated with the n ritiro section. Distinct from slab eigenstrain/autostress cases.", GH_ParamAccess.list);
            pManager[7].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Model", "M", "The FEM model", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Message = "";
            List<GH_Model> models = new List<GH_Model>();
            int n = 0;
            List<string> loadCaseOnlySteel = new List<string>();
            List<string> loadCaseNInf = new List<string>();
            List<string> loadCaseN0 = new List<string>();
            List<string> loadCaseStrainNInf = new List<string>();
            List<string> loadCaseStrainN0 = new List<string>();

            if (!DA.GetDataList(n++, models))
                return;

            if (models.Count == 0 || models.Any(i => i == null))
            {
                _run = false;
                return;
            }

            if (models.Any(i => i.Value == null))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Null model");
                Message = "Error";
                _run = false;
                return;
            }

            // Retrieve all te super elements used in the model
            List<SuperElementAttributeModel> superElementsBuffer = Common.Helpers.ModelHelper.GetSuperElements(models[0].Value);
            List<string> superElementsName = new List<string>();
            for (int i = 0; i < superElementsBuffer.Count; i++)
                superElementsName.Add(superElementsBuffer[i].Value);

            // Connect the ValueList component on the analysis results input item
            IEnumerable<IGH_Param> keys_analysisResultLists = Params.Input[n].Sources.Where(s => s.GetType() == typeof(GH_ValueList));
            foreach (GH_ValueList vallist in keys_analysisResultLists)
            {
                if (vallist.ListMode != GH_ValueListMode.DropDown)
                    vallist.ListMode = GH_ValueListMode.DropDown;

                ComponentsHelper.SetValueList(vallist, superElementsName);
                vallist.NickName = "Super-Element";
            }

            bool has_combinations = DA.GetDataTree(n++, out GH_Structure<IGH_Goo> combinations);

            HashSet<string> casesNamesSet = new HashSet<string>();
            int m = 1;
            for (int i = 0; i < models.Count; i++)
            {
                GH_Model model = models[i];
                // Add the model load and combination cases from results
                List<CaseModel> modelLoadCases = new List<CaseModel>(model.Value.Elements.SelectMany(e => e.Results).Select(l => l.LoadCase).Distinct());
                for (int j = 0; j < modelLoadCases.Count; j++)
                {
                    CaseModel loadCase = modelLoadCases[j];
                    string lcase_name = $"{m}-{loadCase.Name}";
                    casesNamesSet.Add(lcase_name);
                }
                m++;
            }

            List<string> casesNames = casesNamesSet.ToList();
            casesNames.Sort();

            DA.GetDataList(n, loadCaseOnlySteel);
            {
                IEnumerable<IGH_Param> keys_analysisResultListsCase = Params.Input[n].Sources.Where(s => s.GetType() == typeof(GH_ValueList));
                foreach (GH_ValueList vallist in keys_analysisResultListsCase)
                {
                    if (vallist.ListMode != GH_ValueListMode.CheckList)
                        vallist.ListMode = GH_ValueListMode.CheckList;
                    ComponentsHelper.SetValueList(vallist, casesNames);
                    vallist.NickName = Params.Input[n].NickName;
                }
                n++;
            }

            DA.GetDataList(n, loadCaseNInf);
            {
                IEnumerable<IGH_Param> keys_analysisResultListsCase = Params.Input[n].Sources.Where(s => s.GetType() == typeof(GH_ValueList));
                foreach (GH_ValueList vallist in keys_analysisResultListsCase)
                {
                    if (vallist.ListMode != GH_ValueListMode.CheckList)
                        vallist.ListMode = GH_ValueListMode.CheckList;
                    ComponentsHelper.SetValueList(vallist, casesNames);
                    vallist.NickName = Params.Input[n].NickName;
                }
                n++;
            }

            DA.GetDataList(n, loadCaseN0);
            {
                IEnumerable<IGH_Param> keys_analysisResultListsCase = Params.Input[n].Sources.Where(s => s.GetType() == typeof(GH_ValueList));
                foreach (GH_ValueList vallist in keys_analysisResultListsCase)
                {
                    if (vallist.ListMode != GH_ValueListMode.CheckList)
                        vallist.ListMode = GH_ValueListMode.CheckList;
                    ComponentsHelper.SetValueList(vallist, casesNames);
                    vallist.NickName = Params.Input[n].NickName;
                }
                n++;
            }

            HashSet<string> casesNamesStrainSet = new HashSet<string>();
            m = 1;
            for (int i = 0; i < models.Count; i++)
            {
                GH_Model model = models[i];
                // Add the model load and combination cases from results
                List<CaseModel> modelLoadCases = new List<CaseModel>();
                for (int j = 0; j < model.Value.Elements.Count; j++)
                {
                    for (int k = 0; k < model.Value.Elements[j].Loads.Count; k++)
                    {
                        if (model.Value.Elements[j].Loads[k] is CompositeBeamSlabStrainModel compositeBeamSlabStrainModel)
                        {
                            string lcase_name = $"{m}-{compositeBeamSlabStrainModel.LoadCase.Name}";
                            casesNamesStrainSet.Add(lcase_name);
                        }
                    }
                }
                m++;
            }

            List<string> casesNamesStrain = casesNamesStrainSet.ToList();
            casesNamesStrain.Sort();

            DA.GetDataList(n, loadCaseStrainNInf);
            {
                IEnumerable<IGH_Param> keys_analysisResultListsCase = Params.Input[n].Sources.Where(s => s.GetType() == typeof(GH_ValueList));
                foreach (GH_ValueList vallist in keys_analysisResultListsCase)
                {
                    if (vallist.ListMode != GH_ValueListMode.CheckList)
                        vallist.ListMode = GH_ValueListMode.CheckList;
                    ComponentsHelper.SetValueList(vallist, casesNamesStrain);
                    vallist.NickName = Params.Input[n].NickName;
                }
                n++;
            }

            DA.GetDataList(n, loadCaseStrainN0);
            {
                IEnumerable<IGH_Param> keys_analysisResultListsCase = Params.Input[n].Sources.Where(s => s.GetType() == typeof(GH_ValueList));
                foreach (GH_ValueList vallist in keys_analysisResultListsCase)
                {
                    if (vallist.ListMode != GH_ValueListMode.CheckList)
                        vallist.ListMode = GH_ValueListMode.CheckList;
                    ComponentsHelper.SetValueList(vallist, casesNamesStrain);
                    vallist.NickName = Params.Input[n].NickName;
                }
                n++;
            }

            var loadCaseNShrinkage = new List<string>();
            DA.GetDataList(n, loadCaseNShrinkage);
            // This phase takes analysed FEM forces, not the separate slab eigenstrain cases.
            foreach (var valueList in Params.Input[n].Sources.OfType<GH_ValueList>())
            {
                valueList.ListMode = GH_ValueListMode.CheckList;
                ComponentsHelper.SetValueList(valueList, casesNames);
                valueList.NickName = Params.Input[n].NickName;
            }
            n++;

            if (_run)
            {
                try
                {
                    var aligned = Common.Checks.BridgeMergeValidation.Align(models.Select(v=>v.Value).ToArray(), Settings.Instance.Tolerance);
                    Common.Checks.BridgeMergeValidation.ValidatePhases(casesNames, casesNamesStrain,
                        (loadCaseOnlySteel,false),(loadCaseNInf,false),(loadCaseN0,false),(loadCaseStrainNInf,true),(loadCaseStrainN0,true),(loadCaseNShrinkage,false));
                    // Nuovo modello di output
                    ModelModel newModel = new ModelModel(models[0].Value.Units);

                    var stageShrinkage = new LoadCaseModel.Stage("Shrinkage FEM Phase") { BridgePhase=LoadCaseModel.Stage.BridgePhases.NShrinkage };
                    LoadCaseModel.Stage stageOS = new LoadCaseModel.Stage("Only Steel Phase") { BridgePhase = LoadCaseModel.Stage.BridgePhases.OnlySteel };
                    LoadCaseModel.Stage stagenInst = new LoadCaseModel.Stage("Instant Time Loads Phase") { BridgePhase = LoadCaseModel.Stage.BridgePhases.NInstant };
                    LoadCaseModel.Stage stageninf = new LoadCaseModel.Stage("Infinite Time Loads Phase") { BridgePhase = LoadCaseModel.Stage.BridgePhases.NInfinite };
                    LoadCaseModel.Stage stageStrainInst = new LoadCaseModel.Stage("Instant Time Strain Phase") { BridgePhase = LoadCaseModel.Stage.BridgePhases.StrainInstant };
                    LoadCaseModel.Stage stageStraininf = new LoadCaseModel.Stage("Infinite Time Strain Phase") { BridgePhase = LoadCaseModel.Stage.BridgePhases.StrainInfinite };

                    for (int j = 0; j < casesNames.Count; j++)
                    {
                        LoadCaseModel loadCaseModel = new LoadCaseModel((j + 1).ToString(), casesNames[j]);
                        if (loadCaseOnlySteel.Contains(loadCaseModel.Name))
                            loadCaseModel.Stages.Add(stageOS);
                        else if (loadCaseN0.Contains(loadCaseModel.Name))
                            loadCaseModel.Stages.Add(stagenInst);
                        else if (loadCaseNInf.Contains(loadCaseModel.Name))
                            loadCaseModel.Stages.Add(stageninf);
                        else if (loadCaseNShrinkage.Contains(loadCaseModel.Name))
                            loadCaseModel.Stages.Add(stageShrinkage);

                        newModel.LoadCases.Add(loadCaseModel);
                    }

                    // Add geometries from the first model
                    for (int i = 0; i < models[0].Value.Nodes.Count; i++)
                    {
                        NodeModel nodeModel = new NodeModel(models[0].Value.Nodes[i]);
                        newModel.AddElements(new NodeModel[] { nodeModel });
                    }
                    for (int i = 0; i < models[0].Value.Beams.Count; i++)
                    {
                        BeamModel beamModel = new BeamModel(models[0].Value.Beams[i]);
                        newModel.AddElements(new BeamModel[] { beamModel });
                    }
                    for (int i = 0; i < models[0].Value.Plates.Count; i++)
                    {
                        PlateModel plateModel = new PlateModel(models[0].Value.Plates[i]);
                        newModel.AddElements(new PlateModel[] { plateModel });
                    }

                    for (int mm = 1; mm < models.Count; mm++)
                    {
                        //for (int i = 0; i < models[mm].Value.Nodes.Count; i++)
                        //{
                        //    for (int j = 0; j < models[mm].Value.Nodes[i].Loads.Count; j++)
                        //    {
                        //        newModel.Nodes[i].Loads.Add((LoadModel)models[mm].Value.Nodes[i].Loads[j].Clone());
                        //    }
                        //}
                        for (int i = 0; i < models[mm].Value.Beams.Count; i++)
                        {
                            for (int j = 0; j < aligned[mm][i].Loads.Count; j++)
                            {
                                newModel.Beams[i].Loads.Add((LoadModel)aligned[mm][i].Loads[j].Clone());
                            }
                        }
                        //for (int i = 0; i < models[mm].Value.Plates.Count; i++)
                        //{
                        //    for (int j = 0; j < models[mm].Value.Plates[i].Loads.Count; j++)
                        //    {
                        //        newModel.Plates[i].Loads.Add((LoadModel)models[mm].Value.Plates[i].Loads[j].Clone());
                        //    }
                        //}
                    }

                    for (int i = 0; i < newModel.Nodes.Count; i++)
                    {
                        newModel.Nodes[i].Attributes.Clear();
                        newModel.Nodes[i].Results.Clear();
                        newModel.Nodes[i].Loads.Clear();
                    }
                    for (int i = 0; i < newModel.Beams.Count; i++)
                    {
                        newModel.Beams[i].Results.Clear();
                        foreach(var strain in newModel.Beams[i].Loads.OfType<CompositeBeamSlabStrainModel>().ToArray())newModel.Beams[i].Loads.Remove(strain);
                    }
                    for (int i = 0; i < newModel.Plates.Count; i++)
                    {
                        newModel.Plates[i].Attributes.Clear();
                        newModel.Plates[i].Results.Clear();
                        newModel.Plates[i].Loads.Clear();
                    }


                    //TODO: risistemare unità di misura
                    //}

                    HashSet<double>[] stationsHash = new HashSet<double>[newModel.Beams.Count];
                    for (int i = 0; i < newModel.Beams.Count; i++)
                    {
                        stationsHash[i] = new HashSet<double>();
                        for (int mm = 0; mm < models.Count; mm++)
                        {
                            GH_Model model = models[mm];
                            BeamModel beam = aligned[mm][i];

                            for (int j = 0; j < beam.Results.Count; j++)
                            {
                                BeamForceResultModel force = (BeamForceResultModel)beam.Results[j];
                                double dist = Math.Round(force.NormalizedLength ? force.Station * beam.Length : force.Station, 15);
                                stationsHash[i].Add(dist);
                            }
                        }
                    }

                    double[][] stations = new double[newModel.Beams.Count][];
                    for (int i = 0; i < newModel.Beams.Count; i++)
                        stations[i] = Common.Checks.BridgeMergeValidation.CanonicalStations(stationsHash[i],newModel.Beams[i].Length);
                    for (int i = 0; i < stations.Length; i++)
                        stations[i] = stations[i].OrderBy(k => k).ToArray();

                    BeamForceResultModel[][][] results = new BeamForceResultModel[newModel.Beams.Count][][];

                    for (int mm = 0; mm < models.Count; mm++)
                    {
                        for (int i = 0; i < newModel.Beams.Count; i++)
                        {
                            double[] beamStations = stations[i];

                            results[i] = new BeamForceResultModel[beamStations.Length][];

                            for (int kk = 0; kk < beamStations.Length; kk++)
                                results[i][kk] = new BeamForceResultModel[newModel.LoadCases.Count];
                        }
                    }

                    // Add load cases and the tensions for the models                
                    m = 1;
                    for (int mm = 0; mm < models.Count; mm++)
                    {
                        GH_Model model = models[mm];

                        // Add the frame forces
                        for (int i = 0; i < newModel.Beams.Count; i++)
                        {
                            BeamModel newBeam = newModel.Beams[i];
                            BeamModel oldBeam = aligned[mm][i];
                            double[] beamStations = stations[i];

                            for (int j = 0; j < oldBeam.Results.Count; j++)
                            {
                                BeamForceResultModel force = (BeamForceResultModel)oldBeam.Results[j];
                                double stationDist = Math.Round(force.NormalizedLength ? force.Station * oldBeam.Length : force.Station, 15);
                                string loadCaseName = force.LoadCase.Name;

                                int stationIndex = Array.FindIndex(beamStations, x => Math.Abs(x-stationDist)<=Common.Checks.BridgeMergeValidation.StationTolerance(oldBeam.Length));
                                int comboIndex = newModel.LoadCases.Select(ll => ll.Name).ToList().IndexOf($"{m}-{loadCaseName}");

                                BeamForceResultModel newForce = new BeamForceResultModel(newModel.LoadCases.First(k => k.Name == $"{m}-{force.LoadCase.Name}"), beamStations[stationIndex], 0, "", false)
                                {
                                    AxialForce = force.AxialForce,
                                    ShearForce = force.ShearForce,
                                    Torque = force.Torque,
                                    BendingMoment = force.BendingMoment,
                                };

                                if (results[i][stationIndex][comboIndex] != null) throw new InvalidOperationException($"Duplicate force sample for beam {newBeam.BeamId}, case {loadCaseName}, station {stationDist}.");
                                results[i][stationIndex][comboIndex] = newForce;
                            }
                        }
                        m++;
                    }

                    for (int i = 0; i < newModel.Beams.Count; i++)
                    {
                        BeamModel beam = newModel.Beams[i];
                        //BeamModel oldBeam = aligned[mm][i];
                        double[] beamStations = stations[i];

                        for (int j = 0; j < beamStations.Length; j++)
                        {
                            for (int s = 0; s < newModel.LoadCases.Count; s++)
                            {
                                if (results[i][j][s] is null)
                                {
                                    double stationDist = beamStations[j];
                                    BeamForceResultModel forceMinus; ;
                                    if (j > 0)
                                        forceMinus = results[i][j - 1][s];
                                    else
                                        forceMinus = results[i][j][s];

                                    BeamForceResultModel forcePlus = null;
                                    if (j == results[i].Length - 1)
                                    {
                                        for (int jNext = j; jNext >= 0; jNext--)
                                        {
                                            if (results[i][jNext][s] is null)
                                                continue;
                                            else
                                            {
                                                forcePlus = results[i][jNext][s];
                                                break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        for (int jNext = j + 1; jNext < results[i].Length; jNext++)
                                        {
                                            if (results[i][jNext][s] is null)
                                                continue;
                                            else
                                            {
                                                forcePlus = results[i][jNext][s];
                                                break;
                                            }
                                        }
                                    }

                                    if (forceMinus == null || forcePlus == null || ReferenceEquals(forceMinus,forcePlus))
                                        throw new InvalidOperationException($"Incomplete results for beam {beam.BeamId}, case {newModel.LoadCases[s].Name}, station {stationDist}. Extrapolation is not permitted.");
                                    double distPlus = Math.Round(forcePlus.NormalizedLength ? forcePlus.Station * beam.Length : forcePlus.Station, 15);
                                    double distMinus = Math.Round(forceMinus.NormalizedLength ? forceMinus.Station * beam.Length : forceMinus.Station, 15);
                                    double x = stationDist - distMinus;
                                    double l = distPlus - distMinus;

                                    double nForce = Interpolation.GetLinearInterpolation(distMinus, distPlus, forceMinus.AxialForce, forcePlus.AxialForce, stationDist);
                                    double V1Force = Interpolation.GetLinearInterpolation(distMinus, distPlus, forceMinus.Vx, forcePlus.Vx, stationDist);
                                    double V2Force = Interpolation.GetLinearInterpolation(distMinus, distPlus, forceMinus.Vy, forcePlus.Vy, stationDist);
                                    double TForce = Interpolation.GetLinearInterpolation(distMinus, distPlus, forceMinus.Torque, forcePlus.Torque, stationDist);
                                    //double M1Force = Interpolation.GetLinearInterpolation(distMinus, distPlus, forceMinus.Mx, forcePlus.Mx, stationDist);
                                    //double M2Force = Interpolation.GetLinearInterpolation(distMinus, distPlus, forceMinus.My, forcePlus.My, stationDist);
                                    double M2Force = forceMinus.My + (forcePlus.Vy - forceMinus.Vy) / l * Math.Pow(x, 2) / 2.0 + forceMinus.Vy * x;
                                    double M1Force = forceMinus.Mx + (forcePlus.Vx - forceMinus.Vx) / l * Math.Pow(x, 2) / 2.0 + forceMinus.Vx * x;

                                    BeamForceResultModel newForce = new BeamForceResultModel(newModel.LoadCases.First(k => k.Name == $"{newModel.LoadCases[s].Name}"), stationDist, 0, "", false)
                                    {
                                        AxialForce = nForce,
                                        Vx = V1Force,
                                        Vy = V2Force,
                                        Torque = TForce,
                                        Mx = M1Force,
                                        My = M2Force,
                                    };

                                    results[i][j][s] = newForce;
                                }
                            }
                        }

                        for (int j = 0; j < results[i].Length; j++)
                        {
                            for (int k = 0; k < results[i][j].Length; k++)
                            {
                                beam.Results.Add(results[i][j][k]);
                            }
                        }
                    }

                    for (int j = 0; j < casesNamesStrain.Count; j++)
                    {
                        LoadCaseModel loadCaseModel = new LoadCaseModel((j + 1).ToString(), casesNamesStrain[j]);
                        if (loadCaseStrainN0.Contains(loadCaseModel.Name))
                            loadCaseModel.Stages.Add(stageStrainInst);
                        else if (loadCaseStrainNInf.Contains(loadCaseModel.Name))
                            loadCaseModel.Stages.Add(stageStraininf);

                        newModel.LoadCases.Add(loadCaseModel);
                    }

                    m = 1;
                    for (int i = 0; i < models.Count; i++)
                    {
                        GH_Model model = models[i];
                        for (int j = 0; j < model.Value.Beams.Count; j++)
                        {
                            BeamModel beam = aligned[i][j];
                            BeamModel newBeam = newModel.Beams[j];
                            for (int k = 0; k < beam.Loads.Count; k++)
                            {
                                if (beam.Loads[k] is CompositeBeamSlabStrainModel compositeBeamSlabStrainModel)
                                {
                                    string lcase_name = $"{m}-{compositeBeamSlabStrainModel.LoadCase.Name}";

                                    CompositeBeamSlabStrainModel clone = new CompositeBeamSlabStrainModel(compositeBeamSlabStrainModel);
                                    LoadCaseModel loadCaseModelClone = newModel.LoadCases.Where(vv => vv.Name == lcase_name).FirstOrDefault();
                                    loadCaseModelClone.Name = lcase_name;
                                    clone.LoadCase = loadCaseModelClone;
                                    newBeam.Loads.Add(clone);
                                }
                            }
                        }
                        m++;
                    }

                    // Set the load combinations
                    List<LoadCombinationModel> load_combinations = new List<LoadCombinationModel>();
                    if (has_combinations)
                    {
                        List<string> cnames = new List<string>();
                        for (int i = 0; i < combinations.Branches.Count; i++)
                        {
                            List<IGH_Goo> row = (List<IGH_Goo>)combinations.get_Branch(i);
                            if (i == 0) // First rows: check cases name
                            {
                                cnames.AddRange(row.Cast<GH_String>().Where(c => c.Value != "").Select(c => c.Value));
                                IEnumerable<string> invalid_names = cnames.Where(c => !casesNames.Contains(c));
                                invalid_names = invalid_names.Where(c => !casesNamesStrain.Contains(c));
                                if (invalid_names.Count() > 0)
                                {
                                    throw new ArgumentException("Invalid combination cases: " + string.Join(", ", invalid_names));
                                }
                            }
                            else
                            {
                                string name = ((GH_String)row[0]).Value;
                                if (row.Count != cnames.Count+1) throw new ArgumentException($"Combination {name}: expected {cnames.Count} coefficients.");
                                LoadCombinationModel loadCombinationModel = new LoadCombinationModel(name);
                                for (int j = 0; j < cnames.Count; j++)
                                {
                                    string text = ((GH_String)row[j+1]).Value;
                                    if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double factor) &&
                                        !double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out factor) || !double.IsFinite(factor))
                                        throw new ArgumentException($"Invalid coefficient '{text}' in {name}.");
                                    var loadCase = newModel.LoadCases.Single(c => c.Name == cnames[j]);
                                    if (factor != 0 && loadCase.Stages.Count != 1) throw new ArgumentException($"Case {loadCase.Name} in {name} must be assigned to exactly one phase.");
                                    loadCombinationModel.Values.Add(loadCase,factor);
                                }

                                newModel.LoadCombinations.Add(loadCombinationModel);
                            }
                        }
                    }

                    newModel.Name = "Merged Model";
                    GH_Model new_ghmodel = new GH_Model() { Value = newModel };
                    ModelHelper.RestoreModelGuids(models[0], ref new_ghmodel);

                    DA.SetData(0, new_ghmodel);

                    Message = "Done";
                    _run = false;
                }
                catch (Exception e)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Generic error: {e}");
                    Message = "Error";
                    _run = false;
                    return;
                }
            }
            _run = false;
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("16695837-3f63-4305-a1ff-4491826dc3fe");
    }
}

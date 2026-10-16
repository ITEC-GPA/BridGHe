using BridGHe.Common.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;


namespace BridGHe.Common.Models
{
    [Serializable]
    public class ModelModel : ISerializable, ICloneable
    {
        public const double GRAVITY_mmSuS2 = 9806.65;

        public enum ModelUnits
        {
            [Description("kNm (m, kN, T, kPa, s, kJ, C)")]
            Knm,
            [Description("SI (m, N, kg, Pa, s, J, C)")]
            SI,
            [Description("Nmm (mm, N, T, MPa, s, mJ, C)")]
            Nmm,
            [Description("PSI (in, lbf, lb, psi, s, btu, F)")]
            IPS
        }

        protected string _name;
        protected ModelUnits _units;
        protected List<ElementModel> _elements;
        protected List<NodeModel> _nodes;
        protected List<BeamModel> _beams;
        protected List<PlateModel> _plates;
        protected List<LoadCaseModel> _loadCases;
        protected List<LoadCombinationModel> _loadCombinations;
        protected Dictionary<string, int> _nodesIndexes;
        protected Dictionary<string, int> _beamsIndexes;
        protected Dictionary<string, int> _platesIndexes;
        protected List<NamedSetsModel> _namedSetsModels;
        protected List<StagedConstructionModel> _stagedConstruction;
        protected List<FunctionModel> _functionModels;

        public List<MovingLoads.MovingPath> MovingPaths { get; private set; } = new List<MovingLoads.MovingPath>();

        public Dictionary<int, string> GeneratedMovingCases { get; internal set; } = new Dictionary<int, string>();

        public string Name
        {
            get => _name;
            set => _name = value;
        }

        public ModelUnits Units => _units;

        public List<ElementModel> Elements => _elements;

        public List<NodeModel> Nodes => _nodes;

        public List<BeamModel> Beams => _beams;

        public List<PlateModel> Plates => _plates;

        public List<LoadCaseModel> LoadCases => _loadCases;

        public List<LoadCombinationModel> LoadCombinations => _loadCombinations;

        public List<NamedSetsModel> NamedSetsModels => _namedSetsModels;

        public List<StagedConstructionModel> StagedConstruction => _stagedConstruction;

        public List<FunctionModel> FunctionModels => _functionModels;

        public ModelModel(ModelUnits units)
        {
            _name = "";
            _units = units;
            _elements = new List<ElementModel>();
            _nodes = new List<NodeModel>();
            _beams = new List<BeamModel>();
            _plates = new List<PlateModel>();
            _loadCases = new List<LoadCaseModel>();
            _loadCombinations = new List<LoadCombinationModel>();
            _nodesIndexes = new Dictionary<string, int>();
            _beamsIndexes = new Dictionary<string, int>();
            _platesIndexes = new Dictionary<string, int>();
            _namedSetsModels = new List<NamedSetsModel>();
            _stagedConstruction = new List<StagedConstructionModel>();
            _functionModels = new List<FunctionModel>();
        }

        public ModelModel(ModelModel model)
        {
            var copy = ModelArchive.Clone(model ?? throw new ArgumentNullException(nameof(model)));
            _name = copy._name;
            _units = copy._units;
            _elements = copy._elements;
            _nodes = copy._nodes;
            _beams = copy._beams;
            _plates = copy._plates;
            _loadCases = copy._loadCases;
            _loadCombinations = copy._loadCombinations;
            _nodesIndexes = copy._nodesIndexes;
            _beamsIndexes = copy._beamsIndexes;
            _platesIndexes = copy._platesIndexes;
            _namedSetsModels = copy._namedSetsModels;
            _stagedConstruction = copy._stagedConstruction;
            _functionModels = copy._functionModels;
            MovingPaths = copy.MovingPaths;
            GeneratedMovingCases = copy.GeneratedMovingCases;
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "ModelModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 5;

        protected ModelModel(SerializationInfo info, StreamingContext context)
        {
            int savedRelease;
            try
            {
                savedRelease = info.GetInt32(SERIALIZATION_RELEASE_NAME);
            }
            catch
            {
                savedRelease = 0;
            }
            MovingPaths = savedRelease >= 5 ? System.Text.Json.JsonSerializer.Deserialize<List<MovingLoads.MovingPath>>(info.GetString("MovingPaths")) : new List<MovingLoads.MovingPath>();
            GeneratedMovingCases = savedRelease >= 5 ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, string>>(info.GetString("GeneratedMovingCases")) : new Dictionary<int, string>();
            _name = info.GetString("Name");
            _units = (ModelUnits)info.GetValue("Units", typeof(ModelUnits));
            _elements = (List<ElementModel>)info.GetValue("Elements", typeof(List<ElementModel>));
            _loadCases = (List<LoadCaseModel>)info.GetValue("LoadCases", typeof(List<LoadCaseModel>));
            _loadCombinations = (List<LoadCombinationModel>)info.GetValue("LoadCombinations", typeof(List<LoadCombinationModel>));
            _nodes = new List<NodeModel>();
            _beams = new List<BeamModel>();
            _plates = new List<PlateModel>();
            // These indexes are provided to improve the performance getting elements
            _nodesIndexes = new Dictionary<string, int>();
            _beamsIndexes = new Dictionary<string, int>();
            _platesIndexes = new Dictionary<string, int>();
            if (savedRelease >= 2)
                _namedSetsModels = (List<NamedSetsModel>)info.GetValue("NamedSetsModels", typeof(List<NamedSetsModel>));
            else
                _namedSetsModels = new List<NamedSetsModel>();
            if (savedRelease >= 3)
                _stagedConstruction = (List<StagedConstructionModel>)info.GetValue("StagedConstruction", typeof(List<StagedConstructionModel>));
            else
                _stagedConstruction = new List<StagedConstructionModel>();
            if (savedRelease >= 4)
                _functionModels = (List<FunctionModel>)info.GetValue("FunctionModel", typeof(List<FunctionModel>));
            else
                _functionModels = new List<FunctionModel>();
            //Sync(0);//here cannot be done this because _elements is full of nulls! 
        }
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
                throw new ArgumentNullException("info");
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("MovingPaths", System.Text.Json.JsonSerializer.Serialize(MovingPaths));
            info.AddValue("GeneratedMovingCases", System.Text.Json.JsonSerializer.Serialize(GeneratedMovingCases));
            info.AddValue("Name", _name);
            info.AddValue("Units", _units, typeof(ModelUnits));
            info.AddValue("Elements", _elements, typeof(List<ElementModel>));
            info.AddValue("LoadCases", _loadCases, typeof(List<LoadCaseModel>));
            info.AddValue("LoadCombinations", _loadCombinations, typeof(List<LoadCombinationModel>));
            info.AddValue("NamedSetsModels", _namedSetsModels, typeof(List<NamedSetsModel>));
            info.AddValue("StagedConstruction", _stagedConstruction, typeof(List<StagedConstructionModel>));
            info.AddValue("FunctionModel", _functionModels, typeof(List<FunctionModel>));
        }

        #endregion

        public void AddElements(IEnumerable<ElementModel> elements)
        {
            int starting = _elements.Count();
            _elements.AddRange(elements);
            Sync(starting);
        }

        public void AddLoadCases(IEnumerable<LoadCaseModel> loadCases)
        {
            _loadCases.AddRange(loadCases);
        }

        public void AddLoadCombinations(IEnumerable<LoadCombinationModel> combinations)
        {
            _loadCombinations.AddRange(combinations);
        }

        public void AddNamedSets(IEnumerable<NamedSetsModel> namedSets)
        {
            _namedSetsModels.AddRange(namedSets);
        }

        public void AddStagedConstructions(IEnumerable<StagedConstructionModel> stagedConstructions)
        {
            _stagedConstruction.AddRange(stagedConstructions);
        }

        public void AddFunctionModels(IEnumerable<FunctionModel> functionModels)
        {
            _functionModels.AddRange(functionModels);
        }


        public NodeModel GetNode(string id)
        {
            if (_nodesIndexes.TryGetValue(id, out int index))
                return (NodeModel)_elements[index];
            return null;
        }

        public BeamModel GetBeam(string id)
        {
            if (_beamsIndexes.TryGetValue(id, out int index))
                return (BeamModel)_elements[index];
            return null;
        }

        public PlateModel GetPlate(string id)
        {
            if (_platesIndexes.TryGetValue(id, out int index))
                return (PlateModel)_elements[index];
            return null;
        }

        /// <summary>
        /// Synchronizes the indexes to the elements arrays
        /// </summary>
        /// <param name="starting">The index starting item</param>
        protected void Sync(int starting)
        {
            for (int i = starting; i < _elements.Count; i++)
            {
                if (_elements[i] is NodeModel node)
                {
                    if (!_nodesIndexes.ContainsKey(node.NodeId))
                    {
                        _nodes.Add(node);
                        _nodesIndexes.Add(node.NodeId, i);
                    }
                    else
                    {
                        NodeModel nn = (NodeModel)_elements[_nodesIndexes[node.NodeId]];
                        nn.Merge(node);
                    }
                }
                else if (_elements[i] is BeamModel beam)
                {
                    if (!_beamsIndexes.ContainsKey(beam.BeamId))
                    {
                        _beams.Add(beam);
                        _beamsIndexes.Add(beam.BeamId, i);
                    }
                    else
                    {
                        BeamModel nn = (BeamModel)_elements[_beamsIndexes[beam.BeamId]];
                        nn.Merge(beam);
                    }
                }
                else if (_elements[i] is PlateModel plate)
                {
                    if (!_platesIndexes.ContainsKey(plate.PlateId))
                    {
                        _plates.Add(plate);
                        _platesIndexes.Add(plate.PlateId, i);
                    }
                    else
                    {
                        PlateModel nn = (PlateModel)_elements[_platesIndexes[plate.PlateId]];
                        nn.Merge(plate);
                    }
                }
                else
                    throw new InvalidCastException("Unknown Element type");
            }
        }

        /// <summary>
        /// Make a deep clone of the Model including all of his components
        /// </summary>
        /// <returns>The deep copy</returns>
        public object Clone()
        { return Helpers.ModelArchive.Clone(this); }

        internal void RebuildIndexes()
        {
            _nodes.Clear(); _beams.Clear(); _plates.Clear();
            _nodesIndexes.Clear(); _beamsIndexes.Clear(); _platesIndexes.Clear();
            Sync(0);
        }

        /*/// <summary>
        /// Setup the conversion parameter for converting measures through the Utilities library
        /// </summary>
        /// <param name="units">The model units</param>
        /// <param name="lengthUnits"></param>
        /// <param name="forceUnits"></param>
        /// <param name="massUnits"></param>
        /// <param name="pressureUnits"></param>
        /// <param name="temperatureUnits"></param>
        public static void GetUtilitiesConversionParams(
            ModelUnits units,
            out Utilities.Units.LengthUnits lengthUnits,
            out Utilities.Units.ForceUnits forceUnits,
            out Utilities.Units.MassUnits massUnits,
            out Utilities.Units.PressureUnits pressureUnits,
            out Utilities.Units.TemperatureUnits temperatureUnits)
        {
            switch (units)
            {
                case ModelUnits.Knm:
                    lengthUnits = Utilities.Units.LengthUnits.m;
                    forceUnits = Utilities.Units.ForceUnits.kN;
                    massUnits = Utilities.Units.MassUnits.ton;
                    pressureUnits = Utilities.Units.PressureUnits.kPa;
                    temperatureUnits = Utilities.Units.TemperatureUnits.C;
                    break;
                case ModelUnits.Nmm:
                    lengthUnits = Utilities.Units.LengthUnits.mm;
                    forceUnits = Utilities.Units.ForceUnits.N;
                    massUnits = Utilities.Units.MassUnits.ton;
                    pressureUnits = Utilities.Units.PressureUnits.MPa;
                    temperatureUnits = Utilities.Units.TemperatureUnits.C;
                    break;
                case ModelUnits.SI:
                    lengthUnits = Utilities.Units.LengthUnits.m;
                    forceUnits = Utilities.Units.ForceUnits.N;
                    massUnits = Utilities.Units.MassUnits.kg;
                    pressureUnits = Utilities.Units.PressureUnits.Pa;
                    temperatureUnits = Utilities.Units.TemperatureUnits.C;
                    break;
                case ModelUnits.IPS:
                    lengthUnits = Utilities.Units.LengthUnits.inch;
                    forceUnits = Utilities.Units.ForceUnits.lbf;
                    massUnits = Utilities.Units.MassUnits.lb;
                    pressureUnits = Utilities.Units.PressureUnits.psi;
                    temperatureUnits = Utilities.Units.TemperatureUnits.F;
                    break;
                default:
                    throw new NotSupportedException();
            }
        }*/

        public override string ToString()
        {
            string text = $"BridGHe Model {_name}";
            text += "\nMeasure units: " + _units.GetDescription();
            text += string.Format("\n{0} Elements", _elements.Count);
            text += string.Format("\n{0} Cases", _loadCases.Count);
            text += string.Format("\n{0} Combinations", _loadCombinations.Count);
            text += string.Format("\n{0} NamedSets", _namedSetsModels.Count);
            return text;
        }
    }
}

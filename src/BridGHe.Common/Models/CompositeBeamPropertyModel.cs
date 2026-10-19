using GPC.Model.Materials;
using GPC.Model.Sections;
using GPC.Model.Sections.Concrete;
using GPC.Model.Sections.Rebar;
using System;
using System.Linq;
using System.Runtime.Serialization;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class CompositeBeamPropertyModel : BeamPropertyBaseModel, ISerializable
    {
        protected SectionConcreteSlabModel _concreteSection;
        protected SectionModel _steelSection;
        protected MaterialModel _concreteMaterial;
        protected MaterialModel _rebarMaterial;
        protected MaterialModel _steelMaterial;
        protected double _verticalOffset;
        protected double _nInfinite;
        protected double _nInstant;
        protected double _nShrinkage = double.NaN;
        protected bool _considerConcrete;
        protected bool _considerRebar;

        #region Properties

        public SectionConcreteSlabModel ConcreteSection { get => _concreteSection; set => _concreteSection = value; }

        public double VerticalOffset { get => _verticalOffset; set => _verticalOffset = value; }

        public SectionModel SteelSection { get => _steelSection; set => _steelSection = value; }

        public MaterialModel ConcreteMaterial { get => _concreteMaterial; set => _concreteMaterial = value; }

        public MaterialModel RebarMaterial { get => _rebarMaterial; set => _rebarMaterial = value; }

        public MaterialModel SteelMaterial { get => _steelMaterial; set => _steelMaterial = value; }

        public double NInfinite { get => _nInfinite; set => _nInfinite = value; }

        public double NInstant { get => _nInstant; set => _nInstant = value; }

        public double NShrinkage { get => double.IsNaN(_nShrinkage) ? _nInfinite : _nShrinkage; set => _nShrinkage = value; }

        public bool ConsiderConcrete { get => _considerConcrete; set => _considerConcrete = value; }

        public bool ConsiderRebar { get => _considerRebar; set => _considerRebar = value; }

        public override bool IsValid => _steelSection != null && _concreteSection != null && _concreteSection.IsValid
            && _steelMaterial != null && _concreteMaterial != null && _rebarMaterial != null;

        #endregion

        public CompositeBeamPropertyModel()
        {
            _concreteSection = null;
            _steelSection = null;
            _steelMaterial = null;
            _concreteMaterial = null;
            _rebarMaterial = null;
            _verticalOffset = 0;
        }

        public CompositeBeamPropertyModel(string id)
            : this()
        {
            _id = id;
        }

        public CompositeBeamPropertyModel(CompositeBeamPropertyModel bp)
        {
            CopyFrom(bp);
        }

        #region Serialization

        private readonly string SERIALIZATION_RELEASE_NAME = "CompositeBeamPropertyModelVersion"; //do not modify the name!
        private readonly int SERIALIZATION_RELEASE = 4;

        protected CompositeBeamPropertyModel(SerializationInfo info, StreamingContext context) : base(info, context)
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
            _id = info.GetString("Id");
            _name = info.GetString("Name");
            _concreteSection = (SectionConcreteSlabModel)info.GetValue("_concreteSection", typeof(SectionConcreteSlabModel));
            _steelSection = (SectionModel)info.GetValue("_steelSection", typeof(SectionModel));
            _concreteMaterial = (MaterialModel)info.GetValue("_concreteMaterial", typeof(MaterialModel));
            _rebarMaterial = (MaterialModel)info.GetValue("_rebarMaterial", typeof(MaterialModel));
            _steelMaterial = (MaterialModel)info.GetValue("_steelMaterial", typeof(MaterialModel));
            _verticalOffset = info.GetDouble("_verticalOffset");
            if (savedRelease >= 4) _nShrinkage = info.GetDouble("NShrinkage");
            _nInfinite = info.GetDouble("NInfinite"); _nInstant = info.GetDouble("NInstant");
            _considerConcrete = info.GetBoolean("ConsiderConcrete"); _considerRebar = info.GetBoolean("ConsiderRebar");
        }
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
                throw new ArgumentNullException("info");
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("_concreteSection", _concreteSection, typeof(SectionConcreteSlabModel));
            info.AddValue("_steelSection", _steelSection, typeof(SectionModel));
            info.AddValue("_concreteMaterial", _concreteMaterial, typeof(MaterialModel));
            info.AddValue("_rebarMaterial", _rebarMaterial, typeof(MaterialModel));
            info.AddValue("_steelMaterial", _steelMaterial, typeof(MaterialModel));
            info.AddValue("_verticalOffset", _verticalOffset);
            info.AddValue("NShrinkage", _nShrinkage);
            info.AddValue("NInfinite", _nInfinite); info.AddValue("NInstant", _nInstant);
            info.AddValue("ConsiderConcrete", _considerConcrete); info.AddValue("ConsiderRebar", _considerRebar);
        }

        #endregion

        public void CopyFrom(CompositeBeamPropertyModel bp)
        {
            if (bp._id != null)
                _id = bp._id;
            _name = bp._name;
            _concreteSection = bp._concreteSection;
            _steelSection = bp._steelSection;
            _material = bp._material;
            _concreteMaterial = bp._concreteMaterial;
            _rebarMaterial = bp._rebarMaterial;
            _steelMaterial = bp._steelMaterial;
            _verticalOffset = bp._verticalOffset;
            _nInfinite = bp._nInfinite;
            _nInstant = bp._nInstant;
            _nShrinkage = bp._nShrinkage;
            _considerConcrete = bp._considerConcrete;
            _considerRebar = bp._considerRebar;
        }

        public (double areaH, double SxH, double SyH, GPC.Geometry.Point2d centroidH, double JxxH, double JyyH, double JxyH, double JpH, double J11H, double J22H, double angleX)
            GetHomogeneizedMechanicalProperties(double n, bool considerConcrete = true, bool considerRebars = true, bool considerSteelSections = true)
        {
            ReinforcedConcreteSection reinforcedConcreteSection = GetReinforcedConcreteSection();

            double phi = ReinforcedConcreteSection.CalculateHomogenizedFactorPhi(n, reinforcedConcreteSection.SteelSections.First().Section.SteelMaterial,
                reinforcedConcreteSection.ConcreteMaterial);

            var prop = Helpers.CompositePropertyAdapter.Calculate(reinforcedConcreteSection, phi, considerConcrete, considerRebars, considerSteelSections);
            return prop;
        }

        /// <summary>Generic Straus section expressed in steel units, retaining the existing torsion convention.</summary>
        public BeamPropertyModel CreateHomogenizedProperty(double n, string suffix)
        {
            if (!double.IsFinite(n) || n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
            var p = GetHomogeneizedMechanicalProperties(n, ConsiderConcrete, ConsiderRebar, true);
            return new BeamPropertyModel
            {
                Name = Name + suffix, Material = SteelMaterial, PropertyType = BeamPropertyType.Beam,
                Section = new SectionModel
                {
                    SectionType = SectionModel.SectionTypes.Composite,
                    SectionArea = p.areaH / n, I11 = p.J11H / n, I22 = p.J22H / n,
                    J = GetOnlySteelMechanicalProperties().Jt,
                    Centroid = new Rhino.Geometry.Point2d(p.centroidH.X, p.centroidH.Y), AngleX1Rad = 0
                }
            };
        }

        public ReinforcedConcreteSection GetReinforcedConcreteSection()
        {
            if (_concreteSection?.IsValid != true) throw new ArgumentException("Invalid concrete slab or reinforcement rows.");
            double bfw = _steelSection.B1;       // bottom flange width
            double tfw = _steelSection.B2;       // top flange width
            double h = _steelSection.D;          // heigth
            double bft = _steelSection.T1;       // Bottom flange thickness
            double tft = _steelSection.T2;        // Top flange thickness
            double wt = _steelSection.T3;        // Web thickness

            double hh = _concreteSection.Height;      // heigth
            double b = _concreteSection.Width;       // base
            double topRebarsAreaTot = _concreteSection.TopRebarArea;
            double topRebarsCover = _concreteSection.TopCover;

            double verticalOffset = _verticalOffset;

            MaterialModel concreteModel = ConcreteMaterial;
            MaterialModel steelModel = SteelMaterial;
            MaterialModel rebarModel = RebarMaterial;

            SectionH sectionH = new SectionH(h, wt, tfw, tft, bfw, bft, Name);

            ConcreteMaterialEN1992 concreteMaterialEN1992 = new ConcreteMaterialEN1992(concreteModel.Name, concreteModel.SpecificCompressiveStrength,
                GPC.Model.Materials.ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
            SteelMaterialEN1992 steelMaterialEN1992 = new SteelMaterialEN1992(steelModel.Name, steelModel.Modulus, steelModel.MinimumYieldStress, steelModel.MinimumTensileStress,
                0.1, GPC.Model.Materials.SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, GPC.Model.Materials.SteelMaterial.SteelTypes.Structural);
            SteelMaterialEN1992 rebarMaterial = new SteelMaterialEN1992(rebarModel.Name, rebarModel.Modulus, rebarModel.MinimumYieldStress, rebarModel.MinimumTensileStress,
                0.1, GPC.Model.Materials.SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, GPC.Model.Materials.SteelMaterial.SteelTypes.Rebar);

            double rebarDiameterTop = Math.Sqrt(topRebarsAreaTot * 4 / Math.PI);

            RebarSectionCircular rebarTop = rebarDiameterTop != 0 ? new RebarSectionCircular("", rebarDiameterTop, rebarMaterial) : null;
            double rebarDiameterBottom = Math.Sqrt(_concreteSection.BottomRebarArea * 4 / Math.PI);
            RebarSectionCircular rebarBottom = rebarDiameterBottom != 0 ? new RebarSectionCircular("", rebarDiameterBottom, rebarMaterial) : null;
            ReinforcedConcreteSection reinforcedConcreteSection = BridGHe.Common.Checks.CompositeSectionFactory.Create(b, hh, concreteMaterialEN1992, rebarTop, b / 1.0,
                topRebarsCover, rebarBottom, b / 1.0, sectionH, steelMaterialEN1992, _concreteSection.BottomCover, verticalOffset, Name);
            return reinforcedConcreteSection;
        }

        public (double area, GPC.Geometry.Point2d centroid, double Jxx, double Jyy, double Jxy, double Jp, double J11, double J22, double angleX, double Jt)
            GetOnlySteelMechanicalProperties()
        {
            double bfw = _steelSection.B1;       // bottom flange width
            double tfw = _steelSection.B2;       // top flange width
            double h = _steelSection.D;          // heigth
            double bft = _steelSection.T1;       // Bottom flange thickness
            double tft = _steelSection.T2;        // Top flange thickness
            double wt = _steelSection.T3;        // Web thickness

            SectionH sectionH = new SectionH(h, wt, tfw, tft, bfw, bft, Name);

            return (sectionH.Area, sectionH.Centroid, sectionH.Jxx, sectionH.Jyy, sectionH.Jxy, sectionH.Jp, sectionH.J11, sectionH.J22, sectionH.AngleX1, sectionH.Jt);
        }

        public override string ToString()
        {
            string text = "BridGHe composite beam Property";
            if (_id != null && _id != string.Empty && _id != "")
                text += $" #{_id}";
            if (_name != null && _name != string.Empty && _name != "")
                text += $" #{_name}";
            return text;
        }
    }
}

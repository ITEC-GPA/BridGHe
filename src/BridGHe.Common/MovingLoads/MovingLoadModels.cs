using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BridGHe.Common.MovingLoads
{
    [Serializable, JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
    [JsonDerivedType(typeof(MovingPointForce), "point")]
    [JsonDerivedType(typeof(MovingDistributedForce), "distributed")]
    [JsonDerivedType(typeof(MovingHeatSource), "heat")]
    [JsonDerivedType(typeof(MovingVehicle), "vehicle")]
    [JsonDerivedType(typeof(MovingTemplate), "template")]
    [JsonDerivedType(typeof(MovingCentrifugal), "centrifugal")]
    [JsonDerivedType(typeof(MovingPath), "path")]
    public abstract class MovingObject
    {
        public abstract void Validate();
        public MovingObject Clone() => JsonSerializer.Deserialize<MovingObject>(JsonSerializer.Serialize(this));
        protected static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
        protected static void Finite(params double[] values) => Require(values != null && values.All(double.IsFinite), "Valori mancanti o non finiti nel carico mobile.");
        protected static void Range(int value, int max, string field) => Require(value >= 0 && value <= max, $"{field}: valore ammesso da 0 a {max}.");
    }

    [Serializable]
    public sealed class MovingPointForce : MovingObject
    {
        public int Mobility { get; set; }
        public int Axis { get; set; }
        public bool Adjacent { get; set; }
        public bool Centrifugal { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double[] Force { get; set; } = new double[3];
        public override void Validate() { Range(Mobility, 1, "Mobilità puntuale"); Range(Axis, 1, "Assi"); Require(Force?.Length == 3, "Forza: attese 3 componenti."); Finite(Force); Finite(X, Y); }
        public override string ToString() => $"Moving point ({X}, {Y}), F=({string.Join(", ", Force)})";
    }
    [Serializable]
    public sealed class MovingDistributedForce : MovingObject
    {
        public int Mobility { get; set; }
        public int Axis { get; set; }
        public bool Adjacent { get; set; }
        public bool Centrifugal { get; set; }
        public double X1 { get; set; }
        public double X2 { get; set; }
        public double Y1 { get; set; }
        public double Y2 { get; set; }
        public double[] Force { get; set; } = new double[3];
        public override void Validate() { Range(Mobility, 4, "Mobilità distribuita"); Range(Axis, 1, "Assi"); Require(Force?.Length == 3, "Carico: attese 3 componenti."); Finite(Force); Finite(X1, X2, Y1, Y2); Require(X2 > X1 && Y2 >= Y1, "Carico distribuito: X2 > X1 e Y2 >= Y1."); }
        public override string ToString() => $"Moving distributed [{X1}, {X2}] x [{Y1}, {Y2}], mobility={Mobility}";
    }
    [Serializable]
    public sealed class MovingHeatSource : MovingObject
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double SizeX { get; set; }
        public double SizeY { get; set; }
        public double Heat { get; set; }
        public override void Validate() { Finite(X, Y, SizeX, SizeY, Heat); Require(SizeX > 0 && SizeY > 0, "Sorgente termica: dimensioni positive richieste."); }
        public override string ToString() => $"Moving heat {Heat}, patch {SizeX} x {SizeY}";
    }
    [Serializable]
    public sealed class MovingVehicle : MovingObject
    {
        public string Name { get; set; } = "Vehicle";
        public int Instance { get; set; }
        public int Direction { get; set; }
        public double Velocity { get; set; }
        public double StartTime { get; set; }
        public string VehicleSet { get; set; } = "";
        public int[] Lanes { get; set; } = Array.Empty<int>();
        public MovingObject[] Loads { get; set; } = Array.Empty<MovingObject>();
        public override void Validate()
        {
            Require(!string.IsNullOrWhiteSpace(Name), "Nome veicolo vuoto."); Range(Instance, 1, "Istanza veicolo"); Range(Direction, 1, "Direzione"); Finite(Velocity, StartTime);
            Require(Velocity >= 0, "Velocità negativa: usare Direction per il verso.");
            Require(Lanes != null && Lanes.All(i => i > 0), "Le corsie sono numerate da 1.");
            Require(Loads != null && Loads.Length > 0, "Il veicolo deve contenere carichi.");
            foreach (var load in Loads) { Require(load is MovingPointForce || load is MovingDistributedForce || load is MovingHeatSource, "Tipo di carico non ammesso nel veicolo."); load.Validate(); }
        }
        public override string ToString() => $"Vehicle {Name}: {Loads.Length} loads";
    }
    [Serializable]
    public sealed class MovingCentrifugal : MovingObject
    {
        public string K0 { get; set; } = "0";
        public string K1 { get; set; } = "0";
        public bool LimitK1 { get; set; }
        public int LengthUnit { get; set; }
        public int ForceUnit { get; set; }
        public double MinimumK1 { get; set; }
        public double MaximumK1 { get; set; }
        public override void Validate() { Require(!string.IsNullOrWhiteSpace(K0) && !string.IsNullOrWhiteSpace(K1), "Espressioni centrifughe vuote."); Finite(MinimumK1, MaximumK1); Require(!LimitK1 || MaximumK1 >= MinimumK1, "Limiti K1 invertiti."); Range(LengthUnit, 4, "Unità lunghezza Straus"); Range(ForceUnit, 6, "Unità forza Straus"); }
        public override string ToString() => $"Centrifugal Fc = ({K0}) + ({K1}) Fz";
    }
    [Serializable]
    public sealed class MovingTemplate : MovingObject
    {
        public string Name { get; set; } = "Template";
        public MovingVehicle[] Vehicles { get; set; } = Array.Empty<MovingVehicle>();
        public double[] LaneFactors { get; set; } = new[] { 1.0 };
        public int MultiLaneType { get; set; }
        public bool TransitionLoad { get; set; }
        public bool BeamLateralTolerance { get; set; }
        public double RelativeTolerance { get; set; } = 0.01;
        public double MinimumLaneWidth { get; set; }
        public int Colour { get; set; } = 255;
        public MovingCentrifugal Centrifugal { get; set; }
        public string LibraryName { get; set; }
        public string LibraryItem { get; set; }
        public override void Validate()
        {
            Require(!string.IsNullOrWhiteSpace(Name), "Nome template vuoto.");
            if (!string.IsNullOrWhiteSpace(LibraryName)) { Require(!string.IsNullOrWhiteSpace(LibraryItem), "Elemento di libreria vuoto."); return; }
            Require(Vehicles != null && Vehicles.Length > 0, "Il template deve contenere veicoli.");
            Require(LaneFactors != null && LaneFactors.Length > 0, "Definire almeno una corsia."); Finite(LaneFactors); Require(LaneFactors.All(f => f >= 0), "Fattori corsia negativi.");
            Range(MultiLaneType, 1, "Tipo multicorsia"); Finite(RelativeTolerance, MinimumLaneWidth); Require(RelativeTolerance >= 0 && MinimumLaneWidth >= 0, "Tolleranza e larghezza corsia non negative richieste.");
            foreach (var vehicle in Vehicles) { Require(vehicle != null, "Veicolo nullo."); vehicle.Validate(); Require(vehicle.Lanes.All(i => i <= LaneFactors.Length), "Veicolo assegnato a corsia inesistente."); }
            Centrifugal?.Validate();
        }
        public override string ToString() => $"Moving template {Name}: {LaneFactors.Length} lanes";
    }
    [Serializable]
    public sealed class MovingPath : MovingObject
    {
        public string LoadCaseName { get; set; }
        public MovingTemplate Template { get; set; }
        public int Shape { get; set; }
        public int Surface { get; set; }
        public int Target { get; set; }
        public int Divisions { get; set; } = 20;
        public int EntitySetId { get; set; }
        public double[] Coordinates { get; set; } = new double[9];
        public bool ConvertToStaticCases { get; set; }
        public override void Validate()
        {
            Require(!string.IsNullOrWhiteSpace(LoadCaseName), "Caso di carico del percorso mancante."); Require(Template != null, "Template mancante."); Template.Validate();
            Range(Shape, 2, "Forma percorso"); Range(Surface, 1, "Superficie percorso"); Range(Target, 4, "Target percorso"); Require(Divisions > 0, "Numero divisioni positivo richiesto.");
            Require(Target != 1 || EntitySetId > 0, "Specificare l'ID del set di entità."); Require(Coordinates?.Length == 9, "Percorso: attese 9 coordinate."); Finite(Coordinates);
            double[] a = Enumerable.Range(0, 3).Select(i => Coordinates[i + 3] - Coordinates[i]).ToArray();
            double[] b = Enumerable.Range(0, 3).Select(i => Coordinates[i + 6] - Coordinates[i]).ToArray();
            double aa = a.Sum(v => v * v), bb = b.Sum(v => v * v), dot = a.Zip(b, (x, y) => x * y).Sum();
            Require(aa > 0 && bb > 0 && aa * bb - dot * dot > 1e-20 * aa * bb, "I tre punti devono definire un piano non degenere.");
        }
        public override string ToString() => $"Moving path: {LoadCaseName}, {Divisions} divisions";
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Loads
{
    public abstract class MovingComponent : GH_Component
    {
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
        protected MovingComponent(string name, string nickname, string description) : base(name, nickname, description, "BridGHe", "Moving Loads") { }
        protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Moving object", "M", "Oggetto per la catena di carichi mobili Straus R3.", GH_ParamAccess.item);
        protected abstract MovingObject Build(IGH_DataAccess da);
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try { var value = Build(da); value.Validate(); da.SetData(0, new MovingGoo(value)); }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); }
        }
        protected static T Read<T>(IGH_DataAccess da, int index) { T value = default; if (!da.GetData(index, ref value)) throw new ArgumentException($"Input {index + 1} mancante o non valido."); return value; }
        protected static T[] List<T>(IGH_DataAccess da, int index) { var values = new List<T>(); da.GetDataList(index, values); return values.ToArray(); }
        protected static T Object<T>(IGH_DataAccess da, int index, bool optional = false) where T : MovingObject
        {
            MovingGoo goo = null;
            if (!da.GetData(index, ref goo) && optional) return null;
            return goo?.Value is T value ? (T)value.Clone() : throw new ArgumentException($"Input {index + 1}: atteso {typeof(T).Name}.");
        }
        protected static T[] Objects<T>(IGH_DataAccess da, int index) where T : MovingObject => List<MovingGoo>(da, index).Select(g => g.Value is T value ? (T)value.Clone() : throw new ArgumentException("Tipo di oggetto mobile non valido.")).ToArray();
        protected static double[] Vector(Vector3d v) => new[] { v.X, v.Y, v.Z };
        protected static void Switches(GH_InputParamManager p, bool distributed)
        {
            p.AddIntegerParameter("Mobility", "M", distributed ? "0 grouped; 1 leading; 2 trailing; 3 full length; 4 floating." : "0 grouped; 1 floating.", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Axes", "A", "0 local; 1 global (Straus 123).", GH_ParamAccess.item, 0);
            p.AddBooleanParameter("Adjacency", "Adj", "Considerare adiacenza.", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Centrifugal", "Fc", "Applicare effetto centrifugo del template.", GH_ParamAccess.item, false);
        }
    }

    public sealed class MovingPointComponent : MovingComponent
    {
        public MovingPointComponent() : base("Moving Point Force", "MLPoint", "Forza puntuale mobile. Posizioni e forze nelle unità del modello.") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddNumberParameter("X", "X", "Posizione longitudinale nel veicolo.", GH_ParamAccess.item);
            p.AddNumberParameter("Y", "Y", "Posizione trasversale nel veicolo.", GH_ParamAccess.item);
            p.AddVectorParameter("Force", "F", "Componenti F1,F2,F3 nelle unità di forza del modello.", GH_ParamAccess.item);
            Switches(p, false);
        }
        protected override MovingObject Build(IGH_DataAccess da) => new MovingPointForce { X = Read<double>(da, 0), Y = Read<double>(da, 1), Force = Vector(Read<Vector3d>(da, 2)), Mobility = Read<int>(da, 3), Axis = Read<int>(da, 4), Adjacent = Read<bool>(da, 5), Centrifugal = Read<bool>(da, 6) };
        public override Guid ComponentGuid => new Guid("f41f7887-84b9-4f35-8dd1-23469be692d1");
    }
    public sealed class MovingDistributedComponent : MovingComponent
    {
        public MovingDistributedComponent() : base("Moving Distributed Force", "MLDistr", "Carico distribuito mobile Straus R3, tutte le cinque modalità di mobilità.") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            foreach (string name in new[] { "X1", "X2", "Y1", "Y2" }) p.AddNumberParameter(name, name, "Estremo dell'area di carico nelle unità del modello.", GH_ParamAccess.item);
            p.AddVectorParameter("Distributed force", "q", "Componenti distribuite q1,q2,q3 secondo la convenzione del template Straus; unità coerenti con il modello.", GH_ParamAccess.item);
            Switches(p, true);
        }
        protected override MovingObject Build(IGH_DataAccess da) => new MovingDistributedForce { X1 = Read<double>(da, 0), X2 = Read<double>(da, 1), Y1 = Read<double>(da, 2), Y2 = Read<double>(da, 3), Force = Vector(Read<Vector3d>(da, 4)), Mobility = Read<int>(da, 5), Axis = Read<int>(da, 6), Adjacent = Read<bool>(da, 7), Centrifugal = Read<bool>(da, 8) };
        public override Guid ComponentGuid => new Guid("fd54ae98-983c-4d01-ae72-ac562343f240");
    }
    public sealed class MovingHeatComponent : MovingComponent
    {
        public MovingHeatComponent() : base("Moving Heat Source", "MLHeat", "Sorgente termica mobile per analisi termica Straus; non è un incremento di temperatura strutturale.") { }
        protected override void RegisterInputParams(GH_InputParamManager p) { foreach (string name in new[] { "X", "Y", "Size X", "Size Y", "Heat" }) p.AddNumberParameter(name, name, "Valore nelle unità coerenti del modello Straus.", GH_ParamAccess.item); }
        protected override MovingObject Build(IGH_DataAccess da) => new MovingHeatSource { X = Read<double>(da, 0), Y = Read<double>(da, 1), SizeX = Read<double>(da, 2), SizeY = Read<double>(da, 3), Heat = Read<double>(da, 4) };
        public override Guid ComponentGuid => new Guid("ef7ed232-36a2-4fef-b665-5f764fe506e1");
    }
    public sealed class MovingVehicleComponent : MovingComponent
    {
        public MovingVehicleComponent() : base("Moving Vehicle", "MLVehicle", "Assembla carichi mobili in un veicolo; le velocità sono nelle unità di lunghezza del modello per secondo.") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Name", "N", "Nome veicolo.", GH_ParamAccess.item, "Vehicle");
            p.AddGenericParameter("Loads", "L", "Forze puntuali, distribuite e sorgenti termiche.", GH_ParamAccess.list);
            p.AddIntegerParameter("Instance", "I", "0 single lane; 1 double lane.", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Direction", "D", "0 forward; 1 backward.", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Velocity", "V", "Velocità non negativa [lunghezza/s].", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Start time", "t", "Tempo iniziale [s].", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Lanes", "Ln", "Numeri corsia da 1; lista vuota: tutte.", GH_ParamAccess.list); p[6].Optional = true;
            p.AddTextParameter("Vehicle set", "Set", "Nome del set di veicoli, vuoto per nessuno.", GH_ParamAccess.item, "");
        }
        protected override MovingObject Build(IGH_DataAccess da) => new MovingVehicle { Name = Read<string>(da, 0), Loads = Objects<MovingObject>(da, 1), Instance = Read<int>(da, 2), Direction = Read<int>(da, 3), Velocity = Read<double>(da, 4), StartTime = Read<double>(da, 5), Lanes = List<int>(da, 6), VehicleSet = Read<string>(da, 7) };
        public override Guid ComponentGuid => new Guid("6d5252f0-2acb-4887-b041-1912319ace92");
    }
    public sealed class MovingCentrifugalComponent : MovingComponent
    {
        public MovingCentrifugalComponent() : base("Moving Centrifugal Data", "MLCentrifugal", "Fc = K0 + K1 Fz; espressioni Straus con variabili R, L, V, g.") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("K0", "K0", "Espressione del termine indipendente.", GH_ParamAccess.item, "0");
            p.AddTextParameter("K1", "K1", "Espressione del moltiplicatore di Fz.", GH_ParamAccess.item, "V^2/(g*R)");
            p.AddBooleanParameter("Limit K1", "Limit", "Applicare limiti a K1.", GH_ParamAccess.item, false);
            p.AddNumberParameter("Minimum K1", "Min", "Limite inferiore.", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Maximum K1", "Max", "Limite superiore.", GH_ParamAccess.item, 1);
            p.AddIntegerParameter("Length unit", "L", "0 m; 1 cm; 2 mm; 3 ft; 4 in.", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Force unit", "F", "0 N; 1 kN; 2 MN; 3 kgf; 4 lbf; 5 tf; 6 kip.", GH_ParamAccess.item, 1);
        }
        protected override MovingObject Build(IGH_DataAccess da) => new MovingCentrifugal { K0 = Read<string>(da, 0), K1 = Read<string>(da, 1), LimitK1 = Read<bool>(da, 2), MinimumK1 = Read<double>(da, 3), MaximumK1 = Read<double>(da, 4), LengthUnit = Read<int>(da, 5), ForceUnit = Read<int>(da, 6) };
        public override Guid ComponentGuid => new Guid("039847dc-4d0d-4a6f-97b8-a152a8478811");
    }
    public sealed class MovingTemplateComponent : MovingComponent
    {
        public MovingTemplateComponent() : base("Moving Load Template", "MLTemplate", "Assembla veicoli, corsie e parametri di distribuzione Straus R3.") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Name", "N", "Nome template.", GH_ParamAccess.item, "Template");
            p.AddGenericParameter("Vehicles", "V", "Veicoli del template.", GH_ParamAccess.list);
            p.AddNumberParameter("Lane factors", "F", "Un fattore per ogni corsia; ordine da 1.", GH_ParamAccess.list, new[] { 1.0 });
            p.AddIntegerParameter("Multilane type", "M", "0 same; 1 different.", GH_ParamAccess.item, 0);
            p.AddBooleanParameter("Transition load", "Tr", "Abilita carichi di transizione.", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Beam lateral tolerance", "Bt", "Abilita tolleranza laterale per beam.", GH_ParamAccess.item, false);
            p.AddNumberParameter("Relative tolerance", "Tol", "Tolleranza relativa del template.", GH_ParamAccess.item, 0.01);
            p.AddNumberParameter("Minimum lane width", "W", "Larghezza minima nelle unità del modello.", GH_ParamAccess.item, 0);
            p.AddGenericParameter("Centrifugal data", "Fc", "Parametri centrifughi opzionali.", GH_ParamAccess.item); p[8].Optional = true;
        }
        protected override MovingObject Build(IGH_DataAccess da) => new MovingTemplate { Name = Read<string>(da, 0), Vehicles = Objects<MovingVehicle>(da, 1), LaneFactors = List<double>(da, 2), MultiLaneType = Read<int>(da, 3), TransitionLoad = Read<bool>(da, 4), BeamLateralTolerance = Read<bool>(da, 5), RelativeTolerance = Read<double>(da, 6), MinimumLaneWidth = Read<double>(da, 7), Centrifugal = Object<MovingCentrifugal>(da, 8, true) };
        public override Guid ComponentGuid => new Guid("0bcd0f19-d708-4120-bda0-818b1ecdf41d");
    }
    public sealed class MovingLibraryTemplateComponent : MovingComponent
    {
        public MovingLibraryTemplateComponent() : base("Moving Library Template", "MLLibrary", "Usa un template MLT già installato nella libreria Straus; conserva le sue impostazioni.") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        { p.AddTextParameter("Library", "Lib", "Nome esatto della libreria MLT installata.", GH_ParamAccess.item); p.AddTextParameter("Item", "Item", "Nome esatto del template nella libreria.", GH_ParamAccess.item); }
        protected override MovingObject Build(IGH_DataAccess da) => new MovingTemplate { Name = Read<string>(da, 1), LibraryName = Read<string>(da, 0), LibraryItem = Read<string>(da, 1) };
        public override Guid ComponentGuid => new Guid("ac0ed843-691a-4eb6-9089-66d134936be6");
    }
    public sealed class MovingPathComponent : MovingComponent
    {
        public MovingPathComponent() : base("Moving Load Path", "MLPath", "Percorso rettilineo, curvo o quadratico; i tre punti definiscono geometria e piano laterale Straus.") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Template", "T", "Template del percorso.", GH_ParamAccess.item);
            p.AddTextParameter("Load case name", "LC", "Nome del caso mobile; creato se assente dal modello.", GH_ParamAccess.item);
            p.AddPointParameter("Start", "S", "Punto iniziale.", GH_ParamAccess.item);
            p.AddPointParameter("End", "E", "Punto finale.", GH_ParamAccess.item);
            p.AddPointParameter("Lateral", "L", "Terzo punto che definisce il piano/curvatura secondo Straus.", GH_ParamAccess.item);
            p.AddIntegerParameter("Shape", "Sh", "0 straight; 1 curved; 2 quadratic.", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Surface", "Su", "0 flat; 1 curved. Ignorato per straight.", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Target", "Tg", "0 any; 1 entity set; 2 beam; 3 plate; 4 brick.", GH_ParamAccess.item, 0);
            p.AddIntegerParameter("Divisions", "D", "Discretizzazione del percorso.", GH_ParamAccess.item, 20);
            p.AddIntegerParameter("Entity set ID", "Set", "ID del set per Target=1, già presente nel file Straus.", GH_ParamAccess.item, 0);
            p.AddBooleanParameter("Generate static cases", "Static", "Converte il percorso in casi statici durante l'esportazione. Per leggerli usare Moving Static Analysis.", GH_ParamAccess.item, true);
        }
        protected override MovingObject Build(IGH_DataAccess da)
        {
            Point3d s = Read<Point3d>(da, 2), e = Read<Point3d>(da, 3), l = Read<Point3d>(da, 4);
            return new MovingPath { Template = Object<MovingTemplate>(da, 0), LoadCaseName = Read<string>(da, 1), Coordinates = new[] { s.X, s.Y, s.Z, e.X, e.Y, e.Z, l.X, l.Y, l.Z }, Shape = Read<int>(da, 5), Surface = Read<int>(da, 6), Target = Read<int>(da, 7), Divisions = Read<int>(da, 8), EntitySetId = Read<int>(da, 9), ConvertToStaticCases = Read<bool>(da, 10) };
        }
        public override Guid ComponentGuid => new Guid("5adbf6aa-3fe8-4802-b60e-2c5a31e7adf3");
    }
    public sealed class AttachMovingPathsComponent : GH_Component
    {
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
        public AttachMovingPathsComponent() : base("Attach Moving Paths", "MLModel", "Duplica il modello e sostituisce i percorsi mobili; output compatibile con Export e Analysis Straus R3.", "BridGHe", "Moving Loads") { }
        protected override void RegisterInputParams(GH_InputParamManager p) { p.AddGenericParameter("Model", "M", "Modello BridGHe.", GH_ParamAccess.item); p.AddGenericParameter("Paths", "P", "Percorsi mobili.", GH_ParamAccess.list); }
        protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Model", "M", "Modello con percorsi mobili.", GH_ParamAccess.item);
        protected override void SolveInstance(IGH_DataAccess da)
        {
            GH_Model input = null; var paths = new List<MovingGoo>(); if (!da.GetData(0, ref input) || !da.GetDataList(1, paths)) return;
            try
            {
                var model = MovingModelBuilder.ReplacePaths(input.Value,
                    paths.Select(goo => goo.Value as MovingPath ?? throw new ArgumentException("Atteso Moving Load Path.")));
                da.SetData(0, new GH_Model { Value = model });
            }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); }
        }
        public override Guid ComponentGuid => new Guid("db563562-6690-4024-9fc1-9860dc8223ca");
    }
}

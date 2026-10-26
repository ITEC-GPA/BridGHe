using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>Targeted integration into the user's saved REV001; never solve or rewrite the FEM branches.</summary>
internal static class BridgeRevisionInputsExample
{
    private static XElement Objects(XDocument x) => x.Descendants("chunk").Single(c => (string)c.Attribute("name") == "DefinitionObjects").Element("chunks");
    private static XElement Container(XElement o) => o.Element("chunks").Elements("chunk").Single(c => (string)c.Attribute("name") == "Container");
    private static string Value(XElement c, string name) => c.Element("items")?.Elements("item").SingleOrDefault(i => (string)i.Attribute("name") == name)?.Value;
    private static Guid Id(XElement o) => Guid.Parse(Value(Container(o), "InstanceGuid"));
    private static void Counts(XDocument x)
    {
        var list = Objects(x); int n = 0; foreach (var c in list.Elements("chunk")) c.SetAttributeValue("index", n++);
        list.SetAttributeValue("count", n); list.Parent.Element("items").Elements("item").Single(i => (string)i.Attribute("name") == "ObjectCount").Value = n.ToString();
    }
    private static Dictionary<Guid,XElement> Parameters(XDocument x) => Objects(x).Elements("chunk").SelectMany(o =>
        Container(o).Element("chunks").Elements("chunk").Where(c => (string)c.Attribute("name") is "param_input" or "param_output").Prepend(Container(o)))
        .Where(c => Value(c,"InstanceGuid") != null).ToDictionary(c => Guid.Parse(Value(c,"InstanceGuid")));
    private static IGH_Param In(GH_Component c, string name) => c.Params.Input.Single(p => p.Name == name);
    private static IGH_Param Out(GH_Component c, string name) => c.Params.Output.Single(p => p.Name == name);
    private static T Data<T>(IGH_Param p) => p.VolatileData.AllData(true).Select(g => g.ScriptVariable()).OfType<T>().First();

    internal static int Run()
    {
        try { return Execute(); }
        catch (Exception e) { Console.Error.WriteLine("REV001 INPUTS FAILED: " + e); return 1; }
    }
    private static int Execute()
    {
        string folder = ExamplePaths.Case(ParametricBridgePhasesExample.Id);
        string file = Path.Combine(folder, "50-ponte-quattro-modelli_REV001.ghx");
        string artifacts = Path.GetFullPath("artifacts/rev001-inputs"); Directory.CreateDirectory(artifacts);
        byte[] originalBytes = File.ReadAllBytes(file); string originalHash = Convert.ToHexString(SHA256.HashData(originalBytes));
        var original = XDocument.Load(file);
        GH_Document.EnableSolutions = false; BridgeMergeTests.Initialize();
        var math = Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\MathComponents.gha");
        typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(Assembly),typeof(string)},null)
            .Invoke(Grasshopper.Instances.ComponentServer,new object[]{math,math.Location});
        using var full = GrasshopperBridgeExample.Read(file);
        if (full.Objects.OfType<GH_Component>().Any(c => c.GetType().Name == "BridgeSegmentExcelComponent")) throw new InvalidOperationException("REV001 already contains the Excel component; do not integrate it twice.");
        var target = full.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == "BridgeSegmentSectionsComponent");
        var byPort = full.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Output.Select(p => (p,c))).ToDictionary(a => a.p, a => (IGH_DocumentObject)a.c);
        var closure = new HashSet<Guid>();
        void Visit(IGH_DocumentObject o)
        {
            if (!closure.Add(o.InstanceGuid)) return;
            if (o is GH_Component c && (c.GetType().Name.Contains("Straus") || c.GetType().Name.Contains("Analysis") || c.GetType().Name.Contains("Report")))
                throw new InvalidOperationException("Geometry fixture must not include solvers, analysis or reports: " + c.Name);
            var sources = o is GH_Component component ? component.Params.Input.SelectMany(p => p.Sources) : o is IGH_Param p ? p.Sources : Enumerable.Empty<IGH_Param>();
            foreach (var source in sources) Visit(byPort.TryGetValue(source, out var owner) ? owner : source);
        }
        Visit(target);
        var subset = new XDocument(original);
        Objects(subset).Elements("chunk").Where(o => !closure.Contains(Id(o))).Remove(); Counts(subset);
        string subsetPath = Path.Combine(artifacts,"geometry-only.ghx"); subset.Save(subsetPath);
        using var doc = GrasshopperBridgeExample.Read(subsetPath); doc.FilePath = file;
        BridgeSegmentExcelTests.Solve(doc);
        GH_Component Find(string name) => doc.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == name);
        var sections = Find("BridgeSegmentSectionsComponent"); var layout = Find("BridgeParametricLayoutComponent");
        var simple = Find("SimpleBridgeConfigurableComponent"); var segments = Find("BridgeSegmentsComponent");
        var geometryBefore = Data<SimpleBridgeGeometry>(sections.Params.Input[0]);
        var baseProperty = Data<BeamPropertyModel>(sections.Params.Input[1]);
        var oldTable = sections.Params.Input[2].VolatileData.AllData(true).Select(g => g.ScriptVariable().ToString()).ToArray();
        var originalSections = BridgeSegmentSections.Create(geometryBefore, baseProperty, oldTable);
        var curve = Data<Curve>(Out(layout,"Alignment"));
        var supportStations = Out(layout,"Support Stations").VolatileData.AllData(true).Select(g => Convert.ToDouble(g.ScriptVariable(),CultureInfo.InvariantCulture)).ToArray();
        var newObjects = new List<IGH_DocumentObject>(); var rewired = new HashSet<Guid>();
        float top = full.Objects.Where(o => o is not GH_Group).Min(o => o.Attributes.Bounds.Top) - 1100;
        GH_Component Add(string type, float x, float y)
        {
            var c = BridgeSegmentExcelTests.Add(doc,type); c.Attributes.Pivot = new PointF(x,y); newObjects.Add(c); return c;
        }
        T Control<T>(T p,string label,float x,float y,int width=650,int height=100) where T:IGH_Param
        {
            p.Name=p.NickName=label; p.CreateAttributes(); p.Attributes.Pivot=new PointF(x,y);
            if(p is GH_Panel)p.Attributes.Bounds=new RectangleF(x,y,width,height);
            doc.AddObject(p,false);newObjects.Add(p);return p;
        }
        GH_Panel Panel(string label,string text,float x,float y) => Control(new GH_Panel {UserText=text,Properties={Multiline=false}},label,x,y);
        void Replace(IGH_Param targetPort,params IGH_Param[] sources)
        {
            rewired.Add(targetPort.InstanceGuid);targetPort.RemoveAllSources();
            foreach(var source in sources)targetPort.AddSource(source);
        }
        void Wire(IGH_Param source,GH_Component destination,string port)=>Replace(In(destination,port),source);
        float ax=layout.Attributes.Bounds.Left-300;
        var preparation=Add("BridgeAlignmentInputsComponent",ax,top+250);
        preparation.NickName="Tracciato e appoggi per progressiva";
        var custom=In(layout,"Custom Alignment");
        if(custom.VolatileDataCount>0) Replace(In(preparation,"Alignment"),custom.Sources.ToArray());
        else
        {
            var curveParam=Control(new Param_Curve(),"TRACCIATO [mm] — imposta una curva Rhino",ax-850,top+30);
            curveParam.SetPersistentData(new GH_Curve(curve.DuplicateCurve()));Wire(curveParam,preparation,"Alignment");
        }
        double length=curve.GetLength(); bool endSupports=supportStations[0]<1e-5 && Math.Abs(supportStations[^1]-length)<1e-5;
        var distances=endSupports?supportStations.Skip(1).SkipLast(1):supportStations.AsEnumerable();
        var stationPanel=Panel("APPOGGI [mm] — distanze dal principio della curva",string.Join("\n",distances.Select(s=>s.ToString("R",CultureInfo.InvariantCulture))),ax-850,top+120);
        Wire(stationPanel,preparation,"Support Stations");
        var ends=Control(new GH_BooleanToggle{Value=endSupports},"Appoggi automatici agli estremi della curva",ax-850,top+250);Wire(ends,preparation,"End Supports");
        Replace(In(preparation,"Span Count"),In(layout,"Span Count").Sources.ToArray());
        Replace(In(preparation,"Maximum Segment Length"),In(segments,"Maximum Length").Sources.ToArray());
        Replace(In(preparation,"Maximum Element Length"),In(simple,"Maximum Element Length").Sources.ToArray());
        // Keep any user-defined per-girder joint tree. An empty common tree can be prepared here.
        bool sharedJoints=In(segments,"Joints").VolatileDataCount==0;
        if(sharedJoints)
        {
            var joints=Control(new Param_Number(),"Giunti comuni opzionali [mm] — vuoto usa lunghezza massima",ax-850,top+330);
            Wire(joints,preparation,"Joint Stations");Wire(Out(preparation,"Joint Stations"),segments,"Joints");
        }
        var tolerance=Panel("Tolleranza normalizzazione progressive [mm]","1",ax-850,top+420);Wire(tolerance,preparation,"Station Tolerance");
        Wire(Out(preparation,"Alignment"),layout,"Custom Alignment");Wire(Out(preparation,"Span Lengths"),layout,"Span Lengths");
        Wire(Out(preparation,"Start Overhang"),layout,"Start Overhang");Wire(Out(preparation,"End Overhang"),layout,"End Overhang");
        Wire(Out(preparation,"Span Count"),layout,"Span Count");
        Wire(Out(preparation,"Maximum Segment Length"),segments,"Maximum Length");
        Wire(Out(preparation,"Maximum Element Length"),simple,"Maximum Element Length");
        var notes=Panel("Procedura tracciato","Curva in mm. Progressive lungo la curva, ordinate automaticamente.\nSvuota APPOGGI per campate uguali. End Supports=False consente sbalzi.\nConci e mesh FEM sono indipendenti. Dopo cambi ai conci aggiorna Excel.",ax-850,top+550);
        var geometryGroup=new GH_Group {NickName="PREPARA GLI INPUT DAL TRACCIATO",Colour=Color.FromArgb(70,60,110,170)};geometryGroup.CreateAttributes();doc.AddObject(geometryGroup,false);
        foreach(var o in newObjects)geometryGroup.AddObject(o.InstanceGuid);newObjects.Add(geometryGroup);
        int geometryObjects=newObjects.Count;
        BridgeSegmentExcelTests.Solve(doc);
        var after=Data<SimpleBridgeGeometry>(sections.Params.Input[0]);
        if(geometryBefore.Members.Count!=after.Members.Count || !geometryBefore.Segments.SequenceEqual(after.Segments))
            throw new Exception("The preparation changed fabrication segments or the number of FEM axes.");
        var beforeById=geometryBefore.Members.ToDictionary(m=>m.Id);
        foreach(var member in after.Members)
            if(!beforeById.TryGetValue(member.Id,out var old) || old.Axis.From.DistanceTo(member.Axis.From)>1e-5 || old.Axis.To.DistanceTo(member.Axis.To)>1e-5)
                throw new Exception("The preparation changed geometry: "+member.Id);
        var current=BridgeSegmentSections.Create(after,baseProperty,oldTable);
        if(!current.ResolvedTable.SequenceEqual(originalSections.ResolvedTable))throw new Exception("Existing section dimensions changed.");
        string excelFile=Path.Combine(folder,"Sezioni_conci_REV001.xlsx");
        if(File.Exists(excelFile))throw new IOException("Excel already exists: preserve the user's file and choose an explicit next revision before integrating again.");
        BridgeSegmentWorkbook.Write(excelFile,after,initialTable:current.Table);
        float ex=sections.Attributes.Bounds.Left-250;
        var excel=Add("BridgeSegmentExcelComponent",ex,top+250);excel.NickName="Conci: crea Excel e rileggi le sezioni";
        Replace(excel.Params.Input[0],sections.Params.Input[0].Sources.ToArray());
        Replace(excel.Params.Input[3],sections.Params.Input[1].Sources.ToArray());
        Wire(Panel("FILE SEZIONI CONCI",Path.GetFileName(excelFile),ex-850,top+40),excel,"File");
        var mode=Control(new GH_ValueList{ListMode=GH_ValueListMode.DropDown},"EXCEL — Create / Read",ex-850,top+180);
        mode.ListItems.Clear();mode.ListItems.Add(new GH_ValueListItem("0 — Create template","0"));mode.ListItems.Add(new GH_ValueListItem("1 — Read Excel","1"){Selected=true});Wire(mode,excel,"Mode");
        Wire(Control(new GH_BooleanToggle{Value=false},"Consenti sovrascrittura Excel — normalmente False",ex-850,top+250),excel,"Overwrite");
        var oldPanel=sections.Params.Input[2].Sources.OfType<GH_Panel>().SingleOrDefault(p=>p.SourceCount==0);
        if(oldPanel!=null)Replace(oldPanel,Out(excel,"Section Table"));
        else Wire(Out(excel,"Section Table"),sections,"Section Table");
        var count=Control(new Param_Integer(),"NUMERO TOTALE DI CONCI — tutte le travi",ex+400,top+30);count.AddSource(Out(excel,"Count"));
        var ids=Panel("ID DEI CONCI — una riga per Gx-Cy","",ex+400,top+120);ids.AddSource(Out(excel,"Segment IDs"));
        var perGirder=Panel("CONCI PER TRAVE — stesso ordine di Girder IDs","",ex+400,top+280);perGirder.AddSource(Out(excel,"Counts per Girder"));
        Panel("Procedura Excel","File inizializzato con le tue sezioni. Modifica le celle gialle, salva Excel, premi Run in Read.\nCreate genera un template nuovo. * segue Base Property. Non modificare ID/progressive.\nIl pannello originale mostra ora la tabella letta da Excel.",ex-850,top+400);
        var excelGroup=new GH_Group{NickName="CONCI E SEZIONI DA EXCEL",Colour=Color.FromArgb(70,50,140,100)};excelGroup.CreateAttributes();doc.AddObject(excelGroup,false);
        foreach(var o in newObjects.Skip(geometryObjects))excelGroup.AddObject(o.InstanceGuid);newObjects.Add(excelGroup);
        BridgeSegmentExcelTests.Press(excel);excel.ExpireSolution(false);BridgeSegmentExcelTests.Solve(doc);
        var resolved=Out(sections,"Resolved Table").VolatileData.AllData(true).Select(g=>g.ScriptVariable().ToString()).ToArray();
        if(!resolved.SequenceEqual(originalSections.ResolvedTable))throw new Exception("Excel changed the existing section assignments.");
        string fresh=Path.Combine(artifacts,"updated-subgraph.ghx");GrasshopperBridgeExample.Write(doc,fresh);
        var updated=XDocument.Load(fresh);var result=new XDocument(original);
        var from=Parameters(updated);var into=Parameters(result);
        foreach(var id in rewired.Where(into.ContainsKey))
        {
            var items=into[id].Element("items");items.Elements("item").Where(i=>(string)i.Attribute("name") is "Source" or "SourceCount").Remove();
            items.Add(from[id].Element("items").Elements("item").Where(i=>(string)i.Attribute("name") is "Source" or "SourceCount").Select(i=>new XElement(i)));
            items.SetAttributeValue("count",items.Elements("item").Count());
        }
        var additions=newObjects.Select(o=>o.InstanceGuid).ToHashSet();
        Objects(result).Add(Objects(updated).Elements("chunk").Where(o=>additions.Contains(Id(o))).Select(o=>new XElement(o)));Counts(result);
        // Existing serialized objects must be identical after removing only the explicitly changed source links.
        var prior=Parameters(original);var check=Parameters(result);
        foreach(var id in rewired.Where(prior.ContainsKey))
        {
            var a=new XElement(prior[id]);var b=new XElement(check[id]);
            foreach(var p in new[]{a,b}) {var items=p.Element("items");items.Elements("item").Where(i=>(string)i.Attribute("name") is "Source" or "SourceCount").Remove();items.SetAttributeValue("count",items.Elements("item").Count());}
            if(!XNode.DeepEquals(a,b))throw new Exception("Integration changed data beyond the intended source links.");
        }
        string candidate=Path.Combine(artifacts,"REV001-candidate.ghx");result.Save(candidate);
        GH_Document.EnableSolutions=false;using var reopened=GrasshopperBridgeExample.Read(candidate);
        if(reopened.Objects.Count!=full.Objects.Count+newObjects.Count)throw new Exception("Objects lost on reopen.");
        if(!File.ReadAllBytes(file).SequenceEqual(originalBytes))throw new IOException("REV001 was saved again during preparation. The candidate is preserved; reload the new revision before replacing it.");
        string backup=Path.Combine(artifacts,"REV001-before-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".ghx");File.WriteAllBytes(backup,originalBytes);
        File.Copy(candidate,file,true);
        File.WriteAllText(Path.Combine(folder,"REV001-inputs-validation.json"),JsonSerializer.Serialize(new{
            SourceSha256=originalHash,PreservedUserRevision=true,Reopened=true,ExistingGeometryAndSectionsUnchanged=true,
            ExistingSerializedDataPreserved=true,NativeSolverInvoked=false,GeometryOnlyComponents=closure.Count,
            NewObjects=newObjects.Count,RewiredParameters=rewired.Where(into.ContainsKey).Select(id=>id.ToString()),
            Segments=after.Segments.Count,Girders=after.Segments.Select(s=>s.Girder).Distinct().Count(),FEMSteelMembers=after.Members.Count,
            Excel=Path.GetFileName(excelFile),CommonJointPreparationConnected=sharedJoints
        },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"REV001 INPUTS PASS: original geometry and {after.Segments.Count} segment sections preserved; alignment distances + Excel connected; original serialized data preserved; reopened; no solver invoked.");
        return 0;
    }
}

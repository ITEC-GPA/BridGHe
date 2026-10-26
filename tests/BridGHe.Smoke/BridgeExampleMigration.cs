using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using BridGHe.Common.Geometry;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

/// <summary>Transforms the actual saved graph, not a regenerated preset. Never executes solver/report/write branches.</summary>
internal static class BridgeExampleMigration
{
    private static readonly HashSet<string> OldSimple=new(){"SimpleBridgeSolidComponent","SimpleBridgeTrussComponent","SimpleBridgeConfigurableComponent"};
    // Older builders also serve as regression fixtures. Upgrade their saved files only
    // after all assertions on the legacy in-memory graph have completed.
    internal static int UpgradeGenerated(int result, params string[] prefixes)
    {
        if(result!=0)return result;
        foreach(string prefix in prefixes){int migrated=Run(prefix,true);if(migrated!=0)return migrated;}
        return 0;
    }
    internal static void Initialize()
    {
        GH_Document.EnableSolutions=false;BridgeMergeTests.Initialize();
        var parse=typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(Assembly),typeof(string)},null);
        foreach(string name in new[]{"MathComponents","CurveComponents","SurfaceComponents","VectorComponents","IOComponents","XformComponents"})
        {var a=Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\"+name+".gha");parse.Invoke(Grasshopper.Instances.ComponentServer,new object[]{a,a.Location});}
        string tt=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),@"Grasshopper\Libraries\tttoolbox1-9\TT_Toolbox_1-9\TT Toolbox\CORE.Grasshopper.Public.gha");
        if(File.Exists(tt)){var a=Assembly.LoadFrom(tt);parse.Invoke(Grasshopper.Instances.ComponentServer,new object[]{a,a.Location});}
        Directory.CreateDirectory("artifacts/modular-bridge");File.WriteAllText("artifacts/modular-bridge/registered.json",JsonSerializer.Serialize(Grasshopper.Instances.ComponentServer.ObjectProxies.Select(p=>new{p.Guid,p.Desc.Name})));
    }
    private static void Wire(IGH_Param target,params IGH_Param[] sources)
    {target.RemoveAllSources();foreach(var s in sources)target.AddSource(s);}
    private static void Copy(IGH_Param from,IGH_Param to)
    {
        var name=to.Name;var nickname=to.NickName;var description=to.Description;var access=to.Access;bool optional=to.Optional;
        var chunk=new GH_LooseChunk("Input");from.Write(chunk);chunk.RemoveItem("InstanceGuid");chunk.SetGuid("InstanceGuid",to.InstanceGuid);to.Read(chunk);
        to.Name=name;to.NickName=nickname;to.Description=description;to.Access=access;to.Optional=optional;Wire(to,from.Sources.ToArray());
    }
    private static void Replace(IGH_Param from,IGH_Param to)
    {foreach(var r in from.Recipients.ToArray())Wire(r,r.Sources.Select(s=>ReferenceEquals(s,from)?to:s).ToArray());}
    private static bool Supplied(IGH_Param p)=>p.SourceCount>0 || p is Param_Curve c&&c.PersistentDataCount>0 || p is Param_Number n&&n.PersistentDataCount>0;
    private static object Scalar(IGH_Param p)
    {
        if(p.SourceCount==0)
        {var data=p.GetType().GetProperty("PersistentData")?.GetValue(p) as IGH_Structure;return data?.AllData(true).FirstOrDefault()?.ScriptVariable();}
        if(p.SourceCount!=1)throw new InvalidOperationException("Mode must have a single explicit source: "+p.Name);
        var s=p.Sources[0];return s switch{GH_Panel panel when panel.SourceCount==0=>panel.UserText,GH_NumberSlider slider=>slider.CurrentValue,GH_BooleanToggle toggle=>toggle.Value,GH_ValueList list=>list.SelectedItems.Single().Expression,_=>throw new InvalidOperationException("Cannot resolve mode without executing graph: "+s.Name)};
    }
    private readonly record struct GeometrySnapshot(Guid Source,SimpleBridgeGeometry Geometry,SimpleBridgeGeometry Grillage);
    private static GeometrySnapshot[] Geometry(string file,bool migrated)
    {
        using var d=GrasshopperBridgeExample.Read(file);d.FilePath=file;
        var targets=d.Objects.OfType<GH_Component>().Where(c=>migrated?c.GetType().Name=="BridgeGrillageMeshComponent":OldSimple.Contains(c.GetType().Name)).ToArray();
        var closure=new HashSet<Guid>();
        void Include(IGH_DocumentObject o)
        {
            if(!closure.Add(o.InstanceGuid))return;
            var inputs=o is GH_Component c?c.Params.Input:o is IGH_Param p?new[]{p}.ToList():new List<IGH_Param>();
            foreach(var s in inputs.SelectMany(p=>p.Sources))Include(s.Attributes.GetTopLevel.DocObject);
        }
        foreach(var t in targets)Include(t);
        foreach(var o in d.Objects.ToArray())if(!closure.Contains(o.InstanceGuid))d.RemoveObject(o,false);
        if(d.Objects.OfType<GH_Component>().Any(c=>c.GetType().Name.Contains("Straus")||c.GetType().Name.Contains("Excel")||c.GetType().Name.Contains("Report")))
            throw new InvalidOperationException("Geometry ancestors unexpectedly contain external I/O.");
        foreach(var c in d.Objects.OfType<GH_ActiveObject>())c.Locked=false;
        BridgeSegmentExcelTests.Solve(d);GH_Document.EnableSolutions=false;
        if(targets.Any(t=>t.Params.Output[14].VolatileDataCount==0))
        {
            string report=string.Join("\n",d.Objects.OfType<GH_Component>().Select(c=>c.Name+" ["+c.NickName+"]: "+string.Join(", ",c.Params.Input.Select(p=>p.Name+"="+p.VolatileDataCount))+" -> "+string.Join(",",c.Params.Output.Select(p=>p.VolatileDataCount))+"; "+string.Join(";",c.RuntimeMessages(GH_RuntimeMessageLevel.Warning))));
            throw new InvalidOperationException("Empty bridge geometry in "+file+"\n"+report);
        }
        return targets.Select(t=>new GeometrySnapshot(t.InstanceGuid,
            t.Params.Output[14].VolatileData.AllData(true).Select(g=>g.ScriptVariable()).OfType<SimpleBridgeGeometry>().Single(),
            t.Params.Output[19].VolatileData.AllData(true).Select(g=>g.ScriptVariable()).OfType<SimpleBridgeGeometry>().Single())).ToArray();
    }
    internal static int Run(string selection=null,bool publish=false)
    {
        Initialize();string root=ExamplePaths.Root;string output=Path.GetFullPath("artifacts/modular-bridge/candidates");Directory.CreateDirectory(output);
        var results=new List<object>();
        foreach(string file in Directory.GetFiles(root,"*.ghx",SearchOption.AllDirectories)
            .Where(f=>!new[]{"archivio-pacchetti","calcoli","esecuzioni","backup-prima-fasi","Straus File"}.Any(part=>Path.GetRelativePath(root,f).Split(Path.DirectorySeparatorChar).Contains(part)))
            .OrderBy(f=>f))
        {
            string relative=Path.GetRelativePath(root,file);if(selection!=null&&!relative.StartsWith(selection,StringComparison.OrdinalIgnoreCase))continue;
            byte[] original=File.ReadAllBytes(file);string hash=Convert.ToHexString(SHA256.HashData(original));
            string candidate=Path.Combine(output,relative);Directory.CreateDirectory(Path.GetDirectoryName(candidate));
            try
            {
                var registered=Grasshopper.Instances.ComponentServer.ObjectProxies.Select(p=>p.Guid).ToHashSet();
                var unknown=XDocument.Load(file).Descendants("chunk").Where(c=>(string)c.Attribute("name")=="Object")
                    .Select(c=>c.Element("items")?.Elements("item").SingleOrDefault(i=>(string)i.Attribute("name")=="GUID")?.Value)
                    .Where(v=>v!=null&&!registered.Contains(Guid.Parse(v))).Distinct().ToArray();
                if(unknown.Length>0)throw new InvalidOperationException("Unregistered historical components: "+string.Join(", ",unknown));
                var before=Geometry(file,false);
                using var doc=ReadForMigration(file,candidate+".input.ghx");doc.FilePath=file;GH_Document.EnableSolutions=false;
                int changed=Migrate(doc);
                // GH restores saved port metadata after RegisterInput/OutputParams.
                // Refresh the contract without touching values, source order or mapping.
                foreach(var component in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Assembly==BridgeMergeTests.Initialize()))
                {
                    var fresh=(GH_Component)Activator.CreateInstance(component.GetType());
                    foreach(var pair in new[]{(Actual:component.Params.Input,Expected:fresh.Params.Input),(Actual:component.Params.Output,Expected:fresh.Params.Output)})
                    {
                        if(pair.Actual.Count!=pair.Expected.Count)throw new InvalidOperationException("Port count mismatch after migration: "+component.Name);
                        for(int i=0;i<pair.Actual.Count;i++)
                        {
                            var actual=pair.Actual[i];var expected=pair.Expected[i];
                            actual.Name=expected.Name;actual.Description=expected.Description;actual.Access=expected.Access;actual.Optional=expected.Optional;
                        }
                    }
                }
                if(changed>0)
                {
                    if(relative.StartsWith("50-",StringComparison.Ordinal))Example50HorizontalLayout.Apply(doc);
                    else BridgeModularLayout.Apply(doc);
                }
                GrasshopperBridgeExample.Write(doc,candidate);
                var after=Geometry(candidate,true);
                if(before.Length!=after.Length&&before.Length!=0)throw new Exception("Geometry component count changed.");
                for(int i=0;i<before.Length;i++)
                {
                    var target=after.Single(a=>a.Source==before[i].Source);
                    Equal(before[i].Geometry,target.Geometry);Equal(before[i].Grillage,target.Grillage);
                }
                using var reopened=GrasshopperBridgeExample.Read(candidate);
                if(reopened.Objects.Count!=doc.Objects.Count)throw new Exception("Objects lost on reopen.");
                if(reopened.Objects.Select(o=>o.InstanceGuid).Distinct().Count()!=reopened.Objects.Count)throw new Exception("Duplicate object IDs.");
                var wires=doc.Objects.OfType<IGH_Param>().Concat(doc.Objects.OfType<GH_Component>().SelectMany(c=>c.Params.Input)).ToDictionary(p=>p.InstanceGuid,p=>p.Sources.Select(s=>s.InstanceGuid).ToArray());
                foreach(var p in reopened.Objects.OfType<IGH_Param>().Concat(reopened.Objects.OfType<GH_Component>().SelectMany(c=>c.Params.Input)))
                    if(!wires[p.InstanceGuid].SequenceEqual(p.Sources.Select(s=>s.InstanceGuid)))throw new Exception("Sources lost or reordered on reopen.");
                if(publish)
                {if(!original.SequenceEqual(File.ReadAllBytes(file)))throw new IOException("User saved the source during migration; candidate retained.");File.Copy(candidate,file,true);}
                results.Add(new{File=relative,Changed=changed,SourceHash=hash,TargetHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(candidate))),GeometryComparisons=before.Length,Reopened=true,NativeSolverInvoked=false});
                Console.WriteLine($"MIGRATION PASS {relative}: {changed} replacements; {before.Length} geometries unchanged; reopened.");
            }
            catch(Exception ex)
            {results.Add(new{File=relative,Error=ex.ToString()});File.WriteAllText(Path.Combine(output,"migration-results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Console.Error.WriteLine(relative+"\n"+ex);return 1;}
        }
        File.WriteAllText(Path.Combine(output,"migration-results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));return 0;
    }
    private static void Equal(SimpleBridgeGeometry a,SimpleBridgeGeometry b)
    {
        if(!a.Members.SequenceEqual(b.Members)||!a.Nodes.SequenceEqual(b.Nodes)||!a.Segments.SequenceEqual(b.Segments)||!a.Supports.SequenceEqual(b.Supports)||!a.Joints.SequenceEqual(b.Joints)||!a.Stations.SequenceEqual(b.Stations))
            throw new Exception("Migration changed geometry, physical segments or topology.");
    }
    private static GH_Document ReadForMigration(string file,string temporary)
    {
        var xml=XDocument.Load(file);var list=xml.Descendants("chunk").Single(c=>(string)c.Attribute("name")=="DefinitionObjects").Element("chunks");
        var obsolete=new HashSet<string>{"229b4f95-4bc6-43d4-8f58-e4faea8d0b34","e3eeef52-dba7-4bb0-8131-57d6956edf58","dc7cfadf-84f8-4ebd-8a49-17782694754e","66a0d167-4e5b-4ea1-9327-935c772fd97b","92ef4133-2716-4a81-9b70-ff01db7fd15d"};
        string Id(XElement c)=>c.Element("items")?.Elements("item").SingleOrDefault(i=>(string)i.Attribute("name")=="GUID")?.Value;
        if(!list.Elements("chunk").Any(c=>obsolete.Contains(Id(c)??"")))return GrasshopperBridgeExample.Read(file);
        foreach(var c in list.Elements("chunk").Where(c=>Id(c)=="c552a431-af5b-46a9-a8a4-0fcbc27ef596"||
            (c.Element("chunks")?.Elements("chunk").SingleOrDefault(n=>(string)n.Attribute("name")=="Container")?.Element("items")?.Elements("item").SingleOrDefault(i=>(string)i.Attribute("name")=="Name")?.Value??"").StartsWith("BGH50_LAYOUT_",StringComparison.Ordinal)).ToArray())c.Remove();
        int count=0;foreach(var c in list.Elements("chunk"))c.SetAttributeValue("index",count++);list.SetAttributeValue("count",count);
        list.Parent.Element("items").Elements("item").Single(i=>(string)i.Attribute("name")=="ObjectCount").Value=count.ToString();
        xml.Save(temporary);return GrasshopperBridgeExample.Read(temporary);
    }
    internal static int Migrate(GH_Document doc)
    {
        int count=0;var obsolete=new HashSet<GH_Component>();
        var plans=new Dictionary<Guid,IGH_Param>();
        GH_Component Add(string type,GH_Component near,float dx=0,float dy=0)
        {var c=BridgeSegmentExcelTests.Add(doc,type);c.Attributes.Pivot=new(near.Attributes.Pivot.X+dx,near.Attributes.Pivot.Y+dy);return c;}
        void Group(GH_Component old,params IGH_DocumentObject[] added)
        {foreach(var group in doc.Objects.OfType<GH_Group>().Where(g=>g.ObjectIDs.Contains(old.InstanceGuid))){group.RemoveObject(old.InstanceGuid);foreach(var o in added)group.AddObject(o.InstanceGuid);}}
        void Remove(GH_Component old,params IGH_DocumentObject[] added){Group(old,added);doc.RemoveObject(old,false);count++;}
        IGH_Param Expose(IGH_Param input,GH_Component near,string label,float dy)
        {if(input.SourceCount==1)return input.Sources[0];var p=(IGH_Param)Activator.CreateInstance(input.GetType());p.CreateAttributes();Copy(input,p);p.Name=p.NickName=label;p.Attributes.Pivot=new(near.Attributes.Pivot.X-400,near.Attributes.Pivot.Y+dy);doc.AddObject(p,false);return p;}
        foreach(var old in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeAlignmentInputsComponent").ToArray())
        {
            var support=Add("BridgeSupportStationsComponent",old);for(int i=0;i<4;i++)Copy(old.Params.Input[i],support.Params.Input[i]);Copy(old.Params.Input[7],support.Params.Input[4]);
            var joints=Add("BridgeNormalizeJointsComponent",old,0,400);Copy(old.Params.Input[0],joints.Params.Input[0]);Copy(old.Params.Input[4],joints.Params.Input[1]);Copy(old.Params.Input[7],joints.Params.Input[2]);
            for(int i=0;i<7;i++)Replace(old.Params.Output[i],support.Params.Output[i]);Replace(old.Params.Output[7],joints.Params.Output[0]);
            Replace(old.Params.Output[8],Expose(old.Params.Input[5],old,"Lunghezza massima concio [mm]",600));Replace(old.Params.Output[9],Expose(old.Params.Input[6],old,"Lunghezza massima elemento FEM [mm]",750));
            Replace(old.Params.Output[10],support.Params.Output[7]);Replace(old.Params.Output[11],support.Params.Output[8]);Remove(old,support,joints);
        }
        foreach(var old in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeParametricLayoutComponent").ToArray())
        {
            var spans=Add("BridgeSpanStationsComponent",old,-1000,-600);foreach(var pair in new[]{(0,0),(1,1),(2,2),(8,3),(9,4)})Copy(old.Params.Input[pair.Item1],spans.Params.Input[pair.Item2]);
            var curve=Add("BridgeAlignmentCurveComponent",old,-500,-600);Wire(curve.Params.Input[0],spans.Params.Output[2]);Copy(old.Params.Input[10],curve.Params.Input[1]);Copy(old.Params.Input[11],curve.Params.Input[2]);Copy(old.Params.Input[12],curve.Params.Input[3]);Copy(old.Params.Input[13],curve.Params.Input[4]);IGH_Param alignment=curve.Params.Output[0];
            var girders=Add("BridgeRegularGirdersComponent",old,-1000,-200);Copy(old.Params.Input[3],girders.Params.Input[0]);Copy(old.Params.Input[4],girders.Params.Input[1]);Copy(old.Params.Input[5],girders.Params.Input[2]);
            var deck=Add("BridgeDeckWidthsComponent",old,-500,-200);Wire(deck.Params.Input[0],girders.Params.Output[0]);Copy(old.Params.Input[6],deck.Params.Input[1]);Copy(old.Params.Input[7],deck.Params.Input[2]);
            var bearings=Add("BridgeBearingPatternComponent",old,-500,200);Wire(bearings.Params.Input[0],spans.Params.Output[0]);Wire(bearings.Params.Input[1],girders.Params.Output[0]);Copy(old.Params.Input[15],bearings.Params.Input[2]);
            var bracing=Add("BridgeBracingStationsComponent",old,-500,550);Wire(bracing.Params.Input[0],alignment);Wire(bracing.Params.Input[1],spans.Params.Output[0]);Copy(old.Params.Input[14],bracing.Params.Input[2]);
            var outputs=new[]{alignment,spans.Params.Output[0],girders.Params.Output[0],deck.Params.Output[0],spans.Params.Output[1],bearings.Params.Output[0],spans.Params.Output[2]};
            for(int i=0;i<outputs.Length;i++)Replace(old.Params.Output[i],outputs[i]);Replace(old.Params.Output[8],bracing.Params.Output[0]);
            var note=new GH_Panel{UserText="Flusso modulare: campate, tracciato, travi, soletta, conci, mesh e vincoli sono definiti separatamente.",NickName="Procedura geometria"};note.CreateAttributes();note.Attributes.Pivot=old.Attributes.Pivot;doc.AddObject(note,false);Replace(old.Params.Output[7],note);
            Remove(old,new IGH_DocumentObject[]{spans,girders,deck,bearings,bracing,note}.Concat(curve==null?Array.Empty<IGH_DocumentObject>():new[]{curve}).ToArray());
        }
        foreach(var old in doc.Objects.OfType<GH_Component>().Where(c=>OldSimple.Contains(c.GetType().Name)).ToArray())
        {
            var support=Add("BridgeSupportLayoutComponent",old,-1900,-600);Copy(old.Params.Input[0],support.Params.Input[0]);Copy(old.Params.Input[1],support.Params.Input[1]);Copy(old.Params.Input[11],support.Params.Input[2]);
            var girders=Add("BridgeGirderLayoutComponent",old,-1900,-200);Copy(old.Params.Input[2],girders.Params.Input[0]);Copy(old.Params.Input[3],girders.Params.Input[1]);
            var deck=Add("BridgeDeckLayoutComponent",old,-1900,200);Copy(old.Params.Input[4],deck.Params.Input[0]);Copy(old.Params.Input[5],deck.Params.Input[1]);Copy(old.Params.Input[15],deck.Params.Input[4]);
            var cross=Add("BridgeCrossfallTreeComponent",old,-2400,300);Copy(old.Params.Input[6],cross.Params.Input[0]);Wire(deck.Params.Input[2],cross.Params.Output[0]);Wire(deck.Params.Input[3],cross.Params.Output[1]);
            var fabrication=Add("BridgeFabricationSegmentsComponent",old,-1200,-200);Copy(old.Params.Input[0],fabrication.Params.Input[0]);Wire(fabrication.Params.Input[1],girders.Params.Output[0]);
            var added=new List<IGH_DocumentObject>{support,girders,deck,cross,fabrication};
            if(old.Params.Input[7].SourceCount>0)
            {
                var settings=old.Params.Input[7].Sources.Single().Attributes.GetTopLevel.DocObject as GH_Component;
                if(settings?.GetType().Name!="BridgeSegmentsComponent")throw new Exception("Unsupported segment settings source; preserve and migrate explicitly: "+old.NickName);
                var joints=Add("BridgeJointTreeComponent",old,-1900,650);Copy(settings.Params.Input[0],joints.Params.Input[0]);Wire(fabrication.Params.Input[2],joints.Params.Output[0]);Copy(settings.Params.Input[1],fabrication.Params.Input[3]);obsolete.Add(settings);added.Add(joints);
            }
            else
            {
                var raw=((Param_GenericObject)old.Params.Input[7]).PersistentData.AllData(true).FirstOrDefault()?.ScriptVariable();
                if(raw is BridgeSegments settings)
                {((Param_Number)fabrication.Params.Input[3]).SetPersistentData(settings.MaximumLength);foreach(var pair in settings.Joints){var joint=Add("BridgeJointSetComponent",old,-1900,650+added.Count*100);((Param_Number)joint.Params.Input[0]).SetPersistentData(pair.Value);((Param_Integer)joint.Params.Input[1]).SetPersistentData(pair.Key);fabrication.Params.Input[2].AddSource(joint.Params.Output[0]);added.Add(joint);}}
            }
            GH_Component diaphragmSource=null;BridgeDiaphragms persistent=null;
            if(old.Params.Input[8].SourceCount>0)
            {diaphragmSource=old.Params.Input[8].Sources.Single().Attributes.GetTopLevel.DocObject as GH_Component;if(diaphragmSource?.GetType().Name!="BridgeDiaphragmsComponent")throw new Exception("Unsupported diaphragm source.");obsolete.Add(diaphragmSource);}
            else persistent=((Param_GenericObject)old.Params.Input[8]).PersistentData.AllData(true).FirstOrDefault()?.ScriptVariable() as BridgeDiaphragms;
            GH_Component Diaphragms(bool truss,float y)
            {
                var d=Add(truss?"BridgeTrussDiaphragmsComponent":"BridgeSolidDiaphragmsComponent",old,-1200,y);added.Add(d);
                if(diaphragmSource!=null)for(int i=0;i<d.Params.Input.Count;i++)Copy(diaphragmSource.Params.Input[i],d.Params.Input[i]);
                else if(persistent!=null){((Param_Number)d.Params.Input[0]).SetPersistentData(persistent.Stations);((Param_Number)d.Params.Input[1]).SetPersistentData(persistent.MaximumSpacing);((Param_Boolean)d.Params.Input[2]).SetPersistentData(persistent.AtSupports);if(truss){((Param_Number)d.Params.Input[3]).SetPersistentData(persistent.TrussHeight);((Param_Integer)d.Params.Input[4]).SetPersistentData(persistent.PanelsPerBay);((Param_Integer)d.Params.Input[5]).SetPersistentData((int)persistent.Pattern);((Param_Boolean)d.Params.Input[6]).SetPersistentData(persistent.ConnectCrossings);}}
                return d;
            }
            IGH_Param diaphragms;
            if(old.GetType().Name=="SimpleBridgeConfigurableComponent")
            {var solid=Diaphragms(false,300);var truss=Diaphragms(true,650);var choice=Add("BridgeDiaphragmChoiceComponent",old,-650,400);Copy(old.Params.Input[16],choice.Params.Input[0]);Wire(choice.Params.Input[1],solid.Params.Output[0]);Wire(choice.Params.Input[2],truss.Params.Output[0]);diaphragms=choice.Params.Output[0];added.Add(choice);}
            else diaphragms=Diaphragms(old.GetType().Name=="SimpleBridgeTrussComponent",300).Params.Output[0];
            var assembly=Add("BridgeGeometryAssemblyComponent",old,-550,-250);Copy(old.Params.Input[0],assembly.Params.Input[0]);Wire(assembly.Params.Input[1],support.Params.Output[0]);Wire(assembly.Params.Input[2],girders.Params.Output[0]);Wire(assembly.Params.Input[3],deck.Params.Output[0]);Wire(assembly.Params.Input[4],fabrication.Params.Output[0]);Wire(assembly.Params.Input[5],diaphragms);
            var mesh=Add("BridgeMeshSettingsComponent",old,-550,800);foreach(var pair in new[]{(9,0),(10,1),(13,2),(14,3)})Copy(old.Params.Input[pair.Item1],mesh.Params.Input[pair.Item2]);
            var output=(GH_Component)Activator.CreateInstance(BridgeMergeTests.Initialize().GetTypes().Single(t=>t.Name=="BridgeGrillageMeshComponent"));output.CreateAttributes();output.Attributes.Pivot=new(old.Attributes.Pivot.X+150,old.Attributes.Pivot.Y);output.NewInstanceGuid(old.InstanceGuid);output.NickName=old.NickName+" — mesh";Wire(output.Params.Input[0],assembly.Params.Output[0]);Wire(output.Params.Input[1],mesh.Params.Output[0]);Copy(old.Params.Input[12],output.Params.Input[2]);
            plans[output.Params.Output[14].InstanceGuid]=fabrication.Params.Output[0];plans[output.Params.Output[19].InstanceGuid]=fabrication.Params.Output[0];
            for(int i=0;i<old.Params.Output.Count;i++)Replace(old.Params.Output[i],output.Params.Output[i]);
            // Add the replacement only after removing the original: the document caches object IDs.
            var groups=doc.Objects.OfType<GH_Group>().Where(g=>g.ObjectIDs.Contains(old.InstanceGuid)).ToArray();
            foreach(var group in groups)group.RemoveObject(old.InstanceGuid);
            doc.RemoveObject(old,false);doc.AddObject(output,false);count++;added.AddRange(new[]{assembly,mesh,output});
            foreach(var group in groups)foreach(var o in added)group.AddObject(o.InstanceGuid);
        }
        foreach(var old in obsolete)
        {if(old.Params.Output.Any(p=>p.Recipients.Count>0))throw new Exception("Legacy layout still has consumers: "+old.Name);Remove(old);}
        foreach(var c in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name is "BridgeSegmentSectionsComponent" or "BridgeSegmentExcelComponent"))
        {var input=c.Params.Input[0];if(input.SourceCount==1&&plans.TryGetValue(input.Sources[0].InstanceGuid,out var p))Wire(input,p);}
        foreach(var old in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeSegmentExcelComponent").ToArray())
        {
            int mode=Convert.ToInt32(Scalar(old.Params.Input[2]),System.Globalization.CultureInfo.InvariantCulture);
            if(mode is not (0 or 1))throw new Exception("Invalid Excel mode.");
            var template=Add("BridgeSectionTemplateComponent",old,-500,-150);var read=Add("BridgeSectionReadComponent",old,0,150);
            foreach(var c in new[]{template,read}){Copy(old.Params.Input[0],c.Params.Input[0]);Copy(old.Params.Input[1],c.Params.Input[1]);}
            Copy(old.Params.Input[3],template.Params.Input[2]);Copy(old.Params.Input[4],template.Params.Input[3]);
            foreach(string f in new[]{"cachePath","cache","warnings"}){var field=old.GetType().GetField(f,BindingFlags.Instance|BindingFlags.NonPublic);field.SetValue(read,field.GetValue(old));}
            var chosen=mode==1?read:template;for(int i=0;i<old.Params.Output.Count;i++)Replace(old.Params.Output[i],chosen.Params.Output[i]);Remove(old,template,read);
        }
        foreach(var old in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeConnectionsComponent").ToArray())
        {
            int mode=Convert.ToInt32(Scalar(old.Params.Input[0]),System.Globalization.CultureInfo.InvariantCulture);
            var c=Add(mode switch{0=>"BridgeRigidLinksComponent",1=>"BridgeConnectionBeamsComponent",2=>"BridgeBeamOffsetsComponent",_=>throw new Exception("Invalid connection mode.")},old);
            Copy(old.Params.Input[mode==2?3:1],c.Params.Input[0]);if(mode!=0)Copy(old.Params.Input[mode==2?4:2],c.Params.Input[1]);
            for(int i=0;i<2;i++)Replace(old.Params.Output[i],c.Params.Output[i]);Remove(old,c);
        }
        foreach(var old in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeMemberAssignmentComponent").ToArray())
        {
            var assignment=Add("BridgeFemMembersComponent",old);Copy(old.Params.Input[0],assignment.Params.Input[0]);Copy(old.Params.Input[3],assignment.Params.Input[2]);
            var property=Add("BridgeFamilyPropertyComponent",old,-450,250);Copy(old.Params.Input[1],property.Params.Input[0]);Copy(old.Params.Input[2],property.Params.Input[1]);
            if(old.Params.Input[1].SourceCount>0||((Param_GenericObject)old.Params.Input[1]).PersistentDataCount>0)Wire(assignment.Params.Input[1],property.Params.Output[0]);else doc.RemoveObject(property,false);
            for(int i=0;i<old.Params.Output.Count;i++)Replace(old.Params.Output[i],assignment.Params.Output[i]);Remove(old,assignment,property);
        }
        foreach(var analysis in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="StrausR3AnalysisComponent").ToArray())
        {
            if(analysis.Params.Input[2].Sources.Any(s=>s.Attributes.GetTopLevel.DocObject.GetType().Name=="BridgeAnalysisCasesComponent"))continue;
            bool From(IGH_Param port,GH_Component target,HashSet<IGH_Param> seen)
            {if(!seen.Add(port))return false;return port.Sources.Any(s=>ReferenceEquals(s.Attributes.GetTopLevel.DocObject,target)||s.Attributes.GetTopLevel.DocObject is IGH_Param p&&From(p,target,seen));}
            var consumers=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name is "BridgeActionComponent" or "BridgeShrinkageActionComponent")
                .Where(c=>From(c.Params.Input[1],analysis,new())).ToArray();
            if(consumers.Length==0)continue;
            var required=Add("BridgeAnalysisCasesComponent",analysis,-500,-300);required.NickName=analysis.NickName.Split(' ')[0]+" | casi richiesti dalle azioni";
            Copy(analysis.Params.Input[0],required.Params.Input[0]);Copy(analysis.Params.Input[1],required.Params.Input[2]);
            foreach(var action in consumers)
            {
                var source=action.Params.Input[2];
                if(source.SourceCount>0)foreach(var p in source.Sources)required.Params.Input[1].AddSource(p);
                else required.Params.Input[1].AddSource(Expose(source,required,"Caso FEM richiesto da "+action.NickName,0));
            }
            foreach(var dispatch in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BeamMomentDispatchComponent"))
                foreach(var p in dispatch.Params.Input[1].Sources)
                    if(p is GH_Panel panel&&panel.UserText.Trim()=="Dead")required.Params.Input[1].AddSource(p);
            analysis.Params.Input[2].AddSource(required.Params.Output[0]);analysis.Params.Input[2].DataMapping=GH_DataMapping.Flatten;count++;
        }
        return count;
    }
}

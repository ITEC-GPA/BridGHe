using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

internal static partial class PersonalBridgeExplicitInputs
{
    private static IGH_Structure Persistent(IGH_Param p) => p.GetType().GetProperty("PersistentData")?.GetValue(p) as IGH_Structure;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    internal sealed record Entry(Guid ComponentId, string Component, Guid InputId, string Input, Guid ControlId, string Control, string Before);

    internal static int Update(GH_Document doc, Assembly plugin, string root)
    {
        string source=doc.FilePath, folder=Path.GetDirectoryName(source);
        // The existing replay also verifies all 648 section results against the numerical
        // archive inside the Word report. No analysis or report generation is triggered.
        new PersonalBridgeRevision(doc,plugin,root).Replay(false);
        if(!doc.Objects.OfType<GH_Component>().SelectMany(c=>c.Params.Input).Any(p=>Persistent(p)?.IsEmpty==false))
        {
            Validate(doc,new List<Entry>());
            Console.WriteLine("EXAMPLE 99 EXPLICIT INPUTS PASS: definition already explicit; controls, labels and numerical report verified without rewriting it.");
            return 0;
        }
        var protectedFiles=Directory.GetFiles(folder,"*",SearchOption.AllDirectories)
            .Where(p=>new[]{".st7",".lsa",".docx",".xlsx"}.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        string originalHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        var entries=Apply(doc);
        new PersonalBridgeRevision(doc,plugin,root).Replay(false);
        string temporary=Path.Combine(folder,"flusso-acciaio-misto_REV004.inputs.tmp.ghx");
        GrasshopperBridgeExample.Write(doc,temporary);
        GH_Document.EnableSolutions=false;
        using(var reopened=GrasshopperBridgeExample.Read(temporary))
        {
            reopened.FilePath=source;
            GH_Document.EnableSolutions=true; reopened.Enabled=true; reopened.NewSolution(false,GH_SolutionMode.Silent);
            Validate(reopened,entries);
            new PersonalBridgeRevision(reopened,plugin,root).Replay(false);
            Validate(reopened,entries);
        }
        foreach(var file in protectedFiles)
            if(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.Key)))!=file.Value)
                throw new Exception("Unexpected modification to protected analysis/report/workbook: "+file.Key);
        File.Move(temporary,source,true);
        File.WriteAllText(Path.Combine(folder,"explicit-inputs-validation.json"),JsonSerializer.Serialize(new
        {
            revision="REV004", previousDefinitionSha256=originalHash, exposedInputs=entries.Count,
            controls=entries.GroupBy(e=>e.Control).ToDictionary(g=>g.Key,g=>g.Count()),
            internalPersistentValues=0, inputValuesAndTreesUnchangedAfterDefaultNormalization=true, reopened=true,
            normalizedDefaults=new[]{"Consider Concrete: empty legacy slot made explicit as true, matching SolveInstance's existing default."},
            nativeSolverInvoked=false, nativeFilesAndReportAndWorkbookUnchanged=true,
            reportNumericalHashMatches=true,
            inputs=entries.Select(e=>new{e.ComponentId,e.Component,e.Input,e.InputId,e.Control,e.ControlId})
        },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"EXAMPLE 99 EXPLICIT INPUTS PASS: {entries.Count} inputs; no internal persistent values; exact input trees and saved report results preserved.");
        return 0;
    }

    internal static List<Entry> Apply(GH_Document doc)
    {
        // The source REV004 contains an empty serialized bool slot here. The component
        // treats missing data as true; expose that effective value, never GH_Boolean's false placeholder.
        foreach(var c in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BeamPropertyCompositeComponent"))
        {
            var p=(Param_Boolean)c.Params.Input.Single(p=>p.Name=="Consider Concrete");
            if(p.SourceCount==0 && p.VolatileData.Paths.All(path=>p.VolatileData.get_Branch(path).Cast<IGH_Goo>().All(g=>g==null)))
            { p.PersistentData.Clear(); p.SetPersistentData(!p.Invert); p.ExpireSolution(false); c.ExpireSolution(false); }
        }
        GH_Document.EnableSolutions=true; doc.Enabled=true; doc.NewSolution(false,GH_SolutionMode.Silent);
        var originals=doc.Objects.ToArray();
        var controls=new Dictionary<GH_Component,List<IGH_DocumentObject>>();
        var entries=new List<Entry>();
        var beforeInputs=originals.OfType<GH_Component>().SelectMany(c=>c.Params.Input)
            .Where(p=>p.SourceCount==0 && Persistent(p)?.IsEmpty==false)
            .ToDictionary(p=>p.InstanceGuid,p=>Key(p.VolatileData));
        var oldWires=originals.OfType<GH_Component>().SelectMany(c=>c.Params.Input).Where(p=>p.SourceCount>0)
            .ToDictionary(p=>p.InstanceGuid,p=>p.Sources.Select(s=>s.InstanceGuid).ToArray());
        foreach(var component in originals.OfType<GH_Component>())
        foreach(var input in component.Params.Input)
        {
            var data=Persistent(input);
            if(data==null || data.IsEmpty) continue;
            if(input.SourceCount>0) { data.Clear(); continue; } // Inactive fallbacks must not hide another value.
            string before=beforeInputs[input.InstanceGuid];
            var control=Control(input,data);
            control.CreateAttributes(); control.Attributes.Pivot=PointF.Empty;
            if(control is GH_Panel panel) panel.Attributes.Bounds=new RectangleF(0,0,440,Math.Clamp(50+22*data.DataCount,72,220));
            else if(control is GH_NumberSlider) control.Attributes.Bounds=new RectangleF(0,0,430,24);
            doc.AddObject(control,false);
            data.Clear(); input.AddSource(control); input.ExpireSolution(false);
            if(!controls.TryGetValue(component,out var list)) controls[component]=list=new();
            list.Add(control);
            entries.Add(new(component.InstanceGuid,component.NickName,input.InstanceGuid,input.Name,control.InstanceGuid,control.GetType().Name,before));
        }
        RenameExistingControls(originals);
        if(entries.Count>0) Layout(doc,originals,controls);
        GH_Document.EnableSolutions=true; doc.Enabled=true; doc.NewSolution(false,GH_SolutionMode.Silent);
        Validate(doc,entries);
        foreach(var p in originals.OfType<GH_Component>().SelectMany(c=>c.Params.Input).Where(p=>oldWires.ContainsKey(p.InstanceGuid)))
            if(!p.Sources.Select(s=>s.InstanceGuid).SequenceEqual(oldWires[p.InstanceGuid])) throw new Exception("Existing wires changed: "+p.Name);
        return entries;
    }

    private static IGH_Param Control(IGH_Param input,IGH_Structure data)
    {
        var values=data.AllData(true).Cast<IGH_Goo>().ToArray();
        // A panel/slider emits {0}. A nonstandard path must be retained by a typed parameter.
        bool simple=data.PathCount==1 && data.Paths[0].Indices.SequenceEqual(new[]{0});
        IGH_Param control=null;
        if(simple && values.Length==1)
        {
            if(values[0] is GH_Boolean boolean) control=new GH_BooleanToggle{Value=boolean.Value};
            else if(input is Param_Integer integer && values[0] is GH_Integer number && integer.HasNamedValues)
            {
                // Grasshopper exposes AddNamedValue but no public enumeration API in Rhino 8.
                // This is an example-authoring utility; fail explicitly if this SDK field changes.
                var field=typeof(Param_Integer).GetField("m_namedValues",BindingFlags.NonPublic|BindingFlags.Instance)
                    ?? throw new NotSupportedException("Grasshopper named integer choices API changed.");
                var list=new GH_ValueList{ListMode=GH_ValueListMode.DropDown}; list.ListItems.Clear();
                foreach(var named in (IEnumerable)field.GetValue(integer))
                {
                    string name=(string)named.GetType().GetField("Name").GetValue(named);
                    int value=(int)named.GetType().GetField("Value").GetValue(named);
                    list.ListItems.Add(new GH_ValueListItem(name,value.ToString(Invariant)){Selected=value==number.Value});
                }
                if(!list.ListItems.Any(i=>i.Selected)) throw new Exception("Unlisted selected value: "+input.Name);
                control=list;
            }
            else if(values[0] is GH_Number real && input is Param_Number) control=Slider(real.Value,false,input.Name);
            else if(values[0] is GH_Integer whole && input is Param_Integer) control=Slider(whole.Value,true,input.Name);
        }
        if(control==null && simple && values.All(g=>g is GH_String or GH_Number or GH_Integer or GH_Vector)
            && values.All(g=>g is not GH_String s || !string.IsNullOrEmpty(s.Value)))
        {
            // Generic inputs must retain the original goo type; text is safe, numeric casts are not implicit.
            if(input is not Param_GenericObject || values.All(g=>g is GH_String))
            {
                var panel=new GH_Panel{UserText=string.Join(Environment.NewLine,values.Select(Text))};
                panel.Properties.Multiline=values.Length==1;
                control=panel;
            }
        }
        if(control==null)
        {
            // Empty strings and structured trees remain visible, named standalone parameters.
            control=(IGH_Param)Activator.CreateInstance(input.GetType());
            var target=Persistent(control); target.Clear();
            var gooType=target.GetType().GenericTypeArguments[0];
            var append=target.GetType().GetMethod("Append",new[]{gooType,typeof(GH_Path)});
            foreach(var path in data.Paths)
            {
                target.GetType().GetMethod("EnsurePath",new[]{typeof(GH_Path)}).Invoke(target,new object[]{path});
                foreach(IGH_Goo value in data.get_Branch(path)) append.Invoke(target,new object[]{value?.Duplicate(),path});
            }
        }
        control.NickName=Label(input);
        control.Description=input.Description+"\nValore esplicito dell'esempio 99 REV004; conservare le unità indicate.";
        return control;
    }

    private static GH_NumberSlider Slider(double value,bool integer,string name)
    {
        if(!double.IsFinite(value) || Math.Abs(value)>1e20) return null;
        decimal exact;
        if(!decimal.TryParse(value.ToString("R",Invariant),NumberStyles.Float,Invariant,out exact) || (double)exact!=value) return null;
        int places=integer?0:Math.Max(3,(decimal.GetBits(exact)[3]>>16)&0xff);
        var slider=new GH_NumberSlider();
        slider.Slider.Type=integer?GH_SliderAccuracy.Integer:GH_SliderAccuracy.Float;
        slider.Slider.DecimalPlaces=places;
        decimal extent=Math.Max(1,Math.Abs(exact)*2);
        slider.Slider.Minimum=exact<0?-extent:0; slider.Slider.Maximum=extent;
        if(name.Contains("Angle",StringComparison.OrdinalIgnoreCase) || name.Contains("Skew",StringComparison.OrdinalIgnoreCase))
        { slider.Slider.Minimum=-180; slider.Slider.Maximum=180; }
        if(name=="RH") { slider.Slider.Minimum=0; slider.Slider.Maximum=100; }
        slider.SetSliderValue(exact);
        return (double)slider.CurrentValue==value?slider:null; // Never round a coefficient to fit a slider.
    }

    private static string Text(IGH_Goo goo) => goo switch
    {
        GH_String s=>s.Value, GH_Number n=>n.Value.ToString("R",Invariant), GH_Integer n=>n.Value.ToString(Invariant),
        GH_Vector v=>"{"+v.Value.X.ToString("R",Invariant)+", "+v.Value.Y.ToString("R",Invariant)+", "+v.Value.Z.ToString("R",Invariant)+"}",
        GH_Boolean b=>b.Value.ToString(), _=>throw new NotSupportedException("Unexpected explicit input: "+goo?.GetType().Name)
    };
    private static string Key(IGH_Structure tree)
    {
        var text=new StringBuilder();
        foreach(var path in tree.Paths)
        {
            text.Append(path).Append(':');
            foreach(IGH_Goo goo in tree.get_Branch(path)) text.Append(goo?.GetType().FullName).Append('=').Append(goo==null?"<null>":Text(goo)).Append('|');
            text.AppendLine();
        }
        return text.ToString();
    }

    private static void Validate(GH_Document doc,List<Entry> entries)
    {
        var inputs=doc.Objects.OfType<GH_Component>().SelectMany(c=>c.Params.Input).ToDictionary(p=>p.InstanceGuid);
        foreach(var e in entries)
        {
            var component=doc.Objects.OfType<GH_Component>().Single(c=>c.InstanceGuid==e.ComponentId);
            var p=component.Params.Input.Single(p=>p.Name==e.Input);
            if(p.SourceCount!=1 || p.Sources[0].InstanceGuid!=e.ControlId || Key(p.VolatileData)!=e.Before)
                throw new Exception($"Explicit input changed: {e.Component} / {e.Input}\nBefore {e.Before}\nAfter {Key(p.VolatileData)}");
        }
        if(inputs.Values.Any(p=>Persistent(p)?.IsEmpty==false)) throw new Exception("Internal persistent inputs remain.");
        foreach(var (type,port,widget) in new[]{
            ("CreepAndShrinkageConcreteComponent","RH",typeof(GH_NumberSlider)),
            ("SimpleBridgeSolidComponent","Support Stations",typeof(GH_Panel)),
            ("SimpleBridgeSolidComponent","Labels",typeof(GH_BooleanToggle)),
            ("MaterialSteelComponent","Grade",typeof(GH_ValueList)),
            ("BridgeConstructionPhaseComponent","Number",typeof(GH_NumberSlider)),
            ("Component_DuplicateData","Data",typeof(Param_GenericObject)),
            ("BridgeReportComponent","Template",typeof(GH_Panel))})
        {
            var components=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name==type).ToArray();
            if(components.Length==0) throw new Exception("Missing example component: "+type);
            foreach(var c in components)
            {
                var control=c.Params.Input.Single(p=>p.Name==port).Sources.Single();
                if(control.GetType()!=widget || control.NickName!=Label(c.Params.Input.Single(p=>p.Name==port)))
                    throw new Exception("Missing descriptive control: "+type+" / "+port);
            }
        }
        var errors=doc.Objects.OfType<GH_ActiveObject>().SelectMany(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e=>c.Name+": "+e)).ToArray();
        if(errors.Length>0) throw new Exception(string.Join("\n",errors));
    }

    private static void Layout(GH_Document doc,IGH_DocumentObject[] originals,Dictionary<GH_Component,List<IGH_DocumentObject>> controls)
    {
        var occupied=new List<RectangleF>();
        foreach(var item in originals.Where(o=>o is not GH_Group).OrderBy(o=>o.Attributes.Pivot.X).ThenBy(o=>o.Attributes.Pivot.Y))
        {
            item.Attributes.PerformLayout();
            var position=item.Attributes.Pivot; var bounds=item.Attributes.Bounds;
            var widgets=item is GH_Component c && controls.TryGetValue(c,out var list)?list:new List<IGH_DocumentObject>();
            foreach(var w in widgets) w.Attributes.PerformLayout();
            float column=widgets.Count>0?500:0;
            float height=Math.Max(bounds.Height,widgets.Sum(w=>w.Attributes.Bounds.Height+24));
            var box=new RectangleF(position.X*2-column-40,position.Y*2-40,Math.Max(column+bounds.Width,widgets.Count>0?950:bounds.Width)+80,height+100);
            while(true)
            {
                var hits=occupied.Where(r=>r.IntersectsWith(box)).ToArray();
                if(hits.Length==0) break;
                box.Y=hits.Max(r=>r.Bottom)+50;
            }
            occupied.Add(box);
            Move(item,box.Left+40+column,box.Top+45);
            float y=box.Top+45;
            foreach(var widget in widgets) { Move(widget,box.Left+40,y); y+=widget.Attributes.Bounds.Height+24; }
            if(widgets.Count>0)
            {
                var group=new GH_Group{NickName="Input | "+item.NickName,Colour=Color.FromArgb(35,80,145,170)};
                group.CreateAttributes(); doc.AddObject(group,false); group.AddObject(item.InstanceGuid);
                foreach(var widget in widgets) group.AddObject(widget.InstanceGuid);
            }
        }
        foreach(var group in doc.Objects.OfType<GH_Group>()) { group.Attributes.ExpireLayout(); group.Attributes.PerformLayout(); }
    }
    private static void Move(IGH_DocumentObject item,float x,float y)
    {
        var b=item.Attributes.Bounds;
        item.Attributes.Pivot=new PointF(item.Attributes.Pivot.X+x-b.Left,item.Attributes.Pivot.Y+y-b.Top);
        if(item is GH_Panel) item.Attributes.Bounds=new RectangleF(x,y,b.Width,b.Height);
        item.Attributes.ExpireLayout(); item.Attributes.PerformLayout();
    }
    internal static int Inspect(GH_Document doc)
    {
        var rows = doc.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Input.Select((p,i) => new
        {
            component=c.Name, nickname=c.NickName, id=c.InstanceGuid, type=c.GetType().Name, index=i, p.Name, p.Description,
            parameter=p.GetType().Name, sources=p.Sources.Count, p.Optional,
            branches=Persistent(p)?.PathCount ?? 0,
            data=Persistent(p)?.AllData(true).Cast<Grasshopper.Kernel.Types.IGH_Goo>().Select(g=>new {type=g?.GetType().Name, value=g?.ScriptVariable()?.ToString()}).ToArray()
        })).ToArray();
        File.WriteAllText("artifacts/example99/explicit-input-inventory.json",JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("INPUT INVENTORY: "+rows.Length+" ports, "+rows.Count(r=>r.sources==0 && r.data?.Length>0)+" populated internal inputs");
        return 0;
    }
}

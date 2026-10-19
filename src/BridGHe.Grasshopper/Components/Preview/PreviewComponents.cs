using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using BridGHe.Grasshopper.Display;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Preview
{
    public sealed class DisplaySettingsComponent : GH_Component
    {
        private static readonly string[] Names={"Enabled","Geometry","Sections","Shaded","Reinforcement","Loads","Supports","Releases","OtherAttributes","Axes","Labels"};
        public DisplaySettingsComponent():base("Display Settings","Display","Local preview settings; connect to Preview BridGHe Model. Does not change global preferences or FEM data.",CategoryNameConstants.CATEGORY_MODEL,CategoryNameConstants.SUBCATEGORY_PREVIEW){}
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            foreach(string name in Names)p.AddBooleanParameter(name,name,"Show "+name,GH_ParamAccess.item,name is not ("Axes" or "Labels"));
            p.AddNumberParameter("Symbol scale","SS","Multiplier for existing support symbols and local axes",GH_ParamAccess.item,1);
            p.AddNumberParameter("Load scale","LS","Multiplier for existing load arrows",GH_ParamAccess.item,1);
            p.AddNumberParameter("Transparency","T","0 opaque to 1 transparent",GH_ParamAccess.item,.65);
            p.AddTextParameter("Load case","LC","Exact case name; empty = all cases",GH_ParamAccess.item,"");
            for(int i=0;i<p.ParamCount;i++)p[i].Optional=true;
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddGenericParameter("Settings","S","Local display options",GH_ParamAccess.item);
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var options=new DisplayOptions();for(int i=0;i<Names.Length;i++){var property=typeof(DisplayOptions).GetProperty(Names[i]);bool value=(bool)property.GetValue(options);da.GetData(i,ref value);property.SetValue(options,value);}
                double symbols=1,loads=1,transparency=.65;string filter="";da.GetData(11,ref symbols);da.GetData(12,ref loads);da.GetData(13,ref transparency);da.GetData(14,ref filter);
                options=options with{SymbolScale=symbols,LoadScale=loads,Transparency=transparency,LoadCase=filter};options.Validate();da.SetData(0,new GH_ObjectWrapper(options));
            }
            catch(Exception ex){AddRuntimeMessage(GH_RuntimeMessageLevel.Error,ex.Message);}
        }
        protected override System.Drawing.Bitmap Icon=>Properties.ComponentIcons.For(GetType());
        public override Guid ComponentGuid=>new Guid("f90a8ac7-df2d-43a3-8f50-219086386684");
    }
    public sealed class PreviewModelComponent : GH_Component
    {
        private readonly List<object> values=new();
        private DisplayOptions local;
        public PreviewModelComponent():base("Preview BridGHe Model","Preview","Preview models or individual elements with optional local settings. Disable automatic preview in the BridGHe menu to avoid overlapping upstream previews.",CategoryNameConstants.CATEGORY_MODEL,CategoryNameConstants.SUBCATEGORY_PREVIEW){}
        protected override void RegisterInputParams(GH_InputParamManager p){p.AddGenericParameter("Model / elements","M","Models, beams, composite beams, nodes, plates or moving paths",GH_ParamAccess.list);p.AddGenericParameter("Settings","S","Optional local Display Settings; otherwise follows global settings",GH_ParamAccess.item);p[1].Optional=true;}
        protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddTextParameter("Info","I","Preview item count (does not alter model data)",GH_ParamAccess.item);
        protected override void BeforeSolveInstance(){values.Clear();local=null;}
        protected override void SolveInstance(IGH_DataAccess da)
        {
            var goos=new List<IGH_Goo>();if(!da.GetDataList(0,goos))return;
            object settings=null;da.GetData(1,ref settings);if(settings is IGH_Goo g)settings=g.ScriptVariable();
            if(settings!=null && settings is not DisplayOptions){AddRuntimeMessage(GH_RuntimeMessageLevel.Error,"Connect Display Settings.");return;}
            local=(DisplayOptions)settings;
            foreach(var goo in goos){var value=goo?.ScriptVariable();if(value is ModelModel or ElementModel or MovingPath)values.Add(value);else AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,"Unsupported preview value: "+goo?.TypeName);}
            da.SetData(0,$"{values.Count} objects; physical section outlines and schematic reinforcement rows.");
        }
        public override bool IsPreviewCapable=>true;
        public override BoundingBox ClippingBox
        {
            get {var box=BoundingBox.Empty;foreach(var value in values){foreach(var e in ModelPreview.Elements(value))box.Union(ModelPreview.Bounds(e));foreach(var path in Paths(value)){var c=path.Coordinates;if(c?.Length==9 && c.All(double.IsFinite))for(int i=0;i<9;i+=3)box.Union(new Point3d(c[i],c[i+1],c[i+2]));}}return box;}
        }
        private static IEnumerable<MovingPath> Paths(object value)=>value switch{ModelModel m=>m.MovingPaths,MovingPath p=>new[]{p},_=>Array.Empty<MovingPath>()};
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if(Hidden || Locked)return;var options=local??DisplayPreferences.Current;
            foreach(var value in values){foreach(var e in ModelPreview.Elements(value))ModelPreview.Wires(e,args.Display,args.Viewport,args.WireColour,options);foreach(var path in Paths(value))ModelPreview.Moving(path,args.Display,args.WireColour,options);}
        }
        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {if(Hidden || Locked)return;foreach(var value in values)foreach(var e in ModelPreview.Elements(value))ModelPreview.Meshes(e,args.Display,local??DisplayPreferences.Current);}
        protected override System.Drawing.Bitmap Icon=>Properties.ComponentIcons.For(GetType());
        public override Guid ComponentGuid=>new Guid("404d47db-4efd-412c-aec9-9889a7e9e72d");
    }
    public sealed class DisplayPanelComponent : GH_Component
    {
        public DisplayPanelComponent():base("BridGHe Panel","BridGHe","Right-click to open global display preferences, help and About. Also available in the BridGHe menu.",CategoryNameConstants.CATEGORY_MODEL,CategoryNameConstants.SUBCATEGORY_PREVIEW){}
        protected override void RegisterInputParams(GH_InputParamManager p){}
        protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddTextParameter("Help","H","Open the window from the component context menu",GH_ParamAccess.item);
        protected override void SolveInstance(IGH_DataAccess da)=>da.SetData(0,"BridGHe → Visualizzazione… oppure clic destro → Apri BridGHe.");
        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu){base.AppendAdditionalComponentMenuItems(menu);Menu_AppendItem(menu,"Apri BridGHe",(_,_)=>DisplayWindow.Open());Menu_AppendItem(menu,"Guida",(_,_)=>DisplayWindow.Open(1));}
        protected override System.Drawing.Bitmap Icon=>Properties.ComponentIcons.For(GetType());
        public override Guid ComponentGuid=>new Guid("8453bdb0-af5a-46f7-8d93-d7c707d5a9d3");
    }
}

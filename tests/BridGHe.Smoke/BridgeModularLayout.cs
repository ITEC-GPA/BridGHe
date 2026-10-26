using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

/// <summary>Horizontal dependency columns, with the original named controls next to their consumers.</summary>
internal static class BridgeModularLayout
{
    internal static void Apply(GH_Document doc)
    {
        foreach(var group in doc.Objects.OfType<GH_Group>().ToArray())doc.RemoveObject(group,false);
        var objects=doc.Objects.ToArray();var ranks=new Dictionary<IGH_DocumentObject,int>();var visiting=new HashSet<IGH_DocumentObject>();
        IEnumerable<IGH_Param> Inputs(IGH_DocumentObject o)=>o is GH_Component c?c.Params.Input:o is IGH_Param p?new[]{p}:Array.Empty<IGH_Param>();
        int Rank(IGH_DocumentObject o)
        {
            if(ranks.TryGetValue(o,out int rank))return rank;if(!visiting.Add(o))throw new InvalidOperationException("Cyclic canvas dependency: "+o.NickName);
            var sources=Inputs(o).SelectMany(p=>p.Sources).Select(s=>s.Attributes.GetTopLevel.DocObject).Distinct().Where(s=>objects.Contains(s)).ToArray();
            rank=sources.Length==0?0:sources.Max(Rank)+1;visiting.Remove(o);return ranks[o]=rank;
        }
        foreach(var o in objects){o.Attributes.PerformLayout();Rank(o);}
        var components=objects.OfType<GH_Component>().ToArray();
        var attached=new Dictionary<IGH_DocumentObject,GH_Component>();
        foreach(var p in objects.OfType<IGH_Param>().Where(p=>p.SourceCount==0))
        {
            var target=components.Where(c=>c.Params.Input.Any(i=>i.Sources.Contains(p))).OrderBy(c=>ranks[c]).ThenBy(c=>c.Attributes.Pivot.Y).FirstOrDefault();
            if(target!=null)attached[p]=target;
        }
        var main=objects.Where(o=>!attached.ContainsKey(o)).OrderBy(o=>ranks[o]).ThenBy(o=>o.Attributes.Pivot.Y).ToArray();
        float x=100;
        void Move(IGH_DocumentObject o,float left,float top)
        {var b=o.Attributes.Bounds;var p=o.Attributes.Pivot;o.Attributes.Pivot=new(p.X+left-b.Left,p.Y+top-b.Top);o.Attributes.ExpireLayout();o.Attributes.PerformLayout();}
        foreach(var column in main.GroupBy(o=>ranks[o]).OrderBy(g=>g.Key))
        {
            float y=100,width=0;
            foreach(var o in column)
            {
                var controls=attached.Where(p=>p.Value==o).Select(p=>p.Key).OrderBy(p=>p.Attributes.Pivot.Y).ToArray();
                float controlWidth=controls.Select(p=>p.Attributes.Bounds.Width).DefaultIfEmpty(0).Max(),cy=y;
                foreach(var p in controls){Move(p,x,cy);cy+=Math.Max(35,p.Attributes.Bounds.Height)+45;}
                Move(o,x+(controls.Length>0?controlWidth+180:0),y+20);
                width=Math.Max(width,o.Attributes.Bounds.Right-x);
                y=Math.Max(cy,o.Attributes.Bounds.Bottom)+160;
                if(o is GH_Component)
                {
                    var g=new GH_Group{NickName=o.Name,Colour=Color.FromArgb(22,45,130,140)};g.CreateAttributes();doc.AddObject(g,false);g.AddObject(o.InstanceGuid);foreach(var p in controls)g.AddObject(p.InstanceGuid);
                }
            }
            x+=width+350;
        }
        foreach(var o in objects)o.Attributes.PerformLayout();
        for(int i=0;i<objects.Length;i++)for(int j=i+1;j<objects.Length;j++)
        {var overlap=RectangleF.Intersect(objects[i].Attributes.Bounds,objects[j].Attributes.Bounds);if(overlap.Width>1&&overlap.Height>1)throw new InvalidOperationException("Layout overlap: "+objects[i].NickName+" / "+objects[j].NickName);}
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BridGHe.Common.Gpc;
using GPC.Converter.Straus7;
using GPC.Model.ElementProperties;

internal static partial class OpenSectionChecks190
{
    // Fault injection confined to the test: execute the real geometry/calculation part of the native
    // writer but deliberately omit its GPC override. The production writer and libraries are unchanged.
    public class OmittedOverride198 : DispatchProxy
    {
        internal Straus7NativeWriter Writer;
        internal int Omissions;
        protected override object Invoke(MethodInfo method,object[] args)
        {
            if(method.Name=="SetNativeSection")
            {
                Omissions++;
                Writer.GetType().GetMethod("AssignNativeSection",BindingFlags.NonPublic|BindingFlags.Instance)
                    .Invoke(Writer,new[]{args[0],args[1]});
                return null;
            }
            return method.Invoke(Writer,args);
        }
    }
    private static void NativeNegative198(string root)
    {
        var rows=new List<object>();int detected=0;
        foreach(var item in Cases198().Where(c=>c.Mirror&&(c.Gap==40||c.Gap==120)))
        try
        {
            var model=Model198(item,DefinitionUnits.Nmm,0,0,false);
            var expected=Expected198((ElasticBeamProperty)model.Model.BeamElements.Values.Single().BeamProperty,item);
            string path=Path.Combine(root,"negative-no-override-"+item.Label+".st7");
            int omissions;
            using(var writer=new Straus7NativeWriter(path,root))
            {
                var proxy=DispatchProxy.Create<IC1Straus7Recording,OmittedOverride198>();
                var fault=(OmittedOverride198)(object)proxy;fault.Writer=writer;
                Straus7ModelWritePlan.Prepare(model.Model,null,null,model.Report.NativeSections).Write(proxy,model.Model);
                omissions=fault.Omissions;writer.Save();
            }
            using var api=new Straus7NativeApi();api.OpenModelReadOnly(path,root);
            try
            {
                var section=api.ReadSectionDefinition(api.ReadElement(Straus7Entity.Beam,1).Property,root);
                bool matches=ReadbackMatches198(item,expected,section.Shape,section.Dimensions,section.MirrorType,section.MirrorGaps,section.CompatibleTwist,section.ReferenceProperties);
                Check(omissions==1,"R1 negative writer actually omitted one GPC override "+item.Label);
                if(!matches)detected++;
                Console.WriteLine("R1 negative export conservation "+(matches?"MATCH (non-diagnostic case)":"EXPECTED FAIL detected")+": "+item.Label);
                rows.Add(new{item.Label,path,omissions,conservationPass=matches,expected,actual=section.ReferenceProperties});
            }
            finally{api.CloseModel();}
        }
        catch(Exception ex){Check(false,"R1 negative writer test failed to execute: "+ex);}
        Check(detected>0,"R1 real-file readback detects omitted GPC override (at least one diagnostic case)");
        File.WriteAllText(Path.Combine(root,"negative-override.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
    }
}

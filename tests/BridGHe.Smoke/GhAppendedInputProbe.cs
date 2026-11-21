using System;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel;

/// <summary>0.7.0 probe (21/11/2026): what Grasshopper does when a definition saved without the last input of a component (an input added
/// at the end in a later version) is read: the archive of Build BridGHe Model without its last input chunk, read again. Prints the result of
/// ExtractObject, the messages of the archive and the inputs of the read component.</summary>
internal static class GhAppendedInputProbe
{
    public static int Run()
    {
        var assembly = BridgeMergeTests.Initialize();
        var c = (GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.BuildBridgeModel", true)); c.CreateAttributes();
        var archive = new GH_IO.Serialization.GH_Archive(); archive.AppendObject(c, "C");
        string xml = archive.Serialize_Xml(); int last = c.Params.Input.Count - 1;
        string tag = "<chunk name=\"param_input\" index=\"" + last + "\">";
        int at = xml.IndexOf(tag, StringComparison.Ordinal); int lineStart = xml.LastIndexOf('\n', at) + 1; string indent = xml.Substring(lineStart, at - lineStart);
        int close = xml.IndexOf("\n" + indent + "</chunk>", at, StringComparison.Ordinal) + indent.Length + 9;
        xml = xml.Substring(0, lineStart) + xml.Substring(close + 1);
        const string countTag = "<chunks count=\"";
        int count = xml.LastIndexOf(countTag, xml.IndexOf("<chunk name=\"param_input\" index=\"0\">", StringComparison.Ordinal), StringComparison.Ordinal) + countTag.Length;
        int quote = xml.IndexOf('"', count); int n = int.Parse(xml.Substring(count, quote - count)); xml = xml.Substring(0, count) + (n - 1) + xml.Substring(quote);
        var former = new GH_IO.Serialization.GH_Archive(); bool read = former.Deserialize_Xml(xml);
        var restored = (GH_Component)Activator.CreateInstance(c.GetType()); restored.CreateAttributes();
        bool extracted = former.ExtractObject(restored, "C");
        Console.WriteLine($"PROBE appended input: deserialize {read}, extract {extracted}, inputs {restored.Params.Input.Count}, last «{restored.Params.Input[last].Name}» persistent {restored.Params.Input[last].VolatileDataCount}/{(restored.Params.Input[last] as Grasshopper.Kernel.Parameters.Param_Boolean)?.PersistentData.DataCount}");
        foreach (var m in former.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name.Contains("Message")))
            Console.WriteLine("  archive member " + m.MemberType + " " + m.Name);
        var messages = former.GetType().GetProperty("Messages")?.GetValue(former) as System.Collections.IEnumerable;
        if (messages != null) foreach (var m in messages) Console.WriteLine("  message: " + string.Join(" | ", m.GetType().GetProperties().Select(q => q.Name + "=" + q.GetValue(m))));
        return 0;
    }
}

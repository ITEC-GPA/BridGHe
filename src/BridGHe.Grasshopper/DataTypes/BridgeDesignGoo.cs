using System;
using System.Runtime.Serialization;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.DataTypes;

public sealed class BridgeDesignGoo : GH_Goo<object>
{
    public BridgeDesignGoo() { }
    public BridgeDesignGoo(object value) { Value = value; }
    public override bool IsValid => Value is BridgeSectionRule or BridgeFamilyProperty;
    public override string TypeName => "Bridge Design Definition";
    public override string TypeDescription => "Typed bridge assignment and detailing definitions.";
    public override string ToString() => Value?.ToString() ?? "Empty bridge design definition";
    // R6.5 P1 lane 2b: a rule whose property is given by its GPC datum alone is immutable (the datum is not serialized): the copy shares it.
    public override IGH_Goo Duplicate() => new BridgeDesignGoo(!IsValid ? null : GpcOnly ? Value : ModelArchive.Clone(Value));
    /// <summary>R6.5 P1 lane 2b: a section rule or a family property whose beam property is given by its GPC datum alone.</summary>
    internal bool GpcOnly => Value is BridgeSectionRule { Property: null, GpcProperty: not null } or BridgeFamilyProperty { Property: null, GpcProperty: not null };
    /// <summary>The beam property of a rule given by its GPC datum alone (null otherwise).</summary>
    private Common.Gpc.Legacy.LegacyBeamProperty GpcProperty => Value switch
    {
        BridgeSectionRule { Property: null } r => r.GpcProperty,
        BridgeFamilyProperty { Property: null } f => f.GpcProperty,
        _ => null,
    };
    // R6.5 P1 lane 2b: a rule whose property is given by its GPC datum alone also writes the datum (the property record of BGHGPC1:
    // the GPC property and the data of the property), which the binary record of the rule does not hold.
    // R65-C2-02 (review of lane 2, 8/11): nothing is written unless every part is made (before, "Design" was written before the GPC record
    // that could fail).
    public override bool Write(GH_IWriter writer)
    {
        if (!IsValid) return false;
        byte[] design, data = null, records = null;
        try
        {
            design = ModelArchive.Write(Value);
            if (GpcProperty is { } gpc) (data, records) = Common.Gpc.GpcDraftArchive.Write(gpc);
        }
        catch (Exception e) when (e is SerializationException or NotSupportedException or InvalidOperationException) { return false; }
        writer.SetByteArray("Design", design);
        if (data != null) { writer.SetByteArray("DesignGpcData", data); writer.SetByteArray("DesignGpcRecords", records); }
        return true;
    }
    // R65-C2-02: read into locals; a rule without its FEM property needs both GPC chunks (before, a rule with no property and no GPC chunks was
    // valid with no property); the value is set only when the whole rule is read (before, an exception left the partial rule).
    public override bool Read(GH_IReader reader)
    {
        Value = null;
        if (!reader.ItemExists("Design")) return false;
        try
        {
            object read = ModelArchive.Read<object>(reader.GetByteArray("Design"));
            bool gpcOnly = read is BridgeSectionRule { Property: null } or BridgeFamilyProperty { Property: null };
            bool chunks = reader.ItemExists("DesignGpcData") && reader.ItemExists("DesignGpcRecords");
            if (gpcOnly)
            {
                if (!chunks) return false;
                var gpc = Common.Gpc.GpcDraftArchive.ReadBeamProperty(reader.GetByteArray("DesignGpcData"), reader.GetByteArray("DesignGpcRecords"));
                read = read switch
                {
                    BridgeSectionRule r => r with { GpcProperty = gpc },
                    BridgeFamilyProperty f => f with { GpcProperty = gpc },
                    _ => read,
                };
            }
            if (!(read is BridgeSectionRule or BridgeFamilyProperty)) return false;
            Value = read;
            return true;
        }
        catch (Exception e) when (e is SerializationException or ArgumentException or NotSupportedException or InvalidOperationException) { Value = null; return false; }
    }
}

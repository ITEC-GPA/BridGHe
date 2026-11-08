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
    public override bool Write(GH_IWriter writer)
    {
        if (!IsValid) return false; writer.SetByteArray("Design", ModelArchive.Write(Value));
        if (GpcProperty is { } gpc)
        {
            try
            {
                var (data, records) = Common.Gpc.GpcDraftArchive.Write(gpc);
                writer.SetByteArray("DesignGpcData", data); writer.SetByteArray("DesignGpcRecords", records);
            }
            catch (Exception e) when (e is SerializationException or NotSupportedException) { return false; }
        }
        return true;
    }
    public override bool Read(GH_IReader reader)
    {
        Value = null;
        if (!reader.ItemExists("Design")) return false;
        try
        {
            Value = ModelArchive.Read<object>(reader.GetByteArray("Design"));
            if (reader.ItemExists("DesignGpcData") && reader.ItemExists("DesignGpcRecords"))
            {
                var gpc = Common.Gpc.GpcDraftArchive.ReadBeamProperty(reader.GetByteArray("DesignGpcData"), reader.GetByteArray("DesignGpcRecords"));
                Value = Value switch
                {
                    BridgeSectionRule { Property: null } r => r with { GpcProperty = gpc },
                    BridgeFamilyProperty { Property: null } f => f with { GpcProperty = gpc },
                    _ => Value,
                };
            }
            if (IsValid) return true; Value = null; return false;
        }
        catch (Exception e) when (e is SerializationException or ArgumentException or NotSupportedException) { return false; }
    }
}

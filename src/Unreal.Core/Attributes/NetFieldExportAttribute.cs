using Unreal.Core.Models.Enums;

namespace Unreal.Core.Attributes;

public abstract class NetFieldAttribute : Attribute
{
    public RepLayoutCmdType Type { get; set; }
    public ParseMode? MinimalParseMode { get; set; }
}

/// <summary>
/// Attribute to map a property to the name used in the property replication.
/// <see cref="RepLayoutCmdType"/> is used to specify the parsing method.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class NetFieldExportAttribute : NetFieldAttribute
{
    public string Name { get; set; }

    /// <summary>
    /// Some groups contain several exports with the same name (fields of different structs, for example <c>Mnemonic</c> three times
    /// in the game state). By default a property receives all of them, so the last one read wins. With <c>Occurrence</c> = n the
    /// property only receives the n-th export with this name (0 = the one with the lowest handle). -1: all of them.
    /// </summary>
    public int Occurrence { get; set; } = -1;

    public NetFieldExportAttribute(string name, RepLayoutCmdType type)
    {
        Name = name;
        Type = type;
    }

    public NetFieldExportAttribute(string name, RepLayoutCmdType type, ParseMode minimalParseMode)
    {
        Name = name;
        Type = type;
        MinimalParseMode = minimalParseMode;
    }
}

/// <summary>
/// Attribute to map a property to the handle used in the property replication.
/// <see cref="RepLayoutCmdType"/> is used to specify the parsing method.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class NetFieldExportHandleAttribute : NetFieldAttribute
{
    public uint Handle { get; private set; }

    public NetFieldExportHandleAttribute(uint handle, RepLayoutCmdType type)
    {
        Handle = handle;
        Type = type;
    }

    public NetFieldExportHandleAttribute(uint handle, RepLayoutCmdType type, ParseMode minimalParseMode)
    {
        Handle = handle;
        Type = type;
        MinimalParseMode = minimalParseMode;
    }
}

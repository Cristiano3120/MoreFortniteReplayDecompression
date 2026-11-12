using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace FortniteReplayReader.Models.NetFieldExports.RPC;

public abstract class GameplayCue
{
    [NetFieldExport("GameplayCueTag", RepLayoutCmdType.Property)]
    public FGameplayTag GameplayCueTag { get; set; }

    /// <summary>
    /// Parameters of the cue. The field is called <c>Parameters</c> in <c>..._Added_WithParams</c> and
    /// <c>GameplayCueParameters</c> in <c>..._Executed_WithParams</c>.
    /// </summary>
    [NetFieldExport("Parameters", RepLayoutCmdType.Property)]
    [NetFieldExport("GameplayCueParameters", RepLayoutCmdType.Property)]
    public FGameplayCueParameters Parameters { get; set; }

    [NetFieldExport("PredictionKey", RepLayoutCmdType.Property)]
    public FPredictionKey PredictionKey { get; set; }
}

[NetFieldExportGroup("/Script/FortniteGame.FortPawn:NetMulticast_InvokeGameplayCueAdded_WithParams", minimalParseMode: ParseMode.Full)]
public class GameplayCueAdded : GameplayCue, INetFieldExportGroup
{
}

[NetFieldExportGroup("/Script/FortniteGame.FortPawn:NetMulticast_InvokeGameplayCueExecuted_WithParams", minimalParseMode: ParseMode.Full)]
public class GameplayCueExecuted : GameplayCue, INetFieldExportGroup
{
}

/// <summary>
/// A gameplay cue that is sent together with the gameplay effect spec that caused it (<c>FGameplayEffectSpecForRPC</c>).
/// Unlike <see cref="GameplayCue"/> it has no tag of its own: the cue tags belong to the effect (<see cref="Def"/>).
/// An instance is also used for the elements of <see cref="ModifiedAttributes"/>; these only have
/// <see cref="AttributeName"/>, <see cref="AttributeOwner"/> and <see cref="TotalMagnitude"/> set.
/// </summary>
public class GameplayCueFromSpec : INetFieldExportGroup
{
    /// <summary>NetGUID of the gameplay effect class.</summary>
    [NetFieldExport("Def", RepLayoutCmdType.PropertyObject)]
    public uint? Def { get; set; }

    /// <summary>Attributes the effect changed, for example <c>Damage</c> with the amount of health damage.</summary>
    [NetFieldExport("ModifiedAttributes", RepLayoutCmdType.DynamicArray)]
    public GameplayCueFromSpec[] ModifiedAttributes { get; set; }

    /// <summary>Element of <see cref="ModifiedAttributes"/>: name of the changed attribute.</summary>
    [NetFieldExport("AttributeName", RepLayoutCmdType.PropertyString)]
    public string AttributeName { get; set; }

    /// <summary>Element of <see cref="ModifiedAttributes"/>: NetGUID of the attribute set class.</summary>
    [NetFieldExport("AttributeOwner", RepLayoutCmdType.PropertyObject)]
    public uint? AttributeOwner { get; set; }

    /// <summary>Element of <see cref="ModifiedAttributes"/>: total change of the attribute.</summary>
    [NetFieldExport("TotalMagnitude", RepLayoutCmdType.PropertyFloat)]
    public float? TotalMagnitude { get; set; }

    [NetFieldExport("EffectContext", RepLayoutCmdType.Property)]
    public FGameplayEffectContextHandle EffectContext { get; set; }

    [NetFieldExport("AggregatedSourceTags", RepLayoutCmdType.Property)]
    public FGameplayTagContainer AggregatedSourceTags { get; set; }

    [NetFieldExport("AggregatedTargetTags", RepLayoutCmdType.Property)]
    public FGameplayTagContainer AggregatedTargetTags { get; set; }

    [NetFieldExport("Level", RepLayoutCmdType.PropertyFloat)]
    public float? Level { get; set; }

    [NetFieldExport("AbilityLevel", RepLayoutCmdType.PropertyFloat)]
    public float? AbilityLevel { get; set; }

    [NetFieldExport("PredictionKey", RepLayoutCmdType.Property)]
    public FPredictionKey PredictionKey { get; set; }
}

[NetFieldExportGroup("/Script/FortniteGame.FortPawn:NetMulticast_InvokeGameplayCueExecuted_FromSpec", minimalParseMode: ParseMode.Full)]
public class GameplayCueExecutedFromSpec : GameplayCueFromSpec
{
}

[NetFieldExportGroup("/Script/FortniteGame.FortPawn:NetMulticast_InvokeGameplayCueAddedAndWhileActive_FromSpec", minimalParseMode: ParseMode.Full)]
public class GameplayCueAddedFromSpec : GameplayCueFromSpec
{
}

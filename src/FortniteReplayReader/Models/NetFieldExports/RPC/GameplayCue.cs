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

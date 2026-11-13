namespace FortniteReplayReader.Models;

public class GameData
{
    public string GameSessionId { get; set; }
    public DateTime? UtcTimeStartedMatch { get; set; }
    public float? MatchEndTime { get; set; }
    public string MapInfo { get; set; }
    public string CurrentPlaylist { get; set; }
    public IEnumerable<string> AdditionalPlaylistLevels { get; set; }
    public IList<string> ActiveGameplayModifiers { get; set; } = new List<string>();

    /// <summary>
    /// Actor Id of recording player.
    /// </summary>
    public uint? RecorderId { get; set; }

    public int? MaxPlayers { get; set; }
    public int? TotalTeams { get; set; }
    public int? TotalBots { get; set; }
    public int? TeamSize { get; set; }
    public int? TotalPlayerStructures { get; set; }

    /// <summary>The match is a tournament match (game state <c>bIsTournamentMatch</c>).</summary>
    public bool? IsTournamentMatch { get; set; }

    /// <summary>
    /// Mnemonic (code name) of the island link of the match, for example <c>tournament_epicgames_s43_onepiecezorocup_eu</c> in a tournament.
    /// </summary>
    public string? Mnemonic { get; set; }

    public bool IsTournamentRound => TournamentRound > 0;
    public int? TournamentRound { get; set; }
    public bool? IsLargeTeamGame { get; set; }

    public float? AircraftStartTime { get; set; }
    public float? SafeZonesStartTime { get; set; }

    public uint? WinningTeam { get; set; }
    public IEnumerable<int> WinningPlayerIds { get; set; }
}

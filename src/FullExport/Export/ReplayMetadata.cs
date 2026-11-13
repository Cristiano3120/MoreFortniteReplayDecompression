using FortniteReplayReader.Models;
using Unreal.Core.Models.Enums;

namespace MoreFortniteReplayDecompression.Export;

/// <summary>
/// Small facts about a replay: when it was recorded, how long it is and whether it is a tournament match.
/// Read it on its own with <see cref="ReplayExporter.ReadMetadata(string)"/> (or <see cref="ExportSettings.Metadata"/>),
/// which only reads the start of the replay and is much faster than a full read. A full read fills it as well.
/// </summary>
public sealed class ReplayMetadata
{
    /// <summary>
    /// When the recording started, as written into the file by the machine that recorded it. Fortnite writes the local time of the
    /// recording PC for client replays (the file name <c>UnsavedReplay-2026.11.06-19.45.15</c> is the same time), so the value is
    /// not reliably UTC even though its kind can say so. Use <see cref="MatchStartedUtc"/> for an exact UTC time.
    /// </summary>
    public DateTime RecordedAt { get; init; }

    /// <summary>
    /// When the match started on the server, in UTC (from the game state of the match). The same value in every replay of the
    /// same match, client and server replays alike. <c>null</c> if the replay does not contain it.
    /// </summary>
    public DateTime? MatchStartedUtc { get; init; }

    /// <summary>Length of the recording.</summary>
    public TimeSpan Length { get; init; }

    /// <summary>The match is a tournament match (game state <c>bIsTournamentMatch</c>, or a tournament round is set).</summary>
    public bool IsTournament { get; init; }

    /// <summary>
    /// Internal name of the tournament, for example <c>tournament_epicgames_s43_onepiecezorocup_eu</c>: season, cup and region.
    /// The same for all matches of one tournament and different for every tournament, so it tells which tournament the match belongs to.
    /// <c>null</c> outside of tournaments. It is the mnemonic (code name) of the island link the game state replicates.
    /// </summary>
    public string? TournamentCodeName { get; init; }

    /// <summary>Round of the tournament (game state <c>EventTournamentRound</c>), <c>null</c> outside of tournaments.</summary>
    public int? TournamentRound { get; init; }

    /// <summary>Playlist of the match, for example <c>Playlist_ShowdownTournament_FN_Duos</c>.</summary>
    public string? Playlist { get; init; }

    /// <summary>
    /// Id of the match session. The same in every replay of the same match (client and server replays), so it can be used
    /// to find all replays of one match.
    /// </summary>
    public string? GameSessionId { get; init; }

    /// <summary>Map (level) of the match, for example <c>/Delilah/Maps/Delilah_Terrain</c>.</summary>
    public string? Map { get; init; }

    /// <summary>Fortnite version the replay was recorded with, for example <c>++Fortnite+Release-43.00</c>.</summary>
    public string? FortniteVersion { get; init; }

    /// <summary>
    /// <c>true</c>: the replay was recorded by the server and contains every player (health of everyone, ...).
    /// <c>false</c>: a normal replay recorded by a player's game.
    /// </summary>
    public bool IsServerReplay { get; init; }

    internal static ReplayMetadata From(FortniteReplay replay) => new()
    {
        RecordedAt = replay.Info.Timestamp,
        MatchStartedUtc = replay.GameData.UtcTimeStartedMatch,
        Length = TimeSpan.FromMilliseconds(replay.Info.LengthInMs),
        IsTournament = replay.GameData.IsTournamentMatch == true || replay.GameData.IsTournamentRound,
        TournamentCodeName = replay.GameData.IsTournamentMatch == true || replay.GameData.IsTournamentRound ? replay.GameData.Mnemonic : null,
        TournamentRound = replay.GameData.TournamentRound,
        Playlist = replay.GameData.CurrentPlaylist,
        GameSessionId = replay.GameData.GameSessionId,
        Map = replay.Header.LevelNamesAndTimes?.FirstOrDefault().Item1,
        FortniteVersion = replay.Header.Branch,
        IsServerReplay = !replay.Header.Flags.HasFlag(ReplayHeaderFlags.ClientRecorded),
    };
}

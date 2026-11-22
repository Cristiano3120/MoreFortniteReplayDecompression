using MoreFortniteReplayDecompression.Match;
using MoreFortniteReplayDecompression.Players;
using MoreFortniteReplayDecompression.StronglyTypedIds;
using Unreal.Core.Models;
using Xunit;

namespace MoreFortniteReplayDecompression.Test;

/// <summary>The rules in <see cref="FightRules"/>, applied by <see cref="FightBuilder"/>.</summary>
public class FightBuilderTest
{
    private readonly PlayerStats _a = new() { Channel = new ChannelIndex(1) };
    private readonly PlayerStats _b = new() { Channel = new ChannelIndex(2) };

    private void Hit(PlayerStats attacker, float t, float amount = 30)
    {
        PlayerStats victim = attacker == _a ? _b : _a;
        attacker.DamageDealtEvents.Add(new HitEvent { T = t, Attacker = attacker, Victim = victim, Amount = amount });
    }

    private void Knock(PlayerStats by, float t, bool knock = true)
    {
        PlayerStats victim = by == _a ? _b : _a;
        EliminationEvent e = new() { T = t, Eliminator = by, CreditedTo = by, Eliminated = victim, IsKnock = knock };
        by.EliminationEvents.Add(e);
        victim.DeathEvents.Add(e);
    }

    /// <summary>Positions every second from <paramref name="from"/> to <paramref name="to"/>, <paramref name="meters"/> apart.</summary>
    private void Apart(float from, float to, float meters)
    {
        for (float t = from; t <= to; t++)
        {
            _a.Positions.Add(new PositionSample(t, new FVector(0, 0, 0)));
            _b.Positions.Add(new PositionSample(t, new FVector(meters * 100, 0, 0)));
        }
    }

    /// <summary>Storm phases of which <paramref name="finished"/> have ended at <paramref name="endedAt"/>.</summary>
    private static List<StormPhase> Phases(int finished, float endedAt) =>
        [.. Enumerable.Range(0, finished).Select(i => new StormPhase
        {
            Index = i, StartT = endedAt - 10, EndT = endedAt,
            FromCenter = new FVector(0, 0, 0), FromRadius = 0, ToCenter = new FVector(0, 0, 0), ToRadius = 0,
        })];

    private void Build(List<StormPhase>? phases = null) => FightBuilder.Assign([_a, _b], phases ?? []);

    [Fact]
    public void HitsCloseTogetherAreOneFight()
    {
        Hit(_a, 100, 40);
        Hit(_b, 105, 25);
        Hit(_a, 110, 40);
        Build();

        FightEvent fight = Assert.Single(_a.Fights);
        Assert.Equal(_b, fight.Opponent);
        Assert.Equal(100, fight.Start);
        Assert.Equal(110, fight.End);
        Assert.Equal(2, fight.HitsDealt);
        Assert.Equal(1, fight.HitsTaken);
        Assert.Equal(80, fight.DamageDealt);
        Assert.Equal(25, fight.DamageTaken);
        Assert.Equal(FightOutcome.Open, fight.Outcome);

        FightEvent other = Assert.Single(_b.Fights);
        Assert.Equal(1, other.HitsDealt);
        Assert.Equal(80, other.DamageTaken);
    }

    [Fact]
    public void LongPauseStartsANewFight()
    {
        Hit(_a, 100);
        Hit(_a, 100 + FightRules.MaxPauseSeconds + 1);
        Build();

        Assert.Equal(2, _a.Fights.Count);
    }

    [Fact]
    public void PauseWhileCloseIsTheSameFight()
    {
        // Build fights: no hit for a minute, but the two stay close
        Hit(_a, 100);
        Apart(100, 160, 20);
        Hit(_a, 160);
        Build();

        Assert.Single(_a.Fights);
    }

    [Fact]
    public void PauseWhileFarApartStartsANewFight()
    {
        Hit(_a, 100);
        Apart(100, 160, FightRules.SeparationMeters + 50);
        Hit(_a, 160);
        Build();

        Assert.Equal(2, _a.Fights.Count);
    }

    [Fact]
    public void UnknownPositionsAreNoProofOfSeparation()
    {
        Hit(_a, 100);
        Hit(_a, 160);
        Build();

        Assert.Single(_a.Fights);
    }

    [Fact]
    public void EndgameEndsFightsAfterAShortPause()
    {
        List<StormPhase> phases = Phases(FightRules.EndgameZone, 50);
        Hit(_a, 100);
        Apart(100, 200, 10);
        Hit(_a, 100 + FightRules.EndgamePauseSeconds - 5);
        Hit(_a, 100 + (2 * FightRules.EndgamePauseSeconds) + 5);
        Build(phases);

        Assert.Equal(2, _a.Fights.Count);
        Assert.All(_a.Fights, f => Assert.True(f.IsEndgame));
    }

    [Fact]
    public void KnockAndEliminationWinTheFight()
    {
        Hit(_a, 100);
        Hit(_b, 101);
        Hit(_a, 104);
        Knock(_a, 104);
        Hit(_a, 108);
        Knock(_a, 108, knock: false);
        Build();

        FightEvent won = Assert.Single(_a.Fights);
        Assert.Equal(FightOutcome.Won, won.Outcome);
        Assert.True(won.KnockedOpponent);
        Assert.True(won.EliminatedOpponent);
        Assert.Equal(4, won.TimeToKnock);
        Assert.Equal(8, won.TimeToKill);

        FightEvent lost = Assert.Single(_b.Fights);
        Assert.Equal(FightOutcome.Lost, lost.Outcome);
        Assert.True(lost.WasKnocked);
        Assert.True(lost.WasEliminated);
        Assert.Null(lost.TimeToKnock);
    }

    [Fact]
    public void EliminationEndsTheFight()
    {
        // b is eliminated at 105 and rebooted, the hit at 130 is a new fight
        Hit(_a, 100);
        Hit(_a, 105);
        Knock(_a, 105, knock: false);
        Hit(_a, 130);
        Build();

        Assert.Equal(2, _a.Fights.Count);
        Assert.Equal(FightOutcome.Won, _a.Fights[0].Outcome);
        Assert.Equal(FightOutcome.Open, _a.Fights[1].Outcome);
    }

    [Fact]
    public void TeamDamageIsNoFight()
    {
        _a.DamageDealtEvents.Add(new HitEvent { T = 100, Attacker = _a, Victim = _b, Amount = 10, IsTeamDamage = true });
        Build();

        Assert.Empty(_a.Fights);
        Assert.Empty(_b.Fights);
    }
}

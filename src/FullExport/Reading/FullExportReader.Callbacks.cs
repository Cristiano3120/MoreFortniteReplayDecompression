using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.RPC;
using MoreFortniteReplayDecompression.StronglyTypedIds;
using MoreFortniteReplayDecompression.TimelineEvents;
using Unreal.Core.Contracts;
using Unreal.Core.Models;

namespace MoreFortniteReplayDecompression.Reading;

public sealed partial class FullExportReader
{
    protected override void OnChannelOpened(uint uChannelIndex, NetworkGUID? actor)
    {
        ChannelIndex channelIndex = new(uChannelIndex);
        base.OnChannelOpened(channelIndex.Value, actor);

        if (_channelGuid.Remove(channelIndex, out ChannelGuid oldGuid))
        {
            _liveGuidChannel.Remove(oldGuid);
        }

        _lastPositionTime.Remove(channelIndex);
        _lastHealth.Remove(channelIndex);

        Actor? channelActor = Channels[channelIndex.Value]?.Actor;
        string? archetype = null;
        if (channelActor?.Archetype is { } arch && _netGuidCache.TryGetPathName(arch.Value, out var path))
        {
            archetype = path;
        }

        _channelArchetype[channelIndex] = archetype;
        RegisterBuildChannel(channelIndex, archetype, channelActor?.Location);

        if (archetype is not null && archetype.Contains("AthenaSupplyDrop_C", StringComparison.Ordinal))
        {
            _supplyDropSpawns.Add((CurrentTimeSeconds, channelIndex, channelActor?.Location));
        }

        if (actor is { } guid)
        {
            ChannelGuid cGuid = new(guid.Value);
            _liveGuidChannel[cGuid] = channelIndex;
            _channelGuid[channelIndex] = cGuid;
            _guidArchetype[cGuid] = archetype;
            _guidChannel[cGuid] = channelIndex;

            // A pawn that becomes relevant again only gets its non-default states sent, so the old ones are stale.
            _pawnMovementFlags.Remove(cGuid);
        }

        if (_collectTimeline)
        {
            Events.Add(new ChannelOpenedEvent
            {
                T = CurrentTimeSeconds,
                Channel = channelIndex,
                ActorGuid = actor?.Value,
                Archetype = archetype,
                Location = channelActor?.Location,
                Rotation = channelActor?.Rotation,
            });
        }
    }

    protected override void OnChannelClosed(uint uChannelIndex, NetworkGUID? actor)
    {
        ChannelIndex channelIndex = new(uChannelIndex);
        base.OnChannelClosed(channelIndex.Value, actor);

        if (_channelGuid.Remove(channelIndex, out ChannelGuid closedGuid))
        {
            _liveGuidChannel.Remove(closedGuid);
        }

        if (_collectTimeline)
        {
            Events.Add(new ChannelClosedEvent
            {
                T = CurrentTimeSeconds,
                Channel = channelIndex,
                ActorGuid = actor?.Value,
                Archetype = _channelArchetype.GetValueOrDefault(channelIndex),
            });
        }
    }

    protected override void OnExportRead(uint uChannelIndex, INetFieldExportGroup? exportGroup)
    {
        ChannelIndex channelIndex = new(uChannelIndex);
        base.OnExportRead(channelIndex.Value, exportGroup);
        if (exportGroup is null)
        {
            return;
        }

        TrackPlayers(channelIndex, exportGroup);
        if (!_collectTimeline)
        {
            return;
        }

        // Executed cues are also parsed for the hits; they only belong in the timeline if cues are wanted there.
        if (!_settings.TrackGameplayCues && exportGroup is GameplayCue or GameplayCueFromSpec)
        {
            return;
        }

        // Keep only updates that actually carry data. Pawn updates that only contain movement are thinned out to the interval.
        bool keep = true;
        if (exportGroup is PlayerPawn)
        {
            bool due = !_lastPositionTime.TryGetValue(channelIndex, out var last) || CurrentTimeSeconds - last >= _positionInterval;
            if (due)
            {
                _lastPositionTime[channelIndex] = CurrentTimeSeconds;
            }

            keep = due || Presence.Any(exportGroup, NoisyPawnProps);
        }
        else
        {
            keep = Presence.Any(exportGroup, null);
        }

        if (keep && exportGroup is PlayerPawn pawn)
        {
            AddPawnUpdate(channelIndex, pawn);
        }
        else if (keep)
        {
            Events.Add(new ExportEvent
            {
                T = CurrentTimeSeconds,
                Channel = channelIndex,
                Type = exportGroup.GetType().Name,
                Group = Snapshot(exportGroup),
            });
        }
    }

    /// <summary>
    /// Stores a pawn update as a small <see cref="MovementEvent"/> with its movement fields, and only if other fields were sent
    /// too as a <see cref="PlayerPawn"/> copy without the movement fields. A full pawn copy has room for about 160 fields.
    /// </summary>
    private void AddPawnUpdate(ChannelIndex channelIndex, PlayerPawn pawn)
    {
        if (pawn.ReplicatedMovement is not null || pawn.RemoteViewData32 is not null || pawn.RemoteViewPitch is not null ||
            pawn.AccelerationPack is not null || pawn.AccelerationZPack is not null || pawn.ReplayLastTransformUpdateTimeStamp is not null ||
            pawn.PackedReplicatedSlopeAngles is not null || pawn.GravityScale is not null || pawn.Location is not null ||
            pawn.Rotation is not null || pawn.LinearVelocity is not null || pawn.Position is not null || pawn.WorldLookDir is not null ||
            pawn.ForwardAlpha is not null || pawn.RightAlpha is not null || pawn.TurnDelta is not null || pawn.SteerAlpha is not null)
        {
            Events.Add(new MovementEvent
            {
                T = CurrentTimeSeconds,
                Channel = channelIndex,
                PawnType = pawn.GetType().Name,
                ReplicatedMovement = pawn.ReplicatedMovement,
                RemoteViewData32 = pawn.RemoteViewData32,
                RemoteViewPitch = pawn.RemoteViewPitch,
                AccelerationPack = pawn.AccelerationPack,
                AccelerationZPack = pawn.AccelerationZPack,
                ReplayLastTransformUpdateTimeStamp = pawn.ReplayLastTransformUpdateTimeStamp,
                PackedReplicatedSlopeAngles = pawn.PackedReplicatedSlopeAngles,
                GravityScale = pawn.GravityScale,
                Location = pawn.Location,
                Rotation = pawn.Rotation,
                LinearVelocity = pawn.LinearVelocity,
                Position = pawn.Position,
                WorldLookDir = pawn.WorldLookDir,
                ForwardAlpha = pawn.ForwardAlpha,
                RightAlpha = pawn.RightAlpha,
                TurnDelta = pawn.TurnDelta,
                SteerAlpha = pawn.SteerAlpha,
            });
        }

        if (!Presence.Any(pawn, MovementPawnProps))
        {
            return;
        }

        var copy = (PlayerPawn)Snapshot(pawn);
        copy.ReplicatedMovement = null;
        copy.RemoteViewData32 = null;
        copy.RemoteViewPitch = null;
        copy.AccelerationPack = null;
        copy.AccelerationZPack = null;
        copy.ReplayLastTransformUpdateTimeStamp = null;
        copy.PackedReplicatedSlopeAngles = null;
        copy.GravityScale = null;
        copy.Location = null;
        copy.Rotation = null;
        copy.LinearVelocity = null;
        copy.Position = null;
        copy.WorldLookDir = null;
        copy.ForwardAlpha = null;
        copy.RightAlpha = null;
        copy.TurnDelta = null;
        copy.SteerAlpha = null;

        Events.Add(new ExportEvent
        {
            T = CurrentTimeSeconds,
            Channel = channelIndex,
            Type = pawn.GetType().Name,
            Group = copy,
        });
    }

    protected override void OnNetDeltaRead(uint uChannelIndex, NetDeltaUpdate update)
    {
        ChannelIndex channelIndex = new(uChannelIndex);
        base.OnNetDeltaRead(channelIndex.Value, update);
        if (!_collectTimeline || update.Export is null)
        {
            return;
        }

        Events.Add(new NetDeltaEvent
        {
            T = CurrentTimeSeconds,
            Channel = channelIndex,
            Type = update.Export.GetType().Name,
            ElementIndex = update.ElementIndex,
            Deleted = update.Deleted,
            Group = Snapshot(update.Export),
        });
    }
}

using FortniteReplayReader.Models.NetFieldExports;
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

        if (keep)
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

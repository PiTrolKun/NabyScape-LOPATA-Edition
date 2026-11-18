using System;
using System.Collections.Generic;
using System.Linq;

namespace NabyScape;

public enum LocalPlaybackOrder { Sequential, Shuffle, RepeatTrack }

internal sealed class LocalMusicQueue
{
    private LocalMusicTrack[] tracks = [];
    private LocalMusicTrack[] order = [];
    private int position = -1;

    internal LocalMusicTrack? Previous => position > 0 ? order[position - 1] : null;
    internal LocalMusicTrack? Next => position >= 0 && position + 1 < order.Length ? order[position + 1] : null;

    internal void Start(IReadOnlyList<LocalMusicTrack> source, LocalMusicTrack current, LocalPlaybackOrder mode)
    {
        tracks = source.Any(item => item == current) ? source.ToArray() : [current];
        Reorder(current, mode);
    }

    internal void Reorder(LocalMusicTrack current, LocalPlaybackOrder mode)
    {
        if (mode == LocalPlaybackOrder.Shuffle)
        {
            var remaining = tracks.Where(track => !StringComparer.OrdinalIgnoreCase.Equals(track.Path, current.Path))
                .DistinctBy(track => track.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            Random.Shared.Shuffle(remaining);
            order = [current, .. remaining];
        }
        else order = tracks;
        MoveTo(current);
    }

    internal void MoveTo(LocalMusicTrack track) => position = Array.IndexOf(order, track);
}

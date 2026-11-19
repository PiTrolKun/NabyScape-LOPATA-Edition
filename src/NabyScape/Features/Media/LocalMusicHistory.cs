using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Lopata.Collection;

namespace NabyScape;

public sealed partial class Settings
{
    public bool MusicListeningHistory { get; set; }
}

internal sealed class LocalMusicHistory(string profile, Action<Exception> log)
{
    private readonly string path = Path.Combine(profile, "music-listening-history.json");
    private const int Maximum = 3000;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private sealed record Entry(LocalMusicTrack Track, string Artist, DateTimeOffset Started);
    private List<Entry>? entries;

    internal void Record(LocalMusicTrack track, string artist, DateTimeOffset now)
    {
        if (track.Path.Length > 4096) return;
        var history = Read(now);
        if (history.LastOrDefault() is { } last && last.Track == track && now - last.Started < TimeSpan.FromSeconds(20)) return;
        history.Add(new(track, artist, now));
        if (history.Count > Maximum) history.RemoveRange(0, history.Count - Maximum);
        try { Save(history); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { log(error); }
    }

    internal void Prune(DateTimeOffset now) => _ = Read(now);

    internal bool Clear()
    {
        try { Save([]); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { log(error); return false; }
    }

    internal LocalMusicTrack? Find(DateTimeOffset now, int daysAgo, string before, IReadOnlyList<LocalMusicCard> cards)
    {
        var date = now.LocalDateTime.Date.AddDays(-daysAgo);
        var available = cards.ToDictionary(card => card.Track.Path, StringComparer.OrdinalIgnoreCase);
        var day = Read(now).Where(entry => entry.Started.LocalDateTime.Date == date).ToArray();
        if (before.Length == 0)
            return day.LastOrDefault(entry => available.ContainsKey(entry.Track.Path))?.Track;

        // Use the latest matching occurrence on the requested day and its direct predecessor.
        for (int index = day.Length - 1; index > 0; index--)
        {
            var entry = day[index];
            available.TryGetValue(entry.Track.Path, out var card);
            if (Math.Max(LocalMusicSearch.MatchText(before, entry.Artist),
                    LocalMusicSearch.MatchText(before, card?.Label ?? entry.Track.Name)) >= 78)
                return available.ContainsKey(day[index - 1].Track.Path) ? day[index - 1].Track : null;
        }
        return null;
    }

    private List<Entry> Read(DateTimeOffset now)
    {
        if (entries is null)
        {
            entries = [];
            try
            {
                using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (reader.Length <= 4 * 1024 * 1024 && JsonSerializer.Deserialize<List<Entry>>(reader, CollectionFiles.Json) is { } saved)
                    entries = saved.Where(item => item?.Track?.Path is { } file && Path.IsPathFullyQualified(file)
                        && item.Artist is not null)
                        .OrderBy(item => item.Started).TakeLast(Maximum).ToList();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }
        if (entries.RemoveAll(item => item.Started < now - Retention || item.Started > now.AddMinutes(1)) > 0)
            try { Save(entries); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { log(error); }
        return entries;
    }

    private void Save(List<Entry> updated)
    {
        CollectionFiles.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(updated, CollectionFiles.Json));
        entries = updated;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NabyScape;

internal enum LocalMusicList { Collection, Folder, Favorites }

internal sealed record LocalMusicListsDocument
{
    public LocalMusicList Selected { get; init; }
    public LocalMusicTrack[] Favorites { get; init; } = [];
}

internal sealed class LocalMusicLists
{
    private const string Document = "music-lists";
    private readonly Store store;
    private LocalMusicListsDocument state;

    internal LocalMusicLists(Store store)
    {
        this.store = store;
        var saved = store.Load<LocalMusicListsDocument>(Document);
        var favorites = (saved.Favorites ?? []).Where(IsTrack)
            .DistinctBy(track => track.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        state = new()
        {
            Selected = Enum.IsDefined(saved.Selected) ? saved.Selected : LocalMusicList.Collection,
            Favorites = favorites
        };
    }

    internal event Action? FavoritesChanged;
    internal LocalMusicList Selected => state.Selected;
    internal IReadOnlyList<LocalMusicTrack> Favorites => state.Favorites;
    internal bool IsFavorite(LocalMusicTrack track) => state.Favorites.Any(item => SamePath(item, track));

    internal void SelectList(LocalMusicList list)
    {
        if (state.Selected != list) Save(state with { Selected = list });
    }

    internal void ToggleFavorite(LocalMusicTrack track)
    {
        if (!IsTrack(track)) throw new InvalidDataException("Invalid local audio address.");
        var favorites = IsFavorite(track) ? state.Favorites.Where(item => !SamePath(item, track)).ToArray() :
            state.Favorites.Append(track).ToArray();
        Save(state with { Favorites = favorites });
        FavoritesChanged?.Invoke();
    }

    private void Save(LocalMusicListsDocument next)
    {
        store.Save(Document, next);
        state = next;
    }

    private static bool IsTrack(LocalMusicTrack? track) =>
        track is { Path.Length: > 0 and <= 32700 } && Enum.IsDefined(track.Source) &&
        Path.IsPathFullyQualified(track.Path) && !track.Path.Any(char.IsControl);

    private static bool SamePath(LocalMusicTrack first, LocalMusicTrack second) =>
        first.Path.Equals(second.Path, StringComparison.OrdinalIgnoreCase);
}

public sealed partial class App
{
    private LocalMusicLists? musicLists;
    internal LocalMusicLists MusicLists => musicLists ??= CreateMusicLists();

    private LocalMusicLists CreateMusicLists()
    {
        var lists = new LocalMusicLists(Store);
        lists.FavoritesChanged += () => PublishUpdate(RuntimeChange.Media);
        return lists;
    }
}
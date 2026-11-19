using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lopata.Island;

namespace NabyScape;

public sealed partial class App
{
    private const string NoLocalTrack = "No local track is selected.";

    internal async Task<string?> ExecuteIslandVoiceCommandAsync(IslandVoiceCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        switch (command.Action)
        {
            case IslandVoiceAction.Pause:
            case IslandVoiceAction.Resume:
                return Media.SetLocalPlaying(command.Action == IslandVoiceAction.Resume) ? null : Localization.Text(NoLocalTrack);
            case IslandVoiceAction.Next:
            case IslandVoiceAction.Previous:
                return await Media.NavigateLocalFromVoiceAsync(command.Action == IslandVoiceAction.Next, token)
                    ? null : Localization.Text("No available track in that direction.");
            case IslandVoiceAction.SeekRelative:
            case IslandVoiceAction.SeekAbsolute:
                return Media.SeekLocalSeconds(command.Value, command.Action == IslandVoiceAction.SeekRelative)
                    ? null : Localization.Text("Cannot seek in the current local track.");
            case IslandVoiceAction.Volume:
            case IslandVoiceAction.Louder:
            case IslandVoiceAction.Quieter:
                int volume = command.Action switch
                {
                    IslandVoiceAction.Volume => command.Value,
                    IslandVoiceAction.Louder => Math.Min(100, Settings.LocalMusicVolume + 10),
                    _ => Math.Max(0, Settings.LocalMusicVolume - 10)
                };
                if (Settings.LocalMusicVolume != volume)
                {
                    Settings.LocalMusicVolume = volume;
                    ChangeSettings();
                }
                return null;
            case IslandVoiceAction.Sequential:
            case IslandVoiceAction.Shuffle:
            case IslandVoiceAction.Repeat:
                SetVoicePlaybackOrder(command.Action switch
                {
                    IslandVoiceAction.Sequential => LocalPlaybackOrder.Sequential,
                    IslandVoiceAction.Shuffle => LocalPlaybackOrder.Shuffle,
                    _ => LocalPlaybackOrder.RepeatTrack
                });
                return null;
            case IslandVoiceAction.Current:
                return DescribeLocalTrack();
            case IslandVoiceAction.AddFavorite:
            case IslandVoiceAction.RemoveFavorite:
                if (Media.LocalTrack is not { } track) return Localization.Text(NoLocalTrack);
                bool add = command.Action == IslandVoiceAction.AddFavorite;
                if (MusicLists.IsFavorite(track) != add)
                    try { MusicLists.ToggleFavorite(track); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { return Localization.Text("Could not save favorite tracks."); }
                return null;
            case IslandVoiceAction.PlayFavorites:
                return await PlayVoiceFavoritesAsync(token);
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    private string DescribeLocalTrack()
    {
        var current = ((ILocalMusicTransport)Media).CurrentMusic;
        if (current.Track is null) return Localization.Text(NoLocalTrack);
        string name = string.IsNullOrWhiteSpace(current.Artist) ? current.Title : $"{current.Title}, {current.Artist}";
        return Localization.Format(current.Playing ? "Playing {0}." : "Paused: {0}.", name);
    }

    private async Task<string?> PlayVoiceFavoritesAsync(CancellationToken token)
    {
        if (Media.LocalTrack is null && Media.Selected is not null && Media.Playing)
            return Localization.Text("Pause the other player before starting a local track.");
        var tracks = MusicLists.Favorites.Where(track => File.Exists(track.Path)).ToArray();
        if (tracks.Length == 0) return Localization.Text("No available local favorites.");
        if (!await Media.PlayVoiceQueueAsync(tracks[Random.Shared.Next(tracks.Length)], tracks, token))
            return Localization.Text(Media.LocalOpenError ?? "The track could not be opened.");
        SetVoicePlaybackOrder(LocalPlaybackOrder.Shuffle);
        MusicLists.SelectList(LocalMusicList.Favorites);
        return null;
    }

    private void SetVoicePlaybackOrder(LocalPlaybackOrder order)
    {
        if (Settings.PlaybackOrder == order) return;
        Settings.PlaybackOrder = order;
        ChangeSettings();
    }
}

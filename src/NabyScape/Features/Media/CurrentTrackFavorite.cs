using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private Button? favoriteTrackButton;
    private bool? displayedFavorite;

    private Button BuildFavoriteTrackButton()
    {
        displayedFavorite = null;
        var button = Icons.Button("heart","Toggle current track favorite",ToggleCurrentFavorite,false,28);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Margin = new(0);
        ToolTipService.SetShowOnDisabled(button,true);
        favoriteTrackButton = button;
        SyncFavoriteTrackButton();
        return button;
    }

    private void SyncFavoriteTrackButton()
    {
        if (favoriteTrackButton is not { } button) return;
        var track = app.Media.LocalTrack;
        bool saved = track is not null && app.MusicLists.IsFavorite(track);
        button.IsEnabled = track is not null;
        button.ToolTip = Localization.Text(track is null ? "Track favorites are available for local audio files." :
            saved ? "Remove from favorites" : "Add to favorites");
        AutomationProperties.SetItemStatus(button,saved ? "On" : "Off");
        if (displayedFavorite == saved) return;
        displayedFavorite = saved;
        var glyph = Icons.Create(saved ? "heart-filled" : "heart",20);
        if (saved && !Accessibility.HighContrast)
            glyph.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,"Accent");
        button.Content = glyph;
        if (saved) button.SetResourceReference(Control.BackgroundProperty,"SelectionFill");
        else button.Background = Brushes.Transparent;
    }

    private void ToggleCurrentFavorite()
    {
        if (app.Media.LocalTrack is not { } track) return;
        try { app.MusicLists.ToggleFavorite(track); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            app.Store.Log(error);
            app.Notify("Not saved yet",Localization.Text("Could not save favorite tracks."));
        }
    }
}
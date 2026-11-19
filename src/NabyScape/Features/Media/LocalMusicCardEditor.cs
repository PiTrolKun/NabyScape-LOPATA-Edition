using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

internal sealed class LocalMusicCardEditor : Window
{
    internal LocalMusicCardEditor(LocalMusicCatalog catalog, LocalMusicTrack track)
    {
        Width = 460;
        Height = 565;
        MinWidth = 310;
        MinHeight = 280;
        Title = Localization.Text("Edit track search details");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = UI.Brush("#151B25");
        var saved = catalog.CorrectionFor(track);
        var content = new StackPanel { Margin = new(16) };
        content.Children.Add(UI.Text(track.Name, 15));
        content.Children.Add(UI.Caption("Corrections are local. Blank fields use the original track details.", 12, UI.Muted));
        TextBox Field(string caption, string value, int limit)
        {
            var name = Localization.Text(caption);
            var label = UI.Text(name, 12);
            label.Margin = new(0, 8, 0, 3);
            content.Children.Add(label);
            var input = UI.Input(name, value);
            input.MaxLength = limit;
            content.Children.Add(input);
            return input;
        }
        var title = Field("Track title", saved.Title, 240);
        var artist = Field("Track artist", saved.Artist, 240);
        var album = Field("Track album", saved.Album, 240);
        var version = Field("Track version", saved.Version, 96);
        var genre = Field("Track genre", saved.Genre, 240);
        var description = Field("Track description", saved.Description, 768);
        var status = UI.Text("", 12, UI.Muted);
        content.Children.Add(UI.Row(UI.CaptionButton("Save", () =>
        {
            try
            {
                catalog.SaveCorrection(track, new(title.Text.Trim(), artist.Text.Trim(), album.Text.Trim(), version.Text.Trim(),
                    genre.Text.Trim(), description.Text.Trim()));
                DialogResult = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { status.Text = Localization.Text("Could not save track corrections."); }
        }, primary: true), UI.CaptionButton("Cancel", Close)));
        content.Children.Add(status);
        Content = UI.Scroll(content);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Lopata.Island;
using Forms = System.Windows.Forms;

namespace NabyScape;

internal static class LopataEdition
{
    internal const string Name = "NabyScape — LOPATA Edition";
    internal const string Instance = @"Local\LOPATA.NabyScape";
    internal const string StartupName = "LOPATA.NabyScape";
    internal const string OpenCaption = "Open LOPATA";
    internal const string CloseCaption = "Close island";
    internal const string LayerCaption = "Above windows";
    internal static string ProfileDirectory => Path.Combine(IslandPreferences.Root, "Profile");

    internal static KeyValuePair<string, string>[] Captions { get; } =
    [
        new(OpenCaption, "Открыть ЛОПАТУ"),
        new(CloseCaption, "Закрыть остров"),
        new(LayerCaption, "Поверх окон"),
        new("Updates are managed by LOPATA", "Обновления управляются ЛОПАТОЙ")
    ];

    internal static string? HostExecutable()
    {
        var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "AIHub.exe"));
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "LOPATA", "AIHub.exe");
        return File.Exists(parent) ? parent : File.Exists(installed) ? installed : null;
    }

    internal static void OpenHost()
    {
        try
        {
            var target = HostExecutable() ?? throw new FileNotFoundException();
            // LOPATA routes a second launch to its existing instance and restores it from the tray.
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(Localization.Text("Open LOPATA") + " · " + error.Message, Name);
        }
    }

    internal static void ApplyAppearance(Settings settings, IslandPreferences appearance)
    {
        var defaults = new IslandPreferences();
        var geometry = appearance.Advanced ? appearance : defaults;
        settings.ColorTheme = "Dark";
        settings.Background = appearance.Surface;
        settings.Accent = appearance.Accent;
        settings.CollapsedSameColor = true;
        settings.GlassStyle = false;
        settings.Width = geometry.ClosedWidth;
        settings.Height = geometry.ClosedHeight;
        settings.PanelWidth = geometry.PanelWidth;
        settings.PanelMaxHeight = geometry.PanelHeight;
        settings.HoverWidth = geometry.HoverWidth;
        settings.HoverHeight = geometry.HoverHeight;
        settings.PanelAutoHeight = true;
        settings.PanelPadding = 17;
        settings.SectionGap = 10;
        settings.Radius = 18;
        settings.ReduceMotion = appearance.Advanced && appearance.ReduceMotion;
        settings.HotkeyEnabled = appearance.Advanced && appearance.Hotkey;
        settings.VisibilityHotkeyEnabled = appearance.Advanced && appearance.Hotkey;
        settings.TuckHotkeyEnabled = false;

        var monitor = appearance.Advanced && Array.Exists(Forms.Screen.AllScreens,
            screen => screen.DeviceName == appearance.Monitor) ? appearance.Monitor : "";
        settings.Monitor = monitor;
        settings.DisplayMode = appearance.Advanced && appearance.FollowPointer ? "Follow"
            : monitor.Length == 0 ? "Primary" : "Selected";
    }
}

public partial class App
{
    private DispatcherTimer? hostSettingsWatcher;
    private IslandPreferences lopataAppearance = new();
    private string lopataLanguage = "";
    internal bool LopataAboveWindows => lopataAppearance.AboveWindows;

    private void PrepareLopataSettings()
    {
        lopataAppearance = IslandPreferences.Read();
        LopataEdition.ApplyAppearance(Settings, lopataAppearance);
        ReadHostLanguage();
    }

    private bool ReadHostLanguage()
    {
        var language = IslandPreferences.ReadJson<IslandLanguage>("language.json");
        string signature = language is null ? "" : System.Text.Json.JsonSerializer.Serialize(language);
        if (signature == lopataLanguage) return false;
        lopataLanguage = signature;
        if (language is not null)
        {
            Settings.UiLanguage = LocalizationCatalog.IsSupported(language.Code) ? language.Code : "en";
            Localization.LopataCaptions = language.Captions ?? new Dictionary<string, string>();
        }
        else Localization.LopataCaptions = new Dictionary<string, string>();
        Localization.Configure(Settings);
        return true;
    }

    private void ObserveLopataSettings()
    {
        hostSettingsWatcher = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        hostSettingsWatcher.Tick += async (_, _) =>
        {
            RefreshLopataSettings();
            await LopataLink.RefreshAsync();
        };
        hostSettingsWatcher.Start();
    }

    private void RefreshLopataSettings()
    {
        var appearance = IslandPreferences.Read();
        bool languageChanged = ReadHostLanguage();
        if (!languageChanged && appearance == lopataAppearance) return;
        lopataAppearance = appearance;
        LopataEdition.ApplyAppearance(Settings, appearance);
        if (languageChanged)
        {
            settingsWindow?.Close();
            if (tray?.ContextMenuStrip is { } menu)
                foreach (Forms.ToolStripItem item in menu.Items)
                    if (item.Tag is string caption) item.Text = Localization.Text(caption);
            if (overlay is not null)
            {
                var old = overlay;
                bool wasExpanded = old.IsExpanded;
                overlay = new OverlayWindow(this);
                MainWindow = overlay;
                old.Cleanup();
                old.CloseForLanguage = true;
                old.Close();
                overlay.Show();
                if (wasExpanded) overlay.Reveal(false);
            }
        }
        ChangeSettings();
    }

    internal void ToggleLopataLayer()
    {
        var saved = IslandPreferences.Read();
        try { IslandPreferences.Save(saved with { AboveWindows = !saved.AboveWindows }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Store.Log(error); Notify("Not saved yet", error.Message); return; }
        RefreshLopataSettings();
    }
}


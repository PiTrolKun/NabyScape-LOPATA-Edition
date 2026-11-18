using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Lopata.Island;
using Lopata.Updates;
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
    internal const string VkUrl = "https://vk.ru/lopata_ai_localhub";
    internal const string YouTubeUrl = "https://youtube.com/@lopata_ai_hub";
    internal static string ProfileDirectory => Path.Combine(IslandPreferences.Root, "Profile");

    internal static KeyValuePair<string, string>[] Captions { get; } =
    [
        new(OpenCaption, "Открыть ЛОПАТУ"),
        new(CloseCaption, "Закрыть остров"),
        new(LayerCaption, "Поверх окон"),
        new("Updates are managed by LOPATA", "Обновления управляются ЛОПАТОЙ")
    ];

    internal static string? HostExecutable() => HostExecutable(null);

    internal static string? HostExecutable(InstalledUpdateState? registration)
    {
        var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "AIHub.exe"));
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "LOPATA", "AIHub.exe");
        if (File.Exists(parent)) return parent;
        if (registration is not null)
        {
            var registered = Path.Combine(registration.AppDirectory, "AIHub.exe");
            if (File.Exists(registered)) return registered;
        }
        return File.Exists(installed) ? installed : null;
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
    private (DateTime Written, long Bytes)? lastLanguageFile;
    internal bool LopataAboveWindows => lopataAppearance.AboveWindows;

    private void PrepareLopataSettings()
    {
        lopataAppearance = IslandPreferences.Read();
        LopataEdition.ApplyAppearance(Settings, lopataAppearance);
        ReadHostLanguage();
    }

    private bool ReadHostLanguage()
    {
        var file = new FileInfo(Path.Combine(IslandPreferences.Root, "language.json"));
        var stamp = (file.LastWriteTimeUtc, file.Exists ? file.Length : 0);
        if (stamp == lastLanguageFile) return false;
        var language = IslandPreferences.ReadJson<IslandLanguage>("language.json", IslandLanguage.MaximumBytes);
        if (language is null && file.Exists) return false;
        lastLanguageFile = stamp;
        string signature = language is null ? "" : System.Text.Json.JsonSerializer.Serialize(language);
        if (signature == lopataLanguage) return false;
        lopataLanguage = signature;
        if (language is not null)
        {
            var baseLanguage = language.Code?.Split('-', 2)[0].ToLowerInvariant() ?? "en";
            Settings.UiLanguage = LocalizationCatalog.IsSupported(baseLanguage) ? baseLanguage : "en";
            var captions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (language.Captions is not null)
                foreach (var (key, value) in language.Captions) captions[key] = value;
            Localization.LopataCaptions = captions;
        }
        else Localization.LopataCaptions = new Dictionary<string, string>();
        Localization.LopataLanguage = language?.Code;
        Localization.Configure(Settings);
        return true;
    }

    private void ObserveLopataSettings()
    {
        hostSettingsWatcher = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        hostSettingsWatcher.Tick += async (_, _) =>
        {
            RefreshLopataSettings();
            overlay?.SyncLopataWindowLayer();
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
                LocalizeTrayItems(menu.Items);
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

    private static void LocalizeTrayItems(Forms.ToolStripItemCollection items)
    {
        foreach (Forms.ToolStripItem item in items)
        {
            if (item.Tag is string caption) item.Text = Localization.Text(caption);
            if (item is Forms.ToolStripDropDownItem child) LocalizeTrayItems(child.DropDownItems);
        }
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


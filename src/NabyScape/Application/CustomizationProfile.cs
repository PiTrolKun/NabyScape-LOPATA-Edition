using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NabyScape;

internal static class CustomizationProfile
{
    internal static readonly string[] CustomizationKeys={"HoverFollowsWidgets","FollowMonitorAnimation","FollowMonitorDuration","DockUnlocked","PanelAutoHeight","BrowserMediaPreviews","WaveformSource","WaveformSensitivity","LyricsAnimation","LyricsTextMode","LyricsOffset","HideFullscreen","HideMaximized","AlwaysTucked","TuckEdge","Preset","Width","Height","ClosedClockPosition","Radius","Top","Horizontal","DockEdge","DockVertical","DockInset","Monitor","DisplayMode","Background","Accent","Opacity","ColorTheme","CollapsedSameColor","CollapsedBackground","CollapsedOpacity","SettingsMaterial","GlassStyle","GlassStrength","CoverAmbient","ClosedAmbient","AmbientStrength","AmbientSpread","AmbientMotion","AmbientScope","AmbientWidgets","AmbientColorSource","AmbientCustomColor","AmbientSecondaryColor","AmbientPalette","AmbientDirection","AmbientHeight","AmbientBalance","AmbientSoftness","AmbientAudioResponse","AmbientSensitivity","InteractionMotion","AlbumAccent","PanelWidth","PanelMaxHeight","PanelPadding","PanelScale","SectionGap","HoverWidth","HoverHeight","HoverDelayMs","HoverDetailDelayMs","SwipeDistance","HoverViews","Widgets","CompactLayout","HomeWidget","HoverPeek","TwoStageHover","AnimationMs","ReduceMotion","LiveActivity","ClosedMediaIcon","ClosedMediaContent","Performance","WheelAction","MiddleAction","VolumeStep","Clock24Hour","ClockSeconds","ShowDate","MonitorNetwork","MonitorStorage","MonitorProcesses","CategoryStyle","LaunchStyle","BoldMediaTitle","BoldHeadings","WaveformIdleMotion","VolumeFeedback","BrightnessFeedback"};
    internal static string Export(Settings settings, IReadOnlySet<string>? excluded = null)
    {
        var values=CustomizationKeys.Concat(new[]{"DropReveal","DetachablePreviews","CoverGestures","ControlFill","ControlBorder","SelectionFill","PanelSwipe","Tools"}).Where(key=>excluded?.Contains(key)!=true).ToDictionary(key=>key,key=>typeof(Settings).GetProperty(key)!.GetValue(settings));
        return JsonSerializer.Serialize(new{format="luma.layout.v1",values},new JsonSerializerOptions{WriteIndented=true});
    }
    internal static void Import(string source,Settings target,IReadOnlySet<string>? excluded = null)
    {
        if(source.Length>262144)throw new InvalidDataException("Layout file is too large.");
        using var document=JsonDocument.Parse(source);
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("format",out var format) || format.GetString()!="luma.layout.v1" || !root.TryGetProperty("values",out var values) || values.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Choose a NabyScape layout file.");
        var allKeys=CustomizationKeys.Concat(new[]{"DropReveal","DetachablePreviews","CoverGestures","ControlFill","ControlBorder","SelectionFill","PanelSwipe","Tools"}).Where(key=>excluded?.Contains(key)!=true).ToArray();
        var keys=allKeys.Where(key=>values.TryGetProperty(key,out _)).ToArray();if(keys.Length==0)throw new InvalidDataException("The layout has no customization values.");
        var incoming=JsonSerializer.Deserialize<Settings>(values.GetRawText())??throw new InvalidDataException("Layout is empty.");
        var candidate=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(target))!;
        foreach(string key in keys){var property=typeof(Settings).GetProperty(key)!;property.SetValue(candidate,property.GetValue(incoming));}
        if(keys.Contains("Monitor") && excluded?.Contains("DisplayMode")!=true && !values.TryGetProperty("DisplayMode",out _))candidate.DisplayMode=string.IsNullOrEmpty(incoming.Monitor)?"Primary":"Selected";
        candidate.Normalize();
        foreach(string key in allKeys){var property=typeof(Settings).GetProperty(key)!;property.SetValue(target,property.GetValue(candidate));}
    }
}

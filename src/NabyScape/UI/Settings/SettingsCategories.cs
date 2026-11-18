using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;

internal sealed record SettingsCategory(string Id,string Title,string Group,string Description,string Terms,string[]? Sections=null);
public sealed partial class SettingsWindow
{
    private static readonly SettingsCategory[] OriginalSettingsCategories=
    {
        new("Home","Start here","Settings","Make NabyScape work the way you want.","home start setup help beginner"),
        new("Appearance","Look & colors","Personalize","Choose the dock's shape, colors and text style.","appearance theme warna tampilan font shape glass light dark terang gelap mode",new[]{"Theme","Shape","Settings material","Color & material","Surface","Controls & outlines","Typography","Text and navigation"}),
        new("Ambience","Ambience","Personalize","Choose where the light appears and how it follows your music.","ambient ambience cahaya warna glow spread softness light reach closed dock tertutup",new[]{"Ambient light"}),
        new("Motion","Motion & speed","Personalize","Adjust animation and choose a balance between smoothness and power.","animation animasi motion fps speed performance ram cpu hemat reduce",new[]{"Motion","Animation performance"}),
        new("Layout","Position & size","Dock","Place the dock on a screen edge or move it freely.","position posisi pindah atas bawah kanan kiri monitor size resize ukuran"),
        new("Visibility","Show & hide","Dock","Choose when the dock hides, tucks away or shows a preview.","hide show tuck sembunyi tutup youtube fullscreen maximized hover preview",new[]{"Automatic tucking","Manual tuck shortcut","Hide or show the island","Automatic activity"}),
        new("Controls","Controls","Dock","Set mouse gestures, hover behavior and keyboard shortcuts.","mouse keyboard shortcut hotkey swipe scroll gesture klik tombol"),
        new("Content","Widgets & tools","Features","Choose which widgets to show and arrange them.","widget tools notes tasks timer stopwatch clipboard"),
        new("Audio","Music & sound","Features","Customize music controls, lyrics and the audio visualizer.","music spotify volume musik suara waveform spectrum lirik lyrics audio microphone"),
        new("Clipboard","Clipboard","Features","Control whether copied text and images are saved on this device.","clipboard salin copy paste capture history riwayat gambar",new[]{"Clipboard history"}),
        new("Weather","Weather","Features","Choose your city and see the weather in Celsius or Fahrenheit.","weather cuaca kota city temperature suhu rain hujan"),
        new("System","Clock & system","Features","Choose the clock format and computer information shown in widgets.","clock jam tanggal date battery cpu ram network storage system"),
        new("Launch","Shortcuts","Features","Choose your favorite apps, folders and websites.","apps launch aplikasi shortcuts explorer"),
        new("AI","LOPATA chat","Features","Talk to LOPATA through its installed local core.","chat local core lopata чат локальное ядро если накипело",new[] { "LOPATA chat" }),
        new("Notifications","Notifications","App","Choose which alerts appear, their sounds and quiet hours.","notifications notif pemberitahuan alert sound bunyi quiet hours downloads other apps claude telegram windows",new[]{"Notifications","Other apps","Notification presentation","Downloads","Completion sound"}),
        new("Application","General","App","Manage startup, updates, local data and app preferences.","general startup update version install storage data reset backup mulai windows")
    };
    private static readonly SettingsCategory[] LopataSettingsCategories = OriginalSettingsCategories
        .Select(category => category.Id == "Appearance"
            ? category with { Sections = new[] { LopataHomeWidget.Caption }.Concat(category.Sections!).ToArray() }
            : category)
        .ToArray();
    private static bool EditionSettingsActive => Application.Current is App { UsesLopataEdition: true };
    private static SettingsCategory[] SettingsCategories => EditionSettingsActive ? LopataSettingsCategories : OriginalSettingsCategories;
    private static SettingsCategory Category(string id)=>SettingsCategories.FirstOrDefault(c=>c.Id==id)??SettingsCategories[0];
    internal static string CategoryTitle(string id)=>Localization.Text(Category(id).Title);
    private static string CategoryId(string title)=>SettingsCategories.FirstOrDefault(c=>c.Title==title || Localization.Text(c.Title)==title)?.Id??title;
    private static string SettingsRoot(string id)=>id switch{"Ambience" or "Motion"=>"Appearance","Visibility"=>"Controls","Clipboard"=>"Content","Notifications"=>"Application",_=>id};
    private static string FriendlySection(string title)=>title switch
    {
        "Move & resize" when EditionSettingsActive => "Move island",
        "Completion sound" => "Timer sound",
        "Shape"=>"Dock shape","Color & material"=>"Colors","Settings material"=>"Settings window","Surface"=>"Glass appearance","Controls & outlines"=>"Buttons & input fields","Typography"=>"Text weight","Text and navigation"=>"Panel dots","Animation performance"=>"Smoothness & power","Display"=>"Screen","Closed island"=>"Compact dock","Open panel"=>"Expanded dock","Enabled widgets & order"=>"Your widgets","Notch gestures"=>"Mouse actions","Global hotkey"=>"Open dock shortcut","Automatic activity"=>"Automatic previews","Clipboard history"=>"Clipboard capture",_=>title
    };
    private void FilterSettingsCategory(string id)
    {
        var definition=Category(id);
        var excluded=id switch{"Content"=>ShowAllSettings?new[]{"Clipboard history"}:Array.Empty<string>(),"Application"=>Category("Notifications").Sections!,"Controls"=>Category("Visibility").Sections!,_=>Array.Empty<string>()};
        foreach(var section in page.Children.OfType<Border>().Where(b=>b.Tag is string).ToArray())
        {
            string key=(string)section.Tag;
            if(definition.Sections is {} allowed && !allowed.Contains(key) || excluded.Contains(key))page.Children.Remove(section);
        }
        if(definition.Sections!=null)foreach(var note in page.Children.OfType<TextBlock>().Skip(1).ToArray())page.Children.Remove(note);
        var retained=page.Children.OfType<Border>().Select(b=>b.Tag as string).ToHashSet();settingExamples.RemoveAll(scene=>!retained.Contains(scene.Section));
    }
    private void WeatherSettingsPage()
    {
        page.Children.Add(new Border{Child=new WeatherView(app,true),Background=UI.Brush("#16191F"),BorderBrush=UI.Brush("#2A2F39"),BorderThickness=new(.7),CornerRadius=new(14),Padding=new(16),Margin=new(0,0,0,16)});
    }
}

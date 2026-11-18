using System;
using System.IO;
using System.Text.Json;

namespace NabyScape;

internal static class InstallationInputTests
{
    internal static void Run(Action<string,Action> test)
    {
        static void Check(bool value){if(!value)throw new Exception("Assertion failed");}
        test("Fresh profiles use the complete bundled preferences and keep personal paths out",()=>
        {
            using var stream=typeof(FactoryDefaults).Assembly.GetManifestResourceStream("NabyScape.DefaultPreferences.json")!;
            using var json=JsonDocument.Parse(stream);
            foreach(var property in typeof(Settings).GetProperties(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance))
                if (property.CanWrite && property.Name != "Shortcuts" && !json.RootElement.TryGetProperty(property.Name, out _))
                    throw new Exception("Bundled preference missing: " + property.Name);
            var defaults=FactoryDefaults.Create();Check(defaults.LyricsEnabled && defaults.ClipboardHistory && defaults.DownloadsEnabled && defaults.AutoStart && defaults.NotificationDelivery=="Both");
            Check(defaults.Monitor=="" && defaults.DisplayMode=="Primary" && defaults.WeatherCity=="" && defaults.WeatherLatitude==null && defaults.DownloadsFolder=="" && defaults.CountdownSoundFile=="" && defaults.Shortcuts.Count==2);
            string root=Path.Combine(Path.GetTempPath(),"NabyScape-fresh-preference-fixture-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new Store(root);var fresh=store.Load<Settings>("settings");Check(JsonSerializer.Serialize(fresh)==JsonSerializer.Serialize(defaults));
                fresh.LyricsEnabled=false;fresh.NotificationDelivery="Off";store.Save("settings",fresh);
                var existing=store.Load<Settings>("settings");Check(!existing.LyricsEnabled && existing.NotificationDelivery=="Off");
            }
            finally{foreach(string file in Directory.GetFiles(root))File.Delete(file);Directory.Delete(root);}
        });
        test("Panel navigation migrates every legacy style and imported layout to dots",()=>
        {
            foreach(string style in new[]{"Icons","IconsLabels","Text","Dots","invalid"})
            {
                var settings=new Settings{CategoryStyle=style,LaunchStyle="Icons"};settings.Normalize();Check(settings.CategoryStyle=="Dots" && settings.LaunchStyle=="Icons");
                var imported=new Settings();CustomizationProfile.Import(JsonSerializer.Serialize(new{format="luma.layout.v1",values=new{CategoryStyle=style}}),imported);Check(imported.CategoryStyle=="Dots");
            }
            Check(new Settings().CategoryStyle=="Dots" && FactoryDefaults.Create().CategoryStyle=="Dots");
        });
    }
}

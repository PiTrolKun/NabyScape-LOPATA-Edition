using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace NabyScape;

// English source captions are catalog keys. Call only for application-owned UI text.
// Never apply this to transcripts, user labels, filenames, provider IDs or persisted values.
internal static class Localization
{
    internal static string Language { get; private set; } = "en";
    internal static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");
    internal static string? LopataLanguage { get; set; }
    private static IReadOnlyDictionary<string, string> translations = LocalizationCatalog.Translations("en");
    private static readonly Lazy<IReadOnlyDictionary<string, string>> LopataRussian = new(ReadLopataRussian);
    internal static void Configure(Settings settings)
    {
        settings.NormalizeLanguageAccessibility();
        Language = settings.UiLanguage;
        translations = LocalizationCatalog.Translations(Language);
        try { Culture = CultureInfo.GetCultureInfo(LopataLanguage ?? Language); }
        catch (CultureNotFoundException) { Culture = CultureInfo.GetCultureInfo(Language); }
        Accessibility.Configure(settings);
        // Deliberately preserve CurrentCulture: numeric editors retain their existing parsing.
    }

    // One ordinal lookup in the selected language; a missing or blank translation returns the English key.
    internal static IReadOnlyDictionary<string, string> LopataCaptions { get; set; } = new Dictionary<string, string>();

    internal static string Text(string english)
    {
        if (string.IsNullOrEmpty(english)) return english;
        if (LopataCaptions.TryGetValue(english, out var custom) && !string.IsNullOrWhiteSpace(custom)) return custom;
        if (Language == "ru" && LopataRussian.Value.TryGetValue(english, out var shared)) return shared;
        if (translations.TryGetValue(english, out var translated) && translated.Length > 0) return translated;
        if (Language == "ru")
            foreach (var pair in LopataEdition.Captions)
                if (pair.Key == english) return pair.Value;
        return english;
    }

    private static IReadOnlyDictionary<string, string> ReadLopataRussian()
    {
        const string prefix = "NabyScape.";
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream("NabyScape.LopataRussian.json")
            ?? throw new InvalidOperationException("The LOPATA Edition package has no shared Russian catalog.");
        var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("The shared Russian catalog is empty.");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in catalog)
            if (key.StartsWith(prefix, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(value))
                result.Add(key[prefix.Length..], value);
        return result;
    }

    internal static string Format(string english, params object[] values)
    {
        string pattern = Text(english);
        try { return string.Format(Culture, pattern, values); }
        catch (FormatException) when (pattern != english) { return string.Format(Culture, english, values); }
    }
}

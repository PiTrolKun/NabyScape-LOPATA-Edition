using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Lopata.Island;

namespace NabyScape;

internal sealed record MusicOffer(string Label, LocalMusicTrack[] Queue);

internal static class LocalMusicSearch
{
    private static readonly Regex Spaces = new(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant);
    private static readonly Regex RussianEndings = new(@"(?<=[а-я]{4})(?:ами|ого|ему|ыми|ими|ий|ая|ое|ие|ом|ам|ую|ым|ых|ов|ев|ой|ей|ы|и|а|я|е|у)$",
        RegexOptions.CultureInvariant);
    private static readonly string[] Cyrillic =
        ["a", "b", "v", "g", "d", "e", "zh", "z", "i", "i", "k", "l", "m", "n", "o", "p",
         "r", "s", "t", "u", "f", "h", "ts", "ch", "sh", "sch", "", "y", "", "e", "yu", "ya"];
    private static readonly HashSet<string> DescriptionFillers = new(StringComparer.Ordinal)
    {
        "там", "песня", "поет", "играет", "вроде", "такая", "какая", "song", "sings", "playing", "very", "with", "about"
    };
    private static readonly Dictionary<string, string> FeatureAliases = new(StringComparer.Ordinal)
    {
        ["спокойн"] = "calm", ["тих"] = "calm", ["тихая"] = "calm", ["relaxing"] = "calm", ["chill"] = "calm",
        ["женск"] = "female", ["женщина"] = "female", ["мужск"] = "male", ["мужчина"] = "male",
        ["вокал"] = "vocal", ["vocals"] = "vocal", ["длинн"] = "long", ["коротк"] = "short"
    };

    internal static IReadOnlyList<MusicOffer> Find(IReadOnlyList<LocalMusicCard> cards, IslandVoiceAction mode,
        string query, LocalMusicTrack? current, CancellationToken token)
    {
        if (mode == IslandVoiceAction.FindSimilar) return Similar(cards, current, token);
        if (mode == IslandVoiceAction.FindDescription) return Described(cards, query, token);

        var term = new SearchName(query);
        if (term.Raw.Length < 2) return [];
        var scored = new List<(LocalMusicCard Card, int Title, int Artist, int Album)>();
        foreach (var card in cards)
        {
            token.ThrowIfCancellationRequested();
            string parent = Path.GetFileName(Path.GetDirectoryName(card.Track.Path)) ?? "";
            int title = Math.Max(Match(term, card.Title), Match(term, card.Track.Name) - 2);
            int artist = Math.Max(Match(term, card.Artist), ArtistAbbreviation(term, card.Artist));
            int album = Math.Max(Match(term, card.Album), Match(term, parent) - 7);
            if (card.Artist.Length == 0 && card.Track.Name.Split(" - ", 2) is [var prefix, _])
                artist = Math.Max(artist, Match(term, prefix) - 3);
            scored.Add((card, title, artist, album));
        }

        int bestTitle = scored.Select(item => item.Title).DefaultIfEmpty().Max();
        int bestArtist = scored.Select(item => item.Artist).DefaultIfEmpty().Max();
        int bestAlbum = scored.Select(item => item.Album).DefaultIfEmpty().Max();
        if (mode == IslandVoiceAction.FindArtist || mode == IslandVoiceAction.FindTrack && bestArtist >= 84 && bestArtist > bestTitle)
            return Groups(scored.Select(item => (item.Card, Label: ArtistName(item.Card), Score: item.Artist)));
        if (mode == IslandVoiceAction.FindAlbum || mode == IslandVoiceAction.FindTrack && bestAlbum >= 89 && bestAlbum > bestTitle)
            return Groups(scored.Select(item =>
            {
                string album = item.Card.Album.Length > 0 ? item.Card.Album : Path.GetFileName(Path.GetDirectoryName(item.Card.Track.Path)) ?? "";
                return (item.Card, Label: album, Score: item.Album);
            }), byAlbum: true);
        return Singles(scored.Select(item => (item.Card, Score: item.Title)), 74, 9);
    }

    private static IReadOnlyList<MusicOffer> Groups(IEnumerable<(LocalMusicCard Card, string Label, int Score)> found, bool byAlbum = false)
    {
        var groups = found.Where(item => item.Score >= 74 && item.Label.Length > 0)
            .GroupBy(item => byAlbum
                ? Normalize(item.Label) + "|" + Normalize(item.Card.Artist)
                : Normalize(item.Label), StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var queue = group.OrderBy(item => item.Card.Number <= 0 ? int.MaxValue : item.Card.Number)
                    .ThenBy(item => item.Card.Track.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(item => item.Card.Track).ToArray();
                string label = byAlbum && first.Card.Artist.Length > 0 ? $"{first.Label} · {first.Card.Artist}" : first.Label;
                return (Offer: new MusicOffer(label, queue), Score: group.Max(item => item.Score));
            }).OrderByDescending(item => item.Score).ThenBy(item => item.Offer.Label, StringComparer.Ordinal).Take(3).ToArray();
        return groups.Length == 0 ? [] : groups.Where(item => groups[0].Score - item.Score <= 6).Select(item => item.Offer).ToArray();
    }

    private static IReadOnlyList<MusicOffer> Described(IReadOnlyList<LocalMusicCard> cards, string query, CancellationToken token)
    {
        var terms = DescriptionWords(query).ToArray();
        if (terms.Length < 2) return [];
        var ranked = new List<(LocalMusicCard Card, int Score)>();
        foreach (var card in cards)
        {
            token.ThrowIfCancellationRequested();
            string subject = $"{card.Title} {card.Artist} {card.Genre} {card.Description} {card.Track.Name}";
            var words = DescriptionWords($"{subject} {card.Lyrics}").ToHashSet(StringComparer.Ordinal);
            string artist = string.Join("", Normalize(card.Artist).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => word[0]));
            if (artist.Length > 1) words.Add(artist);
            if (card.Seconds >= 360) words.Add("long");
            if (card.Seconds is > 0 and < 180) words.Add("short");
            int hits = terms.Count(term => words.Contains(term) || words.Any(word => word.Length >= 4 && term.Length >= 4
                && (word.StartsWith(term, StringComparison.Ordinal) || term.StartsWith(word, StringComparison.Ordinal))));
            if (hits < 2) continue;
            int score = 100 * hits / terms.Length;
            if (Normalize(card.Lyrics).Contains(Normalize(query), StringComparison.Ordinal)) score += 6;
            if (Normalize(subject).Contains(Normalize(query), StringComparison.Ordinal)) score += 8;
            ranked.Add((card, score));
        }
        return Singles(ranked, 60, 8);
    }

    private static IReadOnlyList<MusicOffer> Similar(IReadOnlyList<LocalMusicCard> cards,
        LocalMusicTrack? current, CancellationToken token)
    {
        var seed = cards.FirstOrDefault(card => string.Equals(card.Track.Path, current?.Path, StringComparison.OrdinalIgnoreCase));
        if (seed is null) return [];
        var profile = DescriptionWords(seed.Genre + " " + seed.Description).ToHashSet(StringComparer.Ordinal);
        if (profile.Count == 0) return [];
        var ranked = new List<(LocalMusicCard Card, int Score)>();
        foreach (var card in cards)
        {
            token.ThrowIfCancellationRequested();
            if (ReferenceEquals(card, seed)) continue;
            var features = DescriptionWords(card.Genre + " " + card.Description).ToHashSet(StringComparer.Ordinal);
            int overlap = features.Count(profile.Contains);
            if (overlap == 0) continue;
            int score = 100 * (2 * overlap) / (features.Count + profile.Count);
            if (seed.Seconds > 0 && card.Seconds > 0 && Math.Abs(seed.Seconds - card.Seconds) < 45) score += 4;
            ranked.Add((card, score));
        }
        return Singles(ranked, 33, 12);
    }

    private static IReadOnlyList<MusicOffer> Singles(IEnumerable<(LocalMusicCard Card, int Score)> found, int threshold, int difference)
    {
        var best = found.Where(item => item.Score >= threshold).OrderByDescending(item => item.Score)
            .ThenBy(item => item.Card.Track.Path, StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        if (best.Length == 0) return [];
        return best.Where(item => best[0].Score - item.Score <= difference).Select(item =>
        {
            string label = item.Card.Label;
            if (best.Count(other => other.Card.Label == label) > 1)
                label = $"{label} · {Path.GetFileName(Path.GetDirectoryName(item.Card.Track.Path))} · {item.Card.Track.Source switch
                { LocalMusicSource.Collection => Localization.Text("Music made with LOPATA"), _ => Localization.Text("Music folder") }}";
            return new MusicOffer(label, [item.Card.Track]);
        }).ToArray();
    }

    internal static int MatchText(string query, string candidate) => Match(new SearchName(query), candidate);

    private static int Match(SearchName query, string value)
    {
        var candidate = new SearchName(value);
        if (candidate.Raw.Length == 0 || query.Raw.Length == 0) return 0;
        if (candidate.Raw == query.Raw) return 100;
        if (candidate.Latin == query.Latin && query.Latin.Length > 1) return 98;
        if (query.Sound.Length >= 3 && candidate.Sound == query.Sound) return 94;
        if (query.Raw.Length >= 4 && (" " + candidate.Raw + " ").Contains(" " + query.Raw + " ", StringComparison.Ordinal))
            return 92;
        if (query.Raw.Length < 3) return 0;
        return Math.Max(EditScore(query.Latin, candidate.Latin), EditScore(query.Sound, candidate.Sound) - 4);
    }

    private static int ArtistAbbreviation(SearchName query, string artist)
    {
        var words = Normalize(artist).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return 0;
        string shortName = string.Concat(words.Select(word => word[0]));
        return shortName == query.Raw ? 93 : 0;
    }

    private static string ArtistName(LocalMusicCard card) => card.Artist.Length > 0 ? card.Artist :
        card.Track.Name.Split(" - ", 2) is [var artist, _] ? artist : "";

    private static int EditScore(string first, string second)
    {
        int max = Math.Max(first.Length, second.Length);
        if (max is < 3 or > 200 || Math.Abs(first.Length - second.Length) > max / 3) return 0;
        Span<int> row = stackalloc int[second.Length + 1];
        for (int j = 0; j <= second.Length; j++) row[j] = j;
        for (int i = 1; i <= first.Length; i++)
        {
            int previous = row[0]++;
            for (int j = 1; j <= second.Length; j++)
            {
                int old = row[j];
                row[j] = Math.Min(Math.Min(row[j - 1], old) + 1, previous + (first[i - 1] == second[j - 1] ? 0 : 1));
                previous = old;
            }
        }
        return 100 - 100 * row[^1] / max;
    }

    internal static string Normalize(string value) => Spaces.Replace(string.Concat(value.ToLowerInvariant().Replace('ё', 'е')
        .Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)), " ").Trim();

    private static IEnumerable<string> DescriptionWords(string value) => Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(word => word.Length >= 3 && !DescriptionFillers.Contains(word))
        .Select(word => RussianEndings.Replace(word, ""))
        .Select(word => FeatureAliases.GetValueOrDefault(word, word)).Distinct(StringComparer.Ordinal);

    private sealed class SearchName
    {
        private string? sound;
        internal SearchName(string value)
        {
            Raw = Normalize(value);
            Latin = string.Concat(Raw.Select(c => c is >= 'а' and <= 'я' ? Cyrillic[c - 'а'] : c.ToString()));
        }

        internal string Raw { get; }
        internal string Latin { get; }
        internal string Sound => sound ??= Regex.Replace(Regex.Replace(Latin.Replace("sch", "s").Replace("sh", "s")
            .Replace("ph", "f").Replace("yu", "u"), "ai|ay|ei|ey", "i"), @"([a-z])\1+", "$1").Replace(" ", "");
    }
}

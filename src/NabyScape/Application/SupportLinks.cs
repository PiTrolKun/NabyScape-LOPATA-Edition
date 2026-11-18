using System;
namespace NabyScape;

// Voluntary donations only. NabyScape stays free; nothing is unlocked, tracked or sent.
// Links open in the default browser; the donation pages belong to Saweria and Ko-fi.
internal static class SupportLinks
{
    internal const string Saweria="https://saweria.co/billynabil";
    internal const string KoFi="https://ko-fi.com/billynabil";
    internal const string DonationHeading = "Support the original NabyScape developer";
    internal const string DonationExplanation = "Donations go to the original NabyScape developer as thanks for the original idea. They do not affect development of the island in LOPATA.";
    internal static void Open(string url,Action<Exception>? log=null)
    {
        try{ShortcutService.Open(new(){Target=url});}catch(Exception ex){log?.Invoke(ex);}
    }
}

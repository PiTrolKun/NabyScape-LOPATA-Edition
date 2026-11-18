namespace NabyScape;

public sealed partial class SettingsWindow
{
    private void LopataChatSettings()
    {
        page.Children.Add(Section("LOPATA chat",
            UI.Caption("When LOPATA is closed, the island uses its installed local core. When LOPATA is open, the chat hands your draft to If You Need to Vent.", 12, UI.Muted),
            UI.Row(UI.CaptionButton("Open chat", app.ShowLopataChat, true), UI.CaptionButton(LopataEdition.OpenCaption, LopataEdition.OpenHost))));
    }
}

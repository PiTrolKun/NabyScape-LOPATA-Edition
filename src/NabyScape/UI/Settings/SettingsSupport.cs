using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    // Donation buttons: Saweria for QRIS/e-wallets in Indonesia, Ko-fi for cards and PayPal elsewhere.
    private Expander AboutSupport()
    {
        var note = UI.Caption(SupportLinks.DonationExplanation, 12, UI.Muted);
        var links = new WrapPanel { Margin = new(0, 10, 0, 0) };
        links.Children.Add(DonationButton("Donate with Saweria", SupportLinks.Saweria,
            "Opens saweria.co · QRIS, e-wallets and Indonesian banks"));
        links.Children.Add(DonationButton("Donate with Ko-fi", SupportLinks.KoFi, "Opens ko-fi.com · cards and PayPal"));

        var details = new StackPanel { Margin = new(16, 4, 0, 0) };
        System.Windows.Documents.TextElement.SetFontWeight(details, FontWeights.Normal);
        details.Children.Add(note);
        details.Children.Add(links);
        var support = new Expander
        {
            Header = Localization.Text(SupportLinks.DonationHeading),
            Content = details,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new(0, 8, 0, 0),
            IsExpanded = false
        };
        UI.LocalizedName(support, SupportLinks.DonationHeading);
        return support;
    }

    private Button DonationButton(string caption, string url, string hint)
    {
        var button = UI.CaptionButton(caption, () => SupportLinks.Open(url, app.Store.Log));
        button.ToolTip = Localization.Text(hint);
        button.Padding = new(16, 8, 16, 8);
        button.Margin = new(0, 0, 8, 6);
        button.Tag = new CornerRadius(16);
        button.Background = Brushes.Transparent;
        button.BorderBrush = SettingsTheme.Line;
        button.BorderThickness = new(1);
        return button;
    }

    private Border HomeSupportCard()
    {
        var support = AboutSupport();
        support.Margin = new(0);
        return new Border
        {
            Background = SettingsTheme.Card,
            BorderBrush = SettingsTheme.LineSoft,
            BorderThickness = new(1),
            CornerRadius = new(14),
            Padding = new(16, 8, 16, 8),
            Margin = new(0, 10, 0, 10),
            Child = support
        };
    }
}

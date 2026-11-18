using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal sealed class LopataMascotView : FrameworkElement
{
    private static readonly Lazy<MascotArt> Art = new(() =>
    {
        var monitor = new BitmapImage();
        monitor.BeginInit();
        monitor.UriSource = new Uri("pack://application:,,,/LopataIsland;component/Assets/lopata-mascot.png");
        monitor.DecodePixelWidth = 288;
        monitor.CacheOption = BitmapCacheOption.OnLoad;
        monitor.EndInit();
        monitor.Freeze();
        return new MascotArt(monitor);
    });
    private readonly DrawingVisual drawing = new();
    private MascotFrame frame;

    internal LopataMascotView(bool compact)
    {
        Compact = compact;
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        AddVisualChild(drawing);
        SizeChanged += (_, _) => DrawPose();
    }

    internal bool Compact { get; }
    internal MascotFrame Frame => frame;
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? drawing : throw new ArgumentOutOfRangeException(nameof(index));

    internal void Present(MascotFrame value)
    {
        if (frame == value) return;
        frame = value;
        DrawPose();
    }

    private void DrawPose()
    {
        using var dc = drawing.RenderOpen();
        if (ActualWidth < 1 || ActualHeight < 1) return;
        if (Compact)
        {
            double zoom = Math.Min(ActualWidth / 58, ActualHeight / 34);
            Art.Value.DrawCompact(dc, frame, new Point(ActualWidth / 2 - 2 * zoom, ActualHeight / 2), zoom);
        }
        else
        {
            double zoom = Math.Min(ActualWidth / 324, ActualHeight / 282);
            Art.Value.DrawFull(dc, frame, new Point(ActualWidth / 2, ActualHeight / 2 - 18 * zoom), zoom);
        }
    }
}

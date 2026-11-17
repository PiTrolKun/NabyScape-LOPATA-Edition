using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NabyScape;

// Native scrolling and keyboard behavior remain owned by WPF; only its chrome fades.
public static class ScrollChrome
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(ScrollChrome),new PropertyMetadata(false,Changed));
    public static readonly DependencyProperty FadeEdgesProperty=DependencyProperty.RegisterAttached(
        "FadeEdges", typeof(bool), typeof(ScrollChrome),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, FadeEdgesChanged));

    public static bool GetFadeEdges(DependencyObject value) => (bool)value.GetValue(FadeEdgesProperty);
    public static void SetFadeEdges(DependencyObject value, bool enabled) => value.SetValue(FadeEdgesProperty, enabled);
    private static void FadeEdgesChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is ScrollViewer viewer && viewer.GetValue(StateProperty) is State state) state.RefreshEdges();
    }

    private static readonly DependencyProperty StateProperty=DependencyProperty.RegisterAttached("State",typeof(State),typeof(ScrollChrome));
    public static bool GetEnabled(DependencyObject value)=>(bool)value.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject value,bool enabled)=>value.SetValue(EnabledProperty,enabled);
    private static void Changed(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {if(target is not ScrollViewer viewer)return;if((bool)args.NewValue){if(viewer.GetValue(StateProperty)==null)viewer.SetValue(StateProperty,new State(viewer));}else if(viewer.GetValue(StateProperty) is State state){state.Dispose();viewer.ClearValue(StateProperty);}}
    private sealed class State : IDisposable
    {
        private readonly ScrollViewer viewer;
        private readonly DispatcherTimer idle=new(){Interval=TimeSpan.FromMilliseconds(750)};
        private ScrollContentPresenter? maskedPresenter;
        private LinearGradientBrush? edgeMask;
        private (bool Top, bool Bottom, double Band) edges;

        private System.Windows.Controls.Primitives.ScrollBar? Bar=>viewer.Template?.FindName("PART_VerticalScrollBar",viewer) as System.Windows.Controls.Primitives.ScrollBar;
        public State(ScrollViewer viewer)
        {this.viewer=viewer;viewer.ScrollChanged+=Scrolled;viewer.MouseLeave+=Left;viewer.LostMouseCapture+=Left;viewer.Unloaded+=Unloaded;viewer.IsVisibleChanged+=VisibilityChanged;viewer.Loaded+=Loaded;viewer.SizeChanged+=Resized;idle.Tick+=Idle;}
        private void Show(){if(!viewer.IsLoaded || !viewer.IsVisible)return;if(Bar is {} bar)Motion.Fade(bar,.65,120);idle.Stop();idle.Start();}
        private void Scrolled(object sender, ScrollChangedEventArgs args)
        {
            if (!ReferenceEquals(args.OriginalSource, viewer)) return;
            if (args.VerticalChange != 0) Show();
            RefreshEdges();
        }

        private void Loaded(object sender, RoutedEventArgs args) => RefreshEdges();
        private void Resized(object sender, SizeChangedEventArgs args) => RefreshEdges();

        internal void RefreshEdges()
        {
            if (!GetFadeEdges(viewer) || Accessibility.HighContrast || !viewer.IsVisible ||
                viewer.ScrollableHeight <= 0.5 ||
                viewer.Template?.FindName("PART_ScrollContentPresenter", viewer) is not ScrollContentPresenter presenter ||
                presenter.ActualHeight < 1)
            {
                ClearEdges();
                return;
            }

            var next = (Top: viewer.VerticalOffset > 0.5,
                Bottom: viewer.ScrollableHeight - viewer.VerticalOffset > 0.5,
                Band: Math.Min(0.22, 16 / presenter.ActualHeight));
            if (maskedPresenter == presenter && edges == next) return;
            ClearEdges();
            if (presenter.OpacityMask is not null || (!next.Top && !next.Bottom)) return;

            var mask = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(0, 1) };
            mask.GradientStops.Add(new(next.Top ? Colors.Transparent : Colors.White, 0));
            if (next.Top) mask.GradientStops.Add(new(Colors.White, next.Band));
            if (next.Bottom) mask.GradientStops.Add(new(Colors.White, 1 - next.Band));
            mask.GradientStops.Add(new(next.Bottom ? Colors.Transparent : Colors.White, 1));
            mask.Freeze();
            presenter.OpacityMask = mask;
            maskedPresenter = presenter;
            edgeMask = mask;
            edges = next;
        }

        private void ClearEdges()
        {
            if (maskedPresenter is not null && maskedPresenter.OpacityMask == edgeMask)
                maskedPresenter.ClearValue(UIElement.OpacityMaskProperty);
            maskedPresenter = null;
            edgeMask = null;
        }
        private void Left(object sender,System.Windows.Input.MouseEventArgs args){idle.Stop();idle.Start();}
        private void Idle(object? sender,EventArgs args){idle.Stop();if(viewer.IsMouseCaptureWithin)return;if(Bar is {} bar)Motion.Fade(bar,0,280);}
        private void Hide(){idle.Stop();if(viewer.IsLoaded && Bar is {} bar){bar.BeginAnimation(UIElement.OpacityProperty,null);bar.Opacity=0;}}
        private void Unloaded(object sender,RoutedEventArgs args){Hide();ClearEdges();}
        private void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs args){if(!(bool)args.NewValue){Hide();ClearEdges();}else RefreshEdges();}
        public void Dispose(){Hide();ClearEdges();viewer.Loaded-=Loaded;viewer.SizeChanged-=Resized;viewer.ScrollChanged-=Scrolled;viewer.MouseLeave-=Left;viewer.LostMouseCapture-=Left;viewer.Unloaded-=Unloaded;viewer.IsVisibleChanged-=VisibilityChanged;idle.Tick-=Idle;}
    }
}

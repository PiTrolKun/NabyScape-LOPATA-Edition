using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private string? selectedClipId;
    private ScrollViewer? clipboardScroll;
    private StackPanel? clipboardHeader;
    private double clipboardOffset;
    private double clipboardWheelOffset=double.NaN;
    private void SizeClipboardViewport()
    {
        if(clipboardScroll==null || clipboardHeader==null || clipboardScroll.Parent!=clipboardHeader || toolsMode!="clips" || activePage is not ("clips" or "tools"))return;
        var screen=Native.ChooseScreen(app.Settings);double scale=app.Settings.PanelScale;
        double available=Math.Min(app.Settings.PanelMaxHeight,screen.WorkingArea.Height/Native.Scale(screen)-16);
        double reserved=PanelFramePadding/scale;
        foreach(FrameworkElement child in clipboardHeader.Children)if(child!=clipboardScroll){child.Measure(new Size(bodyWidth,double.PositiveInfinity));reserved+=child.DesiredSize.Height;}
        foreach(FrameworkElement child in body.Children)if(child!=compactPageHost){child.Measure(new Size(bodyWidth,double.PositiveInfinity));reserved+=child.DesiredSize.Height;}
        clipboardScroll.MaxHeight=Math.Max(36,available/scale-reserved-8);
        // Measuring the header for its budget must not leave a cached page size
        // from the previous viewport width in the outer ScrollViewer.
        clipboardHeader.InvalidateMeasure();if(clipboardHeader.Parent is UIElement owner)owner.InvalidateMeasure();compactPageHost.InvalidateMeasure();body.InvalidateMeasure();
    }
    private void BuildSmartClipboard(StackPanel panel)
    {
        var p=app.Productivity;
        if(clipboardScroll?.IsVisible==true)clipboardOffset=clipboardScroll.VerticalOffset;
        clipboardScroll=null;clipboardWheelOffset=double.NaN;clipboardHeader=panel;
        var heading=new DockPanel();
        var count=UI.Text(Localization.Format("{0} items",p.Clips.Count),10,UI.Muted);
        count.Margin=new(8,0,0,0);DockPanel.SetDock(count,Dock.Right);heading.Children.Add(count);
        heading.Children.Add(WithToolHomeWidgetStar(UI.Heading(Localization.Text("Smart clipboard"),18), "clips"));
        panel.Children.Add(heading);
        if(!p.ClipboardEnabled)
        {
            selectedClipId=null;
            bool requested=app.Settings.ClipboardHistory && p.LastError is not null;
            var intro=UI.Caption(requested?"Could not start clipboard history. Select Retry to try again.":"History is off. Enable it to save the text and images you copy next, only on this device.",12,UI.Muted);
            intro.Margin=new(0,10,0,12);panel.Children.Add(intro);
            panel.Children.Add(UI.CaptionButton(requested?"Retry":"Enable history",()=>{app.Settings.ClipboardHistory=true;app.ChangeSettings();},true));return;
        }
        var pause=new CheckBox{Content=Localization.Text("Pause capture"),IsChecked=p.ClipboardPaused,Style=(Style)FindResource("SettingsSwitch"),MinHeight=28,Margin=new(0)};UI.Named(pause,Localization.Text("Pause capture"));
        pause.Click+=(_,_)=>p.ClipboardPaused=pause.IsChecked==true;
        var more=MinimalWidgets.More("Clipboard actions",("Clear history",()=>MutateUtility(p.ClearClips)),("Turn off",()=>{app.Settings.ClipboardHistory=false;app.ChangeSettings();}));
        var toolbar=new DockPanel{Margin=new(0,4,0,0)};
        DockPanel.SetDock(more,Dock.Right);toolbar.Children.Add(more);toolbar.Children.Add(pause);
        toolbar.SizeChanged+=(_,_)=>{if(toolsMode=="clips" && clipboardHeader==panel){SizeClipboardViewport();Dispatcher.BeginInvoke(()=>{if(expanded && toolsMode=="clips")ResizeToolPanel();});}};
        panel.Children.Add(toolbar);
        var captureStatus=UI.Caption(p.ClipboardPaused?"Capture is paused. Turn off Pause capture to save new items.":"History is on. Copy text, a link or an image with Ctrl+C to see it here.",11,UI.Muted);
        captureStatus.Margin=new(0,6,0,5);panel.Children.Add(captureStatus);
        Button ActionButton(string text,Action action)
        {
            var button=UI.Button(text,action);button.FontSize=10;button.Padding=new(9,5,9,5);button.MinHeight=27;button.Margin=new(0,3,6,3);return button;
        }
        var content=new TileFlow{MinimumTileWidth=152,Gap=16};clipboardScroll=UI.Scroll(content);clipboardScroll.Tag="panel-content-scroll";clipboardScroll.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;clipboardScroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;clipboardScroll.PanningMode=PanningMode.VerticalOnly;clipboardScroll.Margin=new(0,5,0,0);UI.Named(clipboardScroll,Localization.Text("Clipboard history"));panel.Children.Add(clipboardScroll);clipboardThumbnails=new(clipboardScroll);
        clipboardScroll.PreviewMouseWheel+=(_,e)=>{if((System.Windows.Input.Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Shift)!=0)return;ScrollClipboardWheel(e.Delta);e.Handled=true;};
        var historyViewport=clipboardScroll;double restoreOffset=clipboardOffset;
        historyViewport.ScrollChanged+=(_,e)=>{if(e.VerticalChange!=0 && historyViewport.IsVisible && ReferenceEquals(historyViewport,clipboardScroll))clipboardWheelOffset=clipboardOffset=historyViewport.VerticalOffset;};
        historyViewport.Loaded+=(_,_)=>historyViewport.ScrollToVerticalOffset(restoreOffset);
        var clips=p.Clips.OrderByDescending(c=>c.Pinned).ThenByDescending(c=>c.Timestamp).ToArray();
        if(!clips.Any(c=>c.Id==selectedClipId))selectedClipId=null;
        foreach(var item in clips)content.Children.Add(ClipboardTile(item,clipboardThumbnails));
        SizeClipboardViewport();
        if(clips.Length==0)return;
        var selected=clips.FirstOrDefault(c=>c.Id==selectedClipId);if(selected==null)return;
        var detail=SmartClipboard.Classify(selected);
        var section=new StackPanel{Tag="clip-detail",Margin=new(0,12,0,0)};content.Children.Insert(0,section);
        section.Children.Add(UI.Text(detail.Kind+" · "+selected.Timestamp.ToLocalTime().ToString("g"),10,UI.Muted));
        var status=UI.Text("",10,UI.Muted);var actions=new WrapPanel{Margin=new(0,5,0,0)};
        void Copy(string? variant=null){status.Text=p.TryCopyClip(selected,variant)?Localization.Text("Copied."):Localization.Text("Could not copy. The clipboard may be busy; try again.");ResizeToolPanel();}
        void Preview(string title,string text)
        {
            var preview=UI.Input(title,text);preview.IsReadOnly=true;preview.AcceptsReturn=true;preview.TextWrapping=TextWrapping.Wrap;preview.MaxHeight=100;preview.MinHeight=38;preview.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;preview.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;preview.Margin=new(0,6,0,3);section.Children.Add(preview);
        }
        if(detail.Kind==SmartClipKind.Image)
        {
            if(p.LoadClipImage(selected,(int)Math.Ceiling(Math.Min(bodyWidth,400)*VisualTreeHelper.GetDpi(this).DpiScaleX)) is { } image)section.Children.Add(new Image{Source=image,MaxHeight=112,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,7,0,4)});
            actions.Children.Add(ActionButton("Copy image",()=>Copy()));
            actions.Children.Add(ActionButton("Save as PNG…",()=>
            {
                try
                {
                    var dialog=new Microsoft.Win32.SaveFileDialog{Title="Save clipboard image",Filter="PNG image (*.png)|*.png",DefaultExt=".png",AddExtension=true,FileName="Clipboard image.png",OverwritePrompt=true};
                    if(dialog.ShowDialog(this)==true)status.Text=p.SaveClipImage(selected,dialog.FileName)?Localization.Text("Image saved."):Localization.Text("Could not save image. Check the location and try again.");
                }
                catch(Exception ex){app.Store.Log(ex);status.Text=Localization.Text("Could not save image. Try another location.");}
                ResizeToolPanel();
            }));
        }
        else
        {
            Preview(detail.Kind==SmartClipKind.Link?"Original link":"Original text",selected.Text);
            actions.Children.Add(ActionButton(detail.Kind==SmartClipKind.Link?"Copy original":"Copy",()=>Copy()));
            if(detail.Kind==SmartClipKind.Text)actions.Children.Add(ActionButton("Copy plain text",()=>Copy(selected.Text)));
            if(detail.Kind==SmartClipKind.Link)
            {
                var hint=UI.Text(Localization.Text("Cleaned preview · review before copying"),10,UI.Muted);hint.Margin=new(0,6,0,0);section.Children.Add(hint);Preview("Cleaned link preview",detail.CleanedLink);
                actions.Children.Add(ActionButton("Copy cleaned link",()=>Copy(detail.CleanedLink)));
            }
            if(detail.Kind==SmartClipKind.Color)
            {
                section.Children.Add(new Border{Height=24,Background=UI.Brush(detail.Hex),CornerRadius=new(4),Margin=new(0,5,0,5)});
                section.Children.Add(UI.Text(detail.Hex+" · "+detail.Rgb,11,UI.Muted));
                actions.Children.Add(ActionButton("Copy HEX",()=>Copy(detail.Hex)));actions.Children.Add(ActionButton("Copy RGB",()=>Copy(detail.Rgb)));
            }
        }
        actions.Children.Add(ActionButton(selected.Pinned?"Unpin":"Pin",()=>MutateUtility(()=>p.ToggleClipPin(selected.Id))));
        actions.Children.Add(ActionButton("Remove",()=>MutateUtility(()=>p.RemoveClip(selected.Id))));var selectedTools=new StackPanel();
        var selectedHeader=new DockPanel();var close=Icons.Button("x","Close clipboard selection",()=>{selectedClipId=null;RefreshTools();ResizeToolPanel();},false,24);close.Background=Brushes.Transparent;close.Margin=new(0);DockPanel.SetDock(close,Dock.Right);selectedHeader.Children.Add(close);selectedHeader.Children.Add(UI.Text("Selected "+detail.Kind.ToString().ToLowerInvariant(),10,UI.Muted));selectedTools.Children.Add(selectedHeader);
        selectedTools.Children.Add(actions);selectedTools.Children.Add(status);
        var selectedBar=new Border{Child=selectedTools,Background=UI.Brush("#14171C"),BorderBrush=UI.Brush("#2D333D"),BorderThickness=new(.7),CornerRadius=new(10),Padding=new(9,5,9,5),Margin=new(0,7,0,5)};UI.Named(selectedBar,Localization.Text("Clipboard selection actions"));
        panel.Children.Insert(panel.Children.IndexOf(clipboardScroll),selectedBar);selectedBar.SizeChanged+=(_,_)=>{if(clipboardHeader==panel){SizeClipboardViewport();Dispatcher.BeginInvoke(()=>{if(expanded && toolsMode=="clips")ResizeToolPanel();});}};SizeClipboardViewport();
    }
    internal bool ScrollClipboardWheel(int delta)
    {
        if(clipboardScroll?.IsVisible!=true)return false;
        double distance=SystemParameters.WheelScrollLines<0?clipboardScroll.ViewportHeight:SystemParameters.WheelScrollLines*16;
        // ScrollViewer applies queued offsets during layout. Accumulate deltas so
        // several trackpad events in one frame do not replace each other.
        double from=double.IsFinite(clipboardWheelOffset)?clipboardWheelOffset:clipboardScroll.VerticalOffset;
        clipboardWheelOffset=Math.Clamp(from-delta/120d*distance,0,clipboardScroll.ScrollableHeight);
        clipboardScroll.ScrollToVerticalOffset(clipboardWheelOffset);return true;
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace TechLoungeOS;

public partial class MainWindow
{
    private readonly ObservableCollection<ActivityEntry> activityEntries=[];
    private readonly System.Text.StringBuilder diagnosticBuffer=new();
    private Window? diagnosticWindow;
    private TextBox? diagnosticView;
    internal IReadOnlyList<ActivityEntry> ActivityEntries=>activityEntries;
    internal string DiagnosticText=>diagnosticBuffer.ToString();
    private void InitialiseActivity()
    {
        ActivityItems.ItemsSource=activityEntries;
        var descriptor=DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));
        EventHandler changed=(_,_)=>RecordActivity(Status.Text);
        descriptor.AddValueChanged(Status,changed);
        Closed+=(_,_)=>descriptor.RemoveValueChanged(Status,changed);
        RecordActivity(Status.Text);
    }
    private void RecordActivity(string message)
    {
        if(string.IsNullOrWhiteSpace(message))return;
        var entry=ActivityEntry.FromStatus(message,running);
        if(activityEntries.FirstOrDefault() is { } previous && previous.Title==entry.Title && previous.Detail==entry.Detail && previous.State==entry.State)return;
        if(activityEntries.FirstOrDefault() is {State:"Working" or "Stopping"} active)
        {
            var state=entry.State is "Needs attention" or "Stopped"?"Stopped":entry.State=="Stopping"?"Stopping":"Done";
            activityEntries[0]=active with{State=state,Icon=state=="Done"?"\uE73E":"\uE711"};
        }
        activityEntries.Insert(0,entry);
        while(activityEntries.Count>40)activityEntries.RemoveAt(activityEntries.Count-1);
    }
    private void ActivityExpanded(object sender,RoutedEventArgs e)=>Dispatcher.BeginInvoke(
        System.Windows.Threading.DispatcherPriority.Loaded,new Action(ActivityScroll.ScrollToTop));
    private void ActivityRowLoaded(object sender,RoutedEventArgs e)
    {
        if(sender is FrameworkElement row && row.IsVisible && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            row.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(140)){FillBehavior=FillBehavior.Stop});
    }
    private void OpenDiagnosticLog(object sender,RoutedEventArgs e)
    {
        if(diagnosticWindow is not null){diagnosticWindow.Activate();return;}
        var window=new Window{Owner=this,Title="Diagnostic log",Width=840,Height=520,MinWidth=500,MinHeight=320,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background,Foreground=Foreground,FontFamily=FontFamily};
        var root=new DockPanel{Margin=new Thickness(20)};window.Content=root;
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
        var copy=new Button{Content="Copy log"};copy.Click+=(_,_)=>
        {
            try{Clipboard.SetText(DiagnosticText.Length==0?"No diagnostic output yet.":DiagnosticText);copy.Content="Copied";}
            catch(System.Runtime.InteropServices.COMException){copy.Content="Clipboard busy; try again";}
        };
        var close=new Button{Content="Close"};close.Click+=(_,_)=>window.Close();actions.Children.Add(copy);actions.Children.Add(close);
        var heading=new TextBlock{Text="Detailed output for troubleshooting and support.",Margin=new Thickness(0,0,0,12)};
        DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        var text=new TextBox{Text=DiagnosticText,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new System.Windows.Media.FontFamily("Consolas"),FontSize=12};
        System.Windows.Automation.AutomationProperties.SetName(text,"Diagnostic output");root.Children.Add(text);
        diagnosticWindow=window;diagnosticView=text;
        window.SourceInitialized+=(_,_)=>WindowsChrome.Apply(window);
        window.Closed+=(_,_)=>{diagnosticWindow=null;diagnosticView=null;};
        window.Show();text.ScrollToEnd();
    }
}

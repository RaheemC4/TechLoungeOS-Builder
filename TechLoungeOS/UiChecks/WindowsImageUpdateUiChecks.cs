using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TechLoungeOS;

internal static class WindowsImageUpdateUiChecks
{
    internal static void Run()
    {
        var window=new MainWindow(false);
        try
        {
            var editions=(ComboBox)window.FindName("Editions");
            var status=(TextBlock)window.FindName("WindowsVersionStatus");
            var check=(Button)window.FindName("WindowsVersionCheck");
            var update=(CheckBox)window.FindName("UpdateWindowsImage");
            var source=new Edition(1,"Windows 11 Pro","9","10.0.26200.9457");
            var release=new WindowsReleaseCatalog.Release("25H2",new Version(10,0,26200,9725),new DateOnly(2026,10,26),"2026-10 OOB","KB5134133");
            if(check.IsEnabled || update.IsChecked==true)throw new Exception("Image updates must not run or enable themselves without an explicit choice.");
            editions.ItemsSource=new[]{source};editions.SelectedIndex=0;
            Wait(window.CheckWindowsVersion((version,ct)=>{
                if(version!=new Version(10,0,26200,9457))throw new Exception("Wrong source version queried.");
                return Task.FromResult(release);
            }));
            if(!status.Text.Contains("26200.9725") || !status.Text.Contains("KB5134133") || !check.IsEnabled || update.IsChecked==true)
                throw new Exception("Metadata check did not present the exact update while preserving the user's build choice.");
            update.IsChecked=true;
            Wait(window.CheckWindowsVersion((_,_)=>Task.FromException<WindowsReleaseCatalog.Release>(new IOException("inert network failure"))));
            if(!status.Text.Contains("inert network failure") || !check.IsEnabled || update.IsChecked!=true)
                throw new Exception("A failed metadata check lost its error, controls or explicit update choice.");
            var pending=new TaskCompletionSource<WindowsReleaseCatalog.Release>();CancellationToken token=default;
            var operation=window.CheckWindowsVersion((_,ct)=>{token=ct;return pending.Task;});
            if(check.IsEnabled || ((Button)window.FindName("BuildButton")).IsEnabled)
                throw new Exception("A pending image-update check allowed a concurrent build.");
            ((Button)window.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(!token.IsCancellationRequested)throw new Exception("Cancel did not reach the metadata request.");
            pending.SetCanceled(token);Wait(operation);
            if(!status.Text.Contains("cancelled") || !check.IsEnabled)throw new Exception("Cancellation did not restore idle controls.");
            pending=new TaskCompletionSource<WindowsReleaseCatalog.Release>();
            operation=window.CheckWindowsVersion((_,_)=>pending.Task);
            ((Button)window.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            pending.SetResult(release);Wait(operation);
            if(!status.Text.Contains("cancelled"))throw new Exception("A late metadata result overwrote cancellation.");
            Wait(window.CheckWindowsVersion((_,_)=>Task.FromResult(release with{Version=new Version(10,0,28000,1)})));
            if(!status.Text.Contains("does not match"))throw new Exception("An unrelated Windows release was advertised as compatible.");
            editions.ItemsSource=new[]{source with{ImageIndex=2,Version="10.0.26200.9999"}};editions.SelectedIndex=0;
            if(!((TextBlock)window.FindName("WindowsCurrentDetails")).Text.Contains("26200.9999") || status.Text.Contains("KB5134133"))throw new Exception("Edition change retained stale update metadata.");
            Wait(window.CheckWindowsVersion((_,_)=>Task.FromResult(release)));
            if(!status.Text.Contains("up to date") || status.Text.Contains("An update is available"))throw new Exception("A newer source must not be offered a downgrade.");
            editions.ItemsSource=new[]{source with{Architecture="12"}};editions.SelectedIndex=0;
            bool called=false;Wait(window.CheckWindowsVersion((_,_)=>{called=true;return Task.FromResult(release);}));
            if(called || !status.Text.Contains("x64"))throw new Exception("Unsupported architecture reached update discovery.");
        }
        finally{window.Close();}
        Console.WriteLine("Compiled Windows-image update UI: exact metadata, explicit choice, cancellation, busy state, source changes, failures, architecture and no-downgrade checks passed. No network, packages or servicing.");
    }

    private static void Wait(Task task)
    {
        var deadline=DateTime.UtcNow.AddSeconds(5);
        while(!task.IsCompleted && DateTime.UtcNow<deadline)
        {
            var frame=new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));
            Dispatcher.PushFrame(frame);
        }
        if(!task.IsCompleted)throw new TimeoutException("UI metadata fixture did not settle.");
        task.GetAwaiter().GetResult();
    }
}

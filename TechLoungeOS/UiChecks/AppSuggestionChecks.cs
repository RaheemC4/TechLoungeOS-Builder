using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TechLoungeOS;

internal static class AppSuggestionChecks
{
    internal static void Run()
    {
        var window=new MainWindow(false);
        var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        var query=(TextBox)window.FindName("AppQuery");var rows=(StackPanel)window.FindName("SearchResults");
        var status=(TextBlock)window.FindName("SearchStatus");
        var selected=(List<CatalogApp>)typeof(MainWindow).GetField("selectedApps",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
        CatalogApp[] Items()=>rows.Children.OfType<AppChoice>().Select(r=>(CatalogApp)r.Tag).ToArray();
        try
        {
            int calls=0;
            Task<CatalogApp[]> Source(string text,CancellationToken ct){calls++;return Task.FromResult(new[]{new CatalogApp("Steam","Valve.Steam","Latest")});}
            query.Text="ste";var abandoned=window.SearchApps(true,Source);
            Require(Items().Single().Id=="Valve.Steam" && calls==0,"Known app suggestions appear immediately before any online lookup");
            Wait(Task.Delay(30));
            var first=rows.Children.OfType<AppChoice>().Single();
            var artwork=(Grid)((DockPanel)first.Content).Children[0];
            Require(artwork.Children.OfType<Image>().Single().Source!=null,"Suggestion displays the packaged Steam icon");
            first.IsChecked=true;Require(selected.Single().Id=="Valve.Steam","Clicking a suggestion selects the app without changing the query");
            first.IsChecked=false;Require(selected.Count==0 && query.Text=="ste","Clicking again deselects the suggestion");
            query.Text="chr";var latest=window.SearchApps(true,(_,_)=>Task.FromResult(new[]{new CatalogApp("Google Chrome","Google.Chrome","Latest")}));
            Wait(abandoned);Require(calls==0,"Typing another query cancels the previous debounce before it starts WinGet");
            Wait(latest);Require(Items().Single().Id=="Google.Chrome","Latest typed query supplies the displayed suggestions");
            var pending=new TaskCompletionSource<CatalogApp[]>();int active=0,maxActive=0;
            async Task<CatalogApp[]> Slow(string text,CancellationToken ct)
            {
                active++;maxActive=Math.Max(maxActive,active);
                try{return text=="ste"?await pending.Task:[new CatalogApp("Google Chrome","Google.Chrome","Latest")];}
                finally{active--;}
            }
            query.Text="ste";var old=window.SearchApps(false,Slow);
            query.Text="chr";var current=window.SearchApps(false,Slow);
            Require(active==1,"Only one source lookup runs at a time");
            pending.SetResult([new("Steam","Valve.Steam","Latest")]);Wait(old);Wait(current);
            Require(maxActive==1 && Items().Single().Id=="Google.Chrome","Out-of-order completion never replaces the newest query");
            query.Text="";Wait(window.SearchApps(true,Source));
            Require(rows.Children.Count==0 && ((FrameworkElement)window.FindName("SearchResultsSection")).Visibility==Visibility.Collapsed,"Clearing the query dismisses suggestions");
            query.Text="disco";Wait(window.SearchApps(true,(_,_)=>Task.FromException<CatalogApp[]>(new IOException("offline fixture"))));
            Require(Items().Single().Id==RecommendedApps.Discord && status.Text.Contains("kept"),"Offline lookup retains local Discord suggestion and its supported recipe");
            rows.Children.OfType<AppChoice>().Single().IsChecked=true;
            query.Text="chrome";Wait(window.SearchApps(false,(_,_)=>Task.FromResult(new[]{new CatalogApp("Discord PTB","Discord.Discord.PTB","Latest"),new CatalogApp("Microsoft Edge","Microsoft.Edge","Latest"),new CatalogApp("Google Chrome","Google.Chrome","Latest")})));
            Require(Items().Single().Id=="Google.Chrome" && selected.Single().Id==RecommendedApps.Discord,"Suggestions keep selection, exclude other Discord variants and hide mandatory apps");
            query.Text=new string('a',101);Wait(window.SearchApps(true,Source));Require(calls==0,"Invalid queries never reach the external source");
            Console.WriteLine("App suggestion fixtures passed: debounce, latest-result ownership, icons, toggle selection, offline fallback and protected app identities. No WinGet process or Windows servicing was executed.");
        }
        finally{window.Close();SynchronizationContext.SetSynchronizationContext(previous);}
    }
    internal static void RunNative(string folder)
    {
        Directory.CreateDirectory(folder);var window=new MainWindow(false);var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            ((TextBox)window.FindName("AppQuery")).Text="Steam";
            Wait(window.SearchApps(true));
            var rows=((StackPanel)window.FindName("SearchResults")).Children.OfType<AppChoice>().ToArray();
            var status=((TextBlock)window.FindName("SearchStatus")).Text;
            File.WriteAllText(Path.Combine(folder,"native-search.txt"),status+"\n"+string.Join("\n",rows.Select(r=>((CatalogApp)r.Tag).Id)));
            Require(status.Contains("result") && rows.Any(r=>((CatalogApp)r.Tag).Id=="Valve.Steam"),"Actual WinGet lookup populates Steam suggestions through the app");
            Console.WriteLine("Read-only native WinGet search passed. No app was installed and no ISO or drive was changed.");
        }
        finally{window.Close();SynchronizationContext.SetSynchronizationContext(previous);}
    }
    private static void Require(bool value,string message){if(!value)throw new IOException(message);Console.WriteLine("PASS: "+message);}
    private static void Wait(Task task)
    {
        while(!task.IsCompleted)
        {
            var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TechLoungeOS;

public partial class MainWindow
{
    private CancellationTokenSource? appSearch;
    private readonly SemaphoreSlim appSearchGate=new(1,1);
    private readonly List<CatalogApp> recentSearchApps=[];
    private int appSearchRevision;
    private async void AppQueryChanged(object sender,TextChangedEventArgs e)
    {
        if(IsLoaded)await SearchApps(true);
    }
    private async void AppQueryKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Enter){e.Handled=true;await SearchApps();}
        else if(e.Key==Key.Down && SearchResults.Children.OfType<AppChoice>().FirstOrDefault() is { } first)
        {
            e.Handled=true;SearchResultsSection.Visibility=Visibility.Visible;AppLibrarySection.Visibility=Visibility.Collapsed;
            AppPickerPopup.PlacementTarget=AppQuery;
            if(IsVisible && currentStep==2){AppPickerPopup.IsOpen=true;_ = Dispatcher.BeginInvoke(new Action(()=>first.Focus()));}
        }
        else if(e.Key==Key.Escape)
        {appSearchRevision++;appSearch?.Cancel();SearchResultsSection.Visibility=Visibility.Collapsed;AppPickerPopup.IsOpen=false;e.Handled=true;}
    }
    private async void SearchAppsClick(object sender,RoutedEventArgs e)=>await SearchApps();
    internal async Task SearchApps(bool suggest=false,Func<string,CancellationToken,Task<CatalogApp[]>>? search=null)
    {
        if(running || recoveryRequired)return;
        var revision=++appSearchRevision;appSearch?.Cancel();
        using var request=CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);appSearch=request;
        var query=AppQuery.Text.Trim();var ct=request.Token;
        bool Current()=>revision==appSearchRevision && !ct.IsCancellationRequested && !running && !recoveryRequired;
        try
        {
            SearchResultsHeading.Text=suggest?"Suggestions":"Search results";
            if(suggest)
            {
                var local=query.Length==0?[]:TechLoungeOS.RecommendedApps.All.Concat(recentSearchApps)
                    .Where(a=>a.Name.Contains(query,StringComparison.CurrentCultureIgnoreCase)||a.Id.Contains(query,StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(a=>a.Name.StartsWith(query,StringComparison.CurrentCultureIgnoreCase)).ThenBy(a=>a.Name)
                    .DistinctBy(a=>TechLoungeOS.RecommendedApps.Identity(a.Id),StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
                ShowSearchResults(local);
                SearchStatus.Text=query.Length==0?"Type an app name to see suggestions.":"Suggestions update as you type. Click an app to select or deselect it.";
                if(query.Length<2)return;
            }
            try{query=AppCatalog.SearchQuery(query);}
            catch(InvalidOperationException error){SearchStatus.Text=error.Message;return;}
            if(suggest)await Task.Delay(450,ct);
            SearchStatus.Text="Looking for matching apps…";
            // Serialise read-only WinGet searches. A stale process may finish, but cannot publish results.
            await appSearchGate.WaitAsync(ct);
            CatalogApp[] results;
            try{ct.ThrowIfCancellationRequested();results=await (search??AppCatalog.Search)(query,ct);}
            finally{appSearchGate.Release();}
            if(!Current())return;
            recentSearchApps.InsertRange(0,results);if(recentSearchApps.Count>150)recentSearchApps.RemoveRange(150,recentSearchApps.Count-150);
            ShowSearchResults(suggest?results.Take(8).ToArray():results);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){ }
        catch(Exception ex)
        {
            if(Current())
            {
                SearchStatus.Text="Couldn’t search online. Check your connection and App Installer. Existing suggestions and selections are kept.";
                Write("App search: "+ex.Message);
            }
        }
        finally{if(ReferenceEquals(appSearch,request))appSearch=null;}
    }
}

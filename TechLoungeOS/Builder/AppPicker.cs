using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TechLoungeOS;

public partial class MainWindow
{
    private readonly List<CatalogApp> selectedApps = [];
    private bool syncingAppChoices;
    private void EditionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BuildButton is not null) BuildButton.IsEnabled = !running && Editions.SelectedItem is Edition;
        if (SelectedImageLabel is not null && SelectedImageDetails is not null)
        {
            var edition = Editions.SelectedItem as Edition;
            SelectedImageLabel.Text = edition?.ImageName ?? "No image selected";
            SelectedImageDetails.Text = edition == null ? "Import a Windows 11 ISO to begin." :
                (Version.TryParse(edition.Version,out var version)?WindowsReleaseCatalog.DisplayName(version):"Version unavailable")+"\n"+
                (edition.Architecture=="9"?"64-bit":edition.Architecture)+" · build "+edition.Version;
        }
        RefreshApps();
        ResetWindowsVersionStatus();
    }
    private void ShowSearchResults(CatalogApp[] results)
    {
        results=TechLoungeOS.RecommendedApps.Selectable(results);
        var alreadyIncluded=results.Any(app=>TechLoungeOS.RecommendedApps.IncludedInIso(app.Id));
        results=results.Where(app=>!TechLoungeOS.RecommendedApps.IncludedInIso(app.Id)).ToArray();
        SearchStatus.Text=results.Length==0?(alreadyIncluded?"Already included in every ISO. No need to add it.":"No matching apps. Try another name."):$"{results.Length} {(results.Length==1?"result":"results")}. Check the package ID before adding.";
        SearchResults.Children.Clear();
        SearchResultsSection.Visibility=results.Length==0?Visibility.Collapsed:Visibility.Visible;
            foreach (var app in results)
                SearchResults.Children.Add(SelectableRow(app));
        RefreshApps();
    }
    private void SearchResultBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // Selecting a card must not scroll the nested results into the outer page.
        // Keep native focus-following behaviour for Tab/keyboard navigation.
        if (InputManager.Current.MostRecentInputDevice is MouseDevice) e.Handled = true;
    }
    private void ShowRecommendations()
    {
        if(RecommendedApps is null)return;
        RecommendedApps.Children.Clear();
        var recommended=AppCategory.SelectedIndex!=1;
        foreach (var app in TechLoungeOS.RecommendedApps.All.Where(a=>!TechLoungeOS.RecommendedApps.IncludedInIso(a.Id) && (!recommended || TechLoungeOS.RecommendedApps.PresetIds.Contains(a.Id))))
            RecommendedApps.Children.Add(SelectableRow(app,true));
        RefreshApps();
    }
    private void AppCategoryChanged(object sender,SelectionChangedEventArgs e)=>ShowRecommendations();
    private AppChoice SelectableRow(CatalogApp app,bool compact=false)
    {
        var button = AppRow(app,compact);
        void Changed(object sender,RoutedEventArgs e) {
                    if(syncingAppChoices)return;
                    if (running||TechLoungeOS.RecommendedApps.IncludedInIso(app.Id)||AppCatalog.ExplicitArmVariant(app)||TechLoungeOS.RecommendedApps.UnsupportedDiscord(app.Id)){RefreshApps();return;}
                    selectedApps.RemoveAll(a => TechLoungeOS.RecommendedApps.Identity(a.Id).Equals(TechLoungeOS.RecommendedApps.Identity(app.Id), StringComparison.OrdinalIgnoreCase));
                    if(button.IsChecked==true)selectedApps.Add(app);
                    RefreshApps();
        }
        button.Checked+=Changed;button.Unchecked+=Changed;
        return button;
    }
    private static AppChoice AppRow(CatalogApp app,bool compact=false)
    {
        var choice=new AppChoice{Tag=app};
        if(compact){choice.MinHeight=46;choice.Padding=new Thickness(8,6,8,6);choice.Margin=new Thickness(0,3,8,3);}
        System.Windows.Automation.AutomationProperties.SetName(choice,app.Name);
        System.Windows.Automation.AutomationProperties.SetHelpText(choice,app.Details);
        var row = new DockPanel { MinHeight = compact?26:40 };
        var artwork = new Grid { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center };
        var tile = new Border { CornerRadius = new CornerRadius(4), ToolTip = "App artwork unavailable" };
        tile.SetResourceReference(Border.BackgroundProperty, "Tint");
        var placeholder = new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse("M2,3 H22 V21 H2 Z M2,8 H22 M5,5.5 H7 M9,5.5 H11"),
            StrokeThickness = 1.3, Width = 20, Height = 18, Stretch = System.Windows.Media.Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        placeholder.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted"); tile.Child = placeholder;
        artwork.Children.Add(tile);
        row.Children.Add(artwork);
        _ = LoadArtwork(app, artwork);
        var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = app.Name, FontWeight = FontWeights.SemiBold, TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis });
        var details = new TextBlock { Text = app.Details, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        details.SetResourceReference(TextBlock.ForegroundProperty, "Muted");if(!compact)text.Children.Add(details);
        row.Children.Add(text);choice.Content=row;return choice;
    }
    private static async Task LoadArtwork(CatalogApp app, Grid target)
    {
        var image = await AppArtwork.Get(app);
        if (image == null) return;
        await target.Dispatcher.InvokeAsync(() =>
        {
            target.Children.Clear(); target.Children.Add(new Image { Source = image, Width = 28, Height = 28, ToolTip = app.Name });
        });
    }
    private void RefreshApps()
    {
        syncingAppChoices=true;
        try {
        SelectedCount.Text = $"Your app list ({selectedApps.Count})";
        SelectedAppsDetails.Visibility=selectedApps.Count==0?Visibility.Collapsed:Visibility.Visible;
        foreach(var row in Apps.Children.OfType<AppChoice>().ToArray())
        {
            var app=(CatalogApp)row.Tag;
            var present=selectedApps.Any(a=>TechLoungeOS.RecommendedApps.Identity(a.Id).Equals(TechLoungeOS.RecommendedApps.Identity(app.Id),StringComparison.OrdinalIgnoreCase));
            row.IsChecked=present;
            row.SelectionVisibility(present,()=>Apps.Children.Remove(row));
        }
        foreach (var app in selectedApps)
        {
            if(!Apps.Children.OfType<AppChoice>().Any(row=>TechLoungeOS.RecommendedApps.Identity(((CatalogApp)row.Tag).Id).Equals(TechLoungeOS.RecommendedApps.Identity(app.Id),StringComparison.OrdinalIgnoreCase)))
                Apps.Children.Add(SelectableRow(app,true));
        }
        foreach (var button in Apps.Children.OfType<AppChoice>().Concat(SearchResults.Children.OfType<AppChoice>()).Concat(RecommendedApps.Children.OfType<AppChoice>()))
        {
            var app = (CatalogApp)button.Tag;
            if (TechLoungeOS.RecommendedApps.IncludedInIso(app.Id)) { button.ToolTip = "Included in every ISO"; button.IsChecked=true;button.IsEnabled = false; continue; }
            if(AppCatalog.ExplicitArmVariant(app)){button.ToolTip="This package is labelled for ARM. Choose the standard package for an x64 ISO.";button.IsChecked=false;button.IsEnabled=false;continue;}
            var existing = selectedApps.FirstOrDefault(a => TechLoungeOS.RecommendedApps.Identity(a.Id).Equals(TechLoungeOS.RecommendedApps.Identity(app.Id), StringComparison.OrdinalIgnoreCase));
            button.IsChecked=existing!=null;
            button.ToolTip=app.Name+" · "+app.Details;
        }
        }finally{syncingAppChoices=false;}
    }
    private void RecommendedAppsClick(object sender,RoutedEventArgs e)
    {
        if(running)return;
        var next=TechLoungeOS.RecommendedApps.ApplyPreset(selectedApps);
        selectedApps.Clear();selectedApps.AddRange(next);RefreshApps();
        SearchStatus.Text="Recommended apps selected. Click any highlighted app to deselect it.";
    }
    private void ClearAppsClick(object sender, RoutedEventArgs e) { selectedApps.Clear(); RefreshApps(); }
}

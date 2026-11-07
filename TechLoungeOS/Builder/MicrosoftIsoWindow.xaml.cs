using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace TechLoungeOS;

public partial class MicrosoftIsoWindow:Window
{
    private readonly HttpClient client=MicrosoftIso.CreateClient();
    private readonly MicrosoftIso service;
    private IsoCatalogue? catalogue;
    private CancellationTokenSource? operation;
    private bool closeRequested;
    public IsoDownloadResult? Downloaded {get;private set;}
    public MicrosoftIsoWindow():this(true){}
    internal MicrosoftIsoWindow(bool automaticCatalogue)
    {
        InitializeComponent();service=new(client);
        var area=SystemParameters.WorkArea;Width=Math.Min(Width,area.Width);Height=Math.Min(Height,area.Height);MinWidth=Math.Min(MinWidth,Width);MinHeight=Math.Min(MinHeight,Height);
        if(automaticCatalogue)Loaded+=async (_,_)=>await LoadCatalogue();
        SourceInitialized+=(_,_)=>WindowsChrome.Apply(this);
        Closing+=OnClosing;Closed+=(_,_)=>client.Dispose();
    }
    internal void ShowCatalogue(IsoCatalogue value)
    {
        catalogue=value;LanguagesChoice.ItemsSource=value.Languages;LanguagesChoice.SelectedIndex=0;
        ManualLinkPanel.Visibility=RecoveryActions.Visibility=Visibility.Collapsed;DownloadHint.Visibility=Visibility.Visible;
        LanguageSelection.Visibility=Visibility.Visible;
        Height=Math.Min(480,SystemParameters.WorkArea.Height);DownloadStatus.Text="Choose a language. You’ll pick a save location next.";
        if(operation is null){LanguagesChoice.IsEnabled=true;DownloadProgress.Visibility=Visibility.Collapsed;RefreshStart();}
    }
    private async Task Work(Func<CancellationToken,Task> action,bool loading=false)
    {
        if(operation is not null)return;
        operation=new();if(loading)operation.CancelAfter(TimeSpan.FromSeconds(75));
        DownloadStart.IsEnabled=RetryCatalogue.IsEnabled=LanguagesChoice.IsEnabled=DirectLinkBox.IsEnabled=false;
        DownloadProgress.IsIndeterminate=true;DownloadProgress.Visibility=Visibility.Visible;
        try{await action(operation.Token);}
        catch(OperationCanceledException)
        {
            DownloadStatus.Text=closeRequested?"Download cancelled.":"Microsoft did not respond in time. Retry or use its official website.";
            if(!closeRequested)ShowRecovery(loading);
        }
        catch(Exception ex)
        {
            DownloadStatus.Text=ex.Message;
            ShowRecovery(loading);
        }
        finally
        {
            operation.Dispose();operation=null;DownloadProgress.Visibility=Visibility.Collapsed;
            LanguagesChoice.IsEnabled=catalogue is not null;RetryCatalogue.IsEnabled=DirectLinkBox.IsEnabled=true;
            RefreshStart();
            if(Downloaded is not null)DialogResult=true;else if(closeRequested)Close();
        }
    }
    private void ShowRecovery(bool showLink)
    {
        RecoveryActions.Visibility=Visibility.Visible;DownloadHint.Visibility=Visibility.Collapsed;
        if(showLink){catalogue=null;LanguageSelection.Visibility=Visibility.Collapsed;ManualLinkPanel.Visibility=Visibility.Visible;}
        Height=Math.Min(560,SystemParameters.WorkArea.Height);
    }
    private async Task LoadCatalogue()=>await Work(async ct=>
    {
        DownloadStatus.Text="Getting Microsoft's available languages…";
        ShowCatalogue(await service.Catalogue(ct));
    },true);
    private void RefreshStart()=>DownloadStart.IsEnabled=operation is null&&(catalogue is not null||!string.IsNullOrWhiteSpace(DirectLinkBox.Text));
    private void DirectLinkChanged(object sender,TextChangedEventArgs e){if(DownloadStart is not null)RefreshStart();}
    private async void RetryClick(object sender,RoutedEventArgs e)=>await LoadCatalogue();
    private async void DownloadClick(object sender,RoutedEventArgs e)=>await Work(async ct=>
    {
        DownloadStatus.Text="Getting your download link from Microsoft…";
        var link=catalogue is not null&&LanguagesChoice.SelectedItem is IsoLanguage language
            ?await service.Resolve(catalogue,language,ct):MicrosoftIso.DirectLink(DirectLinkBox.Text);
        var save=new SaveFileDialog{Filter="Windows ISO|*.iso",FileName=link.FileName,Title="Save your Microsoft Windows ISO",OverwritePrompt=false};
        while(true)
        {
            if(save.ShowDialog(this)!=true){DownloadStatus.Text="Download not started. Choose Download ISO when you’re ready.";return;}
            if(!File.Exists(save.FileName))break;
            MessageBox.Show(this,"That ISO already exists. Choose another filename to keep it unchanged.","Keep existing ISO",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        DownloadProgress.IsIndeterminate=false;DownloadProgress.Value=0;
        Downloaded=await service.Download(link,save.FileName,new Progress<IsoDownloadProgress>(p=>
        {
            DownloadProgress.Value=100d*p.Bytes/p.Total;
            DownloadStatus.Text=$"Downloading Windows 11 · {DownloadProgress.Value:0}% · {p.Bytes/1073741824d:0.00} of {p.Total/1073741824d:0.00} GB";
        }),ct);
    });
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        if(operation is null)return;
        e.Cancel=true;closeRequested=true;operation.Cancel();DownloadStatus.Text="Stopping and removing the incomplete download…";
    }
    private void CloseClick(object sender,RoutedEventArgs e)=>Close();
    private void WebsiteClick(object sender,RoutedEventArgs e)
    {
        try{Process.Start(new ProcessStartInfo(MicrosoftIso.DownloadPage){UseShellExecute=true});}
        catch(Exception ex){DownloadStatus.Text="Could not open your browser: "+ex.Message;}
    }
}

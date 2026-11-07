using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using TechLoungeOS.Updates;

namespace TechLoungeOS;
public partial class MainWindow
{
    private readonly AppUpdates updates=new();
    private readonly CancellationTokenSource updateLifetime=new();
    private ReadyUpdate? readyUpdate;
    private bool checkingUpdate;
    private bool restartAfterUpdate;
    private bool recoveryRequired;
    private void CheckRecoveryState()
    {
        recoveryRequired=File.Exists(Path.Combine(AppContext.BaseDirectory,".techloungeos-update-journal.json"));
        if(!recoveryRequired)return;
        SetBusy(running);
        RecoveryButton.Visibility=Visibility.Visible;
        Status.Text="An interrupted app update needs recovery. Restore the previous version before creating media.";
    }
    private void RecoverUpdateClick(object sender,RoutedEventArgs e)
    {
        if(running)return;
        try
        {
            var self=System.Diagnostics.Process.GetCurrentProcess();var start=new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"updater","TechLoungeOS.Updater.exe")){UseShellExecute=false,CreateNoWindow=true};
            foreach(var arg in new[]{"--recover",AppContext.BaseDirectory,self.Id.ToString(),self.StartTime.ToUniversalTime().Ticks.ToString()})start.ArgumentList.Add(arg);
            System.Diagnostics.Process.Start(start);Close();
        }
        catch(Exception ex){Status.Text="Recovery did not start: "+ex.Message;}
    }
    private async void CheckUpdateClick(object sender,RoutedEventArgs e)=>await CheckUpdates(true);
    internal async Task CheckUpdates(bool showStatus=false,Func<CancellationToken,Task<ReadyUpdate?>>? check=null)
    {
        if(checkingUpdate || recoveryRequired)return;
        if(readyUpdate!=null)
        {
            ShowAppUpdateResult("Update ready",$"Builder {readyUpdate.Manifest.Version} is ready. Close the app to install it after your work finishes.");
            return;
        }
        checkingUpdate=true;SetUpdateCheckingMotion(true);
        ShowAppUpdateResult("Checking for app updates…","Checking for TechLoungeOS Builder app updates…");
        try
        {
            // Keep the checking state readable even when a cached result or error is immediate.
            // The delay is asynchronous and is cancelled when this window closes.
            async Task<ReadyUpdate?> Request()=>check is null
                ? await Task.Run(()=>updates.Check(AppUpdates.CurrentVersion,updateLifetime.Token))
                : await check(updateLifetime.Token);
            var request=Request();
            await Task.WhenAll(request,Task.Delay(360,updateLifetime.Token));
            readyUpdate=await request;
            if(readyUpdate==null)
                ShowAppUpdateResult("You’re up to date",$"Builder {AppUpdates.CurrentVersion} is up to date. No newer published release was found.");
            else
                ShowAppUpdateResult("Update ready",$"Builder {readyUpdate.Manifest.Version} is ready. It installs automatically when you close the app.");
            InstallUpdateButton.Visibility=readyUpdate==null?Visibility.Collapsed:Visibility.Visible;
            UpdateAvailableBadge.Visibility=readyUpdate==null?Visibility.Collapsed:Visibility.Visible;
        }
        catch(OperationCanceledException){ShowAppUpdateResult("Check cancelled","The app update check was cancelled. Try again when you’re ready.");}
        catch(AppUpdateFeedUnavailableException)
        {
            ShowAppUpdateResult("No app updates available","No public Builder app release has been published yet. You can still customise ISOs and create setup USBs.");
        }
        catch(Exception ex){ShowAppUpdateResult("Couldn’t check for updates","Couldn’t check for app updates. Try again later. Details: "+ex.Message);}
        finally{checkingUpdate=false;SetUpdateCheckingMotion(false);if(showStatus&&!running)Status.Text=UpdateMessage.Text;}
    }
    private void ShowAppUpdateResult(string summary,string details)
    {
        SetUpdateFeedback(summary);UpdateMessage.Text=details;
        System.Windows.Automation.AutomationProperties.SetHelpText(CheckUpdatesButton,"Checks for Builder app updates, not Windows image updates. "+details);
    }
    private async void LocalUpdateClick(object sender,RoutedEventArgs e)
    {
        if(checkingUpdate || running || recoveryRequired)return;
        if(readyUpdate!=null)
        {
            UpdateMessage.Text=$"Builder {readyUpdate.Manifest.Version} is already verified. Close the app to install it before choosing another update.";
            return;
        }
        var manifest=new OpenFileDialog{Filter="Signed Builder update|builder-update.json",Title="Choose the signed update manifest"};
        if(manifest.ShowDialog(this)!=true)return;
        var zip=new OpenFileDialog{Filter="Builder update package|*.zip",Title="Choose its matching Builder ZIP"};
        if(zip.ShowDialog(this)!=true)return;
        checkingUpdate=true;CheckUpdatesButton.IsEnabled=false;
        try
        {
            readyUpdate=await Task.Run(()=>AppUpdates.StageLocal(manifest.FileName,zip.FileName,AppUpdates.CurrentVersion));
            ShowAppUpdateResult("Update ready",$"Builder {readyUpdate.Manifest.Version} verified. It installs when you close the app.");InstallUpdateButton.Visibility=Visibility.Visible;
            UpdateAvailableBadge.Visibility=Visibility.Visible;
        }
        catch(Exception ex){ShowAppUpdateResult("Update not installed","Update not installed: "+ex.Message);}
        finally{checkingUpdate=false;CheckUpdatesButton.IsEnabled=true;}
    }
    private void InstallUpdateClick(object sender,RoutedEventArgs e)
    {
        if(running || readyUpdate==null)return;
        if(Editions.SelectedItem!=null){UpdateMessage.Text="Finish creating your ISO first. The verified update will install when you close the app.";return;}
        restartAfterUpdate=true;Close();
    }
    private void ApplyUpdateOnClose(object? sender,CancelEventArgs e)
    {
        if(e.Cancel || running || readyUpdate==null)return;
        try { UpdateCoordinator.EnsureAppClosed(AppContext.BaseDirectory,Environment.ProcessId); }
        catch(Exception ex)
        {
            // An ordinary close must remain possible when several windows have queued updates.
            e.Cancel=restartAfterUpdate;
            Status.Text=UpdateMessage.Text="Update not started: "+ex.Message;
            return;
        }
        try { AppUpdates.Launch(readyUpdate,restartAfterUpdate); }
        catch(Exception ex){e.Cancel=true;UpdateMessage.Text="Update could not start: "+ex.Message;readyUpdate=null;InstallUpdateButton.Visibility=Visibility.Collapsed;}
    }
}

using System.Globalization;
using System.Windows;

namespace TechLoungeOS;

public partial class MainWindow
{
    private static readonly CultureInfo VersionDateCulture=CultureInfo.GetCultureInfo("en-GB");
    private static string BuildNumber(Version version)=>version.Build+"."+version.Revision;
    private static string PublishedDate(DateOnly? date)=>date?.ToString("d MMMM yyyy",VersionDateCulture)??"Release date unavailable";
    private void ResetWindowsVersionStatus()
    {
        if(WindowsVersionStatus is null)return;
        var valid=Editions.SelectedItem is Edition edition && Version.TryParse(edition.Version,out var _);
        var version=valid?Version.Parse(((Edition)Editions.SelectedItem).Version):null;
        WindowsCurrentName.Text=version is null?"Choose a Windows edition":WindowsReleaseCatalog.DisplayName(version);
        WindowsCurrentDetails.Text=version is null?"Import an ISO to begin.":"Build "+BuildNumber(version)+"\nRelease date not checked";
        WindowsLatestName.Text="Not checked yet";WindowsLatestDetails.Text="Check for compatible Windows updates.";
        WindowsVersionChecked.Text="";WindowsNewRelease.Text="";WindowsNewRelease.Visibility=Visibility.Collapsed;
        WindowsVersionStatus.Text=version is null?"Import an ISO and choose an edition to check its Windows version.":"Check to look up compatible updates and Microsoft’s published release dates.";
        if(WindowsVersionCheck is not null)WindowsVersionCheck.IsEnabled=!running && !recoveryRequired && Editions.SelectedItem is Edition;
    }
    private async void WindowsVersionCheckClick(object sender,RoutedEventArgs e)=>await CheckWindowsVersionDetails(WindowsUpdateDownload.CheckDetails);
    internal Task CheckWindowsVersion(Func<Version,CancellationToken,Task<WindowsReleaseCatalog.Release>> check)=>
        CheckWindowsVersionDetails(async (version,ct)=>new(await check(version,ct),null,null,DateTimeOffset.UtcNow));
    // Metadata only. Looking up versions and dates never downloads servicing packages.
    internal async Task CheckWindowsVersionDetails(Func<Version,CancellationToken,Task<WindowsReleaseCatalog.VersionReport>> check)
    {
        if(running || recoveryRequired)return;
        if(Editions.SelectedItem is not Edition edition || edition.Architecture!="9" || !Version.TryParse(edition.Version,out var version) || version.Major!=10 || version.Minor!=0 || version.Build<22000 || version.Revision<0)
        {WindowsVersionStatus.Text="Choose a Windows 11 x64 edition with a complete version before checking for Windows updates.";return;}
        ResetWindowsVersionStatus();cancellation=new CancellationTokenSource();SetBusy(true);
        Status.Text=WindowsVersionStatus.Text="Checking Windows update information…";
        try
        {
            var report=WindowsUpgradeRules.ForEdition(await check(version,cancellation.Token),edition.EditionId);cancellation.Token.ThrowIfCancellationRequested();
            var release=report.LatestCompatible;
            if(release.Version.Major!=version.Major || release.Version.Minor!=version.Minor || release.Version.Build!=version.Build || (report.Current is not null && report.Current.Version!=version))
                throw new IOException("Microsoft’s result does not match the selected Windows release.");
            WindowsCurrentName.Text="Windows 11 "+release.Name;
            WindowsCurrentDetails.Text="Build "+BuildNumber(version)+"\n"+(report.Current is null?"Release date unavailable":"Build released "+PublishedDate(report.Current.Published));
            var offered=report.Upgrade?.Target??release;
            WindowsLatestName.Text="Windows 11 "+offered.Name;
            WindowsLatestDetails.Text="Build "+BuildNumber(offered.Version)+" · "+offered.KnowledgeBase+"\nBuild released "+PublishedDate(offered.Published);
            WindowsVersionChecked.Text="Checked "+report.CheckedAt.ToLocalTime().ToString("d MMM yyyy 'at' HH:mm",VersionDateCulture);
            WindowsVersionStatus.Text=report.Upgrade is { } upgrade
                ?$"Windows 11 {upgrade.Target.Name} is available through Microsoft’s enablement package. Select the update option below to include the upgrade and required fixes. The image must pass eligibility and preservation checks."
                :release.Version>version
                ? $"A compatible update is available: {release.KnowledgeBase} (build {BuildNumber(release.Version)}). Select the option below to include it when building."
                : $"This ISO is up to date for this Windows release. Latest stable build checked: {BuildNumber(release.Version)}.";
            if(report.NewestFeature is { } newer && string.CompareOrdinal(newer.Name,release.Name)>0)
            {
                WindowsNewRelease.Text=report.Upgrade?.Target.Name==newer.Name
                    ?$"Windows 11 {newer.Name} was released {PublishedDate(newer.Published)}. This reviewed upgrade retains the source setup tools and checks existing components and settings before export."
                    :$"Microsoft also lists Windows 11 {newer.Name}, released {PublishedDate(newer.Published)}. No reviewed upgrade route is available for that release yet; Builder will not guess.";
                WindowsNewRelease.Visibility=Visibility.Visible;
            }
            Status.Text="Windows version checked. Your ISO is unchanged.";
        }
        catch(OperationCanceledException){Status.Text=WindowsVersionStatus.Text="Windows update check cancelled. Your ISO is unchanged.";}
        catch(Exception error){Status.Text=WindowsVersionStatus.Text="Windows update check failed: "+error.Message;}
        finally{cancellation.Dispose();cancellation=null;SetBusy(false);}
    }
}
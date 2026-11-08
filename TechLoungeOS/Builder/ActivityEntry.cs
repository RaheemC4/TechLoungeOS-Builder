using System.Text.RegularExpressions;

namespace TechLoungeOS;

internal record ActivityEntry(DateTimeOffset Time,string Title,string Detail,string State,string Icon)
{
    internal object MotionKey {get;}=new();
    public bool IsWorking=>State is "Working" or "Stopping";
    public string TimeLabel=>Time.ToLocalTime().ToString("HH:mm");
    internal static ActivityEntry FromStatus(string status,bool working)
    {
        var text=Regex.Replace(status,@"\s+"," ").Trim();
        (string title,string detail,string state,string icon) entry;
        var editions=Regex.Match(text,@"^Found (\d+) Windows editions?\.");
        if(editions.Success)entry=("Windows image ready",$"{editions.Groups[1].Value} Windows editions found. Choose the edition you want to customise.","Done","\uE73E");
        else if(text.StartsWith("Import an ISO"))entry=("Ready to begin","Choose a Windows ISO to start customising it.","Ready","\uE8A5");
        else if(text.StartsWith("Select your USB")||text.StartsWith("Select the USB"))entry=("Choose your USB drive","Select the removable drive you want to use for Windows setup.","Ready","\uE88E");
        else if(text.StartsWith("Preparing and checking the media"))entry=("Checking your installation files","Preparing the files and checking that they will fit. The USB has not been changed.","Working","\uE895");
        else if(text.StartsWith("Building your customisations"))entry=("Creating your Windows installer","Adding your chosen apps, desktop and setup settings before preparing the USB.","Working","\uE895");
        else if(text.StartsWith("Creating the confirmed USB"))entry=("Writing your setup USB","Copying the checked files. Keep the drive connected until verification finishes.","Working","\uE895");
        else if(text.StartsWith("Verifying every file written"))entry=("Checking your setup USB","Reading back every copied file to check that the USB is complete.","Working","\uE895");
        else if(text.StartsWith("Setup drive files verified"))entry=("Your USB is ready","The copied files passed verification. Safely eject it before using it on your other PC.","Done","\uE73E");
        else if(text.StartsWith("Stopped safely")||text.StartsWith("The USB may be incomplete"))entry=("USB creation stopped",text,"Stopped","\uE711");
        else if(text.StartsWith("Reading Windows editions"))entry=("Reading your Windows image","Looking for the Windows editions available in your ISO.","Working","\uE895");
        else if(text.Contains("failed",StringComparison.OrdinalIgnoreCase) || text.StartsWith("Could not") || text.StartsWith("Couldn’t"))entry=("Something needs attention",text,"Needs attention","\uE7BA");
        else if(text.StartsWith("Cancellation requested"))entry=("Stopping safely","Waiting for the current step to finish before cleaning up. Keep the app open.","Stopping","\uE711");
        else if(text.StartsWith("Cancelled"))entry=("Build stopped","The build was cancelled. Open the diagnostic log to check cleanup details.","Stopped","\uE711");
        else if(text.StartsWith("Checking the selected files"))entry=("Checking requirements","Checking your selected files, required tools and available storage before the build starts.","Working","\uE895");
        else if(text.StartsWith("Saving Windows changes"))entry=("Saving Windows changes","Saving your customisations into the Windows image. This can take several minutes.","Working","\uE895");
        else if(text.StartsWith("Customising Windows"))entry=("Customising Windows","Applying your selected changes and checking Windows for problems.","Working","\uE895");
        else if(text.StartsWith("Creating your bootable ISO"))entry=("Creating your ISO","Putting the customised Windows setup files into a bootable ISO.","Working","\uE895");
        else if(text.StartsWith("Checking every file in your exported ISO"))entry=("Verifying your ISO","Checking every exported file against the prepared setup files before marking the ISO ready.","Working","\uE895");
        else if(text.StartsWith("ISO exported") || text.StartsWith("Your customised ISO is ready"))entry=("Customised ISO created","The ISO has been created and its files verified. Installation testing is still needed.","Done","\uE73E");
        else if(text.StartsWith("Windows version checked"))entry=("Windows version checked","The Windows update information was checked. Your ISO is unchanged.","Done","\uE73E");
        else
        {
            text=text.Replace(". Full logs are saved in the workspace.","");
            var phase=text switch
            {
                "Preparing build"=>("Preparing your build","Setting up the files needed to customise your Windows image."),
                "Copying Windows installation media"=>("Copying Windows setup files","Making a working copy of the setup files for your customisations."),
                "Exporting selected edition"=>("Preparing your Windows edition","Preparing the edition you selected for customisation."),
                "Mounting image"=>("Opening the Windows image","Opening Windows offline so your changes can be applied."),
                "Downloading and verifying offline Office payload"=>("Downloading Microsoft 365","Getting and checking the Office files for installation after Windows starts."),
                "Downloading and verifying build prerequisites"=>("Getting required setup files","Preparing and checking your apps and shared components together, with fewer jobs on low-memory PCs."),
                "Preparing the required Microsoft build component"=>("Preparing build tools","Getting and verifying the small Microsoft component used to create your installer."),
                "Downloading and verifying offline runtime installers"=>("Preparing app and game runtimes","Getting shared components that many apps and games need."),
                "Downloading and verifying offline Microsoft Edge"=>("Downloading Microsoft Edge","Getting the browser installer and checking its authenticity."),
                "Downloading and verifying the current official TechLoungeTweaks installer"=>("Preparing TechLoungeTweaks","Getting the current official installer and checking its download."),
                "Checking and preparing the WinPE setup and tools interface"=>("Preparing the setup environment","Adding the account wizard and connecting your image’s existing tools."),
                "Acquiring missing offline Store and Xbox packages"=>("Preparing Microsoft Store and Xbox","Getting the required app packages for the new Windows installation."),
                "Preparing Windows Setup compatibility defaults"=>("Preparing Windows setup","Applying the setup compatibility options."),
                "Checking Windows update information…"=>("Checking Windows release information","Looking up compatible updates and Microsoft’s published release dates."),
                _=>(working?"Build in progress":"Activity update",text)
            };
            entry=(phase.Item1,phase.Item2,working?"Working":"Info",working?"\uE895":"\uE946");
        }
        return new(DateTimeOffset.Now,entry.title,entry.detail.Length>280?entry.detail[..277]+"…":entry.detail,entry.state,entry.icon);
    }
}

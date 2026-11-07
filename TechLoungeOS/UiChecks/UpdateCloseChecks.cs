using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using TechLoungeOS;
using TechLoungeOS.Updates;

internal static class UpdateCloseChecks
{
    internal static void Run(string output)
    {
        var install=Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
        if(!install.Equals(@"C:\TLOS\app",StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(output).StartsWith(@"C:\TLOS\",StringComparison.OrdinalIgnoreCase) ||
            new ProcessRunner(_=>{}).PowerShell("(Get-CimInstance Win32_ComputerSystem).Manufacturer").GetAwaiter().GetResult().Trim()!="QEMU")
            throw new IOException("Owned VM application folder required for close-state checks.");
        using var peer=Process.GetProcessesByName(Path.GetFileNameWithoutExtension(UpdateTrust.MainExe))
            .FirstOrDefault(p=>!p.HasExited && string.Equals(p.MainModule?.FileName,Path.Combine(install,UpdateTrust.MainExe),StringComparison.OrdinalIgnoreCase))
            ??throw new IOException("An idle installed Builder peer must be open.");
        var before=UpdatePackage.Hash(Path.Combine(install,UpdateTrust.MainExe));
        var fakeStage=Path.Combine(install,"uncreated-close-fixture-"+Guid.NewGuid().ToString("N"));
        var manifest=new UpdateManifest(UpdateTrust.Product,"999.0.0.0",UpdateTrust.Channel,"win-x64",22000,DateTimeOffset.UtcNow,
            "https://github.com/unused/fixture.zip",1,new string('0',64),[]);
        var ready=new ReadyUpdate(manifest,fakeStage);
        var app=new App(false);app.InitializeComponent();
        var window=new MainWindow(false);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var readyField=typeof(MainWindow).GetField("readyUpdate",flags)!;
        var restartField=typeof(MainWindow).GetField("restartAfterUpdate",flags)!;
        var close=typeof(MainWindow).GetMethod("ApplyUpdateOnClose",flags)!;
        try
        {
            foreach(var restart in new[]{false,true})
            {
                readyField.SetValue(window,ready);restartField.SetValue(window,restart);
                var args=new CancelEventArgs();close.Invoke(window,[window,args]);
                if(args.Cancel!=restart || !ReferenceEquals(readyField.GetValue(window),ready) ||
                    !((TextBlock)window.FindName("Status")).Text.Contains(peer.Id.ToString()))
                    throw new IOException("Close handling must permit ordinary exit, retain the queued update, and visibly block explicit restart while its peer runs.");
            }
            if(Directory.Exists(fakeStage) || UpdatePackage.Hash(Path.Combine(install,UpdateTrust.MainExe))!=before)
                throw new IOException("Close-state checks unexpectedly changed application files.");
            var result=JsonSerializer.Serialize(new{normalCloseAllowed=true,explicitRestartBlocked=true,queuedUpdateRetained=true,
                visiblePeerId=true,filesUnchanged=true,actualClosingHandlerInvoked=true,syntheticReadyState=true,peerId=peer.Id});
            File.WriteAllText(output,result);
            using var http=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromSeconds(15)};
            using var body=new System.Net.Http.StringContent(JsonSerializer.Serialize(new{action="update-close-checks",result}),System.Text.Encoding.UTF8,"application/json");
            using var response=http.PostAsync("http://10.0.2.2:8088/audit",body).GetAwaiter().GetResult();response.EnsureSuccessStatusCode();
        }
        finally{readyField.SetValue(window,null);window.Close();app.Shutdown();}
    }
}

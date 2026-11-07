using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using TechLoungeOS;
using TechLoungeOS.Updates;

internal static class PendingUpdateChecks
{
    internal static void Run(MainWindow window)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var field=typeof(MainWindow).GetField("readyUpdate",flags)!;
        var message=(TextBlock)window.FindName("UpdateMessage");
        var previous=field.GetValue(window);var text=message.Text;
        var uncreated=Path.Combine(Path.GetTempPath(),"TLOS-pending-fixture-"+Guid.NewGuid().ToString("N"));
        var manifest=new UpdateManifest(UpdateTrust.Product,"999.0.0.0",UpdateTrust.Channel,"win-x64",22000,DateTimeOffset.UtcNow,
            "https://github.com/unused/fixture.zip",1,new string('0',64),[]);
        var ready=new ReadyUpdate(manifest,uncreated);
        try
        {
            field.SetValue(window,ready);
            var click=typeof(MainWindow).GetMethod("LocalUpdateClick",flags)!;
            for(var i=0;i<3;i++)
            {
                click.Invoke(window,[window,new RoutedEventArgs()]);
                if(!ReferenceEquals(field.GetValue(window),ready) || Directory.Exists(uncreated) ||
                    !message.Text.Contains("999.0.0.0",StringComparison.Ordinal) || !message.Text.Contains("Close the app",StringComparison.Ordinal))
                    throw new IOException("Repeated local-update selection must retain the verified update and explain how to install it without staging another package.");
            }
            Console.WriteLine("Three actual local-update handler invocations retained the queued update, displayed install guidance and created no replacement stage. Synthetic ready state; no update applied.");
        }
        finally{field.SetValue(window,previous);message.Text=text;}
    }
}

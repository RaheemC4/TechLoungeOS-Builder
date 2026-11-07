using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TechLoungeOS;

// Explicit opt-in integration test. Uses real servicing and the application's build lifecycle.
// Run elevated from a normal interactive desktop context, with a fresh private workspace/output.
internal static class NativeBuildCheck
{
    internal static void Run(string config)
    {
        var options = JsonSerializer.Deserialize<BuildOptions>(File.ReadAllText(config))
            ?? throw new IOException("Build configuration missing.");
        Policy.Validate(options);
        if (Directory.Exists(options.Workspace) || File.Exists(options.Output))
            throw new IOException("Native validation requires a new workspace and output.");
        var app = new App(false); app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new MainWindow(false);
        _ = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        Exception? failure = null;
        app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var editions = await new BuildEngine(_ => { }).Import(options.Iso);
                var selector = (ComboBox)window.FindName("Editions");
                selector.ItemsSource = editions;
                selector.SelectedItem = editions.Single(e => e.ImageIndex == options.Index);
                ((TextBox)window.FindName("OutputPath")).Text = options.Output;
                var method = typeof(MainWindow).GetMethod("RunBuild", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)method.Invoke(window, [options])!;
                var status = ((TextBlock)window.FindName("Status")).Text;
                using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.Workspace, "state.json")));
                if (!File.Exists(options.Output) || state.RootElement.GetProperty("phase").GetString() != "Complete")
                    throw new IOException(status);
                if (!((Button)window.FindName("BuildButton")).IsEnabled || ((Button)window.FindName("CancelButton")).IsEnabled)
                    throw new IOException("App did not restore its idle controls.");
                var imported = await new BuildEngine(_ => { }).Import(options.Output);
                var expected = editions.Single(e => e.ImageIndex == options.Index);
                var updateResult=Path.Combine(options.Workspace,"windows-update","result.json");
                var expectedVersion=expected.Version;
                if(File.Exists(updateResult))
                {
                    using var update=JsonDocument.Parse(File.ReadAllText(updateResult));
                    if(!update.RootElement.GetProperty("preservationPassed").GetBoolean())throw new IOException("Windows update preservation was not accepted.");
                    expectedVersion=update.RootElement.GetProperty("target").GetString()!;
                }
                if (imported.Length != 1 || imported[0].ImageIndex != 1 || imported[0].Version != expectedVersion ||
                    imported[0].ImageName != expected.ImageName || imported[0].Architecture != expected.Architecture)
                    throw new IOException("Export metadata changed.");
                var readback = await ExportReadback.Run(options.Output, options.Workspace,
                    BootableDrive.Hash(Path.Combine(AppContext.BaseDirectory, "FirstLogin.exe")), Console.WriteLine,
                    BootableDrive.Hash(Path.Combine(AppContext.BaseDirectory, "Personalise.exe")));
                File.WriteAllText(Path.Combine(options.Workspace, "export-readback.json"), readback);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1160, 860)); content.Arrange(new Rect(0, 0, 1160, 860)); content.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1160, 860, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(config + ".ui.png")) png.Save(file);
                File.WriteAllText(config + ".ui-result.json", JsonSerializer.Serialize(new {
                    status, output = options.Output, editions = imported, appBuildLifecycle = true,
                    nativeServicing = true, exportedImageReadback = true, interactiveFilePickersAndReviewDialogTested = false
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { failure = ex; File.WriteAllText(config + ".ui-error.txt", ex.ToString()); }
            finally { window.Close(); app.Shutdown(); }
        });
        app.Run(window);
        if (failure != null) throw new IOException("Native app build check failed.", failure);
    }
}

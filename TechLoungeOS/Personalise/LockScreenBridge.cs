using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System.UserProfile;

static class LockScreenBridge
{
    [STAThread] static int Main(string[] args)
    {
        try {
            if(args.Length!=2 || (args[0]!="--apply" && args[0]!="--read-check")) throw new ArgumentException("Unknown lock-screen action.");
            string image=Path.GetFullPath(args[1]);
            var opening=StorageFile.GetFileFromPathAsync(image).AsTask();
            if(!opening.Wait(30000)) throw new TimeoutException("Opening the lock-screen image timed out.");
            var file=opening.GetAwaiter().GetResult();
            if(args[0]=="--apply") {
                var setting=LockScreen.SetImageFileAsync(file).AsTask();
                if(!setting.Wait(30000)) throw new TimeoutException("Windows has not finished setting the lock screen.");
                setting.GetAwaiter().GetResult();
            }
            byte[] expected,actual;
            using(var hash=SHA256.Create()) using(var source=File.OpenRead(image)) expected=hash.ComputeHash(source);
            using(var stream=LockScreen.GetImageStream().AsStreamForRead()) using(var hash=SHA256.Create()) actual=hash.ComputeHash(stream);
            if(args[0]=="--apply" && BitConverter.ToString(expected)!=BitConverter.ToString(actual))
                throw new IOException("Windows did not read back the selected lock-screen image.");
            // Read-check exercises storage/stream interop without changing the user's image.
            return 0;
        } catch(Exception error) {
            string state=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TechLoungeOS");
            try{Directory.CreateDirectory(state);File.WriteAllText(Path.Combine(state,"lock-screen-error.txt"),error.ToString());}catch(IOException){}catch(UnauthorizedAccessException){}
            return 1;
        }
    }
}

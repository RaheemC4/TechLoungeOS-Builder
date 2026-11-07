using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TechLoungeOS.Updates;

static ECDsa SigningKey(string source)
{
    UpdatePackage.Unlinked(source);
    ECDsa signer;
    if(Path.GetExtension(source).Equals(".p8",StringComparison.OrdinalIgnoreCase))
    {
        Console.Write("Encrypted signing-key passphrase: ");var password=Secret();signer=ECDsa.Create();
        signer.ImportEncryptedPkcs8PrivateKey(password,File.ReadAllBytes(source),out _);
    }
    else
    {
        using var metadata=JsonDocument.Parse(File.ReadAllBytes(source));var name=metadata.RootElement.GetProperty("keyName").GetString()!;
        using var key=CngKey.Open(name,CngProvider.MicrosoftSoftwareKeyStorageProvider);signer=new ECDsaCng(key);
    }
    if(Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())!=UpdateTrust.PublicKeySpki){signer.Dispose();throw new IOException("Signing key does not match the app's pinned public identity.");}
    return signer;
}
static string Secret()
{
    var chars=new List<char>();
    while(true){var key=Console.ReadKey(true);if(key.Key==ConsoleKey.Enter)break;if(key.Key==ConsoleKey.Backspace){if(chars.Count>0)chars.RemoveAt(chars.Count-1);}else if(!char.IsControl(key.KeyChar))chars.Add(key.KeyChar);}
    Console.WriteLine();return new string(chars.ToArray());
}
if(args is ["backup-key",var keySource,var backup])
{
    UpdatePackage.Unlinked(backup);using var signer=SigningKey(keySource);
    Console.Write("Choose a backup passphrase (12+ characters): ");var password=Secret();Console.Write("Repeat passphrase: ");var repeat=Secret();
    if(password.Length<12||password!=repeat)throw new IOException("Passphrases do not match or are too short.");
    var encrypted=signer.ExportEncryptedPkcs8PrivateKey(password,new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc,HashAlgorithmName.SHA256,300000));
    using var output=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None);output.Write(encrypted);output.Flush(true);Console.WriteLine("Encrypted backup saved. Store it and its passphrase separately; never publish them.");return;
}
if(args is not ["pack",var folder,var outputFolder,var version,var keyFile])throw new ArgumentException("Usage: pack <published app folder> <new output folder> <four-part version> <private key metadata or encrypted .p8>; backup-key <key metadata> <new .p8>");
folder=Path.GetFullPath(folder);outputFolder=Path.GetFullPath(outputFolder);UpdatePackage.Unlinked(folder);UpdatePackage.Unlinked(outputFolder);
if(Directory.Exists(outputFolder)||File.Exists(outputFolder)||outputFolder.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Choose a new output folder outside the published app.");
if(FileVersionInfo.GetVersionInfo(Path.Combine(folder,UpdateTrust.MainExe)).FileVersion!=version)throw new IOException("Published executable version does not match the requested release.");
// A raw dotnet publish lacks the mandatory support bundle. Refuse it before creating an archive.
foreach(var relative in new[]{"FirstLogin.exe","AccountSetup.exe","Personalise.exe","DiscordLauncher.exe",
    "integration/WebView2Runtime/msedgewebview2.exe","integration/WebView2Runtime/msedge.dll",
    "integration/WebView2Runtime/icudtl.dat","integration/WebView2Runtime/resources.pak",
    "integration/resources/nvidiaProfileInspector.exe","integration/resources/TechLoungeProfile.nip",
    "integration/resources/nvidiaProfileInspector.LICENSE.txt","ThirdParty/Wimlib/source.tar.gz","ThirdParty/Sora/OFL.txt"})
{
    var path=Path.Combine(folder,relative.Replace('/',Path.DirectorySeparatorChar));UpdatePackage.Unlinked(path);
    if(!File.Exists(path)||new FileInfo(path).Length<16)throw new IOException("Required release support file is missing or incomplete: "+relative);
    if(relative.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||relative.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))
    {
        using var image=File.OpenRead(path);
        if(image.ReadByte()!='M'||image.ReadByte()!='Z')throw new IOException("Invalid release executable: "+relative);
    }
}
var paths=new List<string>();
File.WriteAllText(Path.Combine(folder,"Restore previous version.cmd"),"@echo off\r\n\"%~dp0updater\\TechLoungeOS.Updater.exe\" --recover \"%~dp0.\"\r\n");
void Visit(string dir){UpdatePackage.Unlinked(dir);foreach(var file in Directory.GetFiles(dir)){UpdatePackage.Unlinked(file);paths.Add(file);}foreach(var child in Directory.GetDirectories(dir))Visit(child);}
Visit(folder);
var files=paths.Select(p=>new UpdateFile(Path.GetRelativePath(folder,p).Replace('\\','/'),new FileInfo(p).Length,UpdatePackage.Hash(p))).ToArray();
Directory.CreateDirectory(outputFolder);var zipPath=Path.Combine(outputFolder,"TechLoungeOS-Builder-win-x64.zip");
using(var zip=ZipFile.Open(zipPath,ZipArchiveMode.Create))foreach(var path in paths)zip.CreateEntryFromFile(path,UpdateTrust.ArchiveRoot+Path.GetRelativePath(folder,path).Replace('\\','/'),CompressionLevel.Optimal);
var m=new UpdateManifest(UpdateTrust.Product,version,UpdateTrust.Channel,"win-x64",22000,DateTimeOffset.UtcNow,
    $"https://github.com/{UpdateTrust.Repository}/releases/download/v{version}/TechLoungeOS-Builder-win-x64.zip",new FileInfo(zipPath).Length,UpdatePackage.Hash(zipPath),files);
UpdatePackage.Validate(m,new Version(0,0,0,0),Environment.OSVersion.Version.Build,DateTimeOffset.UtcNow);
var payload=JsonSerializer.SerializeToUtf8Bytes(m);using(var signer=SigningKey(keyFile))
{
    var envelope=new UpdateEnvelope(Convert.ToBase64String(payload),Convert.ToBase64String(signer.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
    File.WriteAllBytes(Path.Combine(outputFolder,"builder-update.json"),JsonSerializer.SerializeToUtf8Bytes(envelope));
}
UpdatePackage.Extract(zipPath,Path.Combine(outputFolder,"verified"),m);
Console.WriteLine($"Prepared and verified {version}: {zipPath}. No publication, push or release was performed.");

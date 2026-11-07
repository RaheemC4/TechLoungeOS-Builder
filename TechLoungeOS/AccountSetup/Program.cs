using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace TechLoungeOS.AccountSetup
{
    // Windows Setup runs this once, elevated, for the explicitly created local
    // administrator. No credential is passed in arguments or written to our logs.
    internal static class Program
    {
        private const string Winlogon = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
        [STAThread] private static int Main(string[] args)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TechLoungeOS", "account-setup");
            try
            {
                if (args.Length != 6 || args[0] != "--user" || args[2] != "--automatic" || args[4] != "--hello" ||
                    (args[3] != "0" && args[3] != "1") || (args[5] != "0" && args[5] != "1")) throw new InvalidOperationException("Invalid account setup options.");
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) ||
                        !string.Equals(identity.Name, Environment.MachineName + "\\" + args[1], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Account setup must run elevated as the account created by the WinPE wizard.");
                }
                Directory.CreateDirectory(folder);
                using (var gate = new FileStream(Path.Combine(folder, "apply.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                using (var key = Registry.LocalMachine.OpenSubKey(Winlogon, true))
                {
                    if (key == null) throw new InvalidOperationException("Windows sign-in settings are unavailable.");
                    if (args[3] == "1") EnableAutomaticSignin(key, args[1]);
                    else
                    {
                        key.SetValue("AutoAdminLogon", "0", RegistryValueKind.String);
                        key.DeleteValue("AutoLogonCount", false);
                        key.DeleteValue("DefaultPassword", false);
                        Lsa.Store(null);
                    }
                    File.WriteAllText(Path.Combine(folder, "complete"), "Account setup verified; automatic sign-in=" + args[3] + "; Hello prompt=" + args[5], Encoding.UTF8);
                    File.Delete(Path.Combine(folder, "failed"));
                }
                var payload = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TechLoungeOS");
                var setupLauncher=Path.Combine(payload,"FirstLogin.exe");
                if(File.Exists(Path.Combine(payload,"MachineSetup","steps.json")))
                {
                    // OEM editions can skip SetupComplete. This callback is already
                    // elevated and authenticated as the wizard-created account.
                    using(var setup=Process.Start(new ProcessStartInfo(setupLauncher,"--register-setup"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=payload}))
                    {
                        setup.WaitForExit();
                        if(setup.ExitCode!=0)throw new InvalidOperationException("Deferred setup registration failed.");
                    }
                }
                if (File.Exists(Path.Combine(payload, "TweaksInstaller", "installer.json")))
                {
                    try
                    {
                        RuntimePolicy.Prepare(Registry.LocalMachine, Registry.CurrentUser, Path.Combine(payload, "WebView2Runtime"),
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechLoungeOS", "WebView2"));
                        File.WriteAllText(Path.Combine(folder, "tweaks-runtime-ready"), "App-specific runtime policy verified");
                        File.Delete(Path.Combine(folder, "tweaks-runtime-failed"));
                    }
                    catch (Exception policyError)
                    {
                        File.WriteAllText(Path.Combine(folder, "tweaks-runtime-failed"), "Runtime policy needs administrator review. Error=" + policyError.GetType().Name);
                    }
                }
                var helloRequest = Path.Combine(folder, "hello-requested");
                if (args[5] == "1") File.WriteAllText(helloRequest, args[1], Encoding.UTF8);
                else File.Delete(helloRequest);
                return 0;
            }
            catch(Exception error)
            {
                // Do not include exception text: platform exceptions can contain
                // registry values. A failure remains visible and does not erase a credential.
                try { Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "failed"), "Account setup did not finish. Automatic sign-in and Windows Hello need verification. Error=" + error.GetType().Name + "; HResult=" + error.HResult.ToString("X8") + (error is Win32Exception ? "; Win32=" + ((Win32Exception)error).NativeErrorCode : "")); } catch { }
                System.Windows.Forms.MessageBox.Show("Windows account setup did not finish. Your account remains available. Review ProgramData\\TechLoungeOS\\account-setup before relying on automatic sign-in.", "TechLoungeOS account setup");
                return 1;
            }
        }
        private static void EnableAutomaticSignin(RegistryKey key, string user)
        {
            if (!string.Equals(key.GetValue("DefaultUserName") as string, user, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Windows Setup sign-in account does not match the wizard account.");
            // Read the temporary Windows Setup credential, move it to the LSA
            // private-data store and verify readback before removing the registry copy.
            string temporary = key.GetValue("DefaultPassword", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (temporary != null)
            {
                if (temporary.Length != 0)
                {
                    Lsa.Store(temporary);
                    if (!Lsa.EqualsSecret(temporary)) throw new InvalidOperationException("Credential readback failed.");
                    key.DeleteValue("DefaultPassword", false);
                }
                else
                {
                    // An empty password is not a secret. Winlogon requires this
                    // empty value for passwordless accounts on some Windows builds.
                    Lsa.Store(null);
                    key.SetValue("DefaultPassword", "", RegistryValueKind.String);
                }
            }
            else if (!Lsa.MatchesAccount(user))
            {
                IntPtr token;
                bool loggedOn = LogonUser(user, Environment.MachineName, "", 2, 0, out token);
                int error = loggedOn ? 0 : Marshal.GetLastWin32Error();
                if (loggedOn) CloseHandle(token);
                // A valid blank password can return ERROR_ACCOUNT_RESTRICTION
                // under console-only policy. Keep that policy and the current-user guard.
                else if (error != 1327) throw new Win32Exception(error);
                Lsa.Store(null);
                key.SetValue("DefaultPassword", "", RegistryValueKind.String);
            }
            key.SetValue("DefaultDomainName", Environment.MachineName, RegistryValueKind.String);
            key.SetValue("DefaultUserName", user, RegistryValueKind.String);
            key.SetValue("AutoAdminLogon", "1", RegistryValueKind.String);
            key.DeleteValue("AutoLogonCount", false);
            if ((key.GetValue("AutoAdminLogon") as string) != "1" || key.GetValue("AutoLogonCount") != null)
                throw new InvalidOperationException("Automatic sign-in readback failed.");
        }
        [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool LogonUser(string user, string domain, string password, uint type, uint provider, out IntPtr token);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
    internal static class Lsa
    {
        [StructLayout(LayoutKind.Sequential)] private struct Unicode { public ushort Length, MaximumLength; public IntPtr Buffer; }
        [StructLayout(LayoutKind.Sequential)] private struct Attributes { public int Length; public IntPtr RootDirectory, ObjectName; public uint Flags; public IntPtr SecurityDescriptor, SecurityQualityOfService; }
        private static IntPtr Open()
        {
            var attributes = new Attributes { Length = Marshal.SizeOf(typeof(Attributes)) }; IntPtr policy;
            Check(LsaOpenPolicy(IntPtr.Zero, ref attributes, 0x24, out policy)); // GET_PRIVATE_INFORMATION | CREATE_SECRET
            return policy;
        }
        private static Unicode Text(string text)
        {
            return new Unicode { Length = checked((ushort)(text.Length * 2)), MaximumLength = checked((ushort)((text.Length + 1) * 2)), Buffer = Marshal.StringToHGlobalUni(text) };
        }
        private static void Free(Unicode text)
        {
            if (text.Buffer == IntPtr.Zero) return;
            for (int i = 0; i < text.MaximumLength; i++) Marshal.WriteByte(text.Buffer, i, 0);
            Marshal.FreeHGlobal(text.Buffer);
        }
        internal static void Store(string password)
        {
            var name = Text("DefaultPassword"); var value = password == null ? new Unicode() : Text(password); IntPtr policy = IntPtr.Zero;
            try
            {
                policy = Open();
                uint result = password == null ? LsaDeleteSecret(policy, ref name, IntPtr.Zero) : LsaStorePrivateData(policy, ref name, ref value);
                if (password != null || result != 0xc0000034) Check(result);
            }
            finally { if (policy != IntPtr.Zero) LsaClose(policy); Free(name); Free(value); }
        }
        internal static bool MatchesAccount(string user) { return Read(null, user); }
        internal static bool EqualsSecret(string expected) { return Read(expected, null); }
        private static bool Read(string expected, string user)
        {
            var name = Text("DefaultPassword"); IntPtr policy = IntPtr.Zero, data = IntPtr.Zero;
            try
            {
                policy = Open(); uint result = LsaRetrievePrivateData(policy, ref name, out data);
                if (result == 0xc0000034) return false;
                Check(result); var value = (Unicode)Marshal.PtrToStructure(data, typeof(Unicode));
                if (value.Length>254 || value.Length%2!=0 || value.Buffer==IntPtr.Zero) return false;
                if (user != null)
                {
                    // LSA strings need not be terminated; never pass that buffer
                    // directly to the null-terminated LogonUser API.
                    var terminated = new Unicode { Buffer=Marshal.AllocHGlobal(value.Length+2), Length=value.Length, MaximumLength=checked((ushort)(value.Length+2)) };
                    try
                    {
                        for (int i=0;i<value.Length;i++) Marshal.WriteByte(terminated.Buffer,i,Marshal.ReadByte(value.Buffer,i));
                        Marshal.WriteInt16(terminated.Buffer,value.Length,0);
                        IntPtr token; bool valid=LogonUser(user, Environment.MachineName, terminated.Buffer, 2, 0, out token);
                        if (valid) CloseHandle(token);
                        return valid;
                    }
                    finally { Free(terminated); }
                }
                if (value.Length != expected.Length * 2) return false;
                int different = 0; for (int i = 0; i < expected.Length; i++) different |= (ushort)Marshal.ReadInt16(value.Buffer, i * 2) ^ expected[i];
                return different == 0;
            }
            finally
            {
                if (data != IntPtr.Zero) { var value = (Unicode)Marshal.PtrToStructure(data, typeof(Unicode)); for (int i = 0; i < value.Length; i++) Marshal.WriteByte(value.Buffer, i, 0); LsaFreeMemory(data); }
                if (policy != IntPtr.Zero) LsaClose(policy); Free(name);
            }
        }
        private static void Check(uint status) { if (status != 0) throw new Win32Exception((int)LsaNtStatusToWinError(status)); }
        [DllImport("advapi32.dll")] private static extern uint LsaOpenPolicy(IntPtr system, ref Attributes attributes, uint access, out IntPtr policy);
        [DllImport("advapi32.dll")] private static extern uint LsaClose(IntPtr policy);
        [DllImport("advapi32.dll")] private static extern uint LsaStorePrivateData(IntPtr policy, ref Unicode name, ref Unicode value);
        [DllImport("advapi32.dll", EntryPoint="LsaStorePrivateData")] private static extern uint LsaDeleteSecret(IntPtr policy, ref Unicode name, IntPtr value);
        [DllImport("advapi32.dll")] private static extern uint LsaRetrievePrivateData(IntPtr policy, ref Unicode name, out IntPtr value);
        [DllImport("advapi32.dll")] private static extern uint LsaFreeMemory(IntPtr data);
        [DllImport("advapi32.dll")] private static extern uint LsaNtStatusToWinError(uint status);
        [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool LogonUser(string user, string domain, IntPtr password, uint type, uint provider, out IntPtr token);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}

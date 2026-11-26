// =====================================================================
//  Icewolf Setup / Uninstaller / Updater
//  Compiled with the C# compiler that ships with Windows (.NET Framework 4.x),
//  so building needs no extra software. Language level: C# 5.
//
//  Icewolf = LibreWolf browser engine (signed by its publisher) + Icewolf layer
//  (logo, frost theme, debloat, locked privacy settings).
//
//  IcewolfSetup.exe       this program + embedded LibreWolf package + Icewolf config
//  Icewolf-Uninstall.exe  this program without payload            (uninstall mode)
//  Icewolf-Updater.exe    same small program, started with /update (silent updater)
// =====================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Icewolf.Setup
{
    // ------------------------------------------------------------------ entry point
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
#if PORTABLE
            return Portable.Run(args);
#else
            // Silent background updater (started by the scheduled task, runs as SYSTEM)
            string exeName = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
            if (HasArg(args, "/update") ||
                (string.Equals(exeName, "Icewolf-Updater", StringComparison.OrdinalIgnoreCase) && !HasArg(args, "/uninstall")))
                return Updater.Run();

            try
            {
                bool hasPayload = Res.Has("engine.zip");
                bool uninstall = HasArg(args, "/uninstall") || !hasPayload;

                // The uninstaller first copies itself to a private temp folder and
                // runs from there, so the whole install folder can be removed.
                if (uninstall && !HasArg(args, "/stage2"))
                {
                    Engine.RelaunchFromPrivateTemp();
                    return 0;
                }

                if (!uninstall)
                {
                    if (!Environment.Is64BitOperatingSystem || Environment.OSVersion.Version.Major < 10)
                    {
                        MessageBox.Show(L.T("Icewolf requires 64-bit Windows 10 or Windows 11.",
                                            "Icewolf benötigt 64-Bit Windows 10 oder Windows 11."),
                                        "Icewolf", MessageBoxButton.OK, MessageBoxImage.Error);
                        return 1;
                    }
                }

                bool created;
                using (var mutex = new Mutex(true, @"Global\IcewolfSetupRunning", out created))
                {
                    if (!created)
                    {
                        MessageBox.Show(L.T("Icewolf Setup is already running.", "Icewolf Setup läuft bereits."),
                                        "Icewolf", MessageBoxButton.OK, MessageBoxImage.Information);
                        return 1;
                    }
                    var app = new Application();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    int rc = app.Run(new SetupWindow(uninstall));
                    mutex.ReleaseMutex();
                    return rc;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Icewolf Setup:\n\n" + ex.Message, "Icewolf", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
#endif
        }

        internal static bool HasArg(string[] args, string name)
        {
            foreach (string a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    // ------------------------------------------------------------------ language
    internal static class L
    {
        public static readonly bool De = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
        public static string T(string en, string de) { return De ? de : en; }
    }

    internal sealed class SetupException : Exception
    {
        public SetupException(string message) : base(message) { }
    }

    // ------------------------------------------------------------------ embedded resources
    internal static class Res
    {
        public static Stream Open(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        }

        public static bool Has(string name)
        {
            using (Stream s = Open(name)) { return s != null; }
        }

        public static void Extract(string name, string path)
        {
            using (Stream s = Open(name))
            {
                if (s == null) throw new SetupException("Missing embedded file: " + name);
                using (var f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                    s.CopyTo(f);
            }
        }

        public static string Text(string name)
        {
            using (Stream s = Open(name))
            {
                if (s == null) throw new SetupException("Missing embedded file: " + name);
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        public static BitmapSource Image(string name)
        {
            using (Stream s = Open(name))
            {
                if (s == null) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = s;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }
    }

    // ------------------------------------------------------------------ paths & constants
    internal static class Paths
    {
        public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Icewolf";
        public const string TaskbarId = "Icewolf.Browser";
        public const string UpdateTaskName = "Icewolf Updater";
        public const string OverridesMarker = "// Icewolf overrides";

        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Icewolf"); }
        }
        public static string EngineDir      { get { return Path.Combine(InstallDir, "engine"); } }
        public static string BrowserExe     { get { return Path.Combine(EngineDir, Rebrand.BrandedExeName); } }
        public static string IconFile       { get { return Path.Combine(InstallDir, "icewolf.ico"); } }
        public static string Uninstaller    { get { return Path.Combine(InstallDir, "Icewolf-Uninstall.exe"); } }
        public static string UpdaterExe     { get { return Path.Combine(InstallDir, "Icewolf-Updater.exe"); } }
        public static string PolicyAddons   { get { return Path.Combine(InstallDir, "icewolf-policies.json"); } }
        public static string UpdateLog      { get { return Path.Combine(InstallDir, "update-log.txt"); } }

        // Getem integration
        public const string GetemHostName = "icewolf_getem";
        public const string GetemExtensionId = "getem@icewolf.local";
        public const string NativeHostsKey = @"SOFTWARE\Mozilla\NativeMessagingHosts\" + GetemHostName;
        public static string GetemDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Icewolf", "Getem"); }
        }
        public static string GetemHostDir   { get { return Path.Combine(InstallDir, "getem-host"); } }
        public static string GetemLnk
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Getem.lnk"); }
        }

        public static string ProfileRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Icewolf"); }
        }
        public static string ProfileDir     { get { return Path.Combine(ProfileRoot, "Profile"); } }
        public static string OverridesFile
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                    ".librewolf", "librewolf.overrides.cfg");
            }
        }
        public static string StartMenuLnk
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Icewolf.lnk"); }
        }
        public static string DesktopLnk
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Icewolf.lnk"); }
        }

        /// <summary>
        /// Creates a temp folder that only Administrators and SYSTEM can access,
        /// so nothing can swap files between verification and (elevated) use.
        /// </summary>
        public static string CreatePrivateTempDir()
        {
            string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var me = WindowsIdentity.GetCurrent().User;

            for (int i = 0; i < 5; i++)
            {
                string dir = Path.Combine(baseDir, "Icewolf-" + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(dir) || File.Exists(dir)) continue;

                var ds = new DirectorySecurity();
                ds.SetAccessRuleProtection(true, false);
                var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                ds.AddAccessRule(new FileSystemAccessRule(admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                ds.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                Directory.CreateDirectory(dir, ds);

                var owner = Directory.GetAccessControl(dir).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
                if (owner != null && (owner.Equals(admins) || owner.Equals(system) || owner.Equals(me)))
                    return dir;
            }
            throw new SetupException("Could not create a private temporary folder.");
        }
    }

    // ------------------------------------------------------------------ signature checks
    internal static class Authenticode
    {
        /// <summary>Organisation in the code-signing certificate of LibreWolf's Windows builds (via OSSign).</summary>
        public const string LibreWolfSigner = "Scheibling Consulting AB";

        private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, IntPtr data);

        private const uint WTD_UI_NONE = 2;
        private const uint WTD_REVOKE_NONE = 0;
        private const uint WTD_CHOICE_FILE = 1;
        private const uint WTD_STATEACTION_VERIFY = 1;
        private const uint WTD_STATEACTION_CLOSE = 2;
        private const uint WTD_DISABLE_MD2_MD4 = 0x2000;

        private const uint TRUST_E_NOSIGNATURE = 0x800B0100;
        private const uint TRUST_E_SUBJECT_FORM_UNKNOWN = 0x800B0003;

        /// <summary>
        /// True if the file is intact and carries a valid signature from a trusted publisher.
        /// "unsigned" tells apart a file without any signature from one whose signature is broken.
        /// </summary>
        public static bool IsValidlySigned(string path, out string reason, out bool unsigned)
        {
            reason = "";
            unsigned = false;
            var fi = new WINTRUST_FILE_INFO();
            fi.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            fi.pcwszFilePath = path;

            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            IntPtr pData = IntPtr.Zero;
            bool fileMarshaled = false;
            int hr;
            try
            {
                Marshal.StructureToPtr(fi, pFile, false);
                fileMarshaled = true;

                var wd = new WINTRUST_DATA();
                wd.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
                wd.dwUIChoice = WTD_UI_NONE;
                wd.fdwRevocationChecks = WTD_REVOKE_NONE;
                wd.dwUnionChoice = WTD_CHOICE_FILE;
                wd.pFile = pFile;
                wd.dwStateAction = WTD_STATEACTION_VERIFY;
                wd.dwProvFlags = WTD_DISABLE_MD2_MD4;

                pData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_DATA)));
                Marshal.StructureToPtr(wd, pData, false);

                hr = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, pData);

                wd = (WINTRUST_DATA)Marshal.PtrToStructure(pData, typeof(WINTRUST_DATA));
                wd.dwStateAction = WTD_STATEACTION_CLOSE;
                Marshal.StructureToPtr(wd, pData, false);
                WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, pData);
            }
            finally
            {
                if (pData != IntPtr.Zero) Marshal.FreeHGlobal(pData);
                if (fileMarshaled) Marshal.DestroyStructure(pFile, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(pFile);
            }

            if (hr != 0)
            {
                uint code = unchecked((uint)hr);
                unsigned = code == TRUST_E_NOSIGNATURE || code == TRUST_E_SUBJECT_FORM_UNKNOWN;
                reason = unsigned ? "not signed" : "signature invalid (0x" + hr.ToString("X8") + ")";
                return false;
            }
            return true;
        }

        public static string Signer(string path)
        {
            try { return new X509Certificate2(X509Certificate.CreateFromSignedFile(path)).Subject; }
            catch { return ""; }
        }

        /// <summary>
        /// Verifies a LibreWolf package folder. Throws on failure:
        ///  - librewolf.exe, xul.dll and every .exe must be signed by LibreWolf's publisher;
        ///  - no file may have a broken (tampered) signature;
        ///  - unsigned helper libraries are allowed (the package's SHA256 is verified separately).
        /// </summary>
        public static void VerifyPackage(string dir)
        {
            foreach (string must in new string[] { "librewolf.exe", "xul.dll" })
                if (!File.Exists(Path.Combine(dir, must)))
                    throw new SetupException("Missing " + must + " in browser package.");

            foreach (string f in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".exe" && ext != ".dll") continue;
                string name = Path.GetFileName(f);
                bool isMain = ext == ".exe" || string.Equals(name, "xul.dll", StringComparison.OrdinalIgnoreCase);

                string why;
                bool unsigned;
                if (IsValidlySigned(f, out why, out unsigned))
                {
                    if (isMain && Signer(f).IndexOf(LibreWolfSigner, StringComparison.OrdinalIgnoreCase) < 0)
                        throw new SetupException(L.T("Security check failed: ", "Sicherheitsprüfung fehlgeschlagen: ")
                                                 + name + " is not signed by LibreWolf's publisher (" + Signer(f) + ").");
                }
                else if (!(unsigned && !isMain))
                {
                    throw new SetupException(L.T("Security check failed for ", "Sicherheitsprüfung fehlgeschlagen für ")
                                             + name + ": " + why);
                }
            }
        }
    }

    // ------------------------------------------------------------------ shared engine logic
    internal static class Engine
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);
        private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

        public static bool BrowserRunning() { return BrowserRunning(Paths.EngineDir); }

        public static bool BrowserRunning(string engineDir)
        {
            string prefix = engineDir.TrimEnd('\\') + "\\";
            foreach (string name in new string[] { "icewolf", "librewolf" })
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (p.MainModule.FileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            return false;
        }

        // ---------------------------------------------------------- install
        public static void Install(bool desktopShortcut, Action<double, string> progress)
        {
            if (BrowserRunning())
                throw new SetupException(L.T("Icewolf is running. Please close it and try again.",
                                             "Icewolf läuft gerade. Bitte schließen und erneut versuchen."));

            string tmp = Paths.CreatePrivateTempDir();
            try
            {
                // 1. unpack the embedded browser engine
                progress(0.04, L.T("Unpacking browser engine...", "Browser-Engine wird entpackt..."));
                string zip = Path.Combine(tmp, "engine.zip");
                Res.Extract("engine.zip", zip);
                string unpacked = Path.Combine(tmp, "x");
                SafeExtract(zip, unpacked, delegate(double f)
                {
                    progress(0.06 + 0.40 * f, L.T("Unpacking browser engine...", "Browser-Engine wird entpackt..."));
                });
                File.Delete(zip);
                string package = FindPackageRoot(unpacked);

                // 2. verify digital signatures of every program file on this PC
                progress(0.48, L.T("Verifying digital signatures...", "Digitale Signaturen werden geprüft..."));
                Authenticode.VerifyPackage(package);

                // 3. put the engine in place
                progress(0.62, L.T("Installing browser engine...", "Browser-Engine wird installiert..."));
                Directory.CreateDirectory(Paths.InstallDir);
                ReplaceEngine(package);
                Rebrand.Apply(Paths.EngineDir);

                // 4. Icewolf layer in Program Files
                progress(0.78, L.T("Applying privacy and security settings...", "Datenschutz- und Sicherheitseinstellungen werden angewendet..."));
                Res.Extract("policies.json", Paths.PolicyAddons);
                MergePolicies();
                Res.Extract("icewolf.ico", Paths.IconFile);
                Res.Extract("Uninstaller.exe", Paths.Uninstaller);
                Res.Extract("Uninstaller.exe", Paths.UpdaterExe);

                // 5. profile + locked settings
                string chrome = Path.Combine(Paths.ProfileDir, "chrome");
                string icons = Path.Combine(chrome, "icons", "default");
                Directory.CreateDirectory(icons);
                Res.Extract("user.js", Path.Combine(Paths.ProfileDir, "user.js"));
                Res.Extract("userChrome.css", Path.Combine(chrome, "userChrome.css"));
                Res.Extract("userContent.css", Path.Combine(chrome, "userContent.css"));
                Res.Extract("icewolf.ico", Path.Combine(icons, "main-window.ico"));
                Res.Extract("icewolf.ico", Path.Combine(icons, "default.ico"));
                foreach (string f in new string[] { "logins.json", "logins-backup.json", "autofill-profiles.json",
                                                    "formhistory.sqlite", "formhistory.sqlite-wal", "formhistory.sqlite-shm" })
                    TryDeleteFile(Path.Combine(Paths.ProfileDir, f));
                WriteOverrides();
                DeleteDir(Path.Combine(Paths.ProfileDir, "startupCache"));

                Getem.UnregisterHost(RegistryHive.LocalMachine);     // from an earlier Icewolf version
                if (Getem.Bundled)
                {
                    // Getem: program files for the user, helper + menu entry
                    progress(0.84, L.T("Installing Getem...", "Getem wird installiert..."));
                    Getem.InstallFiles(tmp, Paths.GetemDir, true);
                    Directory.CreateDirectory(Paths.GetemHostDir);
                    Res.Extract("GetemHost.exe", Path.Combine(Paths.GetemHostDir, "GetemHost.exe"));
                    Rebrand.InstallGetemButton(Paths.EngineDir);
                }
                else
                {
                    // Version without Getem: remove the helper of an earlier "with Getem" install
                    // (Getem data in %LOCALAPPDATA%\Icewolf\Getem is kept)
                    DeleteDir(Paths.GetemHostDir);
                    TryDeleteFile(Paths.GetemLnk);
                    Rebrand.InstallScripts(Paths.EngineDir);          // core script only
                }

                // 6. shortcuts with the Icewolf taskbar identity
                progress(0.88, L.T("Creating shortcuts...", "Verknüpfungen werden erstellt..."));
                SetTaskbarId();
                ShellLink.Create(Paths.StartMenuLnk);
                if (desktopShortcut) ShellLink.Create(Paths.DesktopLnk);
                if (Getem.Bundled)
                    ShellLink.CreateSimple(Paths.GetemLnk, Path.Combine(Paths.GetemHostDir, "GetemHost.exe"), "/start",
                                           Paths.GetemDir, "Getem - video search & download");

                // 7. Apps entry + automatic updates
                progress(0.94, L.T("Enabling automatic security updates...", "Automatische Sicherheitsupdates werden aktiviert..."));
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = hklm.CreateSubKey(Paths.UninstallKey))
                {
                    k.SetValue("DisplayName", "Icewolf");
                    k.SetValue("DisplayVersion", BuildInfo.EngineVersion);
                    k.SetValue("EngineVersion", BuildInfo.EngineVersion);
                    k.SetValue("Publisher", "Icewolf Project");
                    k.SetValue("DisplayIcon", Paths.IconFile);
                    k.SetValue("InstallLocation", Paths.InstallDir);
                    k.SetValue("UninstallString", "\"" + Paths.Uninstaller + "\" /uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", DirSizeKb(Paths.InstallDir), RegistryValueKind.DWord);
                }
                CreateUpdateTask(tmp);

                progress(1.0, L.T("Done", "Fertig"));
            }
            finally
            {
                DeleteDir(tmp);
            }
        }

        // ---------------------------------------------------------- uninstall
        public static void Uninstall(bool removeProfile, Action<double, string> progress)
        {
            if (BrowserRunning())
                throw new SetupException(L.T("Icewolf is running. Please close it and try again.",
                                             "Icewolf läuft gerade. Bitte schließen und erneut versuchen."));

            progress(0.10, L.T("Removing automatic updates...", "Automatische Updates werden entfernt..."));
            RunHidden(SchTasks(), "/Delete /TN \"" + Paths.UpdateTaskName + "\" /F");

            progress(0.30, L.T("Removing shortcuts...", "Verknüpfungen werden entfernt..."));
            TryDeleteFile(Paths.StartMenuLnk);
            TryDeleteFile(Paths.DesktopLnk);
            RemoveTaskbarId();

            progress(0.50, L.T("Removing files...", "Dateien werden entfernt..."));
            string dir = Paths.InstallDir;
            if (string.Equals(Path.GetFileName(dir), "Icewolf", StringComparison.OrdinalIgnoreCase) && dir.Length > 10)
                DeleteDir(dir);

            using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                hklm.DeleteSubKeyTree(Paths.UninstallKey, false);

            RemoveOverrides();
            TryDeleteFile(Paths.GetemLnk);
            Getem.UnregisterHost(RegistryHive.LocalMachine);

            if (removeProfile)
            {
                progress(0.85, L.T("Removing profile...", "Profil wird entfernt..."));
                string prof = Paths.ProfileRoot;
                if (string.Equals(Path.GetFileName(prof), "Icewolf", StringComparison.OrdinalIgnoreCase))
                    DeleteDir(prof);
                // Getem program + its Python environment (your downloads in "Downloads\Getem" are kept)
                DeleteDir(Paths.GetemDir);
            }
            progress(1.0, L.T("Done", "Fertig"));
        }

        // ---------------------------------------------------------- engine files
        /// <summary>Extracts a zip, refusing any entry that would land outside the target folder.</summary>
        public static void SafeExtract(string zipPath, string target, Action<double> onProgress)
        {
            Directory.CreateDirectory(target);
            string root = Path.GetFullPath(target).TrimEnd('\\') + "\\";
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                int total = zip.Entries.Count, n = 0;
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    n++;
                    string dest = Path.GetFullPath(Path.Combine(target, e.FullName.Replace('/', '\\')));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new SetupException("Unsafe path in browser package: " + e.FullName);
                    if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\"))
                    {
                        Directory.CreateDirectory(dest);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                    if (onProgress != null && (n % 25 == 0 || n == total)) onProgress((double)n / Math.Max(1, total));
                }
            }
        }

        /// <summary>Finds the folder that contains librewolf.exe inside the extracted package.</summary>
        public static string FindPackageRoot(string dir)
        {
            if (File.Exists(Path.Combine(dir, "librewolf.exe"))) return dir;
            foreach (string sub in Directory.GetDirectories(dir))
            {
                if (File.Exists(Path.Combine(sub, "librewolf.exe"))) return sub;
                foreach (string sub2 in Directory.GetDirectories(sub))
                    if (File.Exists(Path.Combine(sub2, "librewolf.exe"))) return sub2;
            }
            throw new SetupException("librewolf.exe not found in browser package.");
        }

        /// <summary>
        /// Swaps in a verified engine folder. The files are copied (not moved) so they get the
        /// normal Program Files permissions; the old engine is kept until the new one is complete.
        /// </summary>
        public static void ReplaceEngine(string newEngine) { ReplaceEngine(newEngine, Paths.EngineDir); }

        public static void ReplaceEngine(string newEngine, string engineDir)
        {
            string engine = engineDir;
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string staged = engine + ".new-" + suffix;
            string old = engine + ".old-" + suffix;

            CopyDir(newEngine, staged);
            bool hadOld = Directory.Exists(engine);
            try
            {
                if (hadOld) Directory.Move(engine, old);
                Directory.Move(staged, engine);
            }
            catch
            {
                if (hadOld && !Directory.Exists(engine) && Directory.Exists(old)) Directory.Move(old, engine);   // roll back
                DeleteDir(staged);
                throw;
            }
            if (hadOld) DeleteDir(old);
        }

        /// <summary>Adds Icewolf's policies to LibreWolf's own policies.json (re-done after every update).</summary>
        public static void MergePolicies() { MergePolicies(Paths.EngineDir, Paths.PolicyAddons); }

        public static void MergePolicies(string engineDir, string addonsFile)
        {
            var ser = new JavaScriptSerializer();
            string dist = Path.Combine(engineDir, "distribution");
            string target = Path.Combine(dist, "policies.json");
            Directory.CreateDirectory(dist);

            Dictionary<string, object> doc = null;
            if (File.Exists(target))
            {
                try { doc = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(target)); } catch { doc = null; }
            }
            if (doc == null) doc = new Dictionary<string, object>();
            Dictionary<string, object> policies = null;
            object existing;
            if (doc.TryGetValue("policies", out existing)) policies = existing as Dictionary<string, object>;
            if (policies == null) { policies = new Dictionary<string, object>(); doc["policies"] = policies; }

            var addons = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(addonsFile));
            var add = addons["policies"] as Dictionary<string, object>;
            if (add != null) DeepMerge(policies, add);

            File.WriteAllText(target, ser.Serialize(doc), new UTF8Encoding(false));
        }

        /// <summary>
        /// Merges Icewolf's values into LibreWolf's: nested settings are combined key by key
        /// (e.g. "SearchEngines": only "Default" is set, LibreWolf's engine list is kept).
        /// </summary>
        private static void DeepMerge(Dictionary<string, object> target, Dictionary<string, object> add)
        {
            foreach (KeyValuePair<string, object> kv in add)
            {
                object existing;
                var addDict = kv.Value as Dictionary<string, object>;
                if (addDict != null && target.TryGetValue(kv.Key, out existing) && existing is Dictionary<string, object>)
                    DeepMerge((Dictionary<string, object>)existing, addDict);
                else if (kv.Value is System.Collections.IList && target.TryGetValue(kv.Key, out existing) && existing is System.Collections.IList)
                    target[kv.Key] = MergeLists((System.Collections.IList)existing, (System.Collections.IList)kv.Value);
                else
                    target[kv.Key] = kv.Value;
            }
            // DuckDuckGo is Icewolf's search engine: it must never be on a "remove" list.
            object se;
            if (target.TryGetValue("SearchEngines", out se) && se is Dictionary<string, object>)
            {
                object rem;
                var engines = (Dictionary<string, object>)se;
                if (engines.TryGetValue("Remove", out rem) && rem is System.Collections.IList)
                {
                    var kept = new System.Collections.ArrayList();
                    foreach (object o in (System.Collections.IList)rem)
                        if (!string.Equals(o as string, "DuckDuckGo", StringComparison.OrdinalIgnoreCase)) kept.Add(o);
                    engines["Remove"] = kept;
                }
            }
        }

        /// <summary>Lists are combined: LibreWolf's entries stay, Icewolf's are added unless an
        /// entry with the same value (or the same "Name") is already there.</summary>
        private static System.Collections.ArrayList MergeLists(System.Collections.IList existing, System.Collections.IList add)
        {
            var result = new System.Collections.ArrayList(existing);
            foreach (object item in add)
            {
                bool dup = false;
                var itemDict = item as Dictionary<string, object>;
                foreach (object old in result)
                {
                    var oldDict = old as Dictionary<string, object>;
                    if (itemDict != null && oldDict != null)
                    {
                        object a, b;
                        if (itemDict.TryGetValue("Name", out a) && oldDict.TryGetValue("Name", out b) &&
                            string.Equals(a as string, b as string, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                    }
                    else if (Equals(item, old)) { dup = true; break; }
                }
                if (!dup) result.Add(item);
            }
            return result;
        }

        private static void WriteOverrides()
        {
            string file = Paths.OverridesFile;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            if (File.Exists(file))
            {
                string current = File.ReadAllText(file);
                if (!current.StartsWith(Paths.OverridesMarker, StringComparison.Ordinal))
                {
                    string backup = file + ".before-icewolf";
                    if (!File.Exists(backup)) File.Copy(file, backup);
                }
            }
            File.WriteAllText(file, Res.Text("librewolf.overrides.cfg"), new UTF8Encoding(false));
            // The portable engine build may read the override file from the profile folder instead
            File.WriteAllText(Path.Combine(Paths.ProfileDir, "librewolf.overrides.cfg"), Res.Text("librewolf.overrides.cfg"), new UTF8Encoding(false));
        }

        private static void RemoveOverrides()
        {
            try
            {
                string file = Paths.OverridesFile;
                if (File.Exists(file) && File.ReadAllText(file).StartsWith(Paths.OverridesMarker, StringComparison.Ordinal))
                    File.Delete(file);
                string backup = file + ".before-icewolf";
                if (File.Exists(backup) && !File.Exists(file)) File.Move(backup, file);
            }
            catch { }
        }

        // ---------------------------------------------------------- taskbar identity
        /// <summary>
        /// The browser reads its Windows taskbar ID from SOFTWARE\Mozilla\[Name]\TaskBarIDs
        /// ([Name] from the engine's application.ini, i.e. "LibreWolf"). Our shortcuts get the same ID, so Windows
        /// shows the Icewolf icon on the taskbar.
        /// </summary>
        public static void SetTaskbarId()
        {
            foreach (string keyPath in TaskbarKeyPaths())
                foreach (RegistryKey root in TaskbarRoots())
                {
                    using (root)
                    using (RegistryKey k = root.CreateSubKey(keyPath))
                    {
                        RemoveOurValues(k);
                        k.SetValue(Paths.EngineDir, Paths.TaskbarId, RegistryValueKind.String);
                    }
                }
        }

        private static void RemoveTaskbarId()
        {
            foreach (string keyPath in TaskbarKeyPaths())
                foreach (RegistryKey root in TaskbarRoots())
                {
                    try
                    {
                        using (root)
                        using (RegistryKey k = root.OpenSubKey(keyPath, true))
                            if (k != null) RemoveOurValues(k);
                    }
                    catch { }
                }
        }

        public static List<string> TaskbarKeyPaths() { return TaskbarKeyPaths(Paths.EngineDir); }

        public static List<string> TaskbarKeyPaths(string engineDir)
        {
            string vendor = "", name = "LibreWolf";
            try
            {
                string ini = Path.Combine(engineDir, "application.ini");
                if (File.Exists(ini))
                {
                    bool inApp = false;
                    foreach (string raw in File.ReadAllLines(ini))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("[")) { inApp = string.Equals(line, "[App]", StringComparison.OrdinalIgnoreCase); continue; }
                        if (!inApp) continue;
                        if (line.StartsWith("Vendor=", StringComparison.OrdinalIgnoreCase)) vendor = line.Substring(7).Trim();
                        else if (line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase)) name = line.Substring(5).Trim();
                    }
                }
            }
            catch { }

            var list = new List<string>();
            if (Regex.IsMatch(name, @"^[A-Za-z0-9 ._-]{1,64}$"))
            {
                // The engine always looks under "Mozilla\<Name>" (inherited from Firefox),
                // e.g. SOFTWARE\Mozilla\LibreWolf\TaskBarIDs - this is the one that matters.
                list.Add(@"SOFTWARE\Mozilla\" + name + @"\TaskBarIDs");
                if (vendor.Length > 0 && Regex.IsMatch(vendor, @"^[A-Za-z0-9 ._-]{1,64}$"))
                    list.Add(@"SOFTWARE\" + vendor + @"\" + name + @"\TaskBarIDs");
                list.Add(@"SOFTWARE\" + name + @"\TaskBarIDs");
            }
            return list;
        }

        private static RegistryKey[] TaskbarRoots()
        {
            return new RegistryKey[]
            {
                RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
                RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64)
            };
        }

        private static void RemoveOurValues(RegistryKey k)
        {
            foreach (string name in k.GetValueNames())
                if (string.Equals(name.TrimEnd('\\'), Paths.EngineDir, StringComparison.OrdinalIgnoreCase))
                    k.DeleteValue(name, false);
        }

        // ---------------------------------------------------------- automatic updates
        private static string SchTasks()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
        }

        /// <summary>Scheduled task: runs the updater as SYSTEM 3 minutes after login and every 4 hours.</summary>
        private static void CreateUpdateTask(string tmp)
        {
            string start = DateTime.Now.AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>Keeps the Icewolf browser engine up to date with security fixes.</Description></RegistrationInfo>\r\n" +
                "  <Triggers>\r\n" +
                "    <LogonTrigger><Enabled>true</Enabled><Delay>PT3M</Delay></LogonTrigger>\r\n" +
                "    <TimeTrigger><StartBoundary>" + start + "</StartBoundary><Enabled>true</Enabled>\r\n" +
                "      <Repetition><Interval>PT4H</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition></TimeTrigger>\r\n" +
                "  </Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <StartWhenAvailable>true</StartWhenAvailable>\r\n" +
                "    <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>\r\n" +
                "    <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>\r\n" +
                "    <Priority>7</Priority>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + System.Security.SecurityElement.Escape(Paths.UpdaterExe) +
                "</Command><Arguments>/update</Arguments></Exec></Actions>\r\n" +
                "</Task>\r\n";
            string xmlFile = Path.Combine(tmp, "task.xml");
            File.WriteAllText(xmlFile, xml, Encoding.Unicode);
            int code = RunHidden(SchTasks(), "/Create /TN \"" + Paths.UpdateTaskName + "\" /XML \"" + xmlFile + "\" /F");
            if (code != 0)
                throw new SetupException(string.Format(L.T(
                    "Automatic updates could not be enabled (code {0}).",
                    "Automatische Updates konnten nicht aktiviert werden (Code {0})."), code));
        }

        // ---------------------------------------------------------- launch / self handling
        /// <summary>Starts Icewolf as the normal (non-admin) user via Explorer - never elevated.</summary>
        public static void LaunchUnelevated()
        {
            try
            {
                if (!File.Exists(Paths.StartMenuLnk)) return;
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                var psi = new ProcessStartInfo(explorer, "\"" + Paths.StartMenuLnk + "\"");
                psi.UseShellExecute = false;
                Process.Start(psi).Dispose();
            }
            catch { }
        }

        public static void RelaunchFromPrivateTemp()
        {
            string self = Assembly.GetExecutingAssembly().Location;
            string tmp = Paths.CreatePrivateTempDir();
            string copy = Path.Combine(tmp, "Icewolf-Uninstall.exe");
            File.Copy(self, copy, true);
            var psi = new ProcessStartInfo(copy, "/uninstall /stage2");
            psi.UseShellExecute = false;
            psi.WorkingDirectory = tmp;
            Process.Start(psi).Dispose();
        }

        public static void ScheduleSelfDelete()
        {
            try
            {
                string self = Assembly.GetExecutingAssembly().Location;
                string dir = Path.GetDirectoryName(self);
                MoveFileEx(self, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                if (Path.GetFileName(dir).StartsWith("Icewolf-", StringComparison.OrdinalIgnoreCase))
                    MoveFileEx(dir, null, MOVEFILE_DELAY_UNTIL_REBOOT);
            }
            catch { }
        }

        // ---------------------------------------------------------- helpers
        public static int RunHidden(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        public static void CopyDirPublic(string src, string dst) { CopyDir(src, dst); }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        private static int DirSizeKb(string dir)
        {
            try
            {
                long total = 0;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    total += new FileInfo(f).Length;
                return (int)Math.Min(int.MaxValue, total / 1024);
            }
            catch { return 0; }
        }

        public static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        public static void DeleteDir(string dir)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (!Directory.Exists(dir)) return;
                    foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        File.SetAttributes(f, FileAttributes.Normal);
                    Directory.Delete(dir, true);
                    return;
                }
                catch { Thread.Sleep(500); }
            }
        }
    }

    // ------------------------------------------------------------------ silent background updater
    /// <summary>
    /// Runs as SYSTEM from the scheduled task. Checks LibreWolf's official release feed,
    /// downloads a newer engine package, verifies checksum + signatures, swaps it in and
    /// re-applies the Icewolf policies. Never runs while the browser is open.
    /// </summary>
    internal static class Updater
    {
        private const string ReleaseApi = "https://librewolf.dev/api/v1/repos/librewolf/bsys6/releases/latest";
        private const string DownloadBase = "https://dl.librewolf.net/librewolf/";

        public static int Run()
        {
            bool created;
            using (var mutex = new Mutex(true, @"Global\IcewolfUpdaterRunning", out created))
            {
                if (!created) return 0;
                try
                {
                    int rc = Check();
                    mutex.ReleaseMutex();
                    return rc;
                }
                catch (Exception ex)
                {
                    Log("ERROR: " + ex.Message);
                    return 1;
                }
            }
        }

        private static int Check()
        {
            if (!File.Exists(Paths.BrowserExe) || !File.Exists(Paths.PolicyAddons)) { Log("Icewolf is not installed - nothing to do."); return 0; }

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            string installed = InstalledVersion();
            string latest = LatestVersion();
            if (CompareVersions(latest, installed) <= 0) { Log("Up to date (" + installed + ")."); return 0; }
            if (Engine.BrowserRunning()) { Log("Update " + latest + " available - waiting until Icewolf is closed."); return 0; }

            Log("Updating engine " + installed + " -> " + latest);
            string tmp = Paths.CreatePrivateTempDir();
            try
            {
                if (!DownloadVerifyReplace(latest, Paths.EngineDir, Paths.PolicyAddons, tmp, Log)) return 0;
                using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = hklm.OpenSubKey(Paths.UninstallKey, true))
                {
                    if (k != null)
                    {
                        k.SetValue("EngineVersion", latest);
                        k.SetValue("DisplayVersion", latest);
                    }
                }
                Log("Update to " + latest + " installed and verified.");
                return 0;
            }
            finally
            {
                Engine.DeleteDir(tmp);
            }
        }

        /// <summary>
        /// Downloads LibreWolf's signed portable build of the given version, verifies the SHA256
        /// checksum and all signatures, and swaps it in. Returns false if postponed (browser open).
        /// </summary>
        public static bool DownloadVerifyReplace(string version, string engineDir, string addonsFile, string tmp, Action<string> log)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string file = "librewolf-" + version + "-windows-x86_64-portable.zip";
            string zip = Path.Combine(tmp, "engine.zip");
            string sumFile = Path.Combine(tmp, "engine.sha256");
            Download(DownloadBase + version + "/" + file, zip);
            Download(DownloadBase + version + "/" + file + ".sha256sum", sumFile);

            string expected = File.ReadAllText(sumFile).Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant();
            if (!Regex.IsMatch(expected, "^[0-9a-f]{64}$")) throw new SetupException("Invalid checksum file.");
            string actual;
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(zip))
                actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            if (actual != expected) throw new SetupException("Checksum mismatch - download rejected.");

            string unpacked = Path.Combine(tmp, "x");
            Engine.SafeExtract(zip, unpacked, null);
            File.Delete(zip);
            string package = Engine.FindPackageRoot(unpacked);
            Authenticode.VerifyPackage(package);

            if (Engine.BrowserRunning(engineDir)) { log("Icewolf was started during download - update postponed."); return false; }
            Engine.ReplaceEngine(package, engineDir);
            Rebrand.Apply(engineDir);
            Engine.MergePolicies(engineDir, addonsFile);
            return true;
        }

        private static string InstalledVersion()
        {
            using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey k = hklm.OpenSubKey(Paths.UninstallKey))
            {
                string v = k == null ? null : k.GetValue("EngineVersion") as string;
                return string.IsNullOrEmpty(v) ? "0" : v;
            }
        }

        public static string LatestVersion()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            string json;
            using (var wc = NewClient()) json = wc.DownloadString(ReleaseApi);
            var doc = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object tag;
            if (!doc.TryGetValue("tag_name", out tag) || !(tag is string)) throw new SetupException("Release feed has no version.");
            string v = (string)tag;
            if (!Regex.IsMatch(v, @"^\d{1,4}(\.\d{1,4}){0,3}-\d{1,3}$")) throw new SetupException("Unexpected version format: " + v);
            return v;
        }

        /// <summary>Compares versions like "161.0.1-2" numerically. Returns &gt;0 if a is newer.</summary>
        public static int CompareVersions(string a, string b)
        {
            int[] x = Parts(a), y = Parts(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }

        private static int[] Parts(string v)
        {
            var list = new List<int>();
            string[] main = v.Split('-');
            foreach (string s in main[0].Split('.')) { int n; list.Add(int.TryParse(s, out n) ? n : 0); }
            while (list.Count < 4) list.Add(0);
            int rel = 0;
            if (main.Length > 1) int.TryParse(main[1], out rel);
            list.Add(rel);
            return list.ToArray();
        }

        private static WebClient NewClient()
        {
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "Icewolf-Updater";
            return wc;
        }

        private static void Download(string url, string path)
        {
            using (var wc = NewClient()) wc.DownloadFile(url, path);
        }

        private static void Log(string msg)
        {
            try
            {
                string log = Paths.UpdateLog;
                if (File.Exists(log) && new FileInfo(log).Length > 256 * 1024) File.Delete(log);
                File.AppendAllText(log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + msg + "\r\n");
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ program icon
    /// <summary>
    /// Creates engine\icewolf.exe: a copy of the verified librewolf.exe whose embedded program
    /// icons are replaced with the Icewolf logo. Windows takes the taskbar, window and Alt+Tab
    /// icon from the program file, so this shows the Icewolf logo everywhere.
    /// librewolf.exe itself stays untouched (and signed).
    /// </summary>
    internal static class Rebrand
    {
        public const string BrandedExeName = "icewolf.exe";

        private const uint LOAD_LIBRARY_AS_DATAFILE = 0x2;
        private const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;
        private static readonly IntPtr RT_ICON = new IntPtr(3);
        private static readonly IntPtr RT_GROUP_ICON = new IntPtr(14);
        private const ushort IconIdBase = 31000;

        private delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpType, IntPtr lpName, IntPtr lParam);
        private delegate bool EnumResLangProc(IntPtr hModule, IntPtr lpType, IntPtr lpName, ushort wLanguage, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW")]
        private static extern IntPtr LoadLibraryEx(string fileName, IntPtr hFile, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);
        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "EnumResourceNamesW")]
        private static extern bool EnumResourceNames(IntPtr hModule, IntPtr lpType, EnumResNameProc proc, IntPtr lParam);
        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "EnumResourceLanguagesW")]
        private static extern bool EnumResourceLanguages(IntPtr hModule, IntPtr lpType, IntPtr lpName, EnumResLangProc proc, IntPtr lParam);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "BeginUpdateResourceW")]
        private static extern IntPtr BeginUpdateResource(string fileName, bool deleteExisting);
        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "UpdateResourceW")]
        private static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort language, byte[] data, uint size);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "UpdateResourceW")]
        private static extern bool UpdateResourceNamed(IntPtr hUpdate, IntPtr lpType, string lpName, ushort language, byte[] data, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EndUpdateResource(IntPtr hUpdate, bool discard);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr FindResourceEx(IntPtr hModule, IntPtr lpType, IntPtr lpName, ushort language);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LockResource(IntPtr hResData);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);
        private static readonly IntPtr RT_VERSION = new IntPtr(16);

        [DllImport("imagehlp.dll", SetLastError = true)]
        private static extern bool ImageEnumerateCertificates(SafeFileHandle file, ushort typeFilter, out uint count, IntPtr indices, uint indexCount);
        [DllImport("imagehlp.dll", SetLastError = true)]
        private static extern bool ImageRemoveCertificate(SafeFileHandle file, uint index);

        private sealed class Group
        {
            public int Id;          // used when Name == null
            public string Name;
            public ushort Language;
            public byte[] Data;     // resource contents (only loaded when requested)
        }

        public static void CreateBrandedExe(string engineDir)
        {
            string src = Path.Combine(engineDir, "librewolf.exe");
            string dst = Path.Combine(engineDir, BrandedExeName);
            string tmp = dst + ".tmp";
            Engine.TryDeleteFile(tmp);
            File.Copy(src, tmp, true);
            try
            {
                byte[] ico;
                using (Stream s = Res.Open("icewolf.ico"))
                using (var ms = new MemoryStream())
                {
                    if (s == null) throw new SetupException("Missing embedded icon.");
                    s.CopyTo(ms);
                    ico = ms.ToArray();
                }
                StripSignature(tmp);    // the copy is modified, so its (now invalid) signature is removed
                List<Group> groups = ListResources(src, RT_GROUP_ICON, false);
                if (groups.Count == 0) groups.Add(new Group { Id = 1, Language = 0 });
                List<Group> versions = ListResources(src, RT_VERSION, true);
                ReplaceIcons(tmp, ico, groups, versions);
                Log("icewolf.exe: OK (" + groups.Count + " icon groups, " + versions.Count + " version blocks replaced)");
            }
            catch (Exception ex)
            {
                // Never leave Icewolf without a working program: fall back to a plain copy.
                Log("icewolf.exe: FAILED - " + ex.GetType().Name + ": " + ex.Message + " -> using plain copy (LibreWolf logo/name)");
                Engine.TryDeleteFile(tmp);
                File.Copy(src, tmp, true);
            }
            Engine.TryDeleteFile(dst);
            File.Move(tmp, dst);
        }

        /// <summary>
        /// Replaces "LibreWolf" (any case) with "Icewolf" in a UTF-16 version resource. The new
        /// name is padded with two NUL characters, so every length field in the structure stays valid.
        /// </summary>
        private static byte[] RenameInUtf16(byte[] data)
        {
            byte[] r = (byte[])data.Clone();
            const string from = "librewolf";
            for (int i = 0; i + from.Length * 2 <= r.Length; i += 2)
            {
                bool match = true;
                for (int c = 0; c < from.Length && match; c++)
                {
                    char ch = (char)(r[i + c * 2] | (r[i + c * 2 + 1] << 8));
                    if (char.ToLowerInvariant(ch) != from[c]) match = false;
                }
                if (!match) continue;
                bool upper = r[i] == (byte)'L';
                string to = (upper ? "Icewolf" : "icewolf") + "\0\0";
                for (int c = 0; c < to.Length; c++)
                {
                    r[i + c * 2] = (byte)to[c];
                    r[i + c * 2 + 1] = 0;
                }
                i += from.Length * 2 - 2;
            }
            return r;
        }

        /// <summary>
        /// Rebrands the browser's own interface inside the engine's resource archives
        /// (engine\omni.ja and engine\browser\omni.ja):
        ///  - branding texts: "LibreWolf" -> "Icewolf" (window title, About, Settings, prompts)
        ///  - branding images: every logo/icon/wordmark replaced with the Icewolf logo
        /// On any problem the original archive is kept.
        /// </summary>
        public static bool BrandInterface(string engineDir)
        {
            bool any = false;
            foreach (string omni in new string[] { Path.Combine(engineDir, "browser", "omni.ja"), Path.Combine(engineDir, "omni.ja") })
                if (File.Exists(omni) && BrandArchive(omni)) any = true;
            return any;
        }

        private static bool BrandArchive(string omni)
        {
            string tmp = omni + ".icewolf-tmp";
            Engine.TryDeleteFile(tmp);
            bool changed = false;
            int texts = 0, images = 0, entries = 0;
            try
            {
                BitmapSource logo = Res.Image("logo512.png");
                byte[] logoSvg = ResBytes("logo.svg");
                byte[] wordSvg = ResBytes("wordmark.svg");
                byte[] ico = ResBytes("icewolf.ico");

                using (ZipArchive src = ZipFile.OpenRead(omni))
                using (ZipArchive dst = ZipFile.Open(tmp, ZipArchiveMode.Create))
                {
                    foreach (ZipArchiveEntry e in src.Entries)
                    {
                        entries++;
                        ZipArchiveEntry ne = dst.CreateEntry(e.FullName, CompressionLevel.Optimal);
                        try { ne.LastWriteTime = e.LastWriteTime; } catch { }
                        using (Stream input = e.Open())
                        using (Stream output = ne.Open())
                        {
                            byte[] replacement = null;
                            if (IsBrandFile(e.FullName))
                            {
                                string text;
                                using (var reader = new StreamReader(input, new UTF8Encoding(false))) text = reader.ReadToEnd();
                                string renamed = text.Replace("LibreWolf", "Icewolf").Replace("Librewolf", "Icewolf");
                                replacement = new UTF8Encoding(false).GetBytes(renamed);
                                if (renamed != text) { changed = true; texts++; }
                            }
                            else if (IsBrandImage(e.FullName))
                            {
                                byte[] original = ReadAll(input);
                                replacement = ReplaceImage(e.FullName, original, logo, logoSvg, wordSvg, ico);
                                if (replacement != original) { changed = true; images++; }
                            }

                            if (replacement != null) output.Write(replacement, 0, replacement.Length);
                            else input.CopyTo(output);
                        }
                    }
                }
                Log(Path.GetFileName(Path.GetDirectoryName(omni)) + "\\omni.ja: " + entries + " entries, " + texts + " texts and " + images + " images rebranded");
                if (!changed) { Engine.TryDeleteFile(tmp); return false; }
                string backup = omni + ".original";
                Engine.TryDeleteFile(backup);
                File.Move(omni, backup);
                File.Move(tmp, omni);
                Engine.TryDeleteFile(backup);
                return true;
            }
            catch (Exception ex)
            {
                Log(omni + ": FAILED - " + ex.GetType().Name + ": " + ex.Message + " (original kept)");
                Engine.TryDeleteFile(tmp);
                if (!File.Exists(omni) && File.Exists(omni + ".original")) File.Move(omni + ".original", omni);
                return false;
            }
        }

        /// <summary>Logo/icon images of the engine's branding (any "branding" folder, or a file named after LibreWolf).</summary>
        private static bool IsBrandImage(string entry)
        {
            string n = entry.Replace('\\', '/').ToLowerInvariant();
            if (!(n.EndsWith(".png") || n.EndsWith(".svg") || n.EndsWith(".ico"))) return false;
            bool brandingFolder = n.IndexOf("/branding/", StringComparison.Ordinal) >= 0 || n.StartsWith("branding/", StringComparison.Ordinal);
            string file = n.Substring(n.LastIndexOf('/') + 1);
            return brandingFolder || file.IndexOf("librewolf", StringComparison.Ordinal) >= 0;
        }

        private static byte[] ReplaceImage(string entry, byte[] original, BitmapSource logo, byte[] logoSvg, byte[] wordSvg, byte[] ico)
        {
            try
            {
                string n = entry.ToLowerInvariant();
                if (n.EndsWith(".ico")) return ico ?? original;
                if (n.EndsWith(".svg"))
                {
                    bool wide = n.IndexOf("wordmark", StringComparison.Ordinal) >= 0 || n.IndexOf("text", StringComparison.Ordinal) >= 0;
                    return (wide ? wordSvg : logoSvg) ?? original;
                }
                // PNG: render the Icewolf logo at exactly the original pixel size
                if (logo == null || original.Length < 24 || original[0] != 0x89 || original[1] != 0x50) return original;
                int w = (original[16] << 24) | (original[17] << 16) | (original[18] << 8) | original[19];
                int h = (original[20] << 24) | (original[21] << 16) | (original[22] << 8) | original[23];
                if (w <= 0 || h <= 0 || w > 4096 || h > 4096) return original;
                return RenderLogo(logo, w, h);
            }
            catch { return original; }
        }

        private static byte[] RenderLogo(BitmapSource logo, int w, int h)
        {
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (DrawingContext dc = dv.RenderOpen())
            {
                double size = Math.Min(w, h);
                dc.DrawImage(logo, new Rect((w - size) / 2, (h - size) / 2, size, size));
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var ms = new MemoryStream())
            {
                enc.Save(ms);
                return ms.ToArray();
            }
        }

        private static byte[] ResBytes(string name)
        {
            using (Stream s = Res.Open(name))
            {
                if (s == null) return null;
                return ReadAll(s);
            }
        }

        private static byte[] ReadAll(Stream s)
        {
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static bool IsBrandFile(string entry)
        {
            string n = entry.Replace('\\', '/').ToLowerInvariant();
            if (n.IndexOf("/branding/", StringComparison.Ordinal) < 0 && !n.StartsWith("branding/", StringComparison.Ordinal)) return false;
            return n.EndsWith("brand.ftl") || n.EndsWith("brand.properties") || n.EndsWith("brand.dtd");
        }

        /// <summary>Program icon + program name + interface name, in one step.</summary>
        private static string logFile;

        /// <summary>Writes to branding-log.txt next to the engine folder (for troubleshooting).</summary>
        public static void Log(string msg)
        {
            try
            {
                if (logFile == null) return;
                if (File.Exists(logFile) && new FileInfo(logFile).Length > 256 * 1024) File.Delete(logFile);
                File.AppendAllText(logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + msg + "\r\n");
            }
            catch { }
        }

        public static void SetLog(string engineDir)
        {
            logFile = Path.Combine(Path.GetDirectoryName(engineDir.TrimEnd('\\')), "branding-log.txt");
        }

        public static void Apply(string engineDir)
        {
            SetLog(engineDir);
            Log("---- branding " + engineDir);
            CreateBrandedExe(engineDir);
            BrandInterface(engineDir);
            try { InstallScripts(engineDir); } catch (Exception ex) { Log("Browser scripts: FAILED - " + ex.Message); }
        }

        /// <summary>
        /// Built-in Getem menu entry (main menu): a small script loaded by the browser's own
        /// configuration mechanism (autoconfig). No extension and no relaxed security
        /// checks are needed. Only installed when the Getem helper exists.
        /// </summary>
        public static void InstallGetemButton(string engineDir) { InstallScripts(engineDir); }

        /// <summary>
        /// Icewolf's browser scripts (autoconfig): always the core script (no engine pin prompts),
        /// plus the Getem script that matches this version (menu entry, or tab + one-click button).
        /// </summary>
        public static void InstallScripts(string engineDir)
        {
            string root = Path.GetDirectoryName(engineDir.TrimEnd('\\'));
            string getemScript = null;
            if (File.Exists(Path.Combine(root, "getem-host", "GetemHost.exe")))
            {
                // Tab + one-click button: portable (getem\server.py next to the engine) and installed
                // (Getem lives in the user's AppData, so there is no getem folder here). Only an old
                // portable Getem folder without tab support keeps the plain menu entry.
                string localGetem = Path.Combine(root, "getem");
                getemScript = !Directory.Exists(localGetem) || File.Exists(Path.Combine(localGetem, "server.py"))
                    ? "getem-tab.js" : "getem-button.js";
            }
            Log("Browser scripts: core" + (getemScript != null ? " + " + getemScript : " (no Getem in this version)"));

            string js = Res.Text("icewolf-core.js");
            if (getemScript != null) js += "\n" + Res.Text(getemScript);
            js = "/* ICEWOLF-BEGIN */\n" + js.Replace("\r\n", "\n") + "\n/* ICEWOLF-END */";
            string prefDir = Path.Combine(engineDir, "defaults", "pref");
            var utf8 = new UTF8Encoding(false);

            // Does the engine already use an autoconfig file? Then add the button to it.
            if (Directory.Exists(prefDir))
            {
                foreach (string f in Directory.GetFiles(prefDir, "*.js"))
                {
                    if (string.Equals(Path.GetFileName(f), "icewolf-autoconfig.js", StringComparison.OrdinalIgnoreCase)) continue;
                    string prefs = File.ReadAllText(f);
                    Match m = Regex.Match(prefs, "pref\\(\\s*\"general\\.config\\.filename\"\\s*,\\s*\"([A-Za-z0-9._-]+)\"");
                    if (!m.Success) continue;
                    // only plain-text, unsandboxed config files can carry the button
                    if (Regex.IsMatch(prefs, "general\\.config\\.obscure_value\"\\s*,\\s*[1-9]")) { Log("Browser scripts: skipped (engine config is obfuscated)"); return; }
                    if (Regex.IsMatch(prefs, "general\\.config\\.sandbox_enabled\"\\s*,\\s*true")) { Log("Browser scripts: skipped (engine config runs sandboxed)"); return; }
                    string cfg = Path.Combine(engineDir, m.Groups[1].Value);
                    if (!File.Exists(cfg)) { Log("Browser scripts: skipped (" + m.Groups[1].Value + " missing)"); return; }
                    string current = File.ReadAllText(cfg);
                    if (current.IndexOf(js, StringComparison.Ordinal) >= 0) { Log("Browser scripts: up to date (" + m.Groups[1].Value + ")"); return; }
                    string text = RemoveButtonBlock(current);
                    File.WriteAllText(cfg, text.TrimEnd() + "\n\n" + js + "\n", utf8);
                    Log("Browser scripts: added to engine config " + m.Groups[1].Value);
                    return;
                }
            }

            Directory.CreateDirectory(prefDir);
            File.WriteAllText(Path.Combine(prefDir, "icewolf-autoconfig.js"),
                "// Icewolf: loads icewolf.cfg (Icewolf browser scripts)\n" +
                "pref(\"general.config.filename\", \"icewolf.cfg\");\n" +
                "pref(\"general.config.obscure_value\", 0);\n" +
                "pref(\"general.config.sandbox_enabled\", false);\n", utf8);
            string own = "// Icewolf configuration - the first line is ignored by the browser\n" + js + "\n";
            string ownPath = Path.Combine(engineDir, "icewolf.cfg");
            if (File.Exists(ownPath) && File.ReadAllText(ownPath) == own) { Log("Browser scripts: up to date (icewolf.cfg)"); return; }
            File.WriteAllText(ownPath, own, utf8);
            Log("Browser scripts: OK (icewolf.cfg)");
        }

        private static string RemoveButtonBlock(string text)
        {
            foreach (string[] m in new string[][] {
                new string[] { "/* ICEWOLF-BEGIN */", "/* ICEWOLF-END */" },
                new string[] { "/* ICEWOLF-GETEM-BEGIN", "/* ICEWOLF-GETEM-END */" } })
            {
                int a = text.IndexOf(m[0], StringComparison.Ordinal);
                int b = text.IndexOf(m[1], StringComparison.Ordinal);
                if (a >= 0 && b > a) text = text.Substring(0, a) + text.Substring(b + m[1].Length);
            }
            return text;
        }

        private static void StripSignature(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                uint count;
                if (!ImageEnumerateCertificates(fs.SafeFileHandle, 0xFF, out count, IntPtr.Zero, 0)) return;
                for (uint i = 0; i < count; i++) ImageRemoveCertificate(fs.SafeFileHandle, 0);
            }
        }

        private static List<Group> ListResources(string exe, IntPtr resType, bool loadData)
        {
            var result = new List<Group>();
            IntPtr module = LoadLibraryEx(exe, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
            if (module == IntPtr.Zero) return result;
            try
            {
                EnumResNameProc onName = delegate(IntPtr h, IntPtr type, IntPtr name, IntPtr lp)
                {
                    bool isId = ((long)name >> 16) == 0;
                    int id = isId ? (int)(long)name : 0;
                    string str = isId ? null : Marshal.PtrToStringUni(name);
                    EnumResLangProc onLang = delegate(IntPtr h2, IntPtr t2, IntPtr n2, ushort lang, IntPtr lp2)
                    {
                        var g = new Group { Id = id, Name = str, Language = lang };
                        if (loadData)
                        {
                            IntPtr info = FindResourceEx(h2, t2, n2, lang);
                            if (info == IntPtr.Zero) return true;
                            uint size = SizeofResource(h2, info);
                            IntPtr ptr = LockResource(LoadResource(h2, info));
                            if (ptr == IntPtr.Zero || size == 0 || size > 1024 * 1024) return true;
                            g.Data = new byte[size];
                            Marshal.Copy(ptr, g.Data, 0, (int)size);
                        }
                        result.Add(g);
                        return true;
                    };
                    EnumResourceLanguages(h, type, name, onLang, IntPtr.Zero);
                    GC.KeepAlive(onLang);
                    return true;
                };
                EnumResourceNames(module, resType, onName, IntPtr.Zero);
                GC.KeepAlive(onName);
            }
            finally { FreeLibrary(module); }
            return result;
        }

        private static void ReplaceIcons(string exe, byte[] ico, List<Group> groups, List<Group> versions)
        {
            if (ico.Length < 6 || BitConverter.ToUInt16(ico, 2) != 1) throw new SetupException("Invalid icon file.");
            int count = BitConverter.ToUInt16(ico, 4);
            if (count == 0 || count > 64 || ico.Length < 6 + 16 * count) throw new SetupException("Invalid icon file.");

            var images = new List<byte[]>();
            var group = new MemoryStream();
            var w = new BinaryWriter(group);
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)count);
            for (int i = 0; i < count; i++)
            {
                int o = 6 + 16 * i;
                uint size = BitConverter.ToUInt32(ico, o + 8);
                uint offset = BitConverter.ToUInt32(ico, o + 12);
                if ((long)offset + size > ico.Length) throw new SetupException("Invalid icon file.");
                var img = new byte[size];
                Buffer.BlockCopy(ico, (int)offset, img, 0, (int)size);
                images.Add(img);
                w.Write(ico, o, 12);                    // width, height, colors, reserved, planes, bit count, size
                w.Write((ushort)(IconIdBase + i));      // resource id instead of file offset
            }
            w.Flush();
            byte[] groupData = group.ToArray();

            var languages = new List<ushort>();
            foreach (Group g in groups) if (!languages.Contains(g.Language)) languages.Add(g.Language);

            IntPtr h = BeginUpdateResource(exe, false);
            if (h == IntPtr.Zero) throw new SetupException("BeginUpdateResource failed: " + Marshal.GetLastWin32Error());
            bool ok = false;
            try
            {
                foreach (ushort lang in languages)
                    for (int i = 0; i < images.Count; i++)
                        if (!UpdateResource(h, RT_ICON, new IntPtr(IconIdBase + i), lang, images[i], (uint)images[i].Length))
                            throw new SetupException("UpdateResource (icon) failed: " + Marshal.GetLastWin32Error());

                foreach (Group g in groups)
                {
                    bool done = g.Name == null
                        ? UpdateResource(h, RT_GROUP_ICON, new IntPtr(g.Id), g.Language, groupData, (uint)groupData.Length)
                        : UpdateResourceNamed(h, RT_GROUP_ICON, g.Name, g.Language, groupData, (uint)groupData.Length);
                    if (!done) throw new SetupException("UpdateResource (group) failed: " + Marshal.GetLastWin32Error());
                }
                // Program name shown by Windows (permission prompts, Task Manager, file properties)
                foreach (Group v in versions)
                {
                    if (v.Data == null) continue;
                    byte[] data = RenameInUtf16(v.Data);
                    bool done = v.Name == null
                        ? UpdateResource(h, RT_VERSION, new IntPtr(v.Id), v.Language, data, (uint)data.Length)
                        : UpdateResourceNamed(h, RT_VERSION, v.Name, v.Language, data, (uint)data.Length);
                    if (!done) throw new SetupException("UpdateResource (version) failed: " + Marshal.GetLastWin32Error());
                }
                ok = true;
            }
            finally
            {
                if (!EndUpdateResource(h, !ok) && ok)
                    throw new SetupException("EndUpdateResource failed: " + Marshal.GetLastWin32Error());
            }
        }
    }

    // ------------------------------------------------------------------ Getem integration
    /// <summary>
    /// Getem (video search & download, Python app) + its toolbar button:
    /// the Getem extension talks only to the "icewolf_getem" native messaging host
    /// (GetemHost.exe), which can do exactly two things: start Getem or run its updater.
    /// </summary>
    internal static class Getem
    {
        public const string XpiName = "getem@icewolf.xpi";

        /// <summary>True for the "Icewolf with Getem" setup.</summary>
        public static bool Bundled { get { return Res.Has("getem.zip"); } }

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
        private static readonly Guid FOLDERID_Downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        /// <summary>
        /// Copies the Getem program files into targetDir. Existing user data there
        /// (.venv, .cache, downloads) is kept. Optionally sets the default download folder
        /// to "Downloads\Getem" for a fresh installation.
        /// </summary>
        public static void InstallFiles(string tmp, string targetDir, bool useUserDownloads)
        {
            string zip = Path.Combine(tmp, "getem.zip");
            Res.Extract("getem.zip", zip);
            string unpacked = Path.Combine(tmp, "getem");
            Engine.SafeExtract(zip, unpacked, null);
            File.Delete(zip);
            Engine.CopyDirPublic(unpacked, targetDir);

            if (!useUserDownloads) return;
            string settings = Path.Combine(targetDir, ".cache", "settings.json");
            if (File.Exists(settings)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(settings));
            var cfg = new Dictionary<string, object>();
            cfg["folder"] = Path.Combine(DownloadsFolder(), "Getem");
            File.WriteAllText(settings, new JavaScriptSerializer().Serialize(cfg), new UTF8Encoding(false));
        }

        /// <summary>Writes the native messaging manifest next to the helper and registers it.</summary>
        public static void RegisterHost(string hostExe, RegistryHive hive)
        {
            string manifest = Path.Combine(Path.GetDirectoryName(hostExe), Paths.GetemHostName + ".json");
            var m = new Dictionary<string, object>();
            m["name"] = Paths.GetemHostName;
            m["description"] = "Icewolf: starts Getem";
            m["path"] = hostExe;
            m["type"] = "stdio";
            m["allowed_extensions"] = new string[] { Paths.GetemExtensionId };
            File.WriteAllText(manifest, new JavaScriptSerializer().Serialize(m), new UTF8Encoding(false));

            using (RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            using (RegistryKey k = root.CreateSubKey(Paths.NativeHostsKey))
                k.SetValue("", manifest, RegistryValueKind.String);
        }

        public static void UnregisterHost(RegistryHive hive)
        {
            try
            {
                using (RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                    root.DeleteSubKeyTree(Paths.NativeHostsKey, false);
            }
            catch { }
        }

        private static string DownloadsFolder()
        {
            IntPtr p;
            if (SHGetKnownFolderPath(FOLDERID_Downloads, 0, IntPtr.Zero, out p) == 0 && p != IntPtr.Zero)
            {
                try { return Marshal.PtrToStringUni(p); }
                finally { Marshal.FreeCoTaskMem(p); }
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    // ------------------------------------------------------------------ portable launcher
    /// <summary>
    /// Icewolf Portable: everything lives next to Icewolf.exe (engine, profile, settings),
    /// no installation and no administrator rights. Updates are downloaded and verified
    /// automatically after the browser has been closed.
    /// </summary>
    internal static class Portable
    {
        public const string TaskbarId = "Icewolf.Portable";

        private static string Root      { get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } }
        private static string Engine_   { get { return Path.Combine(Root, "engine"); } }
        private static string Profile   { get { return Path.Combine(Root, "profile"); } }
        private static string Addons    { get { return Path.Combine(Root, "icewolf-policies.json"); } }
        private static string VersionFile { get { return Path.Combine(Root, "engine-version.txt"); } }
        private static string CheckFile { get { return Path.Combine(Root, "last-update-check.txt"); } }
        private static string LockFile  { get { return Path.Combine(Root, "updating.lock"); } }
        private static string LogFile   { get { return Path.Combine(Root, "update-log.txt"); } }

        public static int Run(string[] args)
        {
            // Used by the build script to brand the engine before zipping the portable version
            if (args.Length == 2 && string.Equals(args[0], "/brand-engine", StringComparison.OrdinalIgnoreCase))
            {
                try { Rebrand.Apply(args[1]); return 0; } catch { return 1; }
            }
            if (args.Length == 2 && string.Equals(args[0], "/install-getem", StringComparison.OrdinalIgnoreCase))
            {
                try { Rebrand.SetLog(args[1]); Rebrand.InstallGetemButton(args[1]); return 0; } catch { return 1; }
            }
            if (args.Length == 2 && string.Equals(args[0], "/install-getem-tab", StringComparison.OrdinalIgnoreCase))
            {
                try { Rebrand.SetLog(args[1]); Rebrand.InstallScripts(args[1]); return 0; } catch { return 1; }
            }

            if (!File.Exists(Path.Combine(Engine_, Rebrand.BrandedExeName)) && File.Exists(Path.Combine(Engine_, "librewolf.exe")))
            {
                try { Rebrand.Apply(Engine_); Engine.DeleteDir(Path.Combine(Profile, "startupCache")); }
                catch (Exception ex) { Log("Branding: " + ex.Message); }
            }
            string exe = Path.Combine(Engine_, Rebrand.BrandedExeName);
            if (!File.Exists(exe))
            {
                MessageBox.Show(L.T("The Icewolf engine folder is missing. Please extract the complete Icewolf Portable zip.",
                                    "Der Engine-Ordner fehlt. Bitte das komplette Icewolf-Portable-Zip entpacken."),
                                "Icewolf Portable", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }

            // One "owner" launcher per portable folder watches the browser and runs updates afterwards.
            string mutexName = @"Local\IcewolfPortable-" + PathHash(Root);
            bool owner;
            using (var mutex = new Mutex(true, mutexName, out owner))
            {
                if (!owner)
                {
                    WaitForUpdateToFinish();
                    StartBrowser(exe, args);       // hands the request to the running instance
                    return 0;
                }

                WaitForUpdateToFinish();
                try { Engine.MergePolicies(Engine_, Addons); } catch (Exception ex) { Log("Policies: " + ex.Message); }
                try { SetTaskbarId(); } catch { }
                // Self-repair: Icewolf's browser scripts (Getem button / menu, no pin prompts) must
                // match this version - e.g. after an engine update or a manual file change.
                try { Rebrand.SetLog(Engine_); Rebrand.InstallScripts(Engine_); } catch (Exception ex) { Log("Browser scripts: " + ex.Message); }
                // A taskbar pin of an Icewolf Portable copy that was moved/re-extracted points to a
                // folder that no longer exists - point it at this copy again (icon comes back).
                try { RepairTaskbarPins(); } catch (Exception ex) { Log("Taskbar pin: " + ex.Message); }
                StartBrowser(exe, args);

                // Wait until every browser process of this portable folder has closed
                Thread.Sleep(5000);
                while (Engine.BrowserRunning(Engine_)) Thread.Sleep(3000);

                if (!HasArg(args, "/noupdate") && UpdateDue()) UpdateNow();
                mutex.ReleaseMutex();
            }
            return 0;
        }

        private static void StartBrowser(string exe, string[] args)
        {
            var sb = new StringBuilder("-profile \"" + Profile + "\"");
            foreach (string a in args)
            {
                if (a.StartsWith("/", StringComparison.Ordinal)) continue;           // launcher switches
                if (a.IndexOf('"') >= 0) continue;                                  // never pass broken quoting
                sb.Append(" \"").Append(a).Append('"');                            // e.g. a URL or file to open
            }
            var psi = new ProcessStartInfo(exe, sb.ToString());
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Engine_;
            Process.Start(psi).Dispose();
        }

        private static void RepairTaskbarPins()
        {
            string pins = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                       "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
            if (!Directory.Exists(pins)) return;
            string launcher = Assembly.GetExecutingAssembly().Location;
            foreach (string lnk in Directory.GetFiles(pins, "*.lnk"))
            {
                if (Path.GetFileName(lnk).IndexOf("Icewolf", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string target = ShellLink.GetTarget(lnk);
                if (string.IsNullOrEmpty(target) || File.Exists(target)) continue;          // still valid
                string name = Path.GetFileName(target);
                if (!name.Equals("Icewolf.exe", StringComparison.OrdinalIgnoreCase)) continue;   // not ours
                ShellLink.Retarget(lnk, launcher, Root, launcher);
                Log("Taskbar pin repaired: " + Path.GetFileName(lnk) + " -> " + launcher);
            }
        }

        /// <summary>Taskbar identity (current user only, no admin): matches the Icewolf window icon.</summary>
        private static void SetTaskbarId()
        {
            foreach (string keyPath in Engine.TaskbarKeyPaths(Engine_))
            {
                using (RegistryKey hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (RegistryKey k = hkcu.CreateSubKey(keyPath))
                {
                    // drop entries of this portable copy from other drive letters / folders that no longer exist
                    foreach (string name in k.GetValueNames())
                        if (TaskbarId.Equals(k.GetValue(name) as string) && !Directory.Exists(name))
                            k.DeleteValue(name, false);
                    k.SetValue(Engine_, TaskbarId, RegistryValueKind.String);
                }
            }
        }

        private static bool UpdateDue()
        {
            try
            {
                if (!File.Exists(CheckFile)) return true;
                DateTime last;
                if (!DateTime.TryParse(File.ReadAllText(CheckFile).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out last)) return true;
                return (DateTime.UtcNow - last).TotalHours >= 12;
            }
            catch { return true; }
        }

        private static void UpdateNow()
        {
            string tmp = Path.Combine(Root, ".update-tmp");
            try
            {
                File.WriteAllText(CheckFile, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                string installed = File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : "0";
                string latest = Updater.LatestVersion();
                if (Updater.CompareVersions(latest, installed) <= 0) { Log("Up to date (" + installed + ")."); return; }

                Log("Updating engine " + installed + " -> " + latest);
                Engine.DeleteDir(tmp);
                Directory.CreateDirectory(tmp);
                File.WriteAllText(LockFile, latest);
                if (Updater.DownloadVerifyReplace(latest, Engine_, Addons, tmp, Log))
                {
                    File.WriteAllText(VersionFile, latest);
                    Log("Update to " + latest + " installed and verified.");
                }
            }
            catch (Exception ex) { Log("ERROR: " + ex.Message); }
            finally
            {
                Engine.DeleteDir(tmp);
                Engine.TryDeleteFile(LockFile);
            }
        }

        private static void WaitForUpdateToFinish()
        {
            for (int i = 0; i < 240 && File.Exists(LockFile); i++)
            {
                // a lock older than 30 minutes is left over from an interrupted update
                if ((DateTime.Now - File.GetLastWriteTime(LockFile)).TotalMinutes > 30) { Engine.TryDeleteFile(LockFile); break; }
                Thread.Sleep(500);
            }
        }

        private static bool HasArg(string[] args, string name) { return Program.HasArg(args, name); }

        private static string PathHash(string path)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
                return BitConverter.ToString(h, 0, 8).Replace("-", "");
            }
        }

        private static void Log(string msg)
        {
            try
            {
                if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 256 * 1024) File.Delete(LogFile);
                File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + msg + "\r\n");
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ shortcuts (.lnk) with taskbar ID
    internal static class ShellLink
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink { }

        // Mostly setters are used; the other methods are declared to keep the vtable order correct.
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetIDList(IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription(IntPtr pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory(IntPtr pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments(IntPtr pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(IntPtr pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(IntPtr piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation(IntPtr pszIconPath, int cch, IntPtr piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr pointerValue;
            [FieldOffset(16)] public IntPtr padding;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint cProps);
            void GetAt(uint iProp, out PROPERTYKEY pkey);
            void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
            void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
            void Commit();
        }

        private const ushort VT_LPWSTR = 31;

        /// <summary>Target path of an existing shortcut (null if unreadable).</summary>
        public static string GetTarget(string lnkPath)
        {
            object obj = new CShellLink();
            try
            {
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)obj).Load(lnkPath, 0);
                var sb = new StringBuilder(1024);
                ((IShellLinkW)obj).GetPath(sb, sb.Capacity, IntPtr.Zero, 0x4 /* SLGP_RAWPATH */);
                return Environment.ExpandEnvironmentVariables(sb.ToString());
            }
            catch { return null; }
            finally { Marshal.FinalReleaseComObject(obj); }
        }

        /// <summary>Points an existing shortcut (e.g. a taskbar pin) to a new program; other settings stay.</summary>
        public static void Retarget(string lnkPath, string target, string workDir, string iconFile)
        {
            object obj = new CShellLink();
            try
            {
                var file = (System.Runtime.InteropServices.ComTypes.IPersistFile)obj;
                file.Load(lnkPath, 2 /* STGM_READWRITE */);
                var link = (IShellLinkW)obj;
                link.SetPath(target);
                link.SetWorkingDirectory(workDir);
                if (File.Exists(iconFile)) link.SetIconLocation(iconFile, 0);
                file.Save(lnkPath, true);
            }
            finally { Marshal.FinalReleaseComObject(obj); }
        }

        /// <summary>Plain shortcut (used for the Getem Start menu entry).</summary>
        public static void CreateSimple(string lnkPath, string target, string args, string workDir, string description)
        {
            object obj = new CShellLink();
            try
            {
                var link = (IShellLinkW)obj;
                link.SetPath(target);
                link.SetArguments(args);
                link.SetWorkingDirectory(workDir);
                link.SetIconLocation(target, 0);
                link.SetDescription(description);
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)obj).Save(lnkPath, true);
            }
            finally { Marshal.FinalReleaseComObject(obj); }
        }

        public static void Create(string lnkPath)
        {
            object obj = new CShellLink();
            try
            {
                var link = (IShellLinkW)obj;
                link.SetPath(Paths.BrowserExe);
                link.SetArguments("-profile \"" + Paths.ProfileDir + "\"");
                link.SetWorkingDirectory(Paths.EngineDir);
                link.SetIconLocation(Paths.IconFile, 0);
                link.SetDescription("Icewolf - private, fast, secure");

                string appUserModelId = Paths.TaskbarId;
                if (!string.IsNullOrEmpty(appUserModelId))
                {
                    var store = (IPropertyStore)obj;
                    var key = new PROPERTYKEY();
                    key.fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");   // System.AppUserModel.ID
                    key.pid = 5;
                    var pv = new PROPVARIANT();
                    pv.vt = VT_LPWSTR;
                    pv.pointerValue = Marshal.StringToCoTaskMemUni(appUserModelId);
                    try
                    {
                        store.SetValue(ref key, ref pv);
                        store.Commit();
                    }
                    finally { Marshal.FreeCoTaskMem(pv.pointerValue); }
                }

                ((System.Runtime.InteropServices.ComTypes.IPersistFile)obj).Save(lnkPath, true);
            }
            finally { Marshal.FinalReleaseComObject(obj); }
        }
    }

    // ------------------------------------------------------------------ theme
    internal static class Theme
    {
        public static readonly Brush Text   = Make("#E8F5FF");
        public static readonly Brush Muted  = Make("#9CC3E8");
        public static readonly Brush Dim    = Make("#6F93BC");
        public static readonly Brush Accent = Make("#2F8CFF");
        public static readonly Brush Glow   = Make("#00D5FF");
        public static readonly Brush Line   = Make("#3A6696");
        public static readonly Brush Bad    = Make("#FF9C9C");
        public static readonly Brush None   = Brushes.Transparent;

        public static Brush Make(string hex)
        {
            var b = (Brush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        public static Brush Linear(string from, string to, double angle)
        {
            var g = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(from),
                                            (Color)ColorConverter.ConvertFromString(to), angle);
            g.Freeze();
            return g;
        }

        public static readonly FontFamily Ui      = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily Display = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily Icons   = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
    }

    // ------------------------------------------------------------------ controls
    internal sealed class FrostButton : Border
    {
        public event EventHandler Click;
        private readonly Brush normal, hover, press;
        private bool pressed, enabled = true;

        public FrostButton(string caption, bool primary)
        {
            CornerRadius = new CornerRadius(7);
            Padding = new Thickness(24, 9, 24, 10);
            MinWidth = 112;
            Margin = new Thickness(10, 0, 0, 0);
            Cursor = Cursors.Hand;
            if (primary)
            {
                normal = Theme.Linear("#2F8CFF", "#00B4F0", 0);
                hover  = Theme.Linear("#4A9CFF", "#21C4FA", 0);
                press  = Theme.Linear("#2378E0", "#0098CC", 0);
                Effect = new DropShadowEffect { Color = Color.FromRgb(0, 170, 255), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.45 };
            }
            else
            {
                normal = Theme.Make("#132D55");
                hover  = Theme.Make("#1B3C6E");
                press  = Theme.Make("#0F2445");
                BorderBrush = Theme.Make("#335F96");
                BorderThickness = new Thickness(1);
            }
            Background = normal;
            Child = new TextBlock
            {
                Text = caption,
                Foreground = Brushes.White,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            MouseEnter += delegate { if (enabled) Background = pressed ? press : hover; };
            MouseLeave += delegate { Background = normal; };
            MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                if (!enabled) return;
                pressed = true;
                Background = press;
                CaptureMouse();
            };
            MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (!pressed) return;
                pressed = false;
                ReleaseMouseCapture();
                Point p = e.GetPosition(this);
                bool inside = p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight;
                Background = inside ? hover : normal;
                if (inside && enabled && Click != null) Click(this, EventArgs.Empty);
            };
        }

        public bool IsActive
        {
            set { enabled = value; Opacity = value ? 1.0 : 0.45; Cursor = value ? Cursors.Hand : Cursors.Arrow; }
        }
    }

    internal sealed class GlyphButton : Border
    {
        public event EventHandler Click;

        public GlyphButton(string glyph, bool isClose)
        {
            Width = 40; Height = 30;
            CornerRadius = new CornerRadius(6);
            Background = Theme.None;
            Cursor = Cursors.Hand;
            Child = new TextBlock
            {
                Text = glyph, FontFamily = Theme.Icons, FontSize = 10,
                Foreground = Theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Brush hover = isClose ? Theme.Make("#C42B1C") : Theme.Make("#1B3C6E");
            MouseEnter += delegate { Background = hover; };
            MouseLeave += delegate { Background = Theme.None; };
            MouseLeftButtonDown += (s, e) => { e.Handled = true; };
            MouseLeftButtonUp += (s, e) => { e.Handled = true; if (Click != null) Click(this, EventArgs.Empty); };
        }
    }

    internal sealed class FrostCheck : StackPanel
    {
        private bool isChecked;
        private readonly Border box;
        private readonly TextBlock tick;

        public FrostCheck(string label, bool initial)
        {
            Orientation = Orientation.Horizontal;
            Margin = new Thickness(0, 7, 0, 0);
            Background = Theme.None;
            Cursor = Cursors.Hand;

            box = new Border
            {
                Width = 19, Height = 19,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1.5),
                VerticalAlignment = VerticalAlignment.Center
            };
            tick = new TextBlock
            {
                Text = "", FontFamily = Theme.Icons, FontSize = 11,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            box.Child = tick;
            Children.Add(box);
            Children.Add(new TextBlock
            {
                Text = label, Foreground = Theme.Text, FontSize = 13.5,
                Margin = new Thickness(11, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 400
            });

            MouseLeftButtonDown += (s, e) => { e.Handled = true; };
            MouseLeftButtonUp += (s, e) => { e.Handled = true; IsChecked = !IsChecked; };
            IsChecked = initial;
        }

        public bool IsChecked
        {
            get { return isChecked; }
            set
            {
                isChecked = value;
                box.Background = value ? Theme.Accent : Theme.None;
                box.BorderBrush = value ? Theme.Accent : Theme.Line;
                tick.Visibility = value ? Visibility.Visible : Visibility.Hidden;
            }
        }
    }

    internal sealed class FrostProgress : Grid
    {
        private readonly Border fill;
        private double current;

        public FrostProgress()
        {
            Height = 8;
            Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = Theme.Make("#12294D") });
            fill = new Border
            {
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
                Background = Theme.Linear("#2F8CFF", "#00D5FF", 0),
                Effect = new DropShadowEffect { Color = Color.FromRgb(0, 213, 255), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.75 }
            };
            Children.Add(fill);
            SizeChanged += delegate { Apply(false); };
        }

        public double Value
        {
            get { return current; }
            set { current = Math.Max(current, Math.Max(0, Math.Min(1, value))); Apply(true); }
        }

        private void Apply(bool animate)
        {
            double target = ActualWidth * current;
            if (animate)
            {
                var a = new DoubleAnimation(target, TimeSpan.FromMilliseconds(450));
                a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                fill.BeginAnimation(WidthProperty, a);
            }
            else
            {
                fill.BeginAnimation(WidthProperty, null);
                fill.Width = target;
            }
        }
    }

    // ------------------------------------------------------------------ main window
    internal sealed class SetupWindow : Window
    {
        private readonly bool uninstallMode;
        private bool busy;
        private readonly Grid content;
        private readonly StackPanel buttons;
        private readonly TextBlock note;
        private readonly GlyphButton closeButton;

        public SetupWindow(bool uninstall)
        {
            uninstallMode = uninstall;
            Title = uninstall ? "Icewolf Uninstall" : "Icewolf Setup";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Theme.None;
            ResizeMode = ResizeMode.NoResize;
            Width = 812; Height = 532;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Theme.Ui;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            try
            {
                using (Stream s = Res.Open("icewolf.ico"))
                    if (s != null) Icon = BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            catch { }

            // window shell with rounded corners + soft shadow
            var shell = new Border
            {
                Margin = new Thickness(16),
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                BorderBrush = Theme.Make("#553A7BC8"),
                Background = Theme.Linear("#0C2244", "#060F22", 90),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black }
            };
            Content = shell;

            var root = new Grid();
            shell.Child = root;
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(262) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // left artwork
            var art = new Border { CornerRadius = new CornerRadius(13, 0, 0, 13), Background = Theme.Make("#0A1F3E") };
            BitmapSource img = Res.Image("side.png");
            if (img != null)
                art.Background = new ImageBrush(img) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
            root.Children.Add(art);
            var divider = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Right, Background = Theme.Make("#3360B4FF") };
            root.Children.Add(divider);

            // right side
            var right = new Grid { Margin = new Thickness(38, 8, 10, 26) };
            Grid.SetColumn(right, 1);
            root.Children.Add(right);
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var bar = new DockPanel { LastChildFill = false };
            closeButton = new GlyphButton("", true);
            closeButton.Click += delegate { Close(); };
            var minButton = new GlyphButton("", false);
            minButton.Click += delegate { WindowState = WindowState.Minimized; };
            DockPanel.SetDock(closeButton, Dock.Right);
            DockPanel.SetDock(minButton, Dock.Right);
            bar.Children.Add(closeButton);
            bar.Children.Add(minButton);
            bar.Children.Add(new TextBlock
            {
                Text = uninstall ? "ICEWOLF  ·  UNINSTALL" : "ICEWOLF  ·  SETUP",
                FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.Dim,
                VerticalAlignment = VerticalAlignment.Center
            });
            right.Children.Add(bar);

            content = new Grid { Margin = new Thickness(0, 16, 22, 0) };
            Grid.SetRow(content, 1);
            right.Children.Add(content);

            var bottom = new Grid { Margin = new Thickness(0, 0, 22, 0) };
            Grid.SetRow(bottom, 2);
            note = new TextBlock
            {
                FontSize = 11, Foreground = Theme.Dim, TextWrapping = TextWrapping.Wrap,
                MaxWidth = 190, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left
            };
            buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            bottom.Children.Add(note);
            bottom.Children.Add(buttons);
            right.Children.Add(bottom);

            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
            };
            Closing += (s, e) => { if (busy) e.Cancel = true; };
            Opacity = 0;
            Loaded += delegate { BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))); };

            if (uninstallMode) ShowUninstallConfirm(); else ShowWelcome();
        }

        // ---------------------------------------------------------- page helpers
        private void SetPage(UIElement page, string footnote, params FrostButton[] btns)
        {
            content.Children.Clear();
            content.Children.Add(page);
            page.Opacity = 0;
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            note.Text = footnote ?? "";
            buttons.Children.Clear();
            foreach (FrostButton b in btns) buttons.Children.Add(b);
        }

        private static StackPanel PageBase(string title, string subtitle, Brush titleBrush)
        {
            var p = new StackPanel();
            p.Children.Add(new TextBlock
            {
                Text = title, FontFamily = Theme.Display, FontSize = 28, FontWeight = FontWeights.SemiBold,
                Foreground = titleBrush ?? Theme.Text, TextWrapping = TextWrapping.Wrap
            });
            if (subtitle != null)
                p.Children.Add(new TextBlock
                {
                    Text = subtitle, FontSize = 14, Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 0), LineHeight = 21
                });
            return p;
        }

        private static UIElement Feature(string glyph, string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
            row.Children.Add(new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
                Background = Theme.Make("#1A3D6E"),
                Child = new TextBlock
                {
                    Text = glyph, FontFamily = Theme.Icons, FontSize = 12, Foreground = Theme.Glow,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            });
            row.Children.Add(new TextBlock
            {
                Text = text, Foreground = Theme.Text, FontSize = 13.5,
                Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        private delegate void Work(Action<double, string> progress);

        private void RunWork(string title, Work work, Action onSuccess)
        {
            var page = PageBase(title, L.T("Please wait a moment.", "Bitte einen Moment Geduld."), null);
            var status = new TextBlock { FontSize = 14, Foreground = Theme.Text, Margin = new Thickness(0, 64, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            var progressBar = new FrostProgress { Margin = new Thickness(0, 14, 0, 0) };
            var percent = new TextBlock { FontSize = 12, Foreground = Theme.Dim, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, Text = "0 %" };
            page.Children.Add(status);
            page.Children.Add(progressBar);
            page.Children.Add(percent);
            SetPage(page, null);

            busy = true;
            closeButton.Visibility = Visibility.Hidden;

            var thread = new Thread(() =>
            {
                string error = null;
                try
                {
                    work((v, msg) => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        progressBar.Value = v;
                        status.Text = msg;
                        percent.Text = ((int)Math.Round(progressBar.Value * 100)).ToString(CultureInfo.InvariantCulture) + " %";
                    })));
                }
                catch (SetupException ex) { error = ex.Message; }
                catch (Exception ex)
                {
                    error = L.T("An unexpected error occurred:", "Ein unerwarteter Fehler ist aufgetreten:") + "\n\n" + ex.Message;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    busy = false;
                    closeButton.Visibility = Visibility.Visible;
                    if (error == null) onSuccess(); else ShowError(error);
                }));
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);   // WScript.Shell COM for shortcuts
            thread.Start();
        }

        // ---------------------------------------------------------- install pages
        private void ShowWelcome()
        {
            var page = PageBase(L.T("Welcome to Icewolf", "Willkommen bei Icewolf"),
                                L.T("A private, fast and secure browser - no telemetry, no AI, no ads.",
                                    "Ein privater, schneller und sicherer Browser - ohne Telemetrie, KI und Werbung."), null);
            page.Children.Add(new Border { Height = 10 });
            page.Children.Add(Feature("", L.T("uBlock Origin ad blocker built in", "Werbeblocker uBlock Origin integriert")));
            page.Children.Add(Feature("", L.T("Strict tracking protection and HTTPS-only", "Strenger Tracking-Schutz und Nur-HTTPS")));
            page.Children.Add(Feature("", L.T("Debloated: no telemetry, ads or AI features", "Entschlackt: keine Telemetrie, Werbung oder KI")));
            page.Children.Add(Feature("", L.T("Automatic security updates", "Automatische Sicherheitsupdates")));
            if (Getem.Bundled)
                page.Children.Add(Feature("", L.T("Includes Getem (video search & download)", "Inklusive Getem (Videosuche & Download)")));

            page.Children.Add(new TextBlock
            {
                Text = L.T("Install location:  ", "Installationsort:  ") + Paths.InstallDir,
                FontSize = 12, Foreground = Theme.Dim, Margin = new Thickness(0, 22, 0, 2)
            });
            var desktop = new FrostCheck(L.T("Create a desktop shortcut", "Desktop-Verknüpfung erstellen"), true);
            page.Children.Add(desktop);

            var cancel = new FrostButton(L.T("Cancel", "Abbrechen"), false);
            cancel.Click += delegate { Close(); };
            var install = new FrostButton(L.T("Install", "Installieren"), true);
            install.Click += delegate
            {
                bool wantDesktop = desktop.IsChecked;
                RunWork(L.T("Installing Icewolf", "Icewolf wird installiert"),
                        p => Engine.Install(wantDesktop, p),
                        () => ShowFinished(wantDesktop));
            };
            SetPage(page, L.T("Open-source engine (MPL 2.0). Independent project.",
                              "Open-Source-Engine (MPL 2.0). Unabhängiges Projekt."), cancel, install);
        }

        private void ShowFinished(bool desktop)
        {
            var page = PageBase(L.T("Icewolf is ready", "Icewolf ist bereit"),
                desktop
                    ? L.T("You'll find Icewolf in your Start menu and on your desktop. uBlock Origin is already active.",
                          "Icewolf finden Sie im Startmenü und auf dem Desktop. uBlock Origin ist bereits aktiv.")
                    : L.T("You'll find Icewolf in your Start menu. uBlock Origin is already active.",
                          "Icewolf finden Sie im Startmenü. uBlock Origin ist bereits aktiv."), null);
            page.Children.Add(new Border { Height = 18 });
            page.Children.Add(Feature("", L.T("Privacy settings applied", "Datenschutzeinstellungen angewendet")));
            page.Children.Add(Feature("", L.T("Ad blocker installed", "Werbeblocker installiert")));
            page.Children.Add(Feature("", L.T("Automatic updates enabled", "Automatische Updates aktiviert")));
            var launch = new FrostCheck(L.T("Launch Icewolf now", "Icewolf jetzt starten"), true);
            launch.Margin = new Thickness(0, 26, 0, 0);
            page.Children.Add(launch);

            var finish = new FrostButton(L.T("Finish", "Fertigstellen"), true);
            finish.Click += delegate
            {
                if (launch.IsChecked) Engine.LaunchUnelevated();
                Close();
            };
            SetPage(page, null, finish);
        }

        private void ShowError(string message)
        {
            var page = PageBase(uninstallMode ? L.T("Uninstall failed", "Deinstallation fehlgeschlagen")
                                              : L.T("Installation failed", "Installation fehlgeschlagen"),
                                null, Theme.Bad);
            page.Children.Add(new TextBlock
            {
                Text = message, FontSize = 13.5, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 16, 0, 0), LineHeight = 20
            });
            var close = new FrostButton(L.T("Close", "Schließen"), true);
            close.Click += delegate { Close(); };
            SetPage(page, null, close);
        }

        // ---------------------------------------------------------- uninstall pages
        private void ShowUninstallConfirm()
        {
            var page = PageBase(L.T("Uninstall Icewolf", "Icewolf deinstallieren"),
                                L.T("This removes Icewolf and its browser engine from your PC.",
                                    "Dadurch werden Icewolf und seine Browser-Engine von Ihrem PC entfernt."), null);
            var profile = new FrostCheck(L.T("Also delete my profile (bookmarks, passwords, history, settings)",
                                             "Auch mein Profil löschen (Lesezeichen, Passwörter, Verlauf, Einstellungen)"), false);
            profile.Margin = new Thickness(0, 30, 0, 0);
            page.Children.Add(profile);
            page.Children.Add(new TextBlock
            {
                Text = L.T("Leave this unchecked to keep your data for a future reinstall.",
                           "Nicht anhaken, um Ihre Daten für eine spätere Neuinstallation zu behalten."),
                FontSize = 12, Foreground = Theme.Dim, Margin = new Thickness(30, 4, 0, 0), TextWrapping = TextWrapping.Wrap
            });

            var cancel = new FrostButton(L.T("Cancel", "Abbrechen"), false);
            cancel.Click += delegate { Close(); };
            var go = new FrostButton(L.T("Uninstall", "Deinstallieren"), true);
            go.Click += delegate
            {
                bool removeProfile = profile.IsChecked;
                RunWork(L.T("Uninstalling Icewolf", "Icewolf wird deinstalliert"),
                        p => Engine.Uninstall(removeProfile, p),
                        ShowUninstalled);
            };
            SetPage(page, null, cancel, go);
            Closed += delegate { Engine.ScheduleSelfDelete(); };
        }

        private void ShowUninstalled()
        {
            var page = PageBase(L.T("Icewolf was removed", "Icewolf wurde entfernt"),
                                L.T("Thanks for trying Icewolf.", "Danke, dass Sie Icewolf ausprobiert haben."), null);
            var close = new FrostButton(L.T("Close", "Schließen"), true);
            close.Click += delegate { Close(); };
            SetPage(page, null, close);
        }
    }
}

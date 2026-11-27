// =====================================================================
//  Icewolf - Getem helper (native messaging host)
//  Started by the browser when the Getem toolbar button is used. It accepts
//  exactly two commands from the Getem extension - "start" and "update" - and
//  nothing else. Can also be started directly with /start (Start menu shortcut).
//  Language level: C# 5 (compiled with the compiler built into Windows).
// =====================================================================
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Icewolf.GetemHost
{
    internal static class Program
    {
        private const int MaxMessage = 4096;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && string.Equals(args[0], "/start", StringComparison.OrdinalIgnoreCase))
                    return Launcher.Run("start") == null ? 0 : 1;
                if (args.Length > 0 && string.Equals(args[0], "/update", StringComparison.OrdinalIgnoreCase))
                    return Launcher.Run("update") == null ? 0 : 1;
                if (args.Length > 0 && string.Equals(args[0], "/serve", StringComparison.OrdinalIgnoreCase))
                {
                    // GetemTab test fork: Getem inside a browser tab. The browser reads the
                    // result from getem\.cache\host-log.txt if something goes wrong.
                    // optional 2nd argument: the browser's process number - Getem stops when it closes
                    uint pid;
                    string browserPid = args.Length > 1 && uint.TryParse(args[1], out pid) && pid > 0 ? pid.ToString() : null;
                    string err = Launcher.Run("serve", browserPid);
                    Launcher.Log(err == null ? "OK" : "ERROR " + err);
                    return err == null ? 0 : 1;
                }

                // Native messaging: 4-byte length (little endian) + UTF-8 JSON
                Stream stdin = Console.OpenStandardInput();
                Stream stdout = Console.OpenStandardOutput();
                byte[] lenBytes = ReadExactly(stdin, 4);
                if (lenBytes == null) return 0;
                int len = BitConverter.ToInt32(lenBytes, 0);
                if (len <= 0 || len > MaxMessage) { Reply(stdout, false, "Invalid message."); return 1; }
                byte[] body = ReadExactly(stdin, len);
                if (body == null) return 1;
                string json = Encoding.UTF8.GetString(body);

                Match m = Regex.Match(json, "^\\s*\\{\\s*\"action\"\\s*:\\s*\"(start|update)\"\\s*\\}\\s*$");
                if (!m.Success) { Reply(stdout, false, "Unknown command."); return 1; }

                string error = Launcher.Run(m.Groups[1].Value);
                Reply(stdout, error == null, error);
                return error == null ? 0 : 1;
            }
            catch (Exception ex)
            {
                try { Reply(Console.OpenStandardOutput(), false, ex.Message); } catch { }
                return 1;
            }
        }

        private static byte[] ReadExactly(Stream s, int count)
        {
            var buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, read, count - read);
                if (n <= 0) return null;
                read += n;
            }
            return buf;
        }

        private static void Reply(Stream stdout, bool ok, string error)
        {
            string json = ok ? "{\"ok\":true}"
                             : "{\"ok\":false,\"error\":\"" + Escape(error ?? "Error") + "\"}";
            byte[] data = Encoding.UTF8.GetBytes(json);
            stdout.Write(BitConverter.GetBytes(data.Length), 0, 4);
            stdout.Write(data, 0, data.Length);
            stdout.Flush();
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    internal static class Launcher
    {
        /// <summary>Starts Getem (or its updater). Returns null on success, otherwise an error text.</summary>
        public static string Run(string action) { return Run(action, null); }

        public static string Run(string action, string browserPid)
        {
            string dir = GetemDir();
            if (dir == null) return "Getem is not installed. Please reinstall Icewolf.";

            if (action == "update")
                return StartProcess(ComSpec(), "/c \"" + Path.Combine(dir, "Aktualisieren.cmd") + "\"", dir, true);

            string mode = action == "serve" ? " --server" : "";
            string watch = browserPid != null ? " --browser-pid " + browserPid : "";   // digits only (parsed above)
            bool tabVersion = File.Exists(Path.Combine(dir, "server.py"));
            if (action == "serve" && !tabVersion)
                return "This Getem version has no tab mode.";

            string pythonw = Path.Combine(dir, ".venv", "Scripts", "pythonw.exe");
            if (tabVersion)
            {
                // Only start Getem once its components are completely installed.
                // The installation window keeps ".setup-running" open while it works. If the
                // file can be deleted, it is a leftover of a closed/crashed window.
                string setupLock = Path.Combine(dir, ".setup-running");
                if (File.Exists(setupLock))
                {
                    try { File.Delete(setupLock); Log("removed a leftover installation lock"); }
                    catch (IOException) { Log("installation still running - it starts Getem itself"); return null; }
                    catch (UnauthorizedAccessException) { Log("installation still running - it starts Getem itself"); return null; }
                }
                // Browser tab: Getem as a local server. Start menu: Getem's own window.
                string starter = action == "serve" && File.Exists(Path.Combine(dir, "icewolf_server.py")) ? "icewolf_server.py" : "app.py";
                if (File.Exists(Path.Combine(dir, ".venv", "icewolf-ready")) && File.Exists(pythonw))
                {
                    Log("starting Getem (" + starter + ")");
                    return StartProcess(pythonw, "\"" + Path.Combine(dir, starter) + "\"" + (starter == "app.py" ? mode : watch), dir, false);
                }
                Log("first start - installing Getem's components (console window)");
                return StartProcess(ComSpec(), "/c \"\"" + Path.Combine(dir, "Start.cmd") + "\"" + mode + (action == "serve" ? watch : "") + "\"", dir, true);
            }

            if (File.Exists(pythonw))
                return StartProcess(pythonw, "\"" + Path.Combine(dir, "app.py") + "\"" + mode, dir, false);

            // First start: Start.cmd creates the Python environment (visible, shows progress)
            return StartProcess(ComSpec(), "/c \"\"" + Path.Combine(dir, "Start.cmd") + "\"" + mode + "\"", dir, true);
        }

        /// <summary>Appends a line to getem\.cache\host-log.txt (read by the browser on errors).</summary>
        public static void Log(string msg)
        {
            try
            {
                string dir = GetemDir();
                if (dir == null) return;
                string cache = Path.Combine(dir, ".cache");
                Directory.CreateDirectory(cache);
                string f = Path.Combine(cache, "host-log.txt");
                if (File.Exists(f) && new FileInfo(f).Length > 64 * 1024) File.Delete(f);
                File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + "\r\n");
            }
            catch { }
        }

        /// <summary>Portable: ..\getem next to the helper folder. Installed: %LOCALAPPDATA%\Icewolf\Getem.</summary>
        private static string GetemDir()
        {
            string hostDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string portable = Path.GetFullPath(Path.Combine(hostDir, "..", "getem"));
            if (File.Exists(Path.Combine(portable, "app.py"))) return portable;
            string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Icewolf", "Getem");
            if (File.Exists(Path.Combine(installed, "app.py"))) return installed;
            return null;
        }

        private static string ComSpec()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        }

        // ---------------------------------------------------------------- CreateProcess
        // The browser runs this helper inside a job object that is closed when the helper
        // exits. CREATE_BREAKAWAY_FROM_JOB lets Getem keep running after that.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateProcessW")]
        private static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory,
            ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private const uint CREATE_NEW_CONSOLE = 0x00000010;
        private const uint DETACHED_PROCESS = 0x00000008;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        private const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;

        private static string StartProcess(string exe, string args, string dir, bool console)
        {
            uint baseFlags = console ? CREATE_NEW_CONSOLE : DETACHED_PROCESS;
            if (TryCreate(exe, args, dir, baseFlags | CREATE_BREAKAWAY_FROM_JOB)) return null;
            if (TryCreate(exe, args, dir, baseFlags)) return null;      // job does not allow breakaway
            return "Could not start Getem (error " + Marshal.GetLastWin32Error() + ").";
        }

        private static bool TryCreate(string exe, string args, string dir, uint flags)
        {
            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            PROCESS_INFORMATION pi;
            var cmd = new StringBuilder("\"" + exe + "\" " + args);
            // inheritHandles = false: Getem never gets access to the browser's message pipes
            if (!CreateProcess(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, dir, ref si, out pi))
                return false;
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            return true;
        }
    }
}

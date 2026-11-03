using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Couchpotato.Config;
using Couchpotato.Net;
using Couchpotato.Win32;

namespace Couchpotato
{
    static class Program
    {
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Couchpotato");
        public static readonly string LogPath = Path.Combine(DataDir, "log.txt");
        /// <summary>"1.4.0": set once in Directory.Build.props, shown in the window title, the tray and the log.</summary>
        public static readonly string Version = typeof(Program).Assembly.GetName().Version.ToString(3);
        /// <summary>Where "Support it" goes: Couchpotato is free, donations are optional.</summary>
        public const string SupportUrl = "https://ko-fi.com/barleymalt";
        public const string SupportText = "Free and open source · Support it ♥";

        /// <summary>The DLLs that ship next to Couchpotato.exe (listed at build time).</summary>
        public static IEnumerable<string> Libraries()
        {
            using (var s = typeof(Program).Assembly.GetManifestResourceStream("app-files.txt"))
            using (var reader = new StreamReader(s))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                    if (line.Trim().EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) yield return line.Trim();
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            var folder = Path.GetDirectoryName(Application.ExecutablePath);
            var missing = Libraries().Where(f => !File.Exists(Path.Combine(folder, f))).ToList();
            if (missing.Count > 0)
            {
                // Typically: started from inside the zip, where Windows only unpacks the exe itself.
                MessageBox.Show("Couchpotato needs the files that came with it in the zip (" + missing[0] + (missing.Count > 1 ? " and others" : "") + ").\n\n" +
                                "Right-click the zip → Extract All, then run Couchpotato.exe from the extracted folder.",
                                "Couchpotato", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AppDomain.CurrentDomain.AssemblyResolve += ResolveBesideExe;
            Run(args);
        }

        /// <summary>A DLL of another version than the one asked for (the .config's redirects cover most): use the one shipped.</summary>
        static Assembly ResolveBesideExe(object sender, ResolveEventArgs e)
        {
            var file = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        }

        // Kept out of Main so no dependency is touched before the files are checked and the resolver is installed.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Run(string[] args)
        {
            // Log first and catch everything: Couchpotato must never just vanish without saying why.
            Directory.CreateDirectory(DataDir);
            StartLog(args);
            // These must run before the first window of any kind, message boxes included.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ReportCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportCrash(e.ExceptionObject as Exception);
            try
            {
                RunApp(args);
            }
            catch (Exception e)
            {
                ReportCrash(e);
            }
        }

        /// <summary>
        /// Run from anywhere but the install folder: offer to install, or to update the installed copy.
        /// True when the installed copy takes over and this one should quit.
        /// </summary>
        static bool OfferInstall(bool background)
        {
            if (background || AdminMode.IsElevated || AdminMode.IsEnabled || Installer.RunningInstalled) return false;
            if (Installer.IsInstalled)
            {
                if (Installer.SameAsInstalled())
                {
                    Installer.StartInstalled(); // it opens its window, or the running one does
                    return true;
                }
                using (var update = new Ui.WelcomeForm("Update Couchpotato",
                    "This is version " + Version + "; version " + (Installer.InstalledVersion ?? "?") + " is installed. " +
                    "Update it? Your settings and TV pairing are kept.", "Update", "Run this copy"))
                {
                    if (update.ShowDialog() != DialogResult.OK) return false;
                }
            }
            else
            {
                if (Installer.Declined) return false;
                using (var welcome = new Ui.WelcomeForm("Welcome to Couchpotato",
                    "Install it so it starts with Windows and shows up in the Start menu. It installs just for you, " +
                    "without administrator rights, and you can uninstall it any time from Settings → Apps.", "Install", "Just run it"))
                {
                    if (welcome.ShowDialog() != DialogResult.OK)
                    {
                        Installer.Declined = true;
                        return false;
                    }
                }
            }
            Installer.Install();
            Installer.StartInstalled();
            return true;
        }

        static void ReportCrash(Exception e)
        {
            Log.Warn("Couchpotato stopped because of an error: " + e);
            try
            {
                MessageBox.Show("Couchpotato stopped because of an error:\n\n" + (e?.Message ?? "unknown error") +
                                "\n\nDetails are in " + LogPath, "Couchpotato", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception) { }
        }

        static void RunApp(string[] args)
        {
            if (args.Length > 0 && args[0] == AdminMode.InstallArgument)
            {
                AdminMode.Install(args);
                return;
            }

            if (args.Contains(Installer.UninstallArgument, StringComparer.OrdinalIgnoreCase))
            {
                Installer.Uninstall();
                return;
            }

            bool background = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
            if (OfferInstall(background)) return;

            // In administrator mode, the copy in Program Files started by Task Scheduler is the one that runs.
            if (!AdminMode.IsElevated && AdminMode.IsEnabled)
            {
                if (!AdminMode.IsNewerThanInstalled())
                {
                    if (AdminMode.StartInstalled() && WaitForRunningInstance(TimeSpan.FromSeconds(8), show: !background)) return;
                    Log.Warn("Administrator mode is on, but its copy didn't start; running without administrator rights");
                }
                else if (MessageBox.Show("Install this version of Couchpotato? Windows will ask for permission, because Couchpotato runs in administrator mode.",
                                    "Couchpotato", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes && AdminMode.RequestEnable())
                    return;
            }

            Mutex mutex;
            bool firstInstance;
            try { mutex = new Mutex(true, MutexName, out firstInstance); }
            catch (UnauthorizedAccessException)
            {
                // Already running as administrator: ask it to show its window.
                WaitForRunningInstance(TimeSpan.Zero, show: !background);
                return;
            }
            using (mutex)
            using (var showSignal = CreateShowSignal())
            {
                if (!firstInstance)
                {
                    if (!background) showSignal.Set();
                    return;
                }

                ConfigStore.Protect = b => ProtectedData.Protect(b, Entropy, DataProtectionScope.CurrentUser);
                ConfigStore.Unprotect = b => ProtectedData.Unprotect(b, Entropy, DataProtectionScope.CurrentUser);

                var store = new ConfigStore(Path.Combine(DataDir, "config.json"));
                foreach (var error in store.KeyMapErrors) Log.Warn("Key bindings: " + error);
                Log.Info("Monitors: " + string.Join(", ", Displays.Describe()));
                if (PointerThemes.WasLeftApplied) PointerThemes.Restore(); // the app was stopped while a TV was connected

                // With administrator rights, a "TV" connecting from this very PC could be another program
                // trying to reach administrator windows through us. Real TVs never connect from here.
                var server = new AgentServer(store, new WindowsInput(store)) { RejectLocalPeers = AdminMode.IsElevated };
                if (AdminMode.IsElevated) Log.Info("Running in administrator mode");
                try
                {
                    server.Start();
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    var owner = PortOwner.Find(store.Port);
                    Log.Warn("Port " + store.Port + " is in use" + (owner.HasValue ? " by " + owner.Value.Name + " (process " + owner.Value.Pid + ")" : ""));
                    string who = owner.HasValue ? owner.Value.Name + " (process " + owner.Value.Pid + ")" : "another program";
                    string fix = owner.HasValue && owner.Value.Name.IndexOf("MagicRemote", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "That's MagicRemote, Couchpotato's old name or the old MagicRemoteService. Uninstall it (Settings → Apps), and if it still starts " +
                          "with Windows, delete its \"MagicRemote\" task in Task Scheduler."
                        : "Quit that program or change its port, then start Couchpotato again. Task Manager → Details lists processes by id.";
                    MessageBox.Show("Couchpotato can't use network port " + store.Port + " because " + who + " is using it, so the TV can't reach it.\n\n" + fix,
                                    "Couchpotato", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                using (var app = new TrayApp(store, server, showSetup: !background || store.Tvs.Count == 0))
                {
                    // A second launch (e.g. double-clicking the exe again) opens the setup window here.
                    var waiter = new Thread(() =>
                    {
                        try { while (showSignal.WaitOne()) app.ShowSetupFromAnyThread(); }
                        catch (ObjectDisposedException) { }
                    }) { IsBackground = true };
                    waiter.Start();
                    Application.Run(app);
                }
                server.Dispose();
            }
        }

        static readonly byte[] Entropy = System.Text.Encoding.ASCII.GetBytes("Couchpotato pairing key");
        const string MutexName = @"Local\Couchpotato.Agent";
        const string ShowEventName = @"Local\Couchpotato.Show";

        /// <summary>
        /// The "open your window" signal for later launches. An administrator-mode instance lets the
        /// user's normal programs set it, so double-clicking the exe still opens the window. Objects
        /// without an integrity label count as medium, so a DACL entry for the user is all it takes.
        /// Never throws: without a shared signal, later launches just can't open the window.
        /// </summary>
        static EventWaitHandle CreateShowSignal()
        {
            if (AdminMode.IsElevated)
            {
                try
                {
                    var sid = WindowsIdentity.GetCurrent().User.Value;
                    var security = new EventWaitHandleSecurity();
                    // Wait + set for this user; everything for SYSTEM and administrators.
                    security.SetSecurityDescriptorSddlForm("D:(A;;0x100002;;;" + sid + ")(A;;0x1F0003;;;SY)(A;;0x1F0003;;;BA)");
                    return new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName, out _, security);
                }
                catch (Exception e)
                {
                    Log.Warn("Show signal with default security: " + e.Message);
                }
            }
            try
            {
                return new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            }
            catch (Exception e)
            {
                Log.Warn("No shared show signal: " + e.Message);
                return new EventWaitHandle(false, EventResetMode.AutoReset);
            }
        }

        /// <summary>
        /// Waits up to <paramref name="wait"/> for a running Couchpotato (its show signal) and, if
        /// <paramref name="show"/>, asks it to open its window. False if none appeared.
        /// </summary>
        internal static bool WaitForRunningInstance(TimeSpan wait, bool show)
        {
            var until = DateTime.UtcNow + wait;
            while (true)
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize, out var signal))
                {
                    using (signal) if (show) signal.Set();
                    return true;
                }
                if (DateTime.UtcNow >= until) return false;
                Thread.Sleep(200);
            }
        }

        static void StartLog(string[] args)
        {
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1024 * 1024) File.Delete(LogPath);
            }
            catch (IOException) { }
            var gate = new object();
            Log.Sink = line =>
            {
                lock (gate) File.AppendAllText(LogPath, line + Environment.NewLine);
            };
            Log.Info("Couchpotato " + Version + " starting from " + Application.ExecutablePath +
                     (AdminMode.IsElevated ? " (administrator)" : "") + (args.Length > 0 ? " " + string.Join(" ", args) : ""));
        }
    }
}

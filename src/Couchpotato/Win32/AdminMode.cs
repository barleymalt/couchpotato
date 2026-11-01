using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace Couchpotato.Win32
{
    /// <summary>
    /// Optional: run Couchpotato with administrator rights, so the remote also works while an
    /// administrator window (Task Manager, installers, apps allowed in a UAC prompt) is in front.
    /// Windows blocks input from normal programs to those windows. The UAC prompt itself and the
    /// lock screen stay out of reach: only SYSTEM gets there.
    ///
    /// The exe is copied to Program Files (only administrators can change it there) and started at
    /// sign-in by a Task Scheduler task with highest privileges, so there's no prompt every day.
    /// </summary>
    static class AdminMode
    {
        const string TaskName = "Couchpotato";
        public const string InstallArgument = "--admin-install";

        public static readonly string InstalledPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Couchpotato", "Couchpotato.exe");

        public static bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        /// <summary>
        /// The account is an administrator, even if this process runs without the rights. With UAC, an
        /// administrator's normal programs get a "limited" token whose Administrators group is deny-only,
        /// and WindowsIdentity.Groups leaves deny-only groups out, so ask for the elevation type instead.
        /// </summary>
        public static bool UserIsAdmin => IsElevated || ElevationType() == TokenElevationTypeLimited;

        const int TokenElevationTypeLimited = 3;

        static int ElevationType()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                return GetTokenInformation(identity.Token, 18 /* TokenElevationType */, out int type, 4, out _) ? type : 0;
            }
        }

        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returned);

        public static bool IsEnabled => Schtasks("/Query /TN " + TaskName) == 0;

        /// <summary>Asks Windows for permission and hands over to the installer. False if the user said no.</summary>
        public static bool RequestEnable()
        {
            try
            {
                var start = new ProcessStartInfo(Application.ExecutablePath, InstallArgument + " " + Process.GetCurrentProcess().Id)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                };
                Process.Start(start)?.Dispose();
                return true;
            }
            catch (Win32Exception e) when (e.NativeErrorCode == 1223) // the user chose No
            {
                return false;
            }
        }

        /// <summary>Back to running as the user from the next sign-in.</summary>
        public static bool Disable()
        {
            if (Schtasks("/Delete /TN " + TaskName + " /F", elevate: !IsElevated) != 0) return false;
            Autostart.Set(true);
            return true;
        }

        /// <summary>Starts the installed administrator copy; true if it was started.</summary>
        public static bool StartInstalled() => Schtasks("/Run /TN " + TaskName) == 0;

        /// <summary>True when this exe is a different build from the installed administrator copy (an update).</summary>
        public static bool IsNewerThanInstalled()
        {
            var self = Application.ExecutablePath;
            if (string.Equals(Path.GetFullPath(self), Path.GetFullPath(InstalledPath), StringComparison.OrdinalIgnoreCase)) return false;
            if (!File.Exists(InstalledPath)) return true;
            using (var sha = SHA256.Create())
                return !sha.ComputeHash(File.ReadAllBytes(self)).SequenceEqual(sha.ComputeHash(File.ReadAllBytes(InstalledPath)));
        }

        /// <summary>
        /// Runs elevated ("--admin-install &lt;pid&gt;"): waits for the old Couchpotato to quit, installs
        /// this exe into Program Files, registers the sign-in task and starts it.
        /// </summary>
        public static void Install(string[] args)
        {
            try
            {
                if (args.Length > 1 && int.TryParse(args[1], out var pid)) WaitForExit(pid);
                foreach (var p in Process.GetProcessesByName("Couchpotato").Where(p => p.Id != Process.GetCurrentProcess().Id))
                {
                    try { p.Kill(); p.WaitForExit(5000); } catch (Exception) { }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(InstalledPath));
                if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(InstalledPath), StringComparison.OrdinalIgnoreCase))
                    File.Copy(Application.ExecutablePath, InstalledPath, true);
                Log.Info("Administrator mode: copied to " + InstalledPath);
                // Keep the normal install (Start menu entry) on the same version, or it would offer a "downgrade".
                if (Installer.IsInstalled && !string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(Installer.InstalledExe), StringComparison.OrdinalIgnoreCase))
                    File.Copy(Application.ExecutablePath, Installer.InstalledExe, true);

                var xml = Path.Combine(Path.GetTempPath(), "Couchpotato-task.xml");
                File.WriteAllText(xml, TaskXml(WindowsIdentity.GetCurrent().Name, InstalledPath), Encoding.Unicode);
                try
                {
                    if (Schtasks("/Create /TN " + TaskName + " /XML \"" + xml + "\" /F") != 0)
                        throw new InvalidOperationException("Task Scheduler refused the Couchpotato task.");
                }
                finally { File.Delete(xml); }
                Log.Info("Administrator mode: sign-in task created");

                Autostart.Set(false); // the task starts it now
                // Only keep the change if the administrator copy really comes up; also reopens its window.
                if (!StartInstalled() || !Program.WaitForRunningInstance(TimeSpan.FromSeconds(10), show: true))
                {
                    Schtasks("/Delete /TN " + TaskName + " /F");
                    Autostart.Set(true);
                    throw new InvalidOperationException("Couchpotato didn't start with administrator rights, so administrator mode was turned off again.");
                }
                Log.Info("Administrator mode: running");
            }
            catch (Exception e)
            {
                Log.Warn("Administrator mode failed: " + e);
                MessageBox.Show("Couchpotato couldn't switch to administrator mode:\n" + e.Message + "\n\nIt will keep running normally. Details are in " +
                                Program.LogPath, "Couchpotato", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // Don't leave the user without a running Couchpotato.
                try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Application.ExecutablePath + "\""))?.Dispose(); } catch (Exception) { }
            }
        }

        static void WaitForExit(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid)) p.WaitForExit(15000);
            }
            catch (ArgumentException) { } // already gone
        }

        static string TaskXml(string user, string exe) =>
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
            "  <RegistrationInfo><Description>Starts Couchpotato with administrator rights, so the TV remote also works in administrator windows.</Description></RegistrationInfo>\n" +
            "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + SecurityElement.Escape(user) + "</UserId></LogonTrigger></Triggers>\n" +
            "  <Principals><Principal id=\"Author\"><UserId>" + SecurityElement.Escape(user) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\n" +
            "  <Settings>\n" +
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n" +
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n" +
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n" +
            "    <AllowStartOnDemand>true</AllowStartOnDemand>\n" +
            "    <Enabled>true</Enabled>\n" +
            "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n" +
            "    <Priority>4</Priority>\n" + // tasks default to below-normal priority, which would add input lag
            "    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\n" +
            "  </Settings>\n" +
            "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(exe) + "</Command><Arguments>--background</Arguments></Exec></Actions>\n" +
            "</Task>\n";

        static int Schtasks(string arguments, bool elevate = false)
        {
            try
            {
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), arguments)
                {
                    UseShellExecute = elevate,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                if (elevate) start.Verb = "runas";
                using (var p = Process.Start(start))
                {
                    p.WaitForExit(15000);
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Win32Exception)
            {
                return -1; // e.g. the user said no to the permission prompt
            }
        }

        /// <summary>Opens a web page or file as the normal user, even from an elevated Couchpotato.</summary>
        public static void OpenAsUser(string target)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + target + "\""))?.Dispose(); }
            catch (Exception e) { Log.Warn("Can't open " + target + ": " + e.Message); }
        }
    }
}

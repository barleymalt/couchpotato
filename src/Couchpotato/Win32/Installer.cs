using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Couchpotato.Win32
{
    /// <summary>
    /// Couchpotato installs itself (the exe and the files next to it), per user and without administrator rights:
    /// %LOCALAPPDATA%\Programs\Couchpotato, a Start menu entry, start with Windows, and an entry in
    /// Settings → Apps whose Uninstall runs "Couchpotato.exe --uninstall".
    /// </summary>
    static class Installer
    {
        public const string UninstallArgument = "--uninstall";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Couchpotato";

        public static readonly string InstallDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Couchpotato");
        public static readonly string InstalledExe = Path.Combine(InstallDir, "Couchpotato.exe");
        static readonly string Shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Couchpotato.lnk");
        static readonly string DeclinedMarker = Path.Combine(Program.DataDir, "portable");

        public static bool IsInstalled => File.Exists(InstalledExe);

        public static bool RunningInstalled => SamePath(Application.ExecutablePath, InstalledExe) || SamePath(Application.ExecutablePath, AdminMode.InstalledPath);

        /// <summary>The user chose "Just run it" once: don't offer installing again.</summary>
        public static bool Declined
        {
            get => File.Exists(DeclinedMarker);
            set
            {
                if (value) File.WriteAllText(DeclinedMarker, "Run without installing. Delete this file to be offered the installer again.");
                else if (File.Exists(DeclinedMarker)) File.Delete(DeclinedMarker);
            }
        }

        public static string InstalledVersion
        {
            get
            {
                try { return FileVersionInfo.GetVersionInfo(InstalledExe).ProductVersion?.Split('+')[0]; }
                catch (Exception) { return null; }
            }
        }

        public static bool SameAsInstalled()
        {
            if (!IsInstalled) return false;
            using (var sha = SHA256.Create())
                return sha.ComputeHash(File.ReadAllBytes(Application.ExecutablePath)).SequenceEqual(sha.ComputeHash(File.ReadAllBytes(InstalledExe)));
        }

        /// <summary>Copies this exe into place (stopping the installed copy if it runs) and registers it.</summary>
        public static void Install()
        {
            StopOtherCopies();
            CopyAppTo(InstallDir);

            CreateShortcut(Shortcut, InstalledExe);
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", "Couchpotato");
                key.SetValue("DisplayVersion", Program.Version);
                key.SetValue("Publisher", "Couchpotato");
                key.SetValue("DisplayIcon", InstalledExe + ",0");
                key.SetValue("InstallLocation", InstallDir);
                key.SetValue("UninstallString", "\"" + InstalledExe + "\" " + UninstallArgument);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("EstimatedSize", (int)(Directory.GetFiles(InstallDir).Sum(f => new FileInfo(f).Length) / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            Autostart.Set(true, InstalledExe);
            Log.Info("Installed " + Program.Version + " into " + InstallDir);
        }

        /// <summary>
        /// Copies this exe (as Couchpotato.exe, whatever it's called here), its .config and its DLLs into a
        /// folder; nothing when it already runs from there.
        /// </summary>
        public static void CopyAppTo(string folder)
        {
            var exe = Application.ExecutablePath;
            var from = Path.GetDirectoryName(exe);
            Directory.CreateDirectory(folder);
            if (SamePath(from, folder)) return;
            File.Copy(exe, Path.Combine(folder, "Couchpotato.exe"), true);
            if (File.Exists(exe + ".config")) File.Copy(exe + ".config", Path.Combine(folder, "Couchpotato.exe.config"), true);
            foreach (var dll in Program.Libraries()) File.Copy(Path.Combine(from, dll), Path.Combine(folder, dll), true);
        }

        public static void StartInstalled(string arguments = "") =>
            Process.Start(new ProcessStartInfo(InstalledExe, arguments) { WorkingDirectory = InstallDir })?.Dispose();

        /// <summary>Settings → Apps → Uninstall. Asks first; administrator mode is undone with one Windows prompt.</summary>
        public static void Uninstall()
        {
            if (AdminMode.IsEnabled && !AdminMode.IsElevated)
            {
                // Removing the Program Files copy and its task needs administrator rights: redo this elevated.
                try
                {
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath, UninstallArgument) { UseShellExecute = true, Verb = "runas" })?.Dispose();
                }
                catch (System.ComponentModel.Win32Exception) { }
                return;
            }

            var answer = MessageBox.Show("Uninstall Couchpotato?\n\nYes: remove it and keep your settings and TV pairing.\n" +
                                         "No: remove it and also delete settings and pairings.\nCancel: keep it.",
                                         "Uninstall Couchpotato", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;
            bool removeSettings = answer == DialogResult.No;

            StopOtherCopies();
            PointerThemes.Restore();
            if (AdminMode.IsEnabled) AdminMode.Disable(); // this turns autostart back on: so it goes first
            Autostart.Set(false);
            try { File.Delete(Shortcut); } catch (Exception) { }
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            // The running exe can't delete itself: a hidden command does it once we've quit.
            var folders = "\"" + InstallDir + "\"";
            if (Directory.Exists(Path.GetDirectoryName(AdminMode.InstalledPath))) folders += " \"" + Path.GetDirectoryName(AdminMode.InstalledPath) + "\"";
            if (removeSettings) folders += " \"" + Program.DataDir + "\"";
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q " + folders)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            })?.Dispose();

            MessageBox.Show("Couchpotato was removed." + (removeSettings ? "" : " Your settings are kept in case you install it again.") +
                            "\n\nThe Couchpotato app on the TV stays there; you can remove its tile on the TV like any other app.",
                            "Couchpotato", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Quits any other running Couchpotato (it can't be replaced or deleted while it runs).</summary>
        static void StopOtherCopies()
        {
            foreach (var p in Process.GetProcessesByName("Couchpotato").Where(p => p.Id != Process.GetCurrentProcess().Id))
            {
                try { p.Kill(); p.WaitForExit(5000); }
                catch (Exception) { } // e.g. the administrator copy, when we aren't elevated
            }
        }

        /// <summary>A .lnk through the Windows Script Host COM object, so no extra library is needed.</summary>
        static void CreateShortcut(string path, string target)
        {
            object shell = null, link = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var type = link.GetType();
                type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(target) });
                type.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Use the LG Magic Remote with this PC" });
                type.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { target + ",0" });
                type.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
            }
            catch (Exception e)
            {
                Log.Warn("Can't create the Start menu entry: " + e.Message);
            }
            finally
            {
                if (link != null) Marshal.FinalReleaseComObject(link);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }

        static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }
}

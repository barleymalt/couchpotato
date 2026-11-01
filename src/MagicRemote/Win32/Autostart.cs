using System.Windows.Forms;
using Microsoft.Win32;

namespace MagicRemote.Win32
{
    /// <summary>Per-user "start with Windows" through the HKCU Run key: no administrator rights needed.</summary>
    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "MagicRemote";

        static string Command(string exe) => "\"" + exe + "\" --background";

        public static bool IsEnabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key?.GetValue(ValueName) is string v && v == Command(Application.ExecutablePath);
            }
        }

        /// <param name="exe">The exe to start; this one by default.</param>
        public static void Set(bool enabled, string exe = null)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(ValueName, Command(exe ?? Application.ExecutablePath));
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}

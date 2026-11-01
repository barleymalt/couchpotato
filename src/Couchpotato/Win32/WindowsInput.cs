using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Couchpotato.Config;
using Couchpotato.Input;
using Couchpotato.Protocol;

namespace Couchpotato.Win32
{
    /// <summary>Injects input with SendInput, with the rights of this process.</summary>
    sealed class WindowsInput : IInputSink
    {
        const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
                   MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_MIDDLEDOWN = 0x0020,
                   MOUSEEVENTF_MIDDLEUP = 0x0040, MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_VIRTUALDESK = 0x4000,
                   MOUSEEVENTF_ABSOLUTE = 0x8000;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;
        const int MissesBeforeBlocked = 3;

        static readonly int? SelfIntegrity = IntegrityOf((uint)Process.GetCurrentProcess().Id);

        readonly ConfigStore _store;
        readonly object _gate = new object();
        Point? _movedFrom, _movedTo;
        int _misses;
        string _blockedApp;
        IntPtr _checkedWindow; // front window already found not to be above us

        public WindowsInput(ConfigStore store) { _store = store; }

        public string BlockedReason { get; private set; }

        public bool MoveTo(double x, double y)
        {
            var tv = Displays.TvMonitor(_store.Display);
            var desk = Displays.VirtualScreen();
            double px = tv.Left + x * tv.Width, py = tv.Top + y * tv.Height;
            int nx = (int)Math.Round((px - desk.Left) * 65535.0 / Math.Max(1, desk.Width - 1));
            int ny = (int)Math.Round((py - desk.Top) * 65535.0 / Math.Max(1, desk.Height - 1));
            CheckLastMove(new Point((int)Math.Round(px), (int)Math.Round(py)));
            return Send(Mouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, nx, ny, 0));
        }

        public bool Button(MouseButton button, bool down)
        {
            uint flags;
            switch (button)
            {
                case MouseButton.Left: flags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP; break;
                case MouseButton.Right: flags = down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP; break;
                default: flags = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP; break;
            }
            return Send(Mouse(flags, 0, 0, 0));
        }

        public bool Wheel(int delta) => Send(Mouse(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta)));

        public bool Key(ushort vk, bool down)
        {
            uint flags = (down ? 0 : KEYEVENTF_KEYUP) | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0);
            return Send(Keyboard(vk, (ushort)MapVirtualKey(vk, 0), flags));
        }

        public bool Text(string text)
        {
            var inputs = new INPUT[text.Length * 2];
            for (int i = 0; i < text.Length; i++)
            {
                inputs[i * 2] = Keyboard(0, text[i], KEYEVENTF_UNICODE);
                inputs[i * 2 + 1] = Keyboard(0, text[i], KEYEVENTF_UNICODE | KEYEVENTF_KEYUP);
            }
            return Send(inputs);
        }

        public void Power(PowerAction action)
        {
            Log.Info("Power request from TV: " + action);
            if (action == PowerAction.Sleep)
            {
                Application.SetSuspendState(PowerState.Suspend, false, false);
                return;
            }
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), "/s /t 0")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            })?.Dispose();
        }

        static bool IsExtended(ushort vk) =>
            (vk >= 0x21 && vk <= 0x2E) || vk == 0x5B || vk == 0x5C || vk == 0x5D || vk == 0x6F || vk == 0x90 ||
            vk == 0xA3 || vk == 0xA5 || (vk >= 0xA6 && vk <= 0xB7);

        /// <summary>False when Windows refused the input; <see cref="BlockedReason"/> then says why, for the TV.</summary>
        bool Send(params INPUT[] inputs)
        {
            if (inputs.Length == 0) return true;
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) != inputs.Length)
            {
                // Refused outright: the lock screen or a permission prompt is showing.
                BlockedReason = null;
                return false;
            }
            lock (_gate)
            {
                if (_blockedApp == null) return true;
                BlockedReason = AdminMode.IsElevated
                    ? "Windows is blocking the remote in " + _blockedApp + ". Use the PC's mouse or keyboard there."
                    : "Windows is blocking the remote in " + _blockedApp + ", because it runs as administrator. " +
                      "Fix: on the PC, open Couchpotato (TV tab) and tick \"Let the remote work in administrator windows too\".";
                return false;
            }
        }

        /// <summary>
        /// Windows silently drops input from a program to windows with more rights (UIPI): SendInput
        /// still reports success. So it counts as blocked only when the pointer really didn't follow
        /// our last few moves AND the app in front runs with more rights than we do. Either alone
        /// can happen for harmless reasons (a game re-centring the pointer, an app hiding its token).
        /// </summary>
        void CheckLastMove(Point target)
        {
            lock (_gate)
            {
                GetCursorPos(out var now);
                var current = new Point(now.X, now.Y);
                if (_movedTo.HasValue)
                {
                    bool stayed = Near(current, _movedFrom.Value) && !Near(_movedFrom.Value, _movedTo.Value);
                    _misses = stayed ? _misses + 1 : 0;
                }
                _movedFrom = current;
                _movedTo = target;

                if (_misses < MissesBeforeBlocked)
                {
                    _blockedApp = null;
                    _checkedWindow = IntPtr.Zero;
                }
                else if (_blockedApp == null && GetForegroundWindow() != _checkedWindow)
                {
                    _checkedWindow = GetForegroundWindow();
                    var app = ForegroundAppAboveUs(_checkedWindow);
                    if (app != null)
                    {
                        _blockedApp = app;
                        Log.Info("Windows is blocking remote input to " + app + " (it runs with more rights than Couchpotato)");
                    }
                }
            }
        }

        static bool Near(Point a, Point b) => Math.Abs(a.X - b.X) <= 2 && Math.Abs(a.Y - b.Y) <= 2;

        /// <summary>The name of the app in front if it runs with more rights than this process (or hides them), else null.</summary>
        static string ForegroundAppAboveUs(IntPtr window)
        {
            GetWindowThreadProcessId(window, out uint pid);
            if (pid == 0) return null;
            var integrity = IntegrityOf(pid);
            if (integrity.HasValue && SelfIntegrity.HasValue && integrity.Value <= SelfIntegrity.Value) return null;
            return AppName(pid);
        }

        /// <summary>Mandatory integrity level of a process (0x2000 medium, 0x3000 high, 0x4000 system), or null if hidden from us.</summary>
        static int? IntegrityOf(uint pid)
        {
            var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                if (!OpenProcessToken(process, 0x0008 /* TOKEN_QUERY */, out var token)) return null;
                try
                {
                    GetTokenInformation(token, 25 /* TokenIntegrityLevel */, IntPtr.Zero, 0, out int size);
                    if (size <= 0) return null;
                    var buffer = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (!GetTokenInformation(token, 25, buffer, size, out _)) return null;
                        var sid = Marshal.ReadIntPtr(buffer); // TOKEN_MANDATORY_LABEL.Label.Sid
                        int count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                        return count == 0 ? (int?)null : Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                finally { CloseHandle(token); }
            }
            finally { CloseHandle(process); }
        }

        /// <summary>"Steam", from the exe's description, or its file name.</summary>
        static string AppName(uint pid)
        {
            var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return "an app";
            try
            {
                var path = new StringBuilder(1024);
                int length = path.Capacity;
                if (!QueryFullProcessImageName(process, 0, path, ref length)) return "an app";
                var file = path.ToString();
                try
                {
                    var description = FileVersionInfo.GetVersionInfo(file).FileDescription;
                    if (!string.IsNullOrWhiteSpace(description)) return description.Trim() + " (" + Path.GetFileName(file) + ")";
                }
                catch (Exception) { }
                return Path.GetFileName(file);
            }
            finally { CloseHandle(process); }
        }

        static INPUT Mouse(uint flags, int dx, int dy, uint data) =>
            new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } } };

        static INPUT Keyboard(ushort vk, ushort scan, uint flags) =>
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } } };

        struct Point
        {
            public int X, Y;
            public Point(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion u; }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returned);
        [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    }
}

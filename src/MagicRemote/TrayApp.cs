using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Windows.Forms;
using MagicRemote.Config;
using MagicRemote.Net;
using MagicRemote.WebOs;
using MagicRemote.Win32;

namespace MagicRemote
{
    /// <summary>The tray icon: shows the connection state, opens the settings, and swaps in the TV pointer while connected.</summary>
    sealed class TrayApp : ApplicationContext
    {
        readonly ConfigStore _store;
        readonly AgentServer _server;
        readonly NotifyIcon _icon;
        readonly ToolStripMenuItem _status;
        readonly ToolStripMenuItem _autostart;
        readonly Control _ui = new Control();
        readonly Dictionary<IPAddress, DateTime> _warned = new Dictionary<IPAddress, DateTime>();
        readonly List<string> _connected = new List<string>();
        readonly HashSet<string> _versionChecked = new HashSet<string>();
        SettingsForm _settings;

        public TrayApp(ConfigStore store, AgentServer server, bool showSetup)
        {
            _store = store;
            _server = server;
            _ui.CreateControl();

            _status = new ToolStripMenuItem { Enabled = false };
            _autostart = new ToolStripMenuItem("Start with Windows", null, (s, e) => SetAutostart(!_autostart.Checked));
            var menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new Ui.DarkMenuColors()), ForeColor = Ui.Theme.Text, Font = Ui.Theme.Body };
            menu.Items.Add(_status);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Set up a TV…", null, (s, e) => ShowSettings("tv")) { Font = new Font(menu.Font, FontStyle.Bold) });
            menu.Items.Add(new ToolStripMenuItem("Buttons…", null, (s, e) => ShowSettings("buttons")));
            menu.Items.Add(new ToolStripMenuItem("Pointer…", null, (s, e) => ShowSettings("pointer")));
            menu.Items.Add(_autostart);
            menu.Items.Add(new ToolStripMenuItem("Open log", null, (s, e) => Open(Program.LogPath)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Quit", null, (s, e) => ExitThread()));
            menu.Opening += (s, e) =>
            {
                _autostart.Visible = !AdminMode.IsEnabled; // in administrator mode Task Scheduler starts it
                _autostart.Checked = Autostart.IsEnabled;
            };

            using (var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("MagicRemote.ico"))
                _icon = new NotifyIcon { Icon = new Icon(stream), ContextMenuStrip = menu, Visible = true };
            _icon.DoubleClick += (s, e) => ShowSettings(null);

            server.Connected += (name, ip) => OnUi(() =>
            {
                _connected.Add(name);
                if (_connected.Count == 1) ApplyPointer();
                UpdateStatus();
                ScheduleVersionCheck(name);
            });
            server.Disconnected += (name, ip) => OnUi(() =>
            {
                _connected.Remove(name);
                if (_connected.Count == 0) PointerThemes.Restore();
                UpdateStatus();
            });
            server.AuthenticationFailed += ip => OnUi(() => WarnRefused(ip));
            server.KeyPressed += code => OnUi(() => _settings?.OnRemoteKey(code));
            server.TvAppVersion += (name, version) => OnUi(() => CheckTvVersion(name, version));
            UpdateStatus();

            if (showSetup) ShowSettings("tv");
        }

        public void ShowSetupFromAnyThread() => OnUi(() => ShowSettings(null));

        /// <param name="tab">"tv", "buttons", "pointer", or null to keep the current one.</param>
        public void ShowSettings(string tab)
        {
            if (_settings == null || _settings.IsDisposed)
            {
                _settings = new SettingsForm(_store, _server, () => { if (_connected.Count > 0) ApplyPointer(); });
                _settings.Paired += () => { if (!AdminMode.IsEnabled) SetAutostart(true); };
                _settings.AdminModeRequested += ExitThread; // the administrator copy starts once we're gone
            }
            UpdateStatus();
            if (tab != null) _settings.ShowTab(tab);
            _settings.Show();
            if (_settings.WindowState == FormWindowState.Minimized) _settings.WindowState = FormWindowState.Normal;
            _settings.Activate();
        }

        void ApplyPointer() => PointerThemes.Apply(_store.Pointer, _store.PointerSize, Displays.TvMonitor(_store.Display).Height);

        void UpdateStatus()
        {
            string text;
            if (_store.Tvs.Count == 0) text = "No TV set up yet";
            else if (_connected.Count == 0) text = "Waiting for the TV";
            else text = "Connected to " + _connected[_connected.Count - 1];
            _status.Text = "MagicRemote " + Program.Version + " · " + text;
            _settings?.SetStatus(text, _connected.Count > 0);
            var tip = "MagicRemote " + Program.Version + ": " + text;
            _icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip; // Windows' limit for tray tooltips
        }

        /// <summary>TV apps before 1.4.0 don't report a version: give them a few seconds, then treat them as old.</summary>
        void ScheduleVersionCheck(string name)
        {
            if (_versionChecked.Contains(name)) return;
            var timer = new Timer { Interval = 6000 };
            timer.Tick += (s, e) =>
            {
                timer.Dispose();
                if (_connected.Contains(name)) CheckTvVersion(name, null);
            };
            timer.Start();
        }

        /// <summary>Updating means a new exe AND running Set up a TV again; say so once per TV when they differ.</summary>
        void CheckTvVersion(string name, string version)
        {
            if (!_versionChecked.Add(name) || SameMinor(version, IpkBuilder.Version)) return;
            Log.Info("TV app on '" + name + "' is " + (version ?? "older than 1.4.0") + "; this PC has " + IpkBuilder.Version);
            _icon.ShowBalloonTip(15000, "Update the MagicRemote app on the TV",
                "The TV has " + (version != null ? "version " + version : "an older version") + ", this PC has " + IpkBuilder.Version +
                ". Open MagicRemote → TV tab → Install on TV to update it. Your pairing is kept.", ToolTipIcon.Info);
        }

        /// <summary>Patch releases (1.4.0 → 1.4.1) don't need the TV app reinstalled.</summary>
        static bool SameMinor(string a, string b) =>
            a != null && Version.TryParse(a, out var va) && Version.TryParse(b, out var vb) && va.Major == vb.Major && va.Minor == vb.Minor;

        /// <summary>Tells the user once an hour per address that something tried to connect and was refused.</summary>
        void WarnRefused(IPAddress ip)
        {
            if (_warned.TryGetValue(ip, out var last) && DateTime.UtcNow - last < TimeSpan.FromHours(1)) return;
            _warned[ip] = DateTime.UtcNow;
            _icon.ShowBalloonTip(8000, "MagicRemote refused a connection",
                "A device at " + ip + " tried to control this PC without being paired. " +
                "If it's your TV, run \"Set up a TV\" again. If you used MagicRemoteService before, uninstall its TV app.",
                ToolTipIcon.Warning);
        }

        void SetAutostart(bool enabled)
        {
            try
            {
                Autostart.Set(enabled);
                _autostart.Checked = enabled;
            }
            catch (Exception e)
            {
                Log.Warn("Can't change autostart: " + e.Message);
            }
        }

        static void Open(string path) => AdminMode.OpenAsUser(path);

        void OnUi(Action action)
        {
            if (!_ui.IsDisposed) _ui.BeginInvoke(action);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                PointerThemes.Restore();
                _icon.Visible = false;
                _icon.Dispose();
                _settings?.Dispose();
                _ui.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

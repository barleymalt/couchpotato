using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using MagicRemote.Config;
using MagicRemote.Net;
using MagicRemote.Ui;
using MagicRemote.WebOs;
using MagicRemote.Win32;

namespace MagicRemote
{
    /// <summary>
    /// The settings window: a title bar with the connection status, then three pages. "TV": set up and
    /// update the TV app, paired TVs, options. "Buttons" and "Pointer". Closing it only hides it.
    /// </summary>
    sealed class SettingsForm : ChromeForm
    {
        const string DevModeHelp = "https://webostv.developer.lge.com/develop/getting-started/developer-mode-app";

        readonly ConfigStore _store;
        readonly AgentServer _server;
        readonly ComboBox _tv = Make.Combo(300, editable: true);
        readonly PillButton _search = new PillButton("Search again");
        readonly FieldBox _passphrase = new FieldBox(180);
        readonly PillButton _install = new PillButton("Install on TV", PillKind.Primary);
        readonly Label _progress = Make.Text("", muted: true);
        readonly Card _pairedList = new Card(flat: true) { BackColor = Theme.Surface, Margin = new Padding(0) };
        readonly Panel _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        readonly Dictionary<string, (PillButton Tab, Control Page)> _pages = new Dictionary<string, (PillButton, Control)>();
        readonly ButtonsPage _buttons;
        string _current;
        bool _sized, _searching;

        /// <summary>Raised after a TV was installed and paired.</summary>
        public event Action Paired;
        /// <summary>Raised when MagicRemote should quit so its administrator copy can take over.</summary>
        public event Action AdminModeRequested;

        /// <param name="pointerChanged">Called when the pointer style or size changes.</param>
        public SettingsForm(ConfigStore store, AgentServer server, Action pointerChanged)
            : base("MagicRemote", "Version " + Program.Version)
        {
            _store = store;
            _server = server;

            Text = "MagicRemote " + Program.Version;
            using (var s = typeof(SettingsForm).Assembly.GetManifestResourceStream("MagicRemote.ico")) Icon = new Icon(s);
            Theme.Apply(this);
            // Sizes are in 96-DPI pixels; ChromeForm scales them to the screen the window is on.
            MinimumSize = new Size(560, 520);
            StartPosition = FormStartPosition.Manual;

            var tabs = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(30, 10, 30, 18), BackColor = Theme.Background };
            _buttons = new ButtonsPage(store);
            AddPage(tabs, "tv", "TV", BuildTvPage());
            AddPage(tabs, "buttons", "Buttons", _buttons);
            AddPage(tabs, "pointer", "Pointer", new PointerPage(store, pointerChanged));

            Controls.Add(_content);
            Controls.Add(tabs);
            ShowTab("tv");

            _search.Click += async (s, e) => await SearchAsync();
            _install.Click += async (s, e) => await InstallAsync();
            _passphrase.Box.CharacterCasing = CharacterCasing.Upper;
            _passphrase.Box.MaxLength = 16;
            _passphrase.Box.KeyDown += async (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                await InstallAsync();
            };
            // Look for TVs whenever the window opens.
            VisibleChanged += async (s, e) =>
            {
                if (Visible && !_searching) await SearchAsync();
            };
            RefreshPaired();
        }

        /// <summary>
        /// The first time: laid out for the screen the pointer is on (where the user is looking, often the
        /// TV), half its width, never too small to read, and centred there.
        /// </summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (_sized) return;
            _sized = true;
            var cursor = Cursor.Position;
            ScaleTo(DpiAt(cursor));
            var area = Screen.FromPoint(cursor).WorkingArea;
            int width = Math.Min(area.Width, Math.Max(area.Width / 2, (int)Theme.Px(this, 560)));
            int height = Math.Min((int)(area.Height * 0.9), (int)Theme.Px(this, 860));
            Bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
        }

        /// <summary>Closing hides the window, so it opens instantly next time; MagicRemote stays in the tray.</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }

        void AddPage(FlowLayoutPanel tabs, string name, string title, Control page)
        {
            var tab = new PillButton(title, PillKind.Tab);
            tab.Click += (s, e) => ShowTab(name);
            tabs.Controls.Add(tab);
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _content.Controls.Add(page);
            _pages[name] = (tab, page);
        }

        /// <summary>Opens a page: "tv", "buttons" or "pointer".</summary>
        public void ShowTab(string name)
        {
            if (!_pages.ContainsKey(name)) return;
            var clock = Stopwatch.StartNew();
            foreach (var kv in _pages)
            {
                kv.Value.Tab.Kind = kv.Key == name ? PillKind.TabSelected : PillKind.Tab;
                kv.Value.Page.Visible = kv.Key == name;
            }
            _current = name;
            if (Visible)
            {
                _pages[name].Page.Update();
                if (clock.ElapsedMilliseconds > 150) Log.Info("Opening the " + name + " page took " + clock.ElapsedMilliseconds + " ms");
            }
        }

        /// <summary>The header's status, kept current by the tray.</summary>
        public void SetStatus(string text, bool connected) => TitleBar.SetStatus(text, connected);

        /// <summary>A remote key was pressed: point at it on the Buttons page.</summary>
        public void OnRemoteKey(ushort code)
        {
            if (Visible && _current == "buttons") _buttons.Highlight(code);
        }

        Control BuildTvPage()
        {
            var page = Make.Page(out var column);

            var paired = column.Add(new Card());
            paired.Add(Make.Heading("Your TV"));
            paired.Add(_pairedList);

            var setup = column.Add(new Card());
            setup.Add(Make.Heading("Set up a TV"));
            setup.Add(Make.Instructions("Also use this to update the TV app after updating MagicRemote. Your pairing is kept.", muted: true));
            setup.Add(Make.Step(1, "On the TV, open the Developer Mode app (free in the LG Content Store), sign in and turn on Dev Mode Status. " +
                                   "After the TV restarts, open it again and turn on Key Server.",
                                Make.Link("Help with Developer Mode", () => AdminMode.OpenAsUser(DevModeHelp))));
            setup.Add(Make.Step(2, "Pick your TV, or type its IP address", Make.Row(_tv, _search)));
            setup.Add(Make.Step(3, "Type the passphrase shown in the Developer Mode app", Make.Row(_passphrase, _install)));
            _progress.Font = Theme.Instructions;
            setup.Add(_progress);

            var options = column.Add(new Card());
            options.Add(Make.Heading("Options"));
            if (!AdminMode.IsEnabled)
            {
                var autostart = options.Add(new Toggle("Start with Windows") { Checked = Autostart.IsEnabled });
                autostart.CheckedChanged += (s, e) =>
                {
                    try { Autostart.Set(autostart.Checked); }
                    catch (Exception ex) { Log.Warn("Can't change autostart: " + ex.Message); }
                };
            }
            if (AdminMode.UserIsAdmin) AddAdminModeOption(options);

            column.Add(Make.Text("This PC: " + Environment.MachineName + " · port " + _server.Port +
                                  ". If Windows asks whether MagicRemote may use the network, allow it on private networks.", muted: true, small: true));
            return page;
        }

        void AddAdminModeOption(Card options)
        {
            var box = options.Add(new Toggle("Let the remote work in administrator windows too") { Checked = AdminMode.IsEnabled });
            var note = options.Add(Make.Text("Task Manager, installers, and apps you allowed in a Windows permission prompt. Windows asks for " +
                                              "permission once, and MagicRemote moves to Program Files. The permission prompt itself and the lock " +
                                              "screen still need the PC's mouse or keyboard.", muted: true, small: true));
            note.Margin = new Padding(60, 0, 0, 4); // under the toggle's text
            bool reverting = false;
            box.CheckedChanged += (s, e) =>
            {
                if (reverting) return;
                bool ok = box.Checked ? AdminMode.RequestEnable() : AdminMode.Disable();
                if (!ok)
                {
                    reverting = true;
                    box.Checked = !box.Checked;
                    reverting = false;
                    return;
                }
                if (box.Checked) AdminModeRequested?.Invoke();
                else note.Text = "From your next sign-in, MagicRemote runs without administrator rights.";
            };
        }

        async Task SearchAsync()
        {
            _search.Enabled = false;
            var typed = _tv.Text;
            _tv.Items.Clear();
            _searching = true;
            Progress("Looking for LG TVs on your network…", Theme.Muted);
            try
            {
                var found = await TvFinder.SearchAsync(TimeSpan.FromSeconds(3));
                foreach (var tv in found) _tv.Items.Add(tv);
                if (found.Count > 0)
                {
                    _tv.SelectedIndex = 0;
                    Progress(found.Count == 1 ? "Found " + found[0] + "." : "Found " + found.Count + " TVs. Pick yours.", Theme.Muted);
                }
                else
                {
                    _tv.Text = typed;
                    Progress("No TV answered. Check that it's on and on the same network, or type its IP address " +
                             "(TV Settings → Network → Wi-Fi or Wired connection).", Theme.Muted);
                }
            }
            catch (Exception e)
            {
                Progress("Searching failed: " + e.Message, Theme.Danger);
            }
            finally
            {
                _search.Enabled = true;
                _searching = false;
            }
        }

        async Task InstallAsync()
        {
            // A typed address wins over a stale selection in the editable list.
            var tv = _tv.SelectedItem is FoundTv selected && selected.ToString() == _tv.Text ? selected : null;
            if (tv == null && IPAddress.TryParse(_tv.Text.Trim(), out var typed)) tv = new FoundTv { Address = typed, Name = "LG TV (" + typed + ")" };
            if (tv == null)
            {
                Progress("Pick a TV from the list or type its IP address.", Theme.Danger);
                return;
            }
            if (_passphrase.Box.Text.Trim().Length == 0)
            {
                Progress("Type the passphrase shown in the Developer Mode app on the TV.", Theme.Danger);
                _passphrase.Box.Focus();
                return;
            }

            SetBusy(true);
            Pairing pairing = null;
            try
            {
                Progress("Getting the Developer Mode key from the TV…", Theme.Muted);
                var pem = await DevModeInstaller.FetchKeyAsync(tv.Address);
                var ipk = IpkBuilder.Build();
                // Setting up the same TV again (e.g. to update its app) keeps the pairing, so the TV doesn't ask anything.
                pairing = Pairing.Create(_store, Environment.MachineName, tv.Address, _server.Port);
                // Known before the app starts on the TV, so its first connection is accepted.
                _store.AddTv(pairing.TvId, tv.Name, pairing.Key, tv.Address.ToString());

                var passphrase = _passphrase.Box.Text;
                var removedOld = await Task.Run(() => DevModeInstaller.Install(tv.Address, pem, passphrase, ipk, IpkBuilder.AppId, pairing.LaunchParamsJson,
                    step => BeginInvoke((Action)(() => Progress(step, Theme.Muted)))));

                _store.RemoveOtherPairings(ConfigStore.Hex(pairing.TvId), tv.Address.ToString());
                Log.Info((pairing.Reused ? "Updated " : "Installed and paired ") + tv);
                _passphrase.Box.Clear();
                var old = removedOld.Count > 0 ? "The old MagicRemoteService TV app was removed.\n" : "";
                Progress(pairing.Reused
                    ? "Done! The TV app is up to date.\n" + old + "You can turn Key Server off again in the Developer Mode app."
                    : "Done! Now look at the TV:\n" +
                      "•  allow MagicRemote when the TV asks (only the first time),\n" +
                      "•  if it was paired with another PC before, choose Replace,\n" +
                      "•  confirm which HDMI input this PC is on.\n" + old +
                      "From now on, switching the TV to this PC starts MagicRemote by itself. " +
                      "You can turn Key Server off again in the Developer Mode app.", Theme.Accent);
                Paired?.Invoke();
            }
            catch (Exception e)
            {
                if (pairing != null && !pairing.Reused) _store.RemoveTv(ConfigStore.Hex(pairing.TvId));
                Log.Warn("Setup failed: " + e);
                Progress(e is InstallException ? e.Message : "Setup failed: " + e.Message, Theme.Danger);
            }
            finally
            {
                SetBusy(false);
                RefreshPaired();
            }
        }

        /// <summary>One row per paired TV: name, address, Forget.</summary>
        void RefreshPaired()
        {
            _pairedList.SuspendLayout();
            _pairedList.Controls.Clear();
            var tvs = _store.Tvs;
            if (tvs.Count == 0) Adopt(_pairedList.Add(Make.Text("No TV set up yet. Follow the steps below.", muted: true)));
            foreach (var tv in tvs)
            {
                var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var names = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
                names.Controls.Add(new Label { Text = tv.Name, Font = Theme.Strong, ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0) });
                names.Controls.Add(new Label { Text = tv.Address ?? "", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(0) });
                row.Controls.Add(names);
                var forget = new PillButton("Forget", PillKind.Ghost) { Anchor = AnchorStyles.Right, ForeColor = Theme.Danger };
                var id = tv.Id;
                var name = tv.Name;
                forget.Click += (s, e) => Forget(id, name);
                row.Controls.Add(forget);
                Adopt(row);
                _pairedList.Add(row);
            }
            _pairedList.ResumeLayout();
        }

        void Forget(string id, string name)
        {
            if (MessageBox.Show(this, "Forget " + name + "? It won't be able to control this PC until you set it up again.",
                                "MagicRemote", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            _store.RemoveTv(id);
            RefreshPaired();
        }

        void SetBusy(bool busy)
        {
            UseWaitCursor = busy;
            _install.Enabled = _search.Enabled = _tv.Enabled = _passphrase.Enabled = !busy;
        }

        void Progress(string text, Color color)
        {
            _progress.Text = text;
            _progress.ForeColor = color;
        }
    }
}

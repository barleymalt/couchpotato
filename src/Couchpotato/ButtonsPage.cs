using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Couchpotato.Config;
using Couchpotato.Input;
using Couchpotato.Ui;

namespace Couchpotato
{
    /// <summary>
    /// One row per remote button: pick a common action or type keys such as "ctrl+shift+esc".
    /// Changes are saved straight away and apply to the next button press.
    /// </summary>
    sealed class ButtonsPage : Panel
    {
        readonly ConfigStore _store;
        readonly Dictionary<string, Picker> _choices = new Dictionary<string, Picker>();
        readonly Dictionary<string, Label> _labels = new Dictionary<string, Label>();
        readonly Panel _scroll;
        readonly Label _status = Make.Text("", muted: true);
        readonly Timer _flash = new Timer { Interval = 1500 };
        Label _flashing;

        public ButtonsPage(ConfigStore store)
        {
            _store = store;
            BackColor = Theme.Background;
            _scroll = Make.Page(out var column);
            _scroll.Dock = DockStyle.Fill;
            Controls.Add(_scroll);

            column.Add(Make.Instructions("Choose what each remote button does on the PC: pick an action, or pick Other keys and type keys " +
                                         "joined with + (for example ctrl+shift+esc, win+r or f5). Press a button on the remote to find it here.", muted: true));

            var card = column.Add(new Card());
            var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            // About 25 rows: lay them out once at the end, not once per added control.
            column.SuspendLayout();
            card.SuspendLayout();
            table.SuspendLayout();
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            foreach (var button in ButtonCatalog.RemoteButtons)
            {
                var label = new Label
                {
                    Text = button.Value,
                    AutoSize = true,
                    Anchor = AnchorStyles.Left,
                    ForeColor = Theme.Text,
                    Font = Theme.Strong,
                    Margin = new Padding(0, 8, 8, 8),
                };
                // A plain list (a click anywhere opens it); "Other keys…" turns it into a box to type keys in.
                var choice = new Picker(260, "Other keys…") { Anchor = AnchorStyles.Left | AnchorStyles.Right };
                choice.SetItems(ActionNames);
                var key = button.Key;
                choice.Chosen += text => Save(key, text);
                table.Controls.Add(label);
                table.Controls.Add(choice);
                _labels[key] = label;
                _choices[key] = choice;
            }
            card.Add(table);
            table.ResumeLayout(false);
            card.ResumeLayout(false);
            column.ResumeLayout(true);

            var reset = new PillButton("Reset all to defaults");
            reset.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Put every remote button back to its default?", "Couchpotato",
                                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                _store.ResetKeyBindings();
                ShowSaved(null);
                Status("All buttons are back to their defaults.", Theme.Muted);
            };
            column.Add(Make.Row(reset));
            column.Add(_status);
            column.Add(Make.Text("Always the same: pointer and wheel, OK to click (hold for a right click), and holding Back to give the remote back to the TV.",
                                 muted: true, small: true));

            _flash.Tick += (s, e) => StopFlash();
            ShowSaved(null);
        }

        static readonly object[] ActionNames = ButtonCatalog.Actions.Select(a => (object)a.Key).ToArray();

        /// <summary>Scrolls to the row of a remote button the user just pressed, and lights it up.</summary>
        public void Highlight(ushort code)
        {
            var name = ButtonCatalog.NameOf(code);
            if (name == null || !_labels.TryGetValue(name, out var label))
            {
                // Shown here only, never logged: helps find out what an unlisted button sends.
                Status("The remote sent a button this page doesn't list (code " + code + ").", Theme.Muted);
                return;
            }
            StopFlash();
            _scroll.ScrollControlIntoView(_choices[name]);
            label.ForeColor = Theme.Accent;
            label.Font = Theme.Strong;
            _flashing = label;
            _flash.Start();
            Status("You pressed " + label.Text + ".", Theme.Muted);
        }

        void StopFlash()
        {
            _flash.Stop();
            if (_flashing == null) return;
            _flashing.ForeColor = Theme.Text;
            _flashing.Font = Theme.Strong;
            _flashing = null;
        }

        void Save(string key, string text)
        {
            var binding = ButtonCatalog.ToBinding(text);
            if (binding == _store.GetKeyBinding(key)) return;
            var choice = _choices[key];
            try
            {
                _store.SetKeyBinding(key, binding);
                choice.ShowError(false);
                ShowSaved(key);
                Status(_labels[key].Text + ": " + ButtonCatalog.Describe(binding) + ". Saved.", Theme.Accent);
            }
            catch (FormatException e)
            {
                choice.ShowError(true);
                Status(e.Message + ". Use names like ctrl, alt, shift, win, enter, f5, a or volume-up, joined with +.", Theme.Danger);
            }
        }

        /// <summary>Shows the saved binding for one key, or all keys when null.</summary>
        void ShowSaved(string onlyKey)
        {
            foreach (var kv in _choices)
            {
                if (onlyKey != null && kv.Key != onlyKey) continue;
                kv.Value.Value = ButtonCatalog.Describe(_store.GetKeyBinding(kv.Key));
                kv.Value.ShowError(false);
            }
        }

        void Status(string text, Color color)
        {
            _status.Text = text;
            _status.ForeColor = color;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _flash.Dispose();
            base.Dispose(disposing);
        }
    }
}

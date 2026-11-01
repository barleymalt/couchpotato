using System.Drawing;
using System.Windows.Forms;

namespace MagicRemote.Ui
{
    /// <summary>
    /// First run (and updates): install or just run. OK means install; Cancel means run from here.
    /// </summary>
    sealed class WelcomeForm : Form
    {
        public WelcomeForm(string title, string message, string installLabel, string skipLabel)
        {
            Text = "MagicRemote";
            using (var s = typeof(WelcomeForm).Assembly.GetManifestResourceStream("MagicRemote.ico")) Icon = new Icon(s);
            Theme.Apply(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var column = new Card(flat: true) { Dock = DockStyle.None, Padding = new Padding(28, 24, 28, 22), Width = 460 };
            Image logo;
            using (var s = typeof(WelcomeForm).Assembly.GetManifestResourceStream("MagicRemote.ico"))
            using (var icon = new Icon(s, 64, 64))
                logo = icon.ToBitmap();
            column.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(48, 48), Margin = new Padding(0, 0, 0, 12) });
            column.Add(new Label { Text = title, Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 0, 0, 8) });
            var body = column.Add(Make.Text(message, muted: true));
            body.MaximumSize = new Size(400, 0);
            body.Dock = DockStyle.None;

            var install = new PillButton(installLabel, PillKind.Primary) { DialogResult = DialogResult.OK };
            var skip = new PillButton(skipLabel, PillKind.Ghost) { DialogResult = DialogResult.Cancel };
            var buttons = Make.Row(install, skip);
            buttons.Margin = new Padding(0, 12, 0, 0);
            column.Add(buttons);
            Controls.Add(column);

            AcceptButton = install;
            CancelButton = skip;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MagicRemote.Config;
using MagicRemote.Ui;
using MagicRemote.Win32;

namespace MagicRemote
{
    /// <summary>Pick the pointer style and size used while the TV is connected.</summary>
    sealed class PointerPage : Panel
    {
        readonly ConfigStore _store;
        readonly Action _changed;
        readonly List<Tile> _tiles = new List<Tile>();
        readonly List<PillButton> _sizes = new List<PillButton>();
        string _style, _size;

        public PointerPage(ConfigStore store, Action changed)
        {
            _store = store;
            _changed = changed;
            _style = store.Pointer;
            _size = store.PointerSize;
            BackColor = Theme.Background;

            var page = Make.Page(out var column);
            Controls.Add(page);
            column.Add(Make.Instructions("The pointer on the PC while the TV is connected. Big pointers are easy to follow from the sofa; " +
                                 "your usual pointer comes back as soon as the TV disconnects.", muted: true));

            var styles = column.Add(new Card());
            styles.Add(Make.Heading("Style"));
            var tiles = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 4, 0, 0) };
            foreach (var style in PointerThemes.Styles)
            {
                var tile = new Tile(style.Value, PointerThemes.Preview(style.Key) ?? WindowsArrow()) { Selected = style.Key == _style, Tag = style.Key };
                tile.Chosen += (s, e) =>
                {
                    _style = (string)tile.Tag;
                    foreach (var t in _tiles) t.Selected = t == tile;
                    Save();
                };
                _tiles.Add(tile);
                tiles.Controls.Add(tile);
            }
            styles.Add(tiles);

            var sizes = column.Add(new Card());
            sizes.Add(Make.Heading("Size"));
            var row = Make.Row();
            foreach (var size in PointerThemes.Sizes)
            {
                var button = new PillButton(char.ToUpper(size.Key[0]) + size.Key.Substring(1), size.Key == _size ? PillKind.TabSelected : PillKind.Secondary) { Tag = size.Key };
                button.Click += (s, e) =>
                {
                    _size = (string)button.Tag;
                    foreach (var b in _sizes) b.Kind = b == button ? PillKind.TabSelected : PillKind.Secondary;
                    Save();
                };
                _sizes.Add(button);
                row.Controls.Add(button);
            }
            sizes.Add(row);
        }

        void Save()
        {
            _store.SetPointer(_style, _size);
            _changed?.Invoke();
        }

        static Image WindowsArrow()
        {
            var image = new Bitmap(64, 64);
            using (var g = Graphics.FromImage(image)) Cursors.Default.DrawStretched(g, new Rectangle(8, 8, 48, 48));
            return image;
        }
    }
}

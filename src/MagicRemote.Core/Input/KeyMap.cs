using System;
using System.Collections.Generic;
using System.Globalization;
using MagicRemote.Protocol;

namespace MagicRemote.Input
{
    public enum BindingKind { Keys, Mouse, TvAction }

    public sealed class Binding
    {
        public BindingKind Kind { get; private set; }
        public ushort[] Keys { get; private set; }
        public MouseButton Button { get; private set; }
        public TvAction Action { get; private set; }

        public static Binding Parse(string text)
        {
            var t = (text ?? "").Trim().ToLowerInvariant();
            switch (t)
            {
                case "": case "none": return null;
                case "keyboard": return new Binding { Kind = BindingKind.TvAction, Action = TvAction.Keyboard };
                case "power-menu": return new Binding { Kind = BindingKind.TvAction, Action = TvAction.PowerMenu };
                case "mouse-left": return new Binding { Kind = BindingKind.Mouse, Button = MouseButton.Left };
                case "mouse-right": return new Binding { Kind = BindingKind.Mouse, Button = MouseButton.Right };
                case "mouse-middle": return new Binding { Kind = BindingKind.Mouse, Button = MouseButton.Middle };
            }
            var parts = t.Split('+');
            var keys = new ushort[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!VirtualKeys.TryGetValue(parts[i].Trim(), out keys[i]))
                    throw new FormatException("Unknown key '" + parts[i] + "' in '" + text + "'");
            }
            return new Binding { Kind = BindingKind.Keys, Keys = keys };
        }

        static readonly Dictionary<string, ushort> VirtualKeys = BuildVirtualKeys();

        static Dictionary<string, ushort> BuildVirtualKeys()
        {
            var d = new Dictionary<string, ushort>
            {
                ["backspace"] = 0x08, ["tab"] = 0x09, ["enter"] = 0x0D, ["shift"] = 0x10, ["ctrl"] = 0x11,
                ["alt"] = 0x12, ["pause"] = 0x13, ["esc"] = 0x1B, ["space"] = 0x20, ["pageup"] = 0x21,
                ["pagedown"] = 0x22, ["end"] = 0x23, ["home"] = 0x24, ["left"] = 0x25, ["up"] = 0x26,
                ["right"] = 0x27, ["down"] = 0x28, ["insert"] = 0x2D, ["delete"] = 0x2E, ["win"] = 0x5B,
                ["menu"] = 0x5D, ["browser-back"] = 0xA6, ["browser-forward"] = 0xA7, ["browser-home"] = 0xAC,
                ["volume-mute"] = 0xAD, ["volume-down"] = 0xAE, ["volume-up"] = 0xAF, ["media-next"] = 0xB0,
                ["media-prev"] = 0xB1, ["media-stop"] = 0xB2, ["media-play-pause"] = 0xB3,
            };
            for (int i = 0; i <= 9; i++) d[i.ToString(CultureInfo.InvariantCulture)] = (ushort)(0x30 + i);
            for (char c = 'a'; c <= 'z'; c++) d[c.ToString()] = (ushort)(0x41 + (c - 'a'));
            for (int i = 1; i <= 24; i++) d["f" + i] = (ushort)(0x6F + i);
            return d;
        }
    }

    /// <summary>Maps webOS remote key codes to PC input. Defaults can be overridden per key in config.json.</summary>
    public sealed class KeyMap
    {
        public static readonly IReadOnlyDictionary<string, ushort> TvKeys = BuildTvKeys();

        public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
        {
            ["ok"] = "enter", ["back"] = "esc", ["backspace"] = "backspace",
            ["left"] = "left", ["up"] = "up", ["right"] = "right", ["down"] = "down",
            ["red"] = "power-menu", ["green"] = "win", ["yellow"] = "mouse-right", ["blue"] = "keyboard",
            ["play"] = "media-play-pause", ["pause"] = "media-play-pause", ["stop"] = "media-stop",
            ["rewind"] = "media-prev", ["fast-forward"] = "media-next",
            ["channel-up"] = "pageup", ["channel-down"] = "pagedown",
            ["0"] = "0", ["1"] = "1", ["2"] = "2", ["3"] = "3", ["4"] = "4",
            ["5"] = "5", ["6"] = "6", ["7"] = "7", ["8"] = "8", ["9"] = "9",
        };

        readonly Dictionary<ushort, Binding> _map = new Dictionary<ushort, Binding>();

        /// <param name="overrides">TV key name (or numeric webOS key code) → binding text.</param>
        /// <param name="errors">Receives one message per override that could not be parsed; those keep their default.</param>
        public KeyMap(IDictionary<string, string> overrides, IList<string> errors = null)
        {
            foreach (var kv in Defaults) _map[TvKeys[kv.Key]] = Binding.Parse(kv.Value);
            if (overrides == null) return;
            foreach (var kv in overrides)
            {
                try
                {
                    _map[ResolveTvKey(kv.Key)] = Binding.Parse(kv.Value);
                }
                catch (FormatException e)
                {
                    errors?.Add(e.Message);
                }
            }
        }

        public Binding Get(ushort tvKeyCode) => _map.TryGetValue(tvKeyCode, out var b) ? b : null;

        static ushort ResolveTvKey(string name)
        {
            var n = (name ?? "").Trim().ToLowerInvariant();
            if (TvKeys.TryGetValue(n, out var code)) return code;
            if (ushort.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out code)) return code;
            throw new FormatException("Unknown remote key '" + name + "'");
        }

        static Dictionary<string, ushort> BuildTvKeys()
        {
            var d = new Dictionary<string, ushort>
            {
                ["backspace"] = 8, ["ok"] = 13, ["pause"] = 19, ["channel-up"] = 33, ["channel-down"] = 34,
                ["left"] = 37, ["up"] = 38, ["right"] = 39, ["down"] = 40,
                ["red"] = 403, ["green"] = 404, ["yellow"] = 405, ["blue"] = 406,
                ["rewind"] = 412, ["stop"] = 413, ["play"] = 415, ["fast-forward"] = 417,
                ["info"] = 457, ["back"] = 461,
            };
            for (int i = 0; i <= 9; i++) d[i.ToString(CultureInfo.InvariantCulture)] = (ushort)(48 + i);
            return d;
        }
    }
}

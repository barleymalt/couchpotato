using System;
using System.Collections.Generic;
using System.Linq;

namespace MagicRemote.Input
{
    /// <summary>Remote buttons and common actions in plain words, for the settings window.</summary>
    public static class ButtonCatalog
    {
        /// <summary>Remote key name (as in config.json) → label.</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string>> RemoteButtons = new[]
        {
            Pair("ok", "OK (when the pointer is hidden)"),
            Pair("back", "Back (short press)"),
            Pair("up", "Up"), Pair("down", "Down"), Pair("left", "Left"), Pair("right", "Right"),
            Pair("red", "Red"), Pair("green", "Green"), Pair("yellow", "Yellow"), Pair("blue", "Blue"),
            Pair("play", "Play"), Pair("pause", "Pause"), Pair("stop", "Stop"),
            Pair("rewind", "Rewind"), Pair("fast-forward", "Fast forward"),
            Pair("channel-up", "Channel +"), Pair("channel-down", "Channel −"),
            Pair("info", "Info"),
            // No number buttons: the TV keeps them for choosing a channel, so they never reach MagicRemote.
        };

        /// <summary>Label → binding text. Anything else can be typed as keys joined with "+".</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string>> Actions = new[]
        {
            Pair("Nothing", "none"),
            Pair("Left click", "mouse-left"),
            Pair("Right click", "mouse-right"),
            Pair("Middle click", "mouse-middle"),
            Pair("Show the TV keyboard", "keyboard"),
            Pair("Shut down / sleep menu", "power-menu"),
            Pair("Enter", "enter"),
            Pair("Esc", "esc"),
            Pair("Backspace", "backspace"),
            Pair("Tab", "tab"),
            Pair("Space", "space"),
            Pair("Windows key (Start)", "win"),
            Pair("Switch window (Alt+Tab)", "alt+tab"),
            Pair("Task view (Win+Tab)", "win+tab"),
            Pair("Show desktop (Win+D)", "win+d"),
            Pair("Close window (Alt+F4)", "alt+f4"),
            Pair("Full screen (F11)", "f11"),
            Pair("Copy (Ctrl+C)", "ctrl+c"),
            Pair("Paste (Ctrl+V)", "ctrl+v"),
            Pair("Undo (Ctrl+Z)", "ctrl+z"),
            Pair("Page Up", "pageup"),
            Pair("Page Down", "pagedown"),
            Pair("Home", "home"),
            Pair("End", "end"),
            Pair("Arrow up", "up"),
            Pair("Arrow down", "down"),
            Pair("Arrow left", "left"),
            Pair("Arrow right", "right"),
            Pair("Volume up", "volume-up"),
            Pair("Volume down", "volume-down"),
            Pair("Mute", "volume-mute"),
            Pair("Play / pause", "media-play-pause"),
            Pair("Next track", "media-next"),
            Pair("Previous track", "media-prev"),
            Pair("Stop media", "media-stop"),
            Pair("Browser back", "browser-back"),
            Pair("Browser forward", "browser-forward"),
        };

        /// <summary>The label for a binding, or the binding itself when it isn't one of the common actions.</summary>
        public static string Describe(string binding)
        {
            var match = Actions.FirstOrDefault(a => string.Equals(a.Value, binding, StringComparison.OrdinalIgnoreCase));
            return match.Key ?? binding;
        }

        /// <summary>The binding for a label from <see cref="Actions"/>, or the text itself (typed keys).</summary>
        public static string ToBinding(string labelOrKeys)
        {
            var match = Actions.FirstOrDefault(a => a.Key == labelOrKeys);
            return match.Value ?? (labelOrKeys ?? "").Trim().ToLowerInvariant();
        }

        /// <summary>The remote key name for a webOS key code, or null.</summary>
        public static string NameOf(ushort code) =>
            KeyMap.TvKeys.Where(k => k.Value == code).Select(k => k.Key).FirstOrDefault(k => RemoteButtons.Any(b => b.Key == k));

        static KeyValuePair<string, string> Pair(string a, string b) => new KeyValuePair<string, string>(a, b);
    }
}

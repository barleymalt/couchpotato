using System;
using System.Collections.Generic;
using System.Text;
using Couchpotato.Protocol;

namespace Couchpotato.Input
{
    /// <summary>
    /// Turns decrypted TV messages into input for one connection, and remembers what is held
    /// down so a dropped connection never leaves a key or mouse button stuck.
    /// </summary>
    public sealed class InputSession
    {
        const int MaxTextBytes = 512;
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        readonly IInputSink _sink;
        readonly Func<KeyMap> _keyMap;
        readonly Action<byte[]> _send;
        readonly HashSet<ushort> _heldKeys = new HashSet<ushort>();
        readonly HashSet<MouseButton> _heldButtons = new HashSet<MouseButton>();
        readonly HashSet<ushort> _heldActions = new HashSet<ushort>();
        readonly Action<ushort> _keyPressed;
        readonly Action<string> _tvAppVersion;
        DateTime _lastBlockedNotice = DateTime.MinValue;

        /// <param name="keyMap">Read on every key press, so edited bindings apply without reconnecting.</param>
        /// <param name="keyPressed">Told the webOS key code of every remote key press, e.g. to find it in the settings.</param>
        /// <param name="tvAppVersion">Told the TV app's version when it reports it.</param>
        public InputSession(IInputSink sink, Func<KeyMap> keyMap, Action<byte[]> send, Action<ushort> keyPressed = null, Action<string> tvAppVersion = null)
        {
            _sink = sink; _keyMap = keyMap; _send = send; _keyPressed = keyPressed; _tvAppVersion = tvAppVersion;
        }

        public void Handle(byte[] m)
        {
            if (m.Length == 0) return;
            switch ((TvMessage)m[0])
            {
                case TvMessage.Move when m.Length == 5:
                    Report(_sink.MoveTo(U16(m, 1) / 65536.0, U16(m, 3) / 65536.0));
                    break;
                case TvMessage.Button when m.Length == 3 && m[1] <= (byte)MouseButton.Middle:
                    SetButton((MouseButton)m[1], m[2] != 0);
                    break;
                case TvMessage.Wheel when m.Length == 3:
                    int delta = -(short)U16(m, 1) * 3;
                    Report(_sink.Wheel(Math.Max(-1200, Math.Min(1200, delta))));
                    break;
                case TvMessage.Key when m.Length == 4:
                    OnKey(U16(m, 1), m[3] != 0);
                    break;
                case TvMessage.Text when m.Length > 1 && m.Length <= MaxTextBytes + 1:
                    OnText(m);
                    break;
                case TvMessage.Power when m.Length == 2 && (m[1] == (byte)PowerAction.Shutdown || m[1] == (byte)PowerAction.Sleep):
                    ReleaseAll();
                    _sink.Power((PowerAction)m[1]);
                    break;
                case TvMessage.Info when m.Length > 1 && m.Length <= 33:
                    try { _tvAppVersion?.Invoke(StrictUtf8.GetString(m, 1, m.Length - 1)); }
                    catch (DecoderFallbackException) { }
                    break;
                case TvMessage.Ping when m.Length == 1:
                    _send(new[] { (byte)PcMessage.Pong });
                    break;
            }
        }

        public void ReleaseAll()
        {
            foreach (var b in _heldButtons) _sink.Button(b, false);
            _heldButtons.Clear();
            foreach (var k in _heldKeys) _sink.Key(k, false);
            _heldKeys.Clear();
            _heldActions.Clear();
        }

        void OnKey(ushort code, bool down)
        {
            if (down) _keyPressed?.Invoke(code);
            var binding = _keyMap().Get(code);
            if (binding == null) return;
            switch (binding.Kind)
            {
                case BindingKind.Keys:
                    if (down)
                    {
                        // Repeated keydowns from a held remote button are forwarded so Windows auto-repeats.
                        foreach (var vk in binding.Keys) { _heldKeys.Add(vk); Report(_sink.Key(vk, true)); }
                    }
                    else
                    {
                        for (int i = binding.Keys.Length - 1; i >= 0; i--)
                            if (_heldKeys.Remove(binding.Keys[i])) _sink.Key(binding.Keys[i], false);
                    }
                    break;
                case BindingKind.Mouse:
                    SetButton(binding.Button, down);
                    break;
                case BindingKind.TvAction:
                    if (!down) _heldActions.Remove(code);
                    else if (_heldActions.Add(code)) _send(new[] { (byte)PcMessage.Action, (byte)binding.Action });
                    break;
            }
        }

        void SetButton(MouseButton button, bool down)
        {
            if (down)
            {
                _heldButtons.Add(button);
                Report(_sink.Button(button, true));
            }
            else if (_heldButtons.Remove(button))
            {
                _sink.Button(button, false);
            }
        }

        void OnText(byte[] m)
        {
            string text;
            try { text = StrictUtf8.GetString(m, 1, m.Length - 1); }
            catch (DecoderFallbackException) { return; }

            var clean = new StringBuilder(text.Length);
            foreach (var c in text) if (!char.IsControl(c)) clean.Append(c);
            if (clean.Length > 0) Report(_sink.Text(clean.ToString()));
        }

        void Report(bool accepted)
        {
            if (accepted || DateTime.UtcNow - _lastBlockedNotice < TimeSpan.FromSeconds(10)) return;
            _lastBlockedNotice = DateTime.UtcNow;
            var reason = Encoding.UTF8.GetBytes(_sink.BlockedReason ?? "");
            var message = new byte[2 + Math.Min(reason.Length, 400)];
            message[0] = (byte)PcMessage.Status;
            message[1] = (byte)PcStatus.InputBlocked;
            Buffer.BlockCopy(reason, 0, message, 2, message.Length - 2);
            _send(message);
        }

        static ushort U16(byte[] b, int i) => (ushort)(b[i] | b[i + 1] << 8);
    }
}

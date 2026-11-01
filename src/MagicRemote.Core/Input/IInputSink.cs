using MagicRemote.Protocol;

namespace MagicRemote.Input
{
    /// <summary>
    /// Operating-system side of input injection. Methods return false when the OS refused the
    /// input, e.g. because the workstation is locked.
    /// </summary>
    public interface IInputSink
    {
        /// <summary>Moves the pointer to a normalized position (0..1) on the TV display.</summary>
        bool MoveTo(double x, double y);
        bool Button(MouseButton button, bool down);
        /// <summary>Windows wheel units: 120 per notch, positive scrolls up.</summary>
        bool Wheel(int delta);
        bool Key(ushort virtualKey, bool down);
        bool Text(string text);
        void Power(PowerAction action);
        /// <summary>After a refusal: a sentence for the TV explaining why and what to do, or null for the generic one.</summary>
        string BlockedReason { get; }
    }
}

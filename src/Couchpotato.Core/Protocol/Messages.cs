namespace Couchpotato.Protocol
{
    /// <summary>First byte of a decrypted TV → PC record.</summary>
    public enum TvMessage : byte
    {
        Move = 0x01,    // x:u16 y:u16, 0..65535 across the TV screen
        Button = 0x02,  // button:u8 (0 left, 1 right, 2 middle), down:u8
        Wheel = 0x03,   // deltaY:i16 in DOM pixels, positive scrolls down
        Key = 0x04,     // webOS keyCode:u16, down:u8
        Text = 0x05,    // UTF-8 text from the TV on-screen keyboard
        Power = 0x06,   // PowerAction:u8
        Ping = 0x07,
        Info = 0x08,    // UTF-8 version of the TV app, sent once after connecting (from 1.4.0)
    }

    /// <summary>First byte of a decrypted PC → TV record.</summary>
    public enum PcMessage : byte
    {
        Action = 0x81,  // TvAction:u8
        Pong = 0x82,
        Status = 0x83,  // PcStatus:u8, then an optional UTF-8 explanation for the user
    }

    public enum TvAction : byte
    {
        Keyboard = 1,
        PowerMenu = 2,
    }

    public enum PcStatus : byte
    {
        InputBlocked = 1,
    }

    public enum PowerAction : byte
    {
        Shutdown = 1,
        Sleep = 2,
    }

    public enum MouseButton : byte
    {
        Left = 0,
        Right = 1,
        Middle = 2,
    }
}

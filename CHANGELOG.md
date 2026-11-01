# Changelog

Updating: replace `MagicRemote.exe`, then open it → **TV** tab → **Install on TV**. The pairing is kept. From 1.4.0 the PC tells you when the TV app needs updating; patch versions (1.4.x) only need it when noted below.

## 1.7.1 (TV app 1.4.3: reinstall it for the drag fix)
- Fixed: dragging (resizing a window by its edge, selecting text) pressed the button only after the pointer had moved away from where you pressed. It now presses where the drag starts. TV app change.
- Lists are plain lists: a click anywhere opens them, the arrow is always visible, and they no longer stay highlighted. To type keys or an IP address, pick Other keys / Type an IP address.
- An open list is at most half the screen tall.
- MagicRemote's own buttons and links use Windows' hand pointer, so they follow the TV pointer style.
- Arcane's normal pointer is a bronze arrow; the gauntlet is for links.

## 1.7.0 (TV app 1.4.2: no need to reinstall it)
- New pointer styles **Neon** (cyberpunk) and **Arcane** (dark fantasy), replacing Sci-fi and Fantasy. They cover all 17 Windows pointers: links, text, resizing, moving, not allowed, help, pen and more.
- Busy and Working in background are animated, in every built-in style.
- Pointer packs: Windows pointer sets (.cur/.ani with an install.inf) in a folder inside the pointers folder show up as styles.

## 1.6.4 (TV app 1.4.2: no need to reinstall it)
- Fixed: switching tabs flickered the old page for a moment before fading.
- Fixed: opening a list on the Buttons page could fail with "Error creating window handle". Typing in a list still completes the action name.
- Your TV shows the TV like the design: name · address, then Connected or Paired and the TV app version.
- Start with Windows is shown in administrator mode too, as always on (Windows starts MagicRemote at sign-in there).
- The Buttons page says when the remote sends a button it doesn't list.

## 1.6.3 (TV app 1.4.2: no need to reinstall it)
- Switching tabs cross-fades, and the new page is drawn before it shows, so there's no stutter (off when Windows' animation effects are off).
- Drop-down lists match the theme: a rounded dark field with a chevron, and a dark list.
- Fixed: empty space under rows of buttons and tiles (the Pointer cards, the TV search row).

## 1.6.2 (TV app 1.4.2: no need to reinstall it)
- The window is sized for the screen it's on and rescales when moved to another, so it's no longer tiny on a 4K TV next to a laptop.
- It opens at half the width of the screen (was a quarter).

## 1.6.1 (TV app 1.4.2: no need to reinstall it)
- Fixed: the window crashed on opening ("Object reference not set to an instance of an object").

## 1.6.0 (TV app 1.4.2: reinstall only if you want the new icon on the TV)
- The window has its own title bar instead of the Windows one: logo, version and connection status, with minimize, maximize and close. Dragging, snapping and resizing work as before.
- New font (Manrope, built in), larger instructions and more room around the edges.
- The window opens at a quarter of the width of the screen it appears on (never too small to read).
- Closing the window hides it; MagicRemote keeps running in the tray, and the window opens instantly next time.
- The Buttons page opens much faster: button lists are filled only when you use them.
- New logo for the app, the tray and the TV app.

## 1.5.0 (TV app 1.4.1: no need to reinstall it)
- New look: dark "living-room" design matching the TV app, with a live connection status in the header, card layout, switches, and pointer style tiles. Dark title bar and tray menu.
- MagicRemote.exe installs itself: on first run choose Install (just for you, no administrator rights) or Just run it. You get a Start menu entry, start with Windows, and an entry in Settings → Apps to uninstall.
- Running a newer MagicRemote.exe offers to update the installed one.
- "Start with Windows" moved into the TV page's Options.

## 1.4.1 (TV app 1.4.1: no need to reinstall it)
- Fixed: turning on administrator mode could leave MagicRemote not starting at all. Its copy crashed while starting, and a normal launch kept handing over to it.
- Setup now checks that the administrator copy really runs, and turns administrator mode off again if it doesn't.
- If MagicRemote ever stops because of an error, it says so and writes the details to the log.

## 1.4.0
- The "Let the remote work in administrator windows too" option now appears for administrator accounts (it was hidden unless MagicRemote itself ran as administrator).
- Version numbers: in the window title, the tray menu and tooltip, the log, the TV app's home screen, and the exe's properties.
- The PC warns when the TV app is a different version and says how to update it.

## 1.3.0
- "Windows is blocking the remote" appears only when input really is being dropped, and names the app and the fix.

## 1.2.0
- Optional administrator mode, so the remote works in Task Manager, installers and other administrator windows.
- Choosing the MagicRemote tile on the TV home screen takes you to the PC; the app can't get stuck as an invisible window any more.
- The settings window can be resized.

## 1.1.0
- Buttons tab: change what each remote button does; press a button on the remote to find it.
- Pointer tab: six big pointer styles in four sizes while the TV is connected.
- TV menus get the remote back automatically; hold Back to leave MagicRemote.
- Setup removes the old MagicRemoteService TV app and keeps the pairing when you run it again.

## 1.0.0
- First version: paired and encrypted remote control, one-click TV setup, Wake-on-LAN, IP and HDMI auto-detection.

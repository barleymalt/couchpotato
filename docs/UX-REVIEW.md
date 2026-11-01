# Setup and everyday use: MagicRemoteService vs MagicRemote

## Where MagicRemoteService makes you work

| Step | MagicRemoteService | MagicRemote |
|---|---|---|
| Prerequisites on the PC | Install Node.js and LG's webOS CLI globally (`ares-*` on PATH) | None: one 3 MB exe, no admin rights |
| Adding the TV | Type its IP; "Key Server" note; advanced options (port, user, private key) | Found automatically on the network (SSDP); type the IP only if it isn't |
| Installing the TV app | Pick the TV, HDMI input, display, then type the PC's IP, port, subnet mask, MAC, long-click delay and three mode checkboxes (input direct, overlay, extend) | Type the 6-character passphrase shown on the TV, click Install |
| How the TV finds the PC | PC IP, port, MAC and mask are hardcoded into the TV app, rebuilt per TV | Learned at pairing; if the PC's IP changes the TV finds it again by broadcast |
| HDMI input | Picked on the PC from a list | Taken automatically when only one input has a signal, otherwise asked once on the TV |
| Which monitor is the TV | Picked on the PC | Detected from the monitor's name ("LG TV …") |
| Security | None (open port) | Pairing happens during install, nothing to configure |
| Starting with Windows | "Automatically launch at startup" installs a SYSTEM service | Turned on after the first pairing (per-user Run key), toggle in the tray |
| PC asleep or off | Dialog on the TV, press "Start up" | Wake-on-LAN sent automatically when you switch to the PC's input |
| Pointer feel | Gyroscope-based relative mode with fixed speed, one big cursor, and changed Windows mouse speed | Absolute: the PC cursor sits under the remote's pointer. Six big pointer styles in four sizes, only while the TV is connected |
| Button mapping | Per-key dialog, plus "run command" bindings | Buttons tab: list of common actions or typed key combos; press a remote button to find its row |
| Getting back to the TV | Not explained | Hold Back, or open a TV menu; the TV shows the hint the first few times |
| Updating | Stop the service by hand, replace the exe, reinstall the TV app, re-save the PC tab | Replace the exe; run "Set up a TV" again, which keeps the pairing and removes the old app |
| Errors | Debug toasts at the bottom of the TV screen | Plain-language messages on the TV and in the setup window; log file for details |

## What can't be made automatic

- **LG Developer Mode.** Without publishing in the LG Content Store, apps can only be installed through Developer Mode. It needs a free LG account, a restart, and the Key Server for setup. The rest is automated.
- **The one-time permission prompt on the TV.** Reading the foreground app (to open the overlay on the PC's input) goes through the TV's own API, which asks the user once.
- **Wake-on-LAN** must be enabled in the PC's BIOS and network adapter settings.
- **Lock screen.** A per-user app can't type there. On a living-room PC, use automatic sign-in and don't require sign-in after sleep.

## Possible next steps

- Pair without SSH (e.g. a code shown on the TV and typed on the PC) for TVs where the app is installed with other tools such as the webOS Homebrew Channel.
- Optional "advanced" mode as a small signed helper with UI Access, to reach elevated windows without running as SYSTEM.
- Remember the Developer Mode key (DPAPI-protected) so TV app updates can be pushed without retyping the passphrase.
- Translations.

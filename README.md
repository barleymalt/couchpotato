# MagicRemote

Use the LG Magic Remote as the mouse and keyboard of a Windows PC connected to an LG webOS TV.

A rewrite of [MagicRemoteService](https://github.com/Cathwyler/MagicRemoteService), focused on safety and on setup that just works. Why it was rewritten: [security review](docs/SECURITY-REVIEW.md) and [setup comparison](docs/UX-REVIEW.md). How it works: [docs/DESIGN.md](docs/DESIGN.md).

- **One small exe.** It installs itself (no admin rights), or runs portable. No Node.js or LG command-line tools, no Windows service.
- **Paired and encrypted.** Only your TV can control your PC: every message is authenticated and encrypted with a key created during setup.
- **Plug and play.** The PC finds the TV, installs the TV app itself, and works out its own IP, MAC address, the TV's HDMI input and which monitor is the TV.

> **Status:** early. Covered by 22 automated tests (crypto vectors, the TV app and its background service talking to the PC server, packaging, settings) and tried on one LG TV with a Windows PC. Report what breaks.

## Set up (once)

1. **On the TV:** install **Developer Mode** from the LG Content Store, sign in with a free [LG developer account](https://webostv.developer.lge.com/), and turn on **Dev Mode Status**. The TV restarts. Open the app again and turn on **Key Server**.
2. **On the PC:** run `MagicRemote.exe` and choose **Install** (just for you, no administrator rights; or **Just run it** to keep it portable). The setup window finds your TV.
3. Type the **passphrase** shown in the Developer Mode app and click **Install on TV**.
4. **On the TV:** allow MagicRemote when asked, and confirm the HDMI input of the PC if asked.

Then turn **Key Server** off again in the Developer Mode app. MagicRemote now starts with Windows, and switching the TV to the PC's input starts it on the TV by itself. If the old MagicRemoteService TV app is installed, setup removes it.

**Updating:** run the new `MagicRemote.exe` and choose **Update**. If the changelog says the TV app changed, also run **Set up a TV** again (TV tab → **Install on TV**). It keeps the pairing, so the TV asks nothing. The version is in the window title and the tray menu; the PC tells you when the TV app is a different version. What changed: [CHANGELOG.md](CHANGELOG.md).

If Windows asks whether MagicRemote may use the network, allow it on **private** networks.

## Use

Point and click as on the TV. Hold OK still for a moment to right-click; hold and move to drag.

**To use the TV itself, hold Back** for a second: MagicRemote steps aside and the remote is the TV's again. It comes back when you switch to the PC's input, or when you choose the MagicRemote tile on the TV's home screen. Opening the TV's own menus (Home, settings) also hands the remote back automatically.

| Remote button | PC |
|---|---|
| OK / wheel click | Left click (Enter when the pointer is hidden) |
| Hold OK | Right click |
| Wheel | Scroll |
| Arrows | Arrow keys |
| Back | Esc (hold: give the remote back to the TV) |
| Red | Shut down or sleep the PC (asks on the TV). When the PC is off: wake it |
| Green | Windows key (Start menu) |
| Yellow | Right click |
| Blue | Show or hide the TV keyboard, to type on the PC |
| 0–9 | 0–9 |
| CH + / CH − | Page Up / Page Down |
| Play, Pause, Stop, ⏪, ⏩ | Media keys |

When the PC is asleep or off and Wake-on-LAN is enabled in its BIOS and network adapter settings, switching the TV to its input wakes it up.

### Change the buttons

Tray icon → **Buttons…**. Pick an action for each remote button from the list, or type keys joined with `+` (for example `ctrl+shift+esc`, `win+r`, `f5`). Changes apply to the next button press. Not sure which button is which? Press it on the remote and its row lights up.

The settings live in `%LOCALAPPDATA%\MagicRemote\config.json` (`"keys"` section), if you prefer editing text.

### Pointer style

Tray icon → **Pointer…**. While the TV is connected, the PC uses a big pointer that's easy to follow from the sofa; your usual pointer comes back when it disconnects. Four sizes, and these styles:

- **Neon** (cyberpunk) and **Arcane** (dark fantasy): all 17 Windows pointers (links, text, resizing, busy…), with an animated Busy and Working.
- **Drop** (like the TV's own pointer), **Classic**, **Cartoon** and **Retro**: the main pointers, others stay Windows' own.
- **Windows default**, or any **pointer pack**: a Windows pointer set (`.cur`/`.ani` files, usually with an `install.inf`) in its own folder inside `%LOCALAPPDATA%\MagicRemote\Pointers` (Pointer tab → **Open the pointers folder**). Packs made for normal screens can look soft at the big sizes.

![Neon and Arcane](docs/pointer-styles-drawn.png)
![Simple pointer styles](docs/pointer-styles.png)

With several monitors, the TV is picked by its name (e.g. "LG TV SSCR2"). To force one, add `"display": "\\\\.\\DISPLAY2"` or part of its name. Monitor names are listed in the log.

## Good to know

- **Administrator windows.** Windows doesn't let normal programs control windows running as administrator: Task Manager, some installers and launchers, and apps you allowed in an "Allow this app to make changes?" prompt. While one is in front, the remote stops and the TV names the app and the fix. (MagicRemote checks that the pointer really stopped following the remote before saying so.) To make the remote work there too, tick **Let the remote work in administrator windows too** on the TV tab (administrator accounts only). Windows asks once; MagicRemote then lives in `C:\Program Files\MagicRemote` and starts at sign-in through Task Scheduler.
- **Permission prompts and the lock screen** are out of reach in every mode: only SYSTEM can type there, which is what made the old app dangerous. Use the PC's mouse or keyboard for the "Allow" click. For a living-room PC, turn on automatic sign-in and set *Settings → Accounts → Sign-in options → "If you've been away…"* to **Never**.
- **Developer Mode expires.** LG ends Developer Mode sessions after a while and removes sideloaded apps. MagicRemote asks the Developer Mode app to extend the session every 28 days, as MagicRemoteService did. If the app ever disappears, run **Set up a TV** again.
- **Coming from MagicRemoteService?** See [Removing MagicRemoteService](#removing-magicremoteservice). MagicRemote uses a different port (41234), so the two don't clash while you switch.
- **Files:** settings in `%LOCALAPPDATA%\MagicRemote\config.json` (pairing keys protected with Windows DPAPI), log in `log.txt` next to it. The log never contains what you type.

## Uninstalling

Settings → Apps → MagicRemote → Uninstall. It asks whether to keep your settings and TV pairing. The TV app stays on the TV; remove its tile there like any other app.

## Removing MagicRemoteService

**TV:** running **Set up a TV** removes it (all its copies, one per HDMI input). By hand: press Home on the TV, select the MagicRemoteService tile and remove it as you would any other app.

**PC:** depending on how its settings were saved, it starts itself either as a Windows service or as a Task Scheduler task, both called `MagicRemoteService`, and it may have added a firewall rule. Open **Windows PowerShell as administrator** (Start → type *PowerShell* → *Run as administrator*) and paste:

```powershell
Stop-Service MagicRemoteService -Force -ErrorAction SilentlyContinue
sc.exe delete MagicRemoteService
Unregister-ScheduledTask -TaskName MagicRemoteService -Confirm:$false -ErrorAction SilentlyContinue
Get-Process MagicRemoteService -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-NetFirewallRule -DisplayName MagicRemoteService -ErrorAction SilentlyContinue
Remove-Item HKLM:\Software\MagicRemoteService, HKCU:\Software\MagicRemoteService -Recurse -ErrorAction SilentlyContinue
```

Then delete the folder that holds `MagicRemoteService.exe`. If `sc.exe` says the service is "marked for deletion", restart the PC once.

Without PowerShell: in **Services**, open *MagicRemoteService*, click *Stop* and set *Startup type* to *Disabled*; in **Task Scheduler** → *Task Scheduler Library*, delete *MagicRemoteService* if it's there; then delete the folder.

If you installed LG's webOS CLI and Node.js only for it, uninstall them too. Also delete `%USERPROFILE%\.webos`: the CLI keeps the TV's Developer Mode key and passphrase there.

## Build

Needs the [.NET SDK](https://dotnet.microsoft.com/download) 9 or newer, on Windows, macOS or Linux, plus Node.js for the JavaScript tests.

```bash
dotnet build src/MagicRemote -c Release
```

The result is `src/MagicRemote/bin/Release/net48/MagicRemote.exe` (about 3 MB, dependencies embedded).

```bash
dotnet run --project tests/MagicRemote.Tests    # needs Node.js, and port 3000 free for the TV service test
```

To look at the TV screens in a desktop browser, serve the repository root (for example `python3 -m http.server 8765`) and open `http://localhost:8765/tests/tv-preview/?scenario=first-run` (also `home`, `connecting`, `connected`, `not-paired`, `replace`).

After changing the pointer art, run `python3 tools/make_pointer_styles.py` (Neon and Arcane, drawn as SVG; needs Pillow and `pip install resvg-py`) or `python3 tools/make_cursors.py` (the simple styles; needs Pillow).
The logo comes from `tools/make_icon.py` (Pillow). The UI font is [Manrope](https://github.com/googlefonts/manrope) (SIL Open Font License 1.1, see `src/MagicRemote/Fonts/OFL.txt`), cut down to static Latin weights by `tools/make_fonts.py` (needs fontTools).

**Releases:** bump `<Version>` in `Directory.Build.props`, add a `CHANGELOG.md` entry, commit, then run `tools/release.sh`. It tests and builds everything into `distros/<version>/` (the exe, the TV app `.ipk` and their SHA-256 sums) and tags the commit `v<version>`, so `git checkout v1.6.0` gives back any release's source. `distros/` is not in git; every release can be rebuilt from its tag.

To get the TV app as an `.ipk` for other install tools: `MR_IPK_OUT=magicremote.ipk dotnet run --project tests/MagicRemote.Tests`. Pairing only happens through **Set up a TV**.

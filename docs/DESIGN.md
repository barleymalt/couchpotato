# Design

## Pieces

```
 Windows PC                                     LG webOS TV (Developer Mode)
┌──────────────────────────────┐               ┌──────────────────────────────────────┐
│ MagicRemote.exe (per user)   │  WebSocket    │ com.magicremote.tv  (web app)        │
│  tray + setup window         │◀──────────────│  transparent overlay over HDMI input │
│  AgentServer  :41234/tcp     │  encrypted    │  captures pointer, buttons, keys     │
│  DiscoveryResponder :41234/udp◀─ broadcast ──│                                      │
│  SendInput                   │               │ com.magicremote.tv.service (Node)    │
│                              │  SSH :9922    │  watches foreground app (SSAP)       │
│  DevModeInstaller ───────────┼──────────────▶│  opens/closes the overlay            │
│  (setup only)                │               │  Wake-on-LAN, discovery broadcast    │
└──────────────────────────────┘               └──────────────────────────────────────┘
```

| Path | What |
|---|---|
| `src/MagicRemote.Core` | Portable logic (netstandard2.0): protocol, crypto, WebSocket server, key map, config, `.ipk` builder, TV discovery, Developer Mode installer. Embeds `tvapp/`. |
| `src/MagicRemote` | Windows glue (.NET Framework 4.8, preinstalled on Windows 10/11): tray, settings window (TV, Buttons, Pointer), `SendInput`, pointer styles, monitor detection, autostart, DPAPI. Builds into a single `MagicRemote.exe`. |
| `tools/make_cursors.py` | Draws the pointer styles (PNG) embedded in the exe. |
| `tvapp/app` | The webOS app, ES5 so it runs on old webOS browsers. |
| `tvapp/service` | The webOS JS service, ES5 for old Node versions. |
| `tests/` | C# test runner (crypto vectors, protocol, server, packaging) that also drives the TV JavaScript against the C# server through Node. |

## Setup flow

1. The PC finds TVs with SSDP (`urn:lge-com:service:webos-second-screen:1`).
2. It downloads the Developer Mode SSH key from `http://TV:9991/webos_rsa` and decrypts it with the passphrase the user reads off the TV.
3. It builds the `.ipk` in memory from the embedded `tvapp/` (same layout as LG's `ares-package`), streams it over SSH (`prisoner@TV:9922`) and installs it with `luna-send-pub ... com.webos.appInstallService/dev/install`, as `ares-install` does.
4. It launches the app with `{"pair": {tvId, key, pcId, host, port, mac, broadcast}}`. The pairing key only ever travels inside that SSH session.
5. On the TV, the app stores the pairing and asks the service to watch the foreground app. The TV asks once for permission (`READ_RUNNING_APPS`). If exactly one HDMI input has a signal it is taken as the PC's input; otherwise the TV asks once.

From then on: switching the TV to the PC's input → the service opens the overlay → the overlay connects (sending Wake-on-LAN and a discovery broadcast if the PC doesn't answer) → the Magic Remote drives the PC. Switching away closes the overlay. When a TV menu takes the focus, the overlay pauses and shows the TV's pointer again; holding Back closes it until the user comes back to the input.

Setting up the same TV again (to update its app) keeps the pairing. Setup also removes old MagicRemoteService apps (`com.cathwyler.magicremoteservice*`) through the same `appInstallService/dev/remove` call `ares-install --remove` uses.

**Administrator mode (optional).** Windows' integrity levels stop a normal process from sending input to elevated windows. When the user opts in, an elevated helper (`MagicRemote.exe --admin-install`, one UAC prompt) copies the exe to Program Files and registers a Task Scheduler task that starts it at sign-in with highest privileges. Program Files matters: a task that elevates an exe in Downloads would let any program become administrator by replacing the file. In this mode the server refuses peers on the PC itself, so local programs can't use it to reach elevated windows. Windows drops such input silently (SendInput still reports success), so the agent only reports a block when the cursor stopped following its moves and the foreground process has a higher integrity level, and names that app to the TV. The UAC prompt and lock screen remain out of reach (SYSTEM only).

**Pointer styles.** While a TV is connected, the agent replaces the normal, link, text and busy pointers with `SetSystemCursor`, scaled to the TV's height. It restores them with `SystemParametersInfo(SPI_SETCURSORS)`, which reloads the user's own scheme. That happens on disconnect, on exit, and at the next start if the app was killed while a TV was connected (a marker file records that).

## Protocol

Transport: WebSocket binary messages to `ws://PC:41234/`. The server accepts only small, unfragmented, masked binary frames.

**Handshake** (`K` = 32-byte pairing key, `‖` = concatenation, `T` = `tvId ‖ tvNonce ‖ pcNonce`):

| Direction | Bytes |
|---|---|
| TV → PC | `"MR" 0x01 ‖ tvId[16] ‖ tvNonce[16]` |
| PC → TV | `"MR" 0x01 ‖ pcNonce[16] ‖ HMAC(K, "MR1 pc proof" ‖ T)` |
| TV → PC | `HMAC(K, "MR1 tv proof" ‖ T)` |

The TV checks the PC's proof before sending its own, so a fake PC (for example one that answered a discovery broadcast) learns nothing. Session keys are `HMAC(K, label ‖ T)` with labels `"MR1 tv>pc enc"`, `"MR1 tv>pc mac"`, `"MR1 pc>tv enc"` and `"MR1 pc>tv mac"`.

**Records**: `ChaCha20(encKey, nonce = 0⁴ ‖ seq_le64, counter 0) ‖ HMAC(macKey, seq_le64 ‖ ciphertext)[0..16]`. Each direction counts `seq` from 0, so a replayed, dropped, reordered or modified record fails and closes the connection.

**Messages** (first plaintext byte):

| TV → PC | Body |
|---|---|
| `0x01` move | `x:u16 y:u16`, 0–65535 across the TV screen |
| `0x02` button | `button:u8` (0 left, 1 right, 2 middle), `down:u8` |
| `0x03` wheel | `deltaY:i16` (DOM pixels, positive = down) |
| `0x04` key | `webOS keyCode:u16`, `down:u8` |
| `0x05` text | UTF-8, at most 512 bytes |
| `0x06` power | 1 shutdown, 2 sleep |
| `0x07` ping | — |
| `0x08` info | TV app version, UTF-8, once after connecting (1.4.0+) |

| PC → TV | Body |
|---|---|
| `0x81` action | 1 toggle on-screen keyboard, 2 show power menu |
| `0x82` pong | — |
| `0x83` status | 1 input blocked, then an optional UTF-8 explanation shown on the TV |

**Discovery** (UDP, same port): the TV broadcasts `"MRDISCOVER1" ‖ pcId`; the paired PC answers `"MRHERE1" ‖ pcId ‖ port:u16`. The answer is not trusted: the handshake authenticates the PC.

## Threat model

Protected against:
- other devices on the network, including ones that can sniff or inject traffic,
- web pages in a browser on the PC or elsewhere,
- an attacker answering discovery broadcasts or spoofing the PC's IP,
- replaying recorded sessions,
- other local users of the PC (nothing runs with more rights than the signed-in user; the pairing key is protected with DPAPI).

Not protected against, by design or by platform limits:
- **Someone who controls the TV.** Developer Mode gives SSH access to its owner. The pairing key lives in the app's storage on the TV.
- **Other apps on the TV** can launch MagicRemote and call its background service (to open the overlay, send Wake-on-LAN, or change which input it watches). They can't read the pairing key. A pairing sent in launch parameters replaces an existing one only after the user picks "Replace" on the TV; the default is "Keep current".
- **Someone who already runs code as you on the PC.** They don't need this app to control your session.
- **The Developer Mode setup moment.** LG's Key Server serves the SSH key over plain HTTP, protected by a 6-character passphrase, and SSH host keys aren't pinned. Turn Key Server off after setup.
- **No forward secrecy.** If the pairing key leaks, recorded past sessions can be decrypted. Re-pairing creates a new key.
- **Lock screen and UAC prompts can't be controlled.** That is the price of not running as SYSTEM.
- **In administrator mode**, someone holding the pairing key can type into elevated windows that are already open, not just into the user's own. Without it, Windows keeps them out.

## What was left out on purpose, compared to MagicRemoteService

| Feature | Why |
|---|---|
| Windows service as SYSTEM, lock-screen and UAC control | The main source of findings 3–5 in the security review. A per-user process is far simpler. |
| "Run command" key bindings | Shell execution from a network-triggered event isn't worth the risk. Key combinations cover launchers (e.g. `win+r`). |
| Control of admin windows and UAC prompts | Windows only lets administrator or SYSTEM programs do that. A per-user app keeps the blast radius small; the TV explains when Windows blocks it. |
| Gyroscope-based relative pointer | The pointer is absolute: the PC cursor sits exactly under the remote's pointer, with no speed or acceleration settings, and Windows' mouse settings are never changed. |
| Auto-shutdown after inactivity | Windows power settings already do this. Sleep plus Wake-on-LAN gives the same result. |
| "Input source" mode (video inside the app) | The overlay mode covers the same use with less code. |
| French and Spanish UI | English only for now; strings are in two files (`SetupForm.cs`, `app.js`). |

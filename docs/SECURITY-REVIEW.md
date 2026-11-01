# Security review of MagicRemoteService

Reviewed: [Cathwyler/MagicRemoteService](https://github.com/Cathwyler/MagicRemoteService) at commit `36cbec1` (August 2026). File references are relative to `MagicRemoteService/` in that repository.

## Verdict

**Not safe to use on any network where you don't trust every device, and risky even at home.** Anything that can reach port 41230 (any device on the network, or **any web page open in a browser on the PC itself**) can type and click on your PC without a password. With the recommended "start automatically" mode it does so with SYSTEM rights, so it can also approve administrator prompts and type on the lock screen. That same mode lets any local program or user become SYSTEM.

The README warns that traffic is unencrypted, but the problem is bigger than eavesdropping: there is no authentication at all.

If you keep using it until you switch:
- don't enable "Automatically launch at startup" (that is what installs the SYSTEM service),
- in Windows Firewall, limit inbound TCP 41230 to the TV's IP address,
- close your browser while it runs (the firewall can't stop a web page connecting to 127.0.0.1), and never type passwords through it.

## Findings

| # | Severity | Finding |
|---|----------|---------|
| 1 | Critical | Anyone on the network can control the PC |
| 2 | Critical | Any web page can control the PC (cross-site WebSocket) |
| 3 | High | Remote input runs as SYSTEM on the secure desktop |
| 4 | High | Local users and programs can become SYSTEM |
| 5 | Medium | Unsafe IPC: BinaryFormatter on a named pipe, world-writable events |
| 6 | Medium | Unauthenticated shutdown and crash-restart loops |
| 7 | Low | Keystrokes travel in clear text |
| 8 | Low | Programs started by bare name from a SYSTEM process |
| 9 | Info | Developer Mode Key Server left on |

### 1. Anyone on the network can control the PC (Critical)

The server listens on all interfaces (`Service.cs:444`, `Service.cs:567`) and completes a WebSocket handshake with whoever connects (`Service.cs:1010-1016`). After that, binary messages are turned directly into input: keys (`Service.cs:1123-1154`), arbitrary Unicode text (`Service.cs:1155-1161`), mouse moves and clicks. There is no password, pairing, token, or check of who is connecting.

A device on the same network can send "Win+R", type `powershell -c ...`, press Enter, and run code as the signed-in user, or as SYSTEM given finding 3. Port forwarding, UPnP, a guest Wi-Fi bridged to the LAN, or a compromised IoT device all make this reachable.

### 2. Any web page can control the PC (Critical)

The handshake ignores the `Origin` header (`Service.cs:1010-1016`). Browsers let pages open WebSocket connections to `ws://127.0.0.1:41230`. Loopback counts as a secure origin, so mixed-content rules don't block it. Some recent Chrome versions add a permission prompt for local-network access; other browsers don't. A malicious ad or link opened on the PC can therefore send the same messages as the TV, and the firewall doesn't help because the traffic never leaves the machine.

### 3. Remote input runs as SYSTEM on the secure desktop (High)

The installer registers the service as LocalSystem (`ProjectInstaller.Designer.cs:37`). For every connection the service starts its "client" in the user's session with a duplicate of **winlogon.exe's SYSTEM token** (`Service.cs:377-435`). `SendInputAdmin` then switches the thread to the current input desktop (`Service.cs:357-376`), which includes the Winlogon desktop that shows UAC prompts and the lock screen. Combined with findings 1 and 2, a remote attacker can click "Yes" on elevation prompts and type on the lock screen.

### 4. Local users and programs can become SYSTEM (High)

Three independent paths:

- **SYSTEM GUI in the user's session.** The SYSTEM client from finding 3 shows the tray icon and Settings window (`Program.cs:32-55`). Because it runs as SYSTEM, `bElevated` is true (`Program.cs:15`) and settings go to HKLM. From that window a standard user can add a "Command" key binding (`BindCreator.cs`), which runs as SYSTEM through `cmd /c` (`Service.cs:1134-1141`). Text typed into the TV name or passphrase fields is also concatenated into `cmd /c` command lines (`WebOSCLI.cs:149-161`, `WebOSCLI.cs:207-238`), so shell metacharacters run commands as SYSTEM too.
- **World-writable synchronisation objects.** All named mutexes and events are created in `Global\` with Everyone:FullControl (`Program.cs:9-14`, `Service.cs:90-95`). Any local process can open the SYSTEM Settings window (`Program.cs:34`, `Program.cs:63`), make the service hand it the next TV connection, or stop and restart it.
- **Service binary in a user-writable folder.** The service is registered from wherever the exe happens to be (`Setting.cs:191`), typically Downloads or the Desktop. Any program running as the user, even unelevated, can replace that file and gets SYSTEM at the next start. For administrators this is also a silent UAC bypass.

### 5. Unsafe IPC: BinaryFormatter on a named pipe (Medium)

The service hands sockets to the client by serialising them with `BinaryFormatter` over a named pipe with a fixed name and the default ACL (`Service.cs:437`, `457`, `501`, `514`, `630`, `668`). Microsoft documents BinaryFormatter as unsafe for any data an attacker can influence. The client never checks who owns the pipe (`GetServerProcess` is called but not used). A local process that creates the pipe first, which is easy while the service restarts (finding 6), can feed the SYSTEM client a deserialisation payload. Any local process can also connect as the client and receive TV sockets.

### 6. Unauthenticated shutdown and crash-restart loops (Medium)

Message type `0x06` runs `shutdown /s /t 0` for anyone (`Service.cs:1162-1169`). WebSocket frame lengths are read from the network and used without bounds checks against the 4 KB buffer (`Service.cs:1056-1075`). Frames split across TCP reads are not handled, and any exception restarts the whole service (`Service.cs:696-701`). Being memory-safe C#, this gives denial of service rather than code execution.

### 7. Keystrokes in clear text (Low)

Everything, including text typed with the TV keyboard, crosses the network unencrypted. The README acknowledges this. Anyone on the path (shared Wi-Fi, ARP spoofing) can read it or impersonate the PC.

### 8. Programs started by bare name from SYSTEM (Low)

`cmd` and `shutdown` are started without a full path (`Service.cs:731`, `1137`, `1164`, `1204`), so they are resolved through the search path of a SYSTEM process.

### 9. Developer Mode Key Server left on (Info)

Installing requires the TV's Developer Mode "Key Server". It serves the TV's SSH key to anyone on the network, protected only by the 6-character passphrase on screen, which is brute-forceable offline. Nothing tells the user to switch it off afterwards. This applies to every sideloaded webOS app, not just this one.

## How the rewrite addresses them

| Finding | In Couchpotato |
|---|---|
| 1, 7 | Every TV is paired with its own random 256-bit key. Each connection runs a mutual HMAC-SHA256 challenge-response, then every message is encrypted (ChaCha20) and authenticated (HMAC-SHA256 with sequence numbers, so replay and tampering are rejected). The key reaches the TV only inside the SSH session used to install the app; it never crosses the network in the clear. |
| 2 | Same as above: a web page has no key. Browser origins (`http://`, `https://`) are also refused before the handshake. |
| 3, 4, 5, 8 | No Windows service, no SYSTEM, no IPC. One per-user process, started through the HKCU Run key. It cannot reach the lock screen or UAC prompts by design. Nothing runs shell commands; `shutdown.exe` is started by full path. |
| 6 | The WebSocket parser rejects oversized, fragmented, unmasked and text frames. A connection must authenticate within 5 s in total, and unauthenticated connections have their own budget (8 overall, 2 per address), so they can't crowd out the TV. Peers outside private address ranges are dropped. Repeated failures block an address for 5 minutes, except a paired TV's address, so nothing can lock the TV out that way. Held keys and buttons are released when a connection drops. Forgetting a TV ends its live session. Power actions need an authenticated session. |
| 9 | The setup window tells the user to turn Key Server off after installing. |

Remaining limits are listed in [DESIGN.md](DESIGN.md#threat-model).

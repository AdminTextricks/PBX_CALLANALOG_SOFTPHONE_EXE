# CallAnalog Softphone — Operator Guide

## Install

1. Run `build.ps1` on a build machine. Each release is published to `dist\callanalog v1.x.y` (see `VERSION` in the repo root).
2. Copy that version folder to the user's PC (e.g. `C:\Program Files\CallAnalog Softphone`).
3. Launch `CallAnalog.Softphone.exe`.
4. Sign in with extension and password provided by your carrier/IT team.

Optional: enable **Start with Windows** in Settings for automatic startup.

## Logs

| Location | Purpose |
|----------|---------|
| `%LOCALAPPDATA%\CallAnalog\logs\sip.log` | SIP trace and app events |
| `%LOCALAPPDATA%\CallAnalog\crashes\` | Crash report text files |
| `%LOCALAPPDATA%\CallAnalog\exports\` | Diagnostics zip exports from Settings |

Open logs from **Settings → Open Logs Folder** or **Export Diagnostics** for support tickets.

Manual QA: use **`docs/MANUAL_TESTING_SOP.md`** for full pre-release test procedures.

## Firewall / network ports

| Direction | Protocol | Port | Notes |
|-----------|----------|------|-------|
| Outbound | TCP | 5065 (default) | SIP registration and call signaling to carrier |
| Outbound | UDP | 5065 | If transport set to UDP |
| Outbound | UDP | 10000–20000 (typical) | RTP media — exact range depends on carrier/NAT |
| Outbound | HTTPS | 443 | PBX API (`pbxbackend.callanalog.com`) and server reachability probe |

Allow the softphone executable through Windows Firewall on domain/private networks.

## Versioned builds

| Command | Version example | Output folder |
|---------|-----------------|---------------|
| `.\build.ps1` | Rebuild current `VERSION` (e.g. 1.0.1) | `dist\callanalog v1.0.1` |
| `.\build.ps1 -BumpMinor` | 1.0.1 → 1.0.2 | `dist\callanalog v1.0.2` |
| `.\build.ps1 -BumpMajor` | 1.0.2 → 1.1.0 | `dist\callanalog v1.1.0` |

The leading `1` is fixed for this product line. Bump **minor** (last digit) for small fixes; bump **major** (middle digit) for larger changes.

Before each bumped build, document changes under `## x.y.z` in `CHANGELOG.md`. The build copies that section to `CHANGES.txt` in the publish folder.

## Known limits

- **Conference** is not available in this build (button hidden).
- **DND / Auto Answer** are local to this app only; they do not sync with the PBX or other phones.
- **Auto Answer** waits 3 seconds before answering when enabled.
- **Local call recording** captures a mixed WAV/MP3 of both legs on this device (microphone + remote audio). PBX recording is separate.
- **System speaker volume** slider adjusts Windows output volume for the selected device, not per-call RTP gain.
- **Server reachability** in-call reflects HTTP latency to the PBX API, not RTP call quality.
- **Crash report email** requires `CrashReport:SmtpHost` in `appsettings.json`; otherwise reports stay local.
- **Updates** are distributed manually — contact IT for new builds.
- **Remember Me off** still keeps the extension in DPAPI-protected local storage for the session, but not in plaintext settings JSON.

## Support checklist

1. Note extension, app version (Settings), and time of issue.
2. Export diagnostics from Settings.
3. Attach recent `sip.log` (Authorization headers are redacted in exports).
4. Confirm SIP registration status and firewall rules above.

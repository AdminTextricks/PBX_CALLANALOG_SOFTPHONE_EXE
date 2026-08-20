# Windows installer guide (CallAnalog Softphone)

This project ships an [Inno Setup](https://jrsoftware.org/isinfo.php) script at `installer/CallAnalogSoftphone.iss`. It packages the self-contained publish folder into a single setup executable with publisher metadata, Start menu entry, optional desktop shortcut, and an uninstaller.

## Prerequisites

1. **Publish the app** (from repo root):

   ```powershell
   .\build.ps1
   ```

   Output lands in `dist\callanalog v<version>\` (for example `dist\callanalog v1.2.2\`).

2. **Install Inno Setup 6**  
   Download from [jrsoftware.org/isdl.php](https://jrsoftware.org/isdl.php). The free compiler (`ISCC.exe`) is enough.

3. **Sync version in the `.iss` script**  
   Open `installer/CallAnalogSoftphone.iss` and set `#define MyAppVersion` to match `VERSION` at the repo root. The publish path macro uses that value.

## Build the installer

**Option A — Inno Setup Compiler GUI**

1. Open `installer/CallAnalogSoftphone.iss` in Inno Setup.
2. Build → Compile.
3. Setup exe is written to `installer/output/CallAnalogSoftphone-Setup-<version>.exe`.

**Option B — Command line**

```powershell
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" "installer\CallAnalogSoftphone.iss"
```

## What users see (publisher information)

| Location | Field shown | Source in this project |
|----------|-------------|------------------------|
| Settings → Apps → Installed apps | App name, publisher | `AppName`, `AppPublisher` in `.iss` (`CallAnalog Softphone`, `CallAnalog`) |
| Add/Remove Programs (classic) | Publisher, version | Same + `AppVersion` |
| UAC prompt (if elevated install) | Verified publisher | **Only after Authenticode signing** — see [CODE_SIGNING.md](CODE_SIGNING.md) |
| Setup wizard title bar | Product name + version | `AppVerName` |
| Start menu | Shortcut name + icon | `{#MyAppName}`, exe icon from publish output |
| Desktop (optional task) | Shortcut | User-selected during setup |

Unsigned builds show **Publisher: CallAnalog** in Apps & features, but SmartScreen may warn “Unknown publisher” until the setup exe and/or app exe are signed.

## Code signing

Sign **before** distributing to end users. See [CODE_SIGNING.md](CODE_SIGNING.md) for certificate vendors, `signtool.exe` usage, and timestamp servers.

Recommended order:

1. `.\build.ps1` — publish to `dist\`
2. Sign `dist\callanalog v*\CallAnalog.Softphone.exe` (and optionally every `.exe`/`.dll` in that folder)
3. Compile the Inno Setup script
4. Sign the generated `CallAnalogSoftphone-Setup-*.exe`

To sign during Inno compile, uncomment `SignTool` and `SignedUninstaller` in `CallAnalogSoftphone.iss` after your certificate is installed.

## MSIX alternative (brief)

MSIX gives Store-like install/update and clearer publisher identity when signed with a trusted cert. For a desktop SIP softphone with self-contained .NET output, **Inno Setup is simpler** today: no packaging manifest, no MSIX tooling in CI, and familiar “Next → Install” flow for IT admins.

Consider MSIX later if you need:

- Microsoft Store or Intune LOB deployment
- Automatic updates via App Installer / Store
- Strict per-user sandboxing

Tools: [MSIX Packaging Tool](https://learn.microsoft.com/en-us/windows/msix/packaging-tool/tool-overview) or `dotnet publish` + manual manifest. You still need the same Authenticode certificate for trusted publisher display.

## Troubleshooting

| Issue | Fix |
|-------|-----|
| “Source file does not exist” | Run `build.ps1` first; confirm `PublishDir` in `.iss` matches `dist\callanalog v<version>\` |
| Wrong version in installer | Update `#define MyAppVersion` in `.iss` to match `VERSION` |
| SmartScreen blocks setup | Expected for unsigned builds; sign setup exe or build reputation over time |
| App icon missing in shortcut | Ensure publish used a build with `ApplicationIcon` set (v1.2.2+) |

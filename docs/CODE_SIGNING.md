# Code signing (Authenticode)

Windows shows **Verified publisher: CallAnalog** (or your legal entity name) only when executables are signed with a trusted code-signing certificate. Signing also reduces SmartScreen warnings and is required for some enterprise deployment policies.

## Certificate types

| Type | Typical use | SmartScreen | Notes |
|------|-------------|-------------|-------|
| **OV** (Organization Validation) | Standard desktop apps | Builds reputation over time; early downloads may still warn | Lower cost |
| **EV** (Extended Validation) | Commercial shrink-wrap / immediate trust | Immediate SmartScreen benefit (with Microsoft EV program) | Requires hardware token (USB HSM); higher cost |

Common vendors (compare pricing and support): DigiCert, Sectigo, GlobalSign, SSL.com, Certum.

Obtain a certificate in the **exact publisher name** you want displayed (e.g. `CallAnalog` or your registered legal name). That string must match `AppPublisher` / assembly `Company` metadata.

## Install the certificate

1. Complete validation with the CA.
2. Export or install the `.pfx` on the build machine (EV certs stay on the hardware token).
3. Import into **Current User → Personal** or **Local Machine → Personal** (Windows Certificate Manager / `certmgr.msc`).

Keep the private key secure; use a dedicated build VM or CI secret store for release signing.

## Sign with signtool.exe

`signtool` ships with the [Windows SDK](https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/). Typical path:

`C:\Program Files (x86)\Windows Kits\10\bin\<version>\x64\signtool.exe`

### Sign the main application exe

After `.\build.ps1`, from repo root:

```powershell
$dist = "dist\callanalog v1.2.2"
$exe  = Join-Path $dist "CallAnalog.Softphone.exe"

signtool sign `
  /fd SHA256 `
  /tr http://timestamp.digicert.com `
  /td SHA256 `
  /a `
  $exe
```

- `/a` — auto-select the best installed code-signing cert (omit and use `/sha1 <thumbprint>` to pick a specific cert).
- `/fd SHA256` — digest algorithm (required for modern Windows).
- `/tr` + `/td SHA256` — **RFC 3161 timestamp** so the signature remains valid after the cert expires.

### Sign the installer

After compiling Inno Setup:

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a `
  "installer\output\CallAnalogSoftphone-Setup-1.2.2.exe"
```

### Verify

```powershell
signtool verify /pa /v "dist\callanalog v1.2.2\CallAnalog.Softphone.exe"
Get-AuthenticodeSignature "dist\callanalog v1.2.2\CallAnalog.Softphone.exe"
```

Expected: `Status: Valid`, `SignerCertificate` subject matching your publisher.

## Timestamp servers

Use one RFC 3161 HTTPS/HTTP server (CA docs may list their own):

| Provider | URL |
|----------|-----|
| DigiCert | `http://timestamp.digicert.com` |
| Sectigo | `http://timestamp.sectigo.com` |
| GlobalSign | `http://timestamp.globalsign.com/tsa/r6advanced1` |

Always timestamp release builds.

## Inno Setup integration

In `installer/CallAnalogSoftphone.iss`, uncomment:

```ini
SignTool=signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a $f
SignedUninstaller=yes
```

Inno runs `SignTool` on the setup exe and uninstaller when compiling. You should still sign `CallAnalog.Softphone.exe` in the publish folder **before** compiling the installer so the installed app is also signed.

## CI / scripted release (outline)

1. `.\build.ps1`
2. Sign all shipped `.exe` files in the publish directory (main exe at minimum).
3. `ISCC.exe installer\CallAnalogSoftphone.iss`
4. Sign `installer\output\CallAnalogSoftphone-Setup-*.exe`
5. Upload artifacts.

Store the `.pfx` password in CI secrets; on EV hardware-token builds, signing may require an interactive or HSM-attached agent.

## What signing does *not* fix

- Wrong or missing **application icon** — set via `ApplicationIcon` in the `.csproj` (fixed in v1.2.2).
- Incorrect **product name** in Apps & features — set `AppPublisher`, `VersionInfoCompany`, and assembly metadata.
- SIP/TLS trust — separate from Authenticode; use proper server certificates for VoIP infrastructure.

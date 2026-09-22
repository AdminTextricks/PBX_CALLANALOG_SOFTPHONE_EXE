# CallAnalog Softphone

Windows WPF desktop SIP softphone (`CallAnalog Softphone`, assembly `CallAnalog.Softphone`).

This README was written from the current source, project, config, and installer files. **No build or test was performed while creating it.**

## Requirements

| Item | Value (from project files) |
|------|----------------------------|
| App name | CallAnalog Softphone |
| Product / company | CallAnalog Softphone / CallAnalog |
| App version (`VERSION`, `appsettings.json` `App:Version`, `.csproj` `<Version>`, installer `#define MyAppVersion`) | 2.3.0 |
| Main project | `CallAnalog.Softphone.csproj` (repo root) |
| Target framework | `net10.0-windows10.0.18362` |
| Output type | `WinExe` (WPF) |
| Watchdog project | `tools\CallAnalog.Watchdog\CallAnalog.Watchdog.csproj` |
| Watchdog target framework | `net10.0` (`win-x64`, self-contained, `PublishSingleFile`) |
| Windows | TFM `windows10.0.18362`; installer `MinVersion=10.0.18362` |
| Installer architecture | `x64compatible` / 64-bit install mode |

A .NET 10 SDK is required to `dotnet run` / `dotnet publish`. End-user installs use a self-contained publish (runtime bundled).

## How to run locally

From the repository root (requires the .NET 10 SDK):

```powershell
dotnet run --project CallAnalog.Softphone.csproj
```

SDK default output for this TFM (after a local build, not produced for this README):

`bin\Debug\net10.0-windows10.0.18362\CallAnalog.Softphone.exe`

The main project copies `CallAnalog.Watchdog.exe` into that output folder after build (`CopyCallAnalogWatchdogToOutput`). If the watchdog EXE is missing, the app logs a warning and continues.

`appsettings.json` is copied to the output directory (`CopyToOutputDirectory=PreserveNewest`).

## Publish and run the published app

### `build.ps1` (versioned `dist` folder)

From the repo root:

```powershell
.\build.ps1
```

Defaults: `-Configuration Release`, `-Runtime win-x64`, `--self-contained true`, output:

`dist\callanalog v2.3.0\`

(uses the current `VERSION` file). Optional: `.\build.ps1 -BumpMinor` or `.\build.ps1 -BumpMajor`.

The publish command inside `build.ps1` is:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o <dist\callanalog v{VERSION}> `
    /p:PublishReadyToRun=true `
    /p:Version=<VERSION> `
    /p:FileVersion=<VERSION>.0 `
    /p:InformationalVersion=<VERSION>+<git-or-local>
```

Run the published app:

```powershell
.\dist\callanalog v2.3.0\CallAnalog.Softphone.exe
```

Watchdog is copied into the same publish directory by `CopyCallAnalogWatchdogToPublish` (After `Publish`).

### SDK default publish folder (installer source)

`installer\CallAnalogSoftphone.iss` packages:

`bin\Release\net10.0-windows10.0.18362\win-x64\publish`

(not the `dist\` folder). Comments in the `.iss` file describe that layout as multi-file self-contained (exe + native WPF runtime DLLs + `appsettings.json` + Assets), not `PublishSingleFile`.

To produce that folder (standard SDK path for this TFM/RID):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

Then run:

```powershell
.\bin\Release\net10.0-windows10.0.18362\win-x64\publish\CallAnalog.Softphone.exe
```

### Installer

Script: `installer\CallAnalogSoftphone.iss`.

Compile (from comments in that file):

```powershell
"%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" installer\CallAnalogSoftphone.iss
```

Output (from the same file): `installer\output\CallAnalog-Setup.exe`.

The installer copies the SDK publish tree into `{autopf}\CallAnalog Softphone` (`DefaultDirName`) and also copies `tools\CallAnalog.Watchdog\bin\Release\publish-win-x64\CallAnalog.Watchdog.exe` into `{app}`. Start Menu and Desktop shortcuts launch `CallAnalog.Softphone.exe` with `WorkingDir={app}`. User data is **not** stored under `{app}`; the script states settings, logs, recordings, and credentials live under `%LOCALAPPDATA%\CallAnalog`.

## Data locations (`%LOCALAPPDATA%`)

`Environment.SpecialFolder.LocalApplicationData` + `CallAnalog`. On a typical Windows profile that is:

`%LOCALAPPDATA%\CallAnalog`

which expands to:

`C:\Users\<username>\AppData\Local\CallAnalog`

| What | Path |
|------|------|
| App settings | `%LOCALAPPDATA%\CallAnalog\user-settings.json` |
| SIP / application log | `%LOCALAPPDATA%\CallAnalog\logs\sip.log` |
| Rotated SIP logs | `%LOCALAPPDATA%\CallAnalog\logs\sip.log.1` … `sip.log.3` (5 MB rotation, up to 3 archives) |
| Managed crash reports | `%LOCALAPPDATA%\CallAnalog\crashes\crash_*.txt` |
| Watchdog hang dump | `%LOCALAPPDATA%\CallAnalog\crashes\hang_{pid}_{yyyyMMdd_HHmmss}.dmp` |
| Watchdog hang text | `%LOCALAPPDATA%\CallAnalog\crashes\hang_{pid}_{yyyyMMdd_HHmmss}.txt` |
| Watchdog unexpected-exit / parent-missing text | `%LOCALAPPDATA%\CallAnalog\crashes\{reason}_{pid}_{yyyyMMdd_HHmmss}.txt` |
| Diagnostics zip (Settings) | `%LOCALAPPDATA%\CallAnalog\exports\diagnostics_{yyyyMMdd_HHmmss}.zip` |

### How to open these locations

**In the app (Settings)**

- **Open SIP Log** — opens `sip.log` with the default associated editor (`UseShellExecute`).
- **Open Logs Folder** — opens `%LOCALAPPDATA%\CallAnalog\logs` in Explorer.
- **Export Diagnostics** — writes a zip under `%LOCALAPPDATA%\CallAnalog\exports` (includes `version.txt`, redacted settings, and a tail of `sip.log`).

**From Explorer or PowerShell**

```powershell
explorer "$env:LOCALAPPDATA\CallAnalog"
explorer "$env:LOCALAPPDATA\CallAnalog\logs"
explorer "$env:LOCALAPPDATA\CallAnalog\crashes"
explorer "$env:LOCALAPPDATA\CallAnalog\exports"
```

There is no Settings button that opens the crashes folder; use Explorer as above, or paste `%LOCALAPPDATA%\CallAnalog\crashes` into the Explorer address bar. The unhandled UI exception dialog also points at `%LOCALAPPDATA%\CallAnalog\crashes\`.

## Crash, unexpected exit, and UI hang files

All of these are written under `%LOCALAPPDATA%\CallAnalog\crashes\` (created as needed).

### Managed crash (`CrashReportService`)

Triggered from `App.xaml.cs`:

- Dispatcher unhandled exception — source `"UI thread"`, `isTerminating: false`
- `AppDomain.UnhandledException` — source `"AppDomain"`, `isTerminating` from the event
- Unobserved task exception — source `"Task"`, `isTerminating: false`

File:

`crash_{yyyyMMdd_HHmmss_fff}.txt`

If Settings `SendCrashReport` is on and SMTP + recipient are configured, a successful email rename is:

`crash_{...}.txt.sent.txt`

(Pending sender also skips files already ending in `.sent.txt`.)

### UI hang (watchdog)

If the UI dispatcher heartbeat is missing for **15 seconds** (`HeartbeatTimeoutMs`), and `Stopping` is not set and the parent process is still running, the watchdog writes:

- `hang_{pid}_{yyyyMMdd_HHmmss}.dmp` — `MiniDumpWriteDump`
- `hang_{pid}_{yyyyMMdd_HHmmss}.txt` — reason `ui_hang`

The app pulses `Local\CallAnalog.Softphone.Heartbeat.{pid}` every **5 seconds** via `Dispatcher.BeginInvoke`.

### Unexpected parent exit (watchdog, no dump)

If the parent process exits **without** the `Stopping` signal, the watchdog writes a **text file only** (no `.dmp`):

- reason `parent_exited` → `parent_exited_{pid}_{yyyyMMdd_HHmmss}.txt`

If the parent cannot be opened at watchdog start:

- `parent_missing_{pid}_{yyyyMMdd_HHmmss}.txt`
- `parent_already_exited_{pid}_{yyyyMMdd_HHmmss}.txt`

Normal shutdown sets `Local\CallAnalog.Softphone.Stopping.{pid}` (manual-reset). The watchdog then exits **0** and does **not** write a hang dump. The app waits for the watchdog process to exit (no timeout, no `Kill`) before finishing WPF teardown.

## Watchdog (purpose and behavior)

Helper EXE: `CallAnalog.Watchdog.exe`, started only for the primary single-instance process, after the main window is shown.

- Arguments: `--pid {softphone PID}`
- Events (same session, `Local\` prefix): heartbeat (auto-reset) and stopping (manual-reset)
- Also waits on parent process exit
- Purpose: detect a stuck UI (no dispatcher heartbeat for 15s) and capture a hang minidump plus a sibling `.txt`
- Clean exit: `Stopping` set → no dump
- Parent gone without `Stopping` → `.txt` only
- Hang path: dump + `.txt`, then watchdog exits

The watchdog is a separate self-contained single-file publish; the WPF app is not single-file.

## How to verify / run (commands)

No build or test was run for this README. Commands below are those encoded in the project files.

**Run from source (SDK):**

```powershell
cd "<repo-root>"
dotnet run --project CallAnalog.Softphone.csproj
```

**Publish via `build.ps1` and run:**

```powershell
.\build.ps1
.\dist\callanalog v2.3.0\CallAnalog.Softphone.exe
```

**Publish to the installer input folder and run:**

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
.\bin\Release\net10.0-windows10.0.18362\win-x64\publish\CallAnalog.Softphone.exe
```

**Compile installer (after that SDK publish exists, and after the watchdog Release publish path exists):**

```powershell
"%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" installer\CallAnalogSoftphone.iss
```

Setup output: `installer\output\CallAnalog-Setup.exe`.

**Open log/report folders:**

```powershell
explorer "$env:LOCALAPPDATA\CallAnalog\logs"
explorer "$env:LOCALAPPDATA\CallAnalog\crashes"
```

Or in the running app: Settings → **Open Logs Folder** / **Open SIP Log**.

# Changelog

All notable changes for CallAnalog Softphone releases.  
Each build copies the section for the current version into `CHANGES.txt` inside the publish folder.

When starting a new version, add a section here **before** running `build.ps1 -BumpMinor` or `-BumpMajor`.

---

## 2.2.7

### Call waiting
- When one party hangs up during a dual call (hold + switch), the other leg is resumed instead of resetting both calls
- Held party disconnect clears held state only; active leg continues

---

## 2.2.5

### Call waiting & hangup
- Removed 120-second post-decline block on caller number and Call-ID (declined numbers can call back immediately)
- Hangup during call waiting ring now declines only the waiting call; active call stays connected
- Hanging up one leg of a dual call (hold + answer) resumes or promotes the other leg instead of dropping both
- Switch calls: fixed duplicate SIP event handlers on leg promotion; mute state follows the active leg

---

## 2.2.4

### UX & installer
- Desktop shortcut is checked by default on install
- In-call DTMF keypad shows sent digits (`Sent: 123#`)
- Blind transfer target accepts digits, `*`, and `#` only (invalid keys blocked while typing)
- Faster cold start: ReadyToRun publish + crash report upload deferred until after the window appears

### Sign-out & registration
- Sign-out now hangs up all call legs (including held/waiting) before unregister; sign-out fails if the call is still active
- Settings save triggers SIP re-REGISTER only when register expiry or keep-alive timing actually changed
- Re-register refresh ensures a SIP call user agent exists when missing

---

## 2.2.3

### Updates
- Settings → Check for Updates calls PBX `version-check` (`application_key: pbx_desktop_exe`)
- Automatic non-blocking version check after every successful login (soft banner only; no forced update)

### Call wrap-up
- Star rating is sent on the same `/public/api/callNote` API as column `rating` (no separate rating endpoint)

### Crash reports
- SMTP email delivery enabled (`no-reply@textricks.com` via configured SMTP host/port 587)

### Network quality
- Best-effort DSCP 46 (EF) marking on SIP and RTP/RTCP sockets (`Network:EnableDscp`, default on)
- Optional installer task / Start Menu entry runs elevated `Install-CallAnalogQos.ps1` for Windows QoS policy (PBX IP, SIP 5065, RTP range)
- Sticky PBX IP cache after DNS resolve (24h TTL, fail-open — cleared on REGISTER failure)

### Media recovery
- Mid-call media recovery available behind `Media:EnableMidCallRecovery` (default **off**); optional SIP hold/resume refresh via `Media:EnableSipReinviteRecovery` (default **off**)

### Audio (regression fix)
- Fixed garbled/absent call audio in both directions introduced by the WASAPI capture path: WASAPI shared mode returns the device mix format (32-bit IEEE float), and the converter was reinterpreting those bytes as 16-bit PCM, producing full-scale noise and 24x too many samples per frame
- Capture conversion now decodes IEEE float (32/64-bit) and 8/16/24/32-bit PCM, resolves WAVE_FORMAT_EXTENSIBLE, and downmixes any channel count
- Call audio defaults back to the WinMM backend used through 1.5; set `Audio:PreferWasapi` to `true` in `appsettings.json` to opt into WASAPI
- Capture downsampling (e.g. 48 kHz mic → 8 kHz PCMU) now runs a 3.4 kHz anti-alias filter, so high-frequency mic noise no longer folds into the voice band
- AGC no longer amplifies room tone below -40 dBFS, and its gain decays toward unity during silence so speech onset does not clip
- Capture log line now records encoding, bit depth, and channel count

---

## 2.2.2

### Quality
- Expand Layer A unit coverage to **366** automated tests (decline tracking, network loss/RTT, registration timing, voice settings, DSP, codec negotiation, PLC, CallQualityMonitor, silence frames, SIP OPTIONS helpers, PCM helpers)
- Extract `PlaybackConcealmentHelper` so packet-loss concealment is unit-testable on the call playback path

---

## 2.2.1

### Fix
- Reply `200 OK` (with `Allow`) to inbound out-of-dialog SIP `OPTIONS` probes instead of SIPSorcery's default `405 MethodNotAllowed`
- Prevents OpenSIPS (and similar) contact qualify from marking the extension unreachable when the PBX probes the registered contact

---

## 2.2.0

### Voice quality
- Settings → Voice Quality: profile (Low latency / Balanced / Stable Wi-Fi), echo control, noise reduction, AGC, Prefer Opus
- Call path prefers **WASAPI Communications** (shared mode) for capture/playback, with WinMM fallback
- Adaptive capture/playback buffers by voice profile
- Lightweight capture DSP (noise gate, AGC, echo ducking for speakerphone)
- Simple packet-loss concealment (repeat last frame on buffer underrun)
- In-call media quality line (loss/jitter estimate) plus hangup summary in `sip.log`
- Opus offered in SDP when Prefer Opus is on and the peer supports it (G.711 fallback retained)

---

## 2.1.0

### UI
- Premium teal/slate brand palette (replaces indigo accent)
- Segoe UI Variable typography for UI and display text
- Light theme dictionary + refined dark theme
- Dark mode and Follow Windows theme toggles restored in Settings
- Stronger CallAnalog brand on login
- Smoother page transitions

---

## 2.0.0

### Fix
- Non-fatal ringtone/hold-music stream teardown no longer shows the scary "unexpected error" dialog (logged to crashes folder only)
- MediaFoundation Startup/Shutdown reference counting balanced for custom MP3/M4A ringtone and hold music
- Crash dialog text points to `%LOCALAPPDATA%\CallAnalog\crashes\`
- `build.ps1` version scheme updated for 2.x releases

### Docs
- Added Golden Baseline (G1–G12) release gate
- Manual testing smoke updated for 2.0.0

### Note
- V2 ships phased: 2.0.0 bugs → 2.1 UI polish → 2.2 voice quality

---

## 1.5.0

### Fix
- WinMM output serialization: only one WaveOut (ringtone, hold music, ringback, call playback) may use the speaker at a time via `WinMmAudioOutputManager`
- Call waiting: declining a waiting call no longer restarts ringtone and leaves the active call silent (UI clears waiting state before state transition; call WaveOut reinitialized after decline)
- Double ringtone on incoming/call-waiting removed (single start path in `UpdateCallState`; `RingtoneService` dedupes same file/device)
- Custom ringtone: MP3/M4A PCM resampled to 44.1 kHz 16-bit; logs confirm custom vs generated tone path
- Hold music: call WaveOut fully suspended before hold music opens; call playback reinitialized when hold music stops
- Decline incoming: repeated INVITEs from the same caller number within 120s are auto-declined (in addition to Call-ID TTL)

---

## 1.4.9

### Fix
- Call waiting: declining a waiting call or starting call-waiting ringtone no longer kills inbound audio on the active call (primary WinMM playback re-armed)
- Hold music: call audio sink is paused and its RTP buffer flushed before hold music plays, preventing double audio on the agent speaker
- Custom ringtones: improved MP3/M4A playback (PCM conversion), app-storage path fallback, device fallback, and diagnostic logging

---

## 1.4.8

### Fix
- Call waiting: inbound audio restored after "End first call then answer" or ending the active leg during dual-call (playback tap + sink re-arm after leg swap)
- Call waiting: declining a waiting call no longer keeps ringing on re-INVITE (32s decline TTL)
- Custom ringtone upload: MP3/WMA/M4A files now play via Media Foundation (not only WAV)
- Hold music: call audio sink is muted while custom hold music plays so PBX/default RTP audio does not double-play
- Auto-answer toast no longer reappears after answering from the in-app UI

### Changed
- G.722 codec removed from Settings and SDP negotiation (PCMU/PCMA only)

---

## 1.4.7

### Fix
- Auto-login splash now has a Cancel button (L-12)
- Outbound ringback tone while dialing (DP-10)
- Dial failure shows an error on the dialpad (DP-13)
- Custom ringtone files are copied into app storage on upload (I-12 / ST-23)
- Ending the active call during dual-call waiting no longer disconnects the held party (CW-08)

### Changed
- Dark mode and Follow Windows theme controls hidden in Settings (logic unchanged)
- Warm transfer removed; blind transfer only

---

## 1.4.6

### Fix
- Call waiting Answer button now ends the first call then answers the second (label: "End first call then answer")
- Hold + Answer keeps the first call on hold and answers the second call

---

## 1.4.5

### Fix
- Call waiting: second call now uses a dedicated SIP user agent so the first call stays on hold instead of being dropped
- Switch calls UI: held-call strip and Switch buttons stay visible after answering the waiting call
- End call with two parties: hanging up the active leg resumes the held call instead of ending both

---

## 1.4.4

### Fix
- Call waiting SIP: concurrent INVITEs during an active call are now handled at the SIP transport layer because SIPSorcery does not raise OnIncomingCall while a dialog is active (no 180/486 was sent before; call appeared as missed)
- SIP User-Agent header now reports the actual app version from build metadata

---

## 1.4.3

### Fix
- Call waiting: restored prominent in-call panel with Answer / Decline / Hold+Answer buttons (banner alone was easy to miss)
- Call waiting UI now survives in-call state refreshes (mute, hold, duration updates) without hiding the waiting strip
- Dual-call banner only appears when a second call or held call is present

---

## 1.4.2

### Fix
- Call waiting UI: compact dual-call banner pinned above in-call controls shows active call and waiting call simultaneously
- SIP state consistency: established primary calls no longer reset to Idle when `IsCallActive` is briefly false during a second inbound invite

---

## 1.4.1

### Fix
- Outgoing voice meter during live calls now uses mic PCM from the active RTP path (fixes dead meter when WASAPI conflicted with WinMM capture)
- Incoming calls always show in-app UI and ringtone (removed race guard that skipped UI when CallState changed before UI thread ran)
- Windows incoming-call toasts when minimized to taskbar or when window is not focused (`ShouldShowIncomingToast`)

---

## 1.4.0

### Release
- Version **1.4.0** — comprehensive automated test coverage (290 unit tests, Tier A/B)
- Includes all fixes through v1.3.9 (retransmitted INVITE, stale call state, warm-transfer incoming UI, +1 dial, theme restore, v1.3.4 QA batch)

### Test infrastructure
- Tier A/B test suite: SIP incoming, dial, in-call, transfer, login, settings, history, contacts, UI, tray, security, audio, network, build metadata
- Extracted testable helpers for SIP routing, dial validation, declined Call-ID tracking, and shell guards

---

## 1.3.10

### Test infrastructure
- Extracted SIP/dial/tray/network helpers for unit testing (CallState routing, declined Call-ID TTL, registration timing, dial validation)
- Added Tier A/B automated test suite covering SIP, dial, incoming, in-call, transfer, login, settings, history, UI, tray, security, audio, network, and build metadata

---

## 1.3.9

### Fix
- Duplicate/retransmitted INVITE for the same Call-ID no longer triggers 486 Busy and a false "missed call while on another call" notification (fixes double-ring then busy to caller)
- Normal incoming calls now accept the INVITE immediately (180 Ringing) so OpenSIPS retransmissions are handled on the same transaction

---

## 1.3.8

### Fix
- Second inbound during an active call now enters call waiting instead of sending 486 Busy and showing a phantom “missed call while on another call” notification
- Connected outbound calls stuck in `Outgoing` app state (SIP session already active) are promoted to `InCall` before inbound handling so call waiting eligibility is correct
- Waiting INVITEs are accepted immediately (180 Ringing) so the remote caller keeps ringing until the user answers or declines

---

## 1.3.7

### Fix
- Post–warm-transfer incoming calls no longer get stuck ringing when the remote party cancels (stale primary-leg hangup ignored; incoming UI guarded against race)
- Answering a new inbound call after warm transfer now transitions the live call page to connected state (stale BYE/hangup no longer clears the new session)

---

## 1.3.6

### Restored
- **Dark mode** and **Follow Windows theme** toggles in Settings (Account section), matching v1.3.0–v1.3.2 behavior
- App startup and live toggle changes apply theme via `ThemeManager` (Windows `AppsUseLightTheme` registry when following system)

---

## 1.3.5

### Fix
- Outbound calls with an explicit **+1** country prefix no longer fail: E.164 `+` is stripped before building the SIP Request-URI (matches dial-without-prefix behavior)

---

## 1.3.3

### Fix
- Settings nav icon visible again (PackIcon now scales legacy 24px paths correctly)
- Sign-out icon renders properly (Material logout path + button foreground binding)
- Auto-login runs stale-registration API cleanup before SIP REGISTER (fixes first-attempt failure on app restart)
- DND full-screen overlay includes **Turn Off DND** button

### Changed
- Removed Dark mode and Follow Windows theme toggles from Settings (app always uses light theme)
- Speaker test tone: warm C5+E5 major third instead of harsh 440 Hz sine
- Default ringtone: gentle two-note marimba-style pattern instead of flat 440 Hz beep

---

## 1.3.2

### Fix (v1.3.0 regressions from v1.3.1)
- Launch splash no longer covers the manual sign-in screen (blocks Cancel / login controls); splash remains for auto-login only
- History sticky date header no longer intercepts taps/clicks on the first call rows underneath
- Touch tab swipe: enabled manipulation on nav host, ignores vertical scroll gestures, disabled during active/ringing calls
- Global hotkeys log a warning instead of failing silently when Ctrl+Shift+A/H/M cannot register

---

## 1.3.1

### UI/UX (deferred from v1.3.0)
- Touch swipe between Dashboard, History, and Contacts bottom-nav pages
- Bold search highlight for matched text in History and Contacts results
- Branded launch splash with logo and connection steps during sign-in / auto-login
- Audio-reactive incoming-call ringtone visualizer tied to live playback level
- Settings audio device preview ("Using: …") under mic, speaker, and ringtone selectors
- Sticky Today/Yesterday date headers while scrolling History
- Settings section icons grouped as Audio, Account, Calls, and Support

### New features
- Global hotkeys (Ctrl+Shift+A/H/M): Answer, Hangup, Mute when a call is active or ringing
- Missed-call badge on History bottom-nav icon (clears when History is opened)
- Inbound caller ID resolves contact names from local Contacts cache

### Fix
- Settings recording copy now describes both-leg mixed local recording (not mic-only)

### Removed
- Block list fully removed (BlockedNumbers, inbound reject, settings persistence, diagnostics field)

---

## 1.3.0

### Visual polish (v1.3.0 look & feel release)
- Semantic connection status pill and tray icon overlay colors (green online, amber registering/reconnecting, red offline/disconnected)
- Rich tray menu: live status, DND toggle, Open Dialpad, Exit with confirmation
- Incoming call UI: larger caller ID, initials avatar, green pulse ring, ringing waveform, full-width Answer/Decline buttons
- On-hold amber header bar; DND muted overlay on dashboard
- Dashboard: time-based greeting, glass-style hero cards, today Made/Received/Missed stat chips, recent call cards with avatars and disposition color strips
- Dark mode palette (#1a1a2e / #16213e) with Settings toggle and optional follow Windows theme
- Dialpad: iPhone letter labels, monospace formatted display `(866) 555-1234`, long-press 0 → +, backspace hold to clear, full-width Call/End button
- History & Contacts: Today/Yesterday date headers, initials avatars, Copy number action, shimmer skeleton loading, inline Retry on API errors
- Settings: iOS-style toggles, larger live mic meter, appearance section
- Bottom nav active tab glow/underline; 150ms page transitions; subtle button press scale on nav and call actions
- PackIcons for Settings nav; legacy Path message icons replaced where updated

---

## 1.2.3

### Support / diagnostics
- Structured SIP log with section headers, context tags (`[LOGIN]`, `[REGISTER]`, `[INBOUND]`, etc.), and customer-friendly "What to try" lines on errors
- Startup banner records version, OS, and remembered extension at launch
- Login, registration, calls, network, and toast actions instrumented with plain-English summaries alongside wire traces
- Settings → **Open SIP Log** opens `sip.log` in the default editor
- Export Diagnostics includes the last 1000 lines of structured `sip.log`

---

## 1.2.2

### Packaging
- Application icon embedded in `CallAnalog.Softphone.exe` (taskbar, Explorer, Alt+Tab) via `CallAnalog.Softphone.ico`
- Window and tray icons unchanged (`Assets/favicon.png`)
- Inno Setup installer script and code-signing / installer documentation added under `installer/` and `docs/`

---

## 1.2.1

### Notifications
- Incoming-call Windows toast with **Accept** and **Decline** actions when the app is minimized to the taskbar or hidden to the tray
- Clicking the toast body restores the main window and shows the in-app incoming-call UI
- Call-waiting toasts use the same action pattern; toasts auto-dismiss when the call is answered, declined, or ends
- Missed-call tray balloon unchanged for busy-line rejections

---

## 1.2.0

### Auth / login
- Preemptive REGISTER digest auth (cache credentials, Authorization on first REGISTER, clear on 403)
- Login step progress: Signing in → Carrier → Registering, with Cancel during login
- Integration tests for auth cache, preemptive header, and registration challenge detection

### Telephony
- Call waiting: second inbound while on call shows Answer, Decline, Hold+Answer, Switch (no Busy reject)
- Both-leg local recording: mixed microphone + remote playback WAV
- Faster TCP reconnect: immediate retry on ConnectionAborted (no 5s backoff first)
- Configurable TURN fallback in appsettings when STUN fails (best effort)

### Settings / shell
- BlockedNumbers enforced on inbound calls
- CallForwardNumber wired: forwards idle inbound via SIP redirect
- AgentQueueModeEnabled wired: queue-call logging and UI labeling
- Conference button removed from in-call UI (backend retained)
- Start with Windows registry applied on app startup from saved settings
- Single-instance Mutex: second launch focuses existing window

### Media / UX
- In-call audio hot-swap: mic/speaker changes in Settings apply during active call
- Server reachability indicator uses SIP OPTIONS RTT and registration state

### Data
- Offline contacts/history cache per extension with banner when API is unreachable

### Docs
- Manual testing SOP updated for v1.2.0 scenarios

---

## 1.1.4

### Login speed (preemptive REGISTER auth)
- Persist per-extension SIP digest params (realm, nonce, qop, algorithm, opaque) after successful registration
- Send Authorization on the first REGISTER when cached digest params are available (skips initial 401 round-trip on relogin)
- Refresh cache from WWW-Authenticate on 401/407; clear cache on 403 Forbidden
- Unit tests for auth cache serialize/restore and preemptive header generation

---

## 1.1.3

### Login speed (401 REGISTER fix)
- Ignore 401/407 on initial REGISTER and re-REGISTER so SIPSorcery completes auth retry without abort
- TCP pre-connect via OPTIONS before REGISTER (3s cap)
- Registration wait reduced to 15 seconds; retry only on timeout (not 401)
- Dashboard shown immediately after SIP register succeeds (credential save follows)

---

## 1.1.2

### Login speed and first-attempt reliability
- Auto-login fast path: skips HTTP login, carrier APIs, and unregister when Remember Me + saved carrier exist
- Registration retry repeats SIP register only (not the full login pipeline)
- 25 second registration attempt window (was up to 90s before retry)
- STUN runs in background; REGISTER starts immediately
- Public IP cached to disk for 24 hours so restarts skip STUN wait
- Pre-register unregister capped at 3 seconds on manual login only

---

## 1.1.1

### Login reliability
- Auto sign-in on startup when Remember Me is enabled and credentials are saved
- STUN public-IP lookup capped at 4 seconds per server (5 seconds total warmup budget)
- Saved SIP carrier used before proxy-domain API waterfall
- Proxy-domain API lookups run in parallel (first success wins)
- SIP registration wait extended to 90 seconds to match retry window
- Automatic single retry when first sign-in times out during SIP registration
- Login errors show user-friendly explanation from `LoginErrorCatalog`

### Includes v1.1.0
- Outbound 407 auth challenge handling and call UI during ringing
- Pre-register unregister capped at 5 seconds

---

## 1.0.2

### Fixes
- History and Contacts: show "No results found" when search returns nothing
- Call history colors by disposition: ANSWERED green, NO ANSWER/BUSY red, CANCEL blue
- Call notes use SIP Call-ID only (part before @), not CDR numeric id
- Outbound calls show the number you dialed (fixed race where UI updated before dialed number was set)
- Removed block list from Settings (feature removed)
- Transfer is blind-only; attended/warm transfer is not available
- Call wrap-up no longer reappears after Skip/Save when a new call arrives (fixed duplicate hangup event + dismiss overlay on incoming)
- Call wrap-up auto-closes after 30 seconds of inactivity (same as Skip)
- One-way audio: resume speaker playback on connect and when speaker was muted at hangup; route RTP through mute wrapper sink
- Settings **Save All** returns to the dashboard after a successful save
- Contacts and call history load when API returns null phone number fields (null-safe JSON parsing)
- Cancel outbound call no longer freezes the app (UI deadlock fix)
- History/contacts search with no matches shows empty results instead of API parse errors
- App window enlarged ~30% width and ~40% height
- Contacts empty search state centered like History; dialpad keys and list fonts scaled up for larger window
- Incoming audio: route playback directly to Windows audio endpoint (regression fix for one-way audio after wrapper change)

---

## 1.0.1

### Telephony & call reliability
- Blind transfer from in-call UI
- In-call DTMF keypad (full 0-9, *, #)
- Voicemail shortcut pre-fills dialpad with *97
- RTP-safe mute (mic and G.722) - prevents ~60s carrier drops when muted
- Speaker mute button on live call screen
- Registration reconnect with "Reconnecting..." status and backoff
- End Call works on repeat calls; call controls reset between calls
- Ringtone stops cleanly on answer (reduced bleed after pickup)
- Block list in Settings (reject blocked incoming numbers)
- Missed-call tray notification when a second call arrives while busy

### Audio & devices
- WASAPI device enumeration with persisted device IDs
- Audio device hot-plug refresh in Settings
- WinMM fallback logging when a selected device cannot be mapped
- Input volume applied at call start; system speaker volume labels (honest UX)
- Codec UI limited to G.711 (PCMU/PCMA) and G.722 with SDP restriction

### UI & workflow
- Dialpad and in-call keypad layout fixes (no clipping)
- Call wrap-up / notes panel after hangup (does not drop active calls)
- Conference button visible but disabled ("Coming soon")
- Server reachability indicator (renamed from misleading "call quality")
- DND / Auto Answer noted as local-only in Settings
- Contacts and History show API errors when load fails
- Hold/resume duration timer excludes time on hold

### Settings, security & support
- Remember Me fix — password not saved when unchecked
- Extension stored with DPAPI (not plaintext in settings JSON)
- SIP log rotation (5 MB × 3) and Authorization redaction in traces
- Diagnostics export zip and Open Logs Folder in Settings
- Crash reports saved locally (email when SMTP configured)
- Tray tooltip reflects Online / Reconnecting / Ringing / On call
- SIP register/keep-alive timing applies via re-REGISTER without full re-login
- TCP connect-host support when provisioned by carrier

### Build & packaging
- Versioned publish folders: `dist\callanalog v1.x.y`
- `build.ps1` with `-BumpMinor` / `-BumpMajor` versioning
- Operator guide: `docs/OPERATOR.md`
- Automated tests (7) for codec, mute silence, dial validation, settings

---

## 1.0.0

- Initial CallAnalog Softphone WPF build

# CallAnalog Softphone — Manual Testing Checklist

| Field | Value |
|-------|-------|
| **Product** | CallAnalog Softphone |
| **Document version** | 2.2.3 |
| **Target build** | `dist\callanalog v2.2.3\` or `installer\output\CallAnalogSoftphone-Setup-2.2.3.exe` |
| **Purpose** | Exhaustive manual verification of every user-facing function, edge case, and integration point before release |
| **Last updated** | 2026-08-12 |
| **Companion** | `docs/GOLDEN_BASELINE.md` (G1–G15 must pass every build) |

---

## How to use this document

1. Install or run build **2.2.3** (installer or publish folder). Read `CHANGES.txt` in the publish folder.
2. Use a **primary test extension**, a **second extension** (transfer / call waiting), and an **external PSTN/mobile** number.
3. Use a **USB headset** as the primary audio path. Optionally retest with built-in mic/speakers. Avoid SoundWire / virtual audio cables for release sign-off.
4. Mark each row: **P** = Pass · **F** = Fail · **B** = Blocked · **N/A** = Not applicable.
5. On failure: Settings → Export Diagnostics + attach `%LOCALAPPDATA%\CallAnalog\logs\sip.log` and any `%LOCALAPPDATA%\CallAnalog\crashes\crash_*.txt`.
6. **Every build:** run **Section 25 — Golden Smoke (G1–G15)** first (~25 min).
7. **Release candidate:** run all sections.
8. **Audio / SIP media change:** run Golden Smoke + **Section 19 — Audio regression** + fragile sequences in Section 22.
9. **Excel / CSV checklist:** regenerate with `scripts\Export-TestingChecklist.ps1` → `docs\CallAnalog-Softphone-v2.2.3-Testing-Checklist.xlsx`.

**Logs:** `%LOCALAPPDATA%\CallAnalog\logs\sip.log`  
**Crashes:** `%LOCALAPPDATA%\CallAnalog\crashes\`  
**Recordings:** `%LOCALAPPDATA%\CallAnalog\recordings\` (default)  
**Diagnostics export:** `%LOCALAPPDATA%\CallAnalog\exports\`  
**Settings:** `%LOCALAPPDATA%\CallAnalog\`

---

## Prerequisites

| Requirement | Details |
|-------------|---------|
| OS | Windows 10 build 17763+ or Windows 11 (64-bit) |
| Network | Outbound TCP/UDP 5065 (SIP), UDP RTP, HTTPS 443 to `*.callanalog.com` |
| Audio | USB headset recommended; test hot-plug if possible |
| Accounts | Primary extension + password; second extension; external mobile/PSTN |
| Installer test | Run `CallAnalogSoftphone-Setup-2.2.3.exe`; verify Start menu shortcut + uninstall |

**Test matrix (recommended)**

| Role | Audio | Network scenarios |
|------|-------|-------------------|
| Primary | USB headset | Stable Wi‑Fi |
| Secondary | Built-in mic/speaker | Wi‑Fi off/on, sleep/wake |
| Optional | Bluetooth headset | Strict firewall (if IT can test) |

**Fill in before testing:**

| Item | Value |
|------|-------|
| Primary extension | _________________ |
| Second extension (transfer/waiting) | _________________ |
| External test number | _________________ |
| Tester name | _________________ |
| Build tested | 2.2.3 |
| Date | _________________ |
| Machine / OS | _________________ |
| Headset model | _________________ |

---

## 0. Install & environment setup

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| E-01 | Run `CallAnalogSoftphone-Setup-2.2.3.exe`; complete wizard | Installs to `%LOCALAPPDATA%\Programs\CallAnalog Softphone\`; Start menu entry created | | | |
| E-02 | Optional: check "Create desktop icon" | Desktop shortcut launches app | | | |
| E-03 | Launch from Start menu | App opens; phone-frame UI; version **2.2.3** in login footer / Settings → Check for Updates | | | |
| E-04 | Verify `build-info.txt` / `CHANGES.txt` in install or publish folder | `Version=2.2.3`; changelog includes audio WinMM default | | | |
| E-05 | First launch before login | `sip.log` created under `%LOCALAPPDATA%\CallAnalog\logs\` | | | |
| E-06 | Windows Firewall — allow on private network | SIP REGISTER succeeds | | | |
| E-07 | Launch exe **twice** quickly | Second instance exits; first window focused; log: `Second app instance detected` | | | |
| E-08 | Resize / multi-monitor check | Phone frame scales inside Viewbox; not clipped on 1080p/1440p | | | |
| E-09 | Confirm `appsettings.json` → `Audio:PreferWasapi` | Default is **`false`** (WinMM call audio) | | | Critical 2.2.3 |
| E-10 | Uninstall via Settings → Apps | App removed; Start menu entry gone | | | |

---

## 1. Application shell & window controls

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| S-01 | Drag title bar | Window moves | | | |
| S-02 | Minimize (−) button | Minimizes to taskbar; tray icon remains | | | |
| S-03 | Minimize to tray (▼) | Window hidden; not in taskbar; tray icon active | | | |
| S-04 | Close (X) while **logged out** | App exits immediately (no confirm) | | | |
| S-05 | Close (X) while **logged in** idle | Confirm overlay "Exit CallAnalog?"; Cancel stays; Exit signs out + closes | | | |
| S-06 | Title bar logo | Hidden on login; visible after login | | | |
| S-07 | Sign-out icon in header | Material logout icon visible (not a dot); click signs out | | | |
| S-08 | Home indicator bar | Visible at bottom of phone frame | | | |
| S-09 | Logged-in header extension label | Shows "Extension {number}" | | | |
| S-10 | Connection status chip | Green=Online, Amber=Registering/Reconnecting, Red=Disconnected, Gray=Offline | | | |
| S-11 | During **on-hold** call | Header bar amber tint | | | |
| S-12 | During connected call | Header shows `{remote} · MM:SS` timer | | | |

---

## 2. Launch splash (auto-login vs manual)

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| SP-01 | Remember Me + saved creds → **restart app** | Branded splash: logo, spinner, step text ("Signing in automatically…" → "Registering on PBX…") | | | |
| SP-02 | Splash during auto-login | Hides when dashboard appears | | | |
| SP-03 | **Manual login** (no auto-login) | Splash does **NOT** cover login form | | | |
| SP-04 | Manual login — click **Cancel** during sign-in | Cancel button clickable; sign-in stops; splash hidden | | | |
| SP-05 | Manual login step text | Shows Signing in → Resolving carrier → Registering on PBX | | | |

---

## 3. Login, credentials & sign-out

### 3.1 Manual login — happy path

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| L-01 | Enter valid extension + password → Login | Spinner; dashboard; chip **Online** | | | |
| L-02 | Press **Enter** in password field | Same as Login click | | | |
| L-03 | Check `sip.log` | `[LOGIN]` section; `[REGISTER]` success; `Registered extension …` | | | |

### 3.2 Login — validation & errors

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| L-04 | Empty extension → Login | "Extension number is required" | | | |
| L-05 | Empty password → Login | "Password is required" | | | |
| L-06 | Type letters in extension field | Blocked (digits only) | | | |
| L-07 | Wrong password | Clear error from API; stays on login | | | |
| L-08 | Invalid extension | API failure message with code | | | |
| L-09 | Disconnect network → Login | Network/timeout error; recovery hint in log | | | |
| L-10 | Registration timeout | One retry; firewall hint in log; password cleared if Remember Me off | | | |

### 3.3 Cancel login

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| L-11 | Start login → **Cancel** | Spinner stops; "Sign-in cancelled."; Login re-enabled | | | |
| L-12 | Cancel during auto-login | Same; splash hidden | | | |

### 3.4 Remember Me

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| L-13 | Remember Me **ON** → close app → reopen | Extension + password restored; auto-login starts | | | |
| L-14 | Remember Me **OFF** → close → reopen | Password **not** saved | | | |
| L-15 | Inspect settings storage | Extension DPAPI-protected; no plaintext password in JSON | | | |
| L-16 | Sign out with Remember Me ON | Login screen with extension pre-filled | | | |

### 3.5 Auto-login & fast path

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| L-17 | Restart with Remember Me + saved carrier | Auto-login with splash | | | |
| L-18 | Fast path (saved carrier host) | Log: `Using saved carrier credentials — skipping API login` | | | |
| L-19 | Restart app — first REGISTER | Log: `Requested stale registration cleanup via API`; **first attempt succeeds** | | | Critical |
| L-20 | Manual login after sign-out | Full pipeline: API login → carrier → REGISTER | | | |
| L-21 | Preemptive digest on relogin | Authorization on first REGISTER when challenge cached | | | |

### 3.6 Sign out

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| L-22 | Sign out while idle | SIP unregister; login screen; Offline | | | |
| L-23 | Sign out during **active call** | Confirm "End call and sign out?"; on confirm: hangup + unregister | | | |
| L-24 | Sign out → sign in same extension | Clean re-register; no duplicate-registration errors | | | |
| L-25 | Tray → Exit while logged in | Same confirm + sign-out as Close (X) | | | |

---

## 4. SIP registration, reconnect & network

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| R-01 | After login | Chip **Online**; tray "Online"; green dot on tray icon | | | |
| R-02 | During login/re-register | Chip **Registering** (amber) | | | |
| R-03 | Wi‑Fi off 30s → on while logged in | Reconnecting → Online; log shows re-register | | | |
| R-04 | Sleep laptop 2 min → wake | Registration recovers within ~1–2 min | | | |
| R-05 | Kill network during idle | Disconnected/Reconnecting; no crash | | | |
| R-06 | Kill network during active call | Call may drop; app recovers when network returns | | | |
| R-07 | Dial while Offline/Reconnecting | Clear error; call blocked | | | |
| R-08 | Inbound while not registered | No ring UI | | | |
| R-09 | Inbound out-of-dialog SIP OPTIONS from PBX | Softphone replies **200 OK** with Allow; extension stays reachable | | | Critical 2.2.1 |
| R-10 | Log shows outbound OPTIONS keepalive | OPTIONS → 200 OK from OpenSIPS; no flood of failures | | | |
| R-11 | STUN public IP | Log: `Using public media IP … (from STUN …)` | | | |

---

## 5. Bottom navigation, swipe & transitions

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| N-01 | Tap Dashboard / History / Contacts / Settings | Correct page; active tab highlight | | | |
| N-02 | **Center keypad button** (circular) | Opens dialpad overlay; active style when dialpad open | | | Critical 2.2.x |
| N-03 | Settings nav icon | Gear icon visible in bottom nav | | | |
| N-04 | Rapid tab switch 10× | No crash; correct page each time | | | |
| N-05 | Page transitions | ~150ms slide animation between tabs | | | |
| N-06 | Open History with missed badge | Badge **clears** when History opens | | | |
| N-07 | Swipe left: Dashboard → History → Contacts | Horizontal swipe changes tab | | | Touch |
| N-08 | Swipe right: reverse direction | Contacts → History → Dashboard | | | |
| N-09 | Swipe on **Settings** page | No tab change | | | |
| N-10 | Swipe on **Dialpad** overlay | No tab change | | | |
| N-11 | Swipe during **active/ringing call** | Swipe blocked; call unaffected | | | |
| N-12 | Vertical scroll in History while swiping | Scroll works; does not change tabs | | | |
| N-13 | Change tab during active call | Call session overlay stays on top; call not dropped | | | |

---

## 6. Dashboard

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| D-01 | Open dashboard | Time greeting: "Good morning/afternoon/evening, Extension {ext}" | | | |
| D-02 | Today **Made** stat | Count of outbound today; tap → History All filter | | | |
| D-03 | Today **Received** stat | Count inbound answered; tap → History Answered filter | | | |
| D-04 | Today **Missed** stat | Count missed today; tap → History Missed filter | | | |
| D-05 | After test calls | Stats increment correctly | | | |
| D-06 | API failure / offline | Stats show "—"; no crash | | | |
| D-07 | Tap **Open Dialpad** hero | Dialpad overlay opens | | | |
| D-08 | Tap **Voicemail** | Dialpad opens with `*97` (or configured code) | | | |
| D-09 | Tap **SMS** | Coming Soon overlay | | | |
| D-10 | Recent calls list | Shimmer → cards with avatar, disposition color strip | | | |
| D-11 | Empty recent calls | "No recent calls" empty state | | | |
| D-12 | Tap recent call row | Dialpad with number pre-filled | | | |
| D-13 | Resize window taller | More recent call rows visible | | | |

### 6.1 DND toggle

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| D-14 | DND OFF → ON | Pill ON (red); **full-screen DND overlay** on dashboard | | | |
| D-15 | **Turn Off DND** button on overlay | DND disabled; overlay hides; pill OFF | | | Critical |
| D-16 | DND ON + inbound call | No ring UI; log: `Rejecting call … — DND enabled` | | | |
| D-17 | DND OFF + inbound | Normal ring | | | |
| D-18 | Tray menu → Turn DND On/Off | Syncs with dashboard pill + overlay | | | |
| D-19 | Restart app | DND state persists | | | |
| D-20 | Settings CALL HANDLING note | States DND is **local only** (not PBX sync) | | | |

### 6.2 Auto Answer

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| D-21 | Auto Answer ON + inbound | Answers immediately; no manual Answer | | | |
| D-22 | Auto Answer OFF + inbound | Normal Answer/Decline UI + ringtone | | | |
| D-23 | Restart app | Auto Answer state persists | | | |

---

## 7. Dialpad & outbound calling

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| DP-01 | Tap digits 0–9, *, # | Appear in field with letter labels under keys | | | |
| DP-02 | 10-digit US number | Monospace format e.g. `(866) 555-1234` | | | |
| DP-03 | Physical keyboard digits + Enter | Dials on Enter | | | |
| DP-04 | Shift+8, Shift+3 | Inserts * and # | | | |
| DP-05 | **Long-press 0** (~450ms) | Inserts `+` | | | |
| DP-06 | Backspace | Deletes one digit | | | |
| DP-07 | **Hold backspace** (~500ms) | Clears entire field | | | |
| DP-08 | Clear button | Empties field | | | |
| DP-09 | Paste formatted number | Re-formats correctly | | | |
| DP-10 | Valid number → Call | Outgoing UI; ringback; correct dialed number shown | | | |
| DP-11 | Remote answers | Connected; timer; **two-way audio clear** | | | Critical |
| DP-12 | Unanswered until timeout (~90s) | Graceful end; idle | | | |
| DP-13 | Invalid/unreachable number | Error in status area | | | |
| DP-14 | Empty field → Call | Validation error; no call placed | | | |
| DP-15 | Cancel while ringing | Call cancelled; UI responsive (no freeze) | | | |
| DP-16 | End Call on dialpad during call | Hangup works | | | |
| DP-17 | End call → place **second call immediately** | Works normally; audio OK | | | |
| DP-18 | Redial button | Pre-fills last number and dials | | | |
| DP-19 | Back to Dashboard while idle | Returns; number may persist | | | |
| DP-20 | Back during active call | Call continues; session overlay visible | | | |
| DP-21 | Dial while not registered | Clear error | | | |

---

## 8. Incoming calls

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| I-01 | Inbound from external | Ringtone on ringtone device; caller ID; green pulse ring | | | |
| I-02 | Default ringtone (no custom file) | Gentle two-note marimba pattern, not flat beep | | | |
| I-03 | Ringtone visualizer | Waveform bars react to audio level | | | |
| I-04 | Contact in cache matches number | **Contact name** shown | | | |
| I-05 | Answer | Ring stops immediately; connected; **two-way audio clear** | | | Critical |
| I-06 | Decline | Rejected; idle | | | |
| I-07 | Let ring timeout (no action) | Idle; **missed-call badge** increments on History nav | | | |
| I-08 | Decline explicitly | Badge does **NOT** increment | | | |
| I-09 | Queue/ACD call | Header "Queue Call" when applicable | | | |
| I-10 | Incoming while app backgrounded | Window flashes; toast with Accept/Decline | | | |
| I-11 | Incoming while app foregrounded | Window restored; in-app incoming UI | | | |
| I-12 | Custom ringtone uploaded in Settings | Custom file plays instead of default | | | |

---

## 9. Call waiting (requires second inbound while on call)

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| CW-01 | On active call, receive second inbound | Call waiting panel + dual-call banner; ringtone; primary stays connected | | | |
| CW-02 | Buttons visible | **End first call then answer** \| Decline \| **Hold + Answer** | | | |
| CW-03 | **End first call then answer** | First call ends (BYE); connected to waiting party only; **no** held-call strip | | | |
| CW-04 | **Decline** waiting | Waiting rejected; back to primary only; **primary audio still works** | | | Critical |
| CW-05 | **Hold + Answer** | Primary on hold; connected to waiting party; held-call strip + Switch visible | | | |
| CW-06 | **Switch** (after hold+answer) | Swaps active/held; audio follows active leg | | | |
| CW-07 | Switch back → Hold → Resume | Audio + UI remote party correct each step | | | Fragile |
| CW-08 | Waiting party hangs up first | Waiting UI clears; primary continues | | | |

---

## 10. In-call session (live call page)

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| C-01 | Connected UI | Avatar initials; name + number; duration timer | | | |
| C-02 | Duration timer | Counts active time; excludes hold time | | | |
| C-03 | On hold | Header "On Hold"; status "On hold · MM:SS" | | | |
| C-04 | Network/reachability indicator | 4-bar indicator; OPTIONS RTT based | | | |
| C-05 | Voice meters | Incoming/outgoing bars move with audio | | | |
| C-06 | Media quality line (if shown) | Bars / loss / jitter update without crash | | | 2.2.0 |
| C-07 | **Mute** mic | Remote hears silence | | | |
| C-08 | Mute **5+ minutes** | Call stays up | | | |
| C-09 | Unmute | Remote hears again clearly | | | |
| C-10 | **Speaker mute** (local) | You hear silence; mic still works | | | |
| C-11 | Unmute speaker | Remote audio returns | | | |
| C-12 | Speaker mute → end call → **new call** | Hear remote on new call (no stuck mute) | | | Regression |
| C-13 | **Hold** | Hold state; remote hears hold/MOH per PBX | | | |
| C-14 | **Resume** from hold | Two-way audio restored | | | |
| C-15 | Hold disabled during warm transfer | Hold button disabled | | | |
| C-16 | Volume slider | Changes Windows system speaker volume | | | |
| C-17 | In-call **keypad** toggle | DTMF pad expands/collapses | | | |
| C-18 | Send DTMF during IVR | IVR reacts; log: `Sent DTMF 'X'` | | | |
| C-19 | **End Call** | Call ends; wrap-up if was connected | | | |
| C-20 | Remote hangs up first | Local UI ends; wrap-up if connected | | | |
| C-21 | End call → second call → End again | Controls reset; works | | | |
| C-22 | Unmuted call **7+ min** | Stays connected; audio remains clear | | | |
| C-23 | Conference button | **Not present** in UI | | | |

### 10.1 Blind transfer

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| T-01 | Transfer → empty target → Transfer | "Enter an extension or number." | | | |
| T-02 | Invalid chars (letters) | "Only digits, * and # are allowed." | | | |
| T-03 | Blind transfer to valid extension | Transferred; local ends | | | |
| T-04 | Cancel transfer panel | Panel closes; call continues | | | |
| T-05 | Blind during warm transfer | Blocked: finish warm transfer first | | | |

### 10.2 Warm (attended) transfer

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| T-06 | Start warm transfer | Primary on hold; consult rings | | | |
| T-07 | Consult answers | "Warm Transfer" UI; Complete + Cancel | | | |
| T-08 | **Complete** warm transfer | Agent drops; parties connected | | | |
| T-09 | **Cancel** warm transfer | Returns to primary call with audio | | | |
| T-10 | Consult no answer / busy | Error; primary recoverable | | | |
| T-11 | Remote hangup during consult | Sensible UI state | | | |
| T-12 | Start warm from hold | Works | | | |

### 10.3 Local recording

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| REC-01 | Recording **disabled** in Settings | Record button disabled/dimmed | | | |
| REC-02 | Recording **enabled** + in call | Record button active | | | |
| REC-03 | Start/stop recording | Log: `Recording both call legs (mic + remote)` | | | |
| REC-04 | Play back WAV file | **Both** mic and remote audible | | | |
| REC-05 | MP3 format in Settings | Valid MP3 if selected | | | |
| REC-06 | Settings copy text | Describes **both-leg mixed** recording | | | |

---

## 11. Global hotkeys

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| HK-01 | **Ctrl+Shift+A** during incoming ring | Answers call | | | |
| HK-02 | **Ctrl+Shift+A** during call waiting ring | **Hold + Answer** waiting call | | | |
| HK-03 | **Ctrl+Shift+H** during incoming | Declines | | | |
| HK-04 | **Ctrl+Shift+H** during in-call/outgoing | Hangup | | | |
| HK-05 | **Ctrl+Shift+M** during in-call/on-hold | Toggles mic mute | | | |
| HK-06 | Hotkeys when logged out | No action | | | |
| HK-07 | Hotkeys when idle (no call) | Mute/Answer no-op | | | |
| HK-08 | Conflict with another app using same hotkeys | Log warning: hotkeys could not register | | | |

---

## 12. Tray icon, toasts & badges

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| TR-01 | Tray tooltip when Online | "CallAnalog Softphone — Online" | | | |
| TR-02 | Tray tooltip when Ringing / On call / etc. | Matches call state | | | |
| TR-03 | Tray icon color dot | Green online; amber reconnecting; red offline | | | |
| TR-04 | Double-click tray | Restores window | | | |
| TR-05 | Tray menu: Show CallAnalog | Restores window | | | |
| TR-06 | Tray menu: Status line | Shows live status (disabled item) | | | |
| TR-07 | Tray menu: Turn DND On/Off | Toggles DND + overlay | | | |
| TR-08 | Tray menu: Open Dialpad | Window + dialpad (if logged in) | | | |
| TR-09 | Tray menu: Exit | Confirm + sign out + exit | | | |
| TR-10 | Minimized + incoming toast | Accept + Decline buttons | | | |
| TR-11 | Toast **Accept** | Answers call | | | |
| TR-12 | Toast **Decline** | Declines call | | | |
| TR-13 | Click toast body (not button) | Restores window + incoming UI | | | |
| TR-14 | Toast auto-dismiss | Gone when call answered/declined/ended | | | |
| TR-15 | Missed badge on History nav | Red count; caps at 99+ | | | |
| TR-16 | Open History tab | Badge clears | | | |
| TR-17 | Second call rejected while busy | Tray balloon "Missed call … while on another call" | | | |

---

## 13. Call wrap-up (post-call notes)

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| W-01 | End **connected** call | Wrap-up overlay: "Call ended" + summary | | | |
| W-02 | Star rating 1–5 | Selectable | | | |
| W-03 | Save with note + rating | POST `/public/api/callNote` with **`note` and `rating`** (1–5) on same payload; uses SIP Call-ID | | | Critical 2.2.3 |
| W-04 | Save with note only (no rating) | Note saved; rating omitted or null per API contract | | | |
| W-05 | Skip | Closes without save | | | |
| W-06 | Wait 30s on wrap-up | Auto-closes (same as Skip) | | | |
| W-07 | **New inbound during wrap-up** | Wrap-up cancelled; incoming UI immediate | | | |
| W-08 | Unanswered outbound / missed inbound | **No** wrap-up overlay | | | |
| W-09 | Confirm on server / admin tool | Rating column populated when stars selected | | | |

---

## 14. Call history

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| H-01 | Open History | Shimmer → list from API | | | |
| H-02 | Refresh button | Reloads | | | |
| H-03 | Row colors by disposition | ANSWERED green, NO ANSWER red, etc. | | | |
| H-04 | Today/Yesterday inline headers | Correct grouping | | | |
| H-05 | **Sticky date header** while scrolling | Header floats; **first rows still tappable** | | | |
| H-06 | Search partial number/name → Search | Results filtered; **bold highlight** on match | | | |
| H-07 | Search no matches | "No results found" | | | |
| H-08 | Dashboard Made/Received/Missed → History | Correct filter applied | | | |
| H-09 | Clear filter ("Show All") | Restores all calls | | | |
| H-10 | Row **Dial** icon | Opens dialpad with number | | | |
| H-11 | Row **Copy** icon | Number on clipboard; status "Copied …" | | | |
| H-12 | Row **Message** icon | Coming Soon SMS overlay | | | |
| H-13 | Load more pagination | Appends; button shows progress | | | |
| H-14 | Disconnect network → open History | Cached data + offline banner | | | |
| H-15 | API error | Error panel with Retry | | | |

---

## 15. Contacts

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| CT-01 | Open Contacts | List from PBX API | | | |
| CT-02 | Refresh | Reloads | | | |
| CT-03 | Search by name/number | Filtered; bold highlight on match | | | |
| CT-04 | Search no matches | Empty state | | | |
| CT-05 | **Add** contact | Form overlay; name + number required | | | |
| CT-06 | Empty name/number on save | Validation error | | | |
| CT-07 | **Edit** contact | Pre-filled; saves via API | | | |
| CT-08 | **Delete** contact | Confirm; removed from list | | | |
| CT-09 | Row Call | Dialpad with number | | | |
| CT-10 | Row Copy | Clipboard + status | | | |
| CT-11 | Row Message | Coming Soon SMS | | | |
| CT-12 | Disconnect → open Contacts | Cached list + offline banner | | | |
| CT-13 | Inbound from saved contact | Caller ID shows contact name | | | |
| CT-14 | Load more pagination | Works | | | |

---

## 16. Settings — every field

### 16.1 AUDIO

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| ST-01 | Microphone dropdown | "Using: {device}" preview below | | | |
| ST-02 | Speaker dropdown | Preview updates | | | |
| ST-03 | Ringtone device dropdown | Preview updates | | | |
| ST-04 | Input volume slider + Save All | Applied on **next call** | | | |
| ST-05 | System speaker volume + Save All | Windows volume changes | | | |
| ST-06 | **Test Mic** | Level bar moves; auto-stops ~5s | | | |
| ST-07 | **Test Speaker** | Warm C5+E5 tone ~5s, not harsh beep | | | Critical |
| ST-08 | **Stop** during audio test | Tests stop immediately | | | |
| ST-09 | Hot-plug USB headset | "Audio devices changed — lists refreshed." | | | |
| ST-10 | Saved device unplugged | Warning: device not found; system default | | | |
| ST-11 | Test Speaker → outbound call | Call audio OK on same device | | | |

### 16.2 VOICE QUALITY (2.2.0+)

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| ST-12 | Profile: Low latency / Balanced / Stable Wi‑Fi | Saves; next call uses profile buffers | | | |
| ST-13 | Echo control Off / On / Strong | Far-end echo behaviour changes appropriately | | | |
| ST-14 | Noise reduction Off / Low / High | Quiet room tone not boosted into hiss | | | |
| ST-15 | Auto Gain ON | Quiet speech audible; silence not roaring hiss | | | Critical 2.2.3 |
| ST-16 | Auto Gain OFF | Natural mic level; no AGC boost | | | |
| ST-17 | Prefer Opus ON + call to Opus-capable peer | Log: Opus negotiated when possible | | | |
| ST-18 | Prefer Opus OFF | PCMU/PCMA/G722 path; no Opus offer when disabled | | | |

### 16.3 ACCOUNT

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| ST-19 | Start with Windows ON + Save All | Registry Run key; reboot → app launches | | | |
| ST-20 | Start with Windows OFF | Startup entry removed | | | |
| ST-21 | Carrier host / SIP port | Read-only display | | | |
| ST-22 | Register request seconds | Valid ≥60; invalid → error | | | |
| ST-23 | Keep alive seconds | Valid ≥5; invalid → error | | | |
| ST-24 | Transport TCP/UDP + Save Transport | Warning: sign out/in required | | | |
| ST-25 | **Dark mode toggle** | Toggles dark palette immediately; persists after Save All | | | |
| ST-26 | **Follow Windows theme** | When on, app matches Windows light/dark; dark mode toggle ignored | | | |

### 16.4 CALLS

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| ST-27 | Upload hold music (MP3/WAV) | Path shown; plays on hold | | | |
| ST-28 | Remove hold music | Clears | | | |
| ST-29 | Upload custom ringtone | Custom ring on incoming | | | |
| ST-30 | Remove ringtone | Default marimba pattern | | | |
| ST-31 | CALL HANDLING note | DND/Auto Answer are local-only | | | |

### 16.5 CODEC

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| ST-32 | PCMU / PCMA / G722 checkboxes | Present; Opus offered only via Prefer Opus | | | |
| ST-33 | Uncheck all + Save | "Select at least one codec" | | | |
| ST-34 | G.711 only → test call | PCMU/PCMA in SDP; two-way audio | | | |
| ST-35 | G.722 only → test call | G.722 if peer supports; audio OK | | | |

### 16.6 RECORDING

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| ST-36 | Enable local recording toggle | In-call record enabled | | | |
| ST-37 | Recording format WAV/MP3 | Next recording uses format | | | |
| ST-38 | Choose folder | Files written to chosen path | | | |

### 16.7 SUPPORT

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| ST-39 | Save crash reports toggle | Persists | | | |
| ST-40 | **Check for Updates** | POST version-check API (`application_key: pbx_desktop_exe`); shows up-to-date or update-available vs server `current_version` | | | Critical 2.2.3 |
| ST-41 | Check for Updates offline | Clear error; no crash | | | |
| ST-42 | Export Diagnostics | Zip in exports folder; **no plaintext password** | | | |
| ST-43 | Open SIP Log | Opens `sip.log` in default editor | | | |
| ST-44 | Open Logs Folder | Explorer opens logs dir | | | |
| ST-45 | Help link | Opens callanalog.com | | | |
| ST-46 | **Save All** | Settings persist; navigates to Dashboard | | | |
| ST-47 | Save All with register/keep-alive change | Re-REGISTER message in status | | | |
| ST-48 | Save All **during active call** + change mic/speaker | Hot-swap applied; status confirms | | | Fragile |

### 16.8 Removed features (confirm absent)

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| ST-49 | Block list | **No UI or setting** anywhere | | | |
| ST-50 | Call forward UI | **Not in Settings** | | | |
| ST-51 | Conference settings/button | **Not in UI** | | | |

---

## 17. Shell overlays & modals

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| O-01 | Confirm dialog (sign out during call, delete contact) | Confirm/Cancel work | | | |
| O-02 | Contact form (add/edit) | Validates; saves/cancels | | | |
| O-03 | Transfer panel | Blind/Warm radio; target field | | | |
| O-04 | Coming Soon (SMS) | Dismissible overlay | | | |
| O-05 | Call session Z-order | Covers nav during ring/call | | | |
| O-06 | Shell modal Z-order | Above splash and call UI when shown | | | |

---

## 18. Crash reporting (SMTP)

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| CR-01 | Crash reports enabled in Settings | Toggle ON persists | | | |
| CR-02 | Force a handled crash path if available / inspect config | `appsettings.json` CrashReport has SmtpHost/Port/User/From; no UI password leak | | | 2.2.3 |
| CR-03 | After real crash (if reproducible) | `crash_*.txt` written; email attempt logged (success or SMTP error) | | | |
| CR-04 | Crash reports disabled | No SMTP send on crash | | | |

---

## 19. Audio regression suite (run after any media change)

Tail `sip.log` during every call. Default backend for 2.2.3 is **WinMM**.

### 19.1 Backend & log markers

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| A-01 | Answer or place a call; inspect log | `Call playback WinMM …` and `Call capture WinMM …` (not WASAPI) when PreferWasapi=false | | | Critical 2.2.3 |
| A-02 | First RTP / playback frames | `First RTP audio frame received` and `Call audio playback frame #1` within a few seconds of answer | | | |
| A-03 | Frame #100 / #200 buffer size | Buffer stays modest (hundreds–low thousands of bytes), not pinned at capacity (e.g. 32000/32000) | | | |
| A-04 | No playback stop exception | No `AUDCLNT_E_DEVICE_INVALIDATED` / `Call playback stopped` on normal headset | | | |

### 19.2 Two-way voice quality

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| A-05 | Outbound answered | Far end hears you within ~2s; **clear speech, no constant hiss/distortion** | | | Critical |
| A-06 | Inbound answered | You hear far end clearly; ringtone stops on answer | | | Critical |
| A-07 | Stay quiet 10s | Far end does **not** hear loud continuous noise / distortion | | | Critical 2.2.3 |
| A-08 | Speak normally after silence | Speech onset clear; no clip blast of first syllable | | | 2.2.3 AGC |
| A-09 | Whisper → normal → loud | Levels usable; no extreme pumping | | | |
| A-10 | Both parties talk over each other briefly | Echo control does not mute you permanently | | | |

### 19.3 Mute / hold / device sequences

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| A-11 | Mute mic 30s → unmute | Far end hears silence then voice again | | | |
| A-12 | Speaker mute 30s → unmute | Local audio returns | | | |
| A-13 | Hold 30s → resume | Two-way audio returns | | | |
| A-14 | Sequence: mute → hold → resume → unmute | Audio OK | | | |
| A-15 | Speaker mute → end call → new call | New call hears remote (no stuck mute) | | | |
| A-16 | Change mic mid-call + Save All | New mic used (**fragile**) | | | |
| A-17 | Change speaker mid-call + Save All | New speaker used (**fragile**) | | | |
| A-18 | Custom hold music | Plays on agent speaker once (no double-play) | | | G9 |
| A-19 | Ringtone device ≠ call speaker | Ring on ringtone device; call audio on speaker | | | |

### 19.4 Codec paths

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| A-20 | Default codecs → PSTN call | Typically PCMU; two-way audio | | | |
| A-21 | Prefer Opus ON → Opus peer (if available) | Opus negotiated; audio OK | | | |
| A-22 | G.722 only (if peer supports) | Wideband; audio OK | | | |
| A-23 | Long mute on G.711 | Call stays up 5+ min | | | |

### 19.5 Optional WASAPI opt-in (not required for release)

Only if validating `Audio:PreferWasapi=true`.

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| A-24 | Set PreferWasapi=true; restart; place call | Log shows WASAPI lines with encoding/bit-depth/channels | | | Optional |
| A-25 | WASAPI float capture path | Far end hears **clear speech**, not noise; quiet periods silent | | | Optional |
| A-26 | Set PreferWasapi=false; restart | Back to WinMM; audio still OK | | | Optional |

### 19.6 Multi-call audio continuity

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| A-27 | Call 1 → hangup → Call 2 within 5s | Audio both directions on Call 2 | | | |
| A-28 | Decline inbound while idle → place outbound | Outbound audio OK | | | |
| A-29 | Call waiting decline → continue primary | Primary audio uninterrupted | | | |
| A-30 | 3 consecutive inbound answers | Each call has clear two-way audio | | | |

---

## 20. Security & data

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| SEC-01 | Export Diagnostics zip | No plaintext password | | | |
| SEC-02 | `sip.log` wire traces | Authorization / WWW-Authenticate **[REDACTED]** | | | |
| SEC-03 | Sign out | Session cleared; REGISTER expires | | | |
| SEC-04 | Remember Me off | No readable password on disk | | | |
| SEC-05 | Uninstaller (Settings → Apps) | App removed | | | |
| SEC-06 | `appsettings.json` SMTP password | Present for crash mail; not shown in UI | | | |

---

## 21. Features explicitly out of scope

Verify these are **not** available (or Coming Soon only):

| Feature | Expected |
|---------|----------|
| Conference in-call button | Absent |
| SMS | Coming Soon only |
| Video | Not available |
| Block list | Fully removed |
| Server-side DND sync | Local app only |
| Call forward UI | Not in Settings |
| Park button | Dial `*70` manually if PBX supports |

---

## 22. Known fragile areas — extra test passes

Run these sequences when touching telephony or audio code:

1. **Call waiting Switch** — Answer waiting → Switch → Switch back → Hold → Resume (audio + UI remote party each step)
2. **Warm transfer** — Start → consult answer → Complete; repeat with Cancel; remote hangup on consult
3. **In-call audio hot-swap** — Change mic and speaker separately during active call
4. **Wrap-up vs incoming race** — End call → wrap-up appears → receive inbound before Skip
5. **Outbound cancel UI** — Cancel while ringing must not freeze UI
6. **Auto-login restart** — First REGISTER succeeds without false failure
7. **History sticky header** — Tap first row under floating header
8. **Global hotkey conflict** — If another app holds Ctrl+Shift+A, check log warning
9. **Missed badge** — Timeout miss increments; Decline does not
10. **Speaker mute → new call** — No one-way audio on second call
11. **Quiet mic periods** — Far end must not hear constant distortion (2.2.3 capture fix)
12. **WinMM default** — Confirm PreferWasapi=false and WinMM log lines on release builds

---

## 23. Log verification quick reference

| Scenario | Search in `sip.log` |
|----------|---------------------|
| Stale reg cleanup | `Requested stale registration cleanup via API` |
| Fast auto-login | `Using saved carrier credentials — skipping API login` |
| REGISTER success | `Registered extension … — line is online` |
| Inbound OPTIONS 200 | `Inbound OPTIONS … → 200 OK` |
| WinMM audio (default) | `Call playback WinMM` / `Call capture WinMM` |
| WASAPI (opt-in only) | `Call playback WASAPI` / `Call capture WASAPI` + encoding/bit-depth |
| First media | `First RTP audio frame received` / `First playback frame queued` |
| Playback stall recovery | `Call playback is not draining` / `restarted on WinMM` |
| Reconnect | `Scheduling re-register`, `Attempting SIP re-registration` |
| Call waiting | `Call waiting from`, `Connected to waiting caller` |
| DND reject | `Rejecting call from … — DND enabled` |
| Both-leg record | `Recording both call legs (mic + remote)` |
| In-call hot-swap | `Applying in-call audio device change` |
| Hotkey conflict | `One or more global hotkeys could not be registered` |
| Toast | `[TOAST]` |
| Version check | Settings Check for Updates success/fail messages |

**Fail if you see:** Plaintext digest credentials; endless registration failure loop; buffer pinned at full capacity for many frames; constant far-end noise while agent is quiet; WASAPI float capture treated as noise on release (PreferWasapi should be false).

---

## 24. Version / release delta — 2.2.3

Run these in addition to Golden Smoke on this build:

| ID | Steps | Expected | P | F | Notes |
|----|-------|----------|---|---|-------|
| V-01 | Confirm build version UI / build-info | **2.2.3** | | | |
| V-02 | Settings → Check for Updates | Hits `/public/api/application/version-check` with `application_key` + local version | | | |
| V-03 | Call note + star rating save | Same `callNote` API includes `rating` column | | | |
| V-04 | Default audio backend | WinMM capture + playback log lines | | | |
| V-05 | Quiet period on call | Far end hears silence / soft room tone — **not** loud distortion | | | |
| V-06 | Two-way speech | Agent ↔ PSTN both hear clear voice | | | |
| V-07 | Footer center keypad | Circular dialpad button opens dialpad | | | |
| V-08 | Layer A automated | `dotnet test` passes (404+ tests as of 2.2.3) | | | Dev |

---

## 25. Golden Smoke G1–G15 (~25 min — every build)

Run in order. **All items must pass** for a release candidate. See also `docs/GOLDEN_BASELINE.md`.

| # | Test | ID |
|---|------|----|
| 1 | Login → Online; version **2.2.3** visible | G1 |
| 2 | Outbound answered + **clear two-way audio** | G2 |
| 3 | Inbound answer + decline | G3 |
| 4 | Mute → unmute | G4 |
| 5 | Hold 30s → resume (no false crash dialog) | G5 |
| 6 | Blind transfer to test extension | G6 |
| 7 | Call waiting: decline waiting → active call still hears audio | G7 |
| 8 | Custom ringtone or default tone on incoming | G8 |
| 9 | Hold with custom music — no double-play on agent | G9 |
| 10 | End call → second call → End call (audio OK both) | G10 |
| 11 | **Restart app** — first REGISTER succeeds | G11 |
| 12 | Settings: Test Speaker + device pickers | G12 |
| 13 | Quiet 10s on connected call — far end has **no loud distortion** | G13 |
| 14 | Log shows **WinMM** capture/playback (PreferWasapi=false) | G14 |
| 15 | Wrap-up: save note + rating on same callNote API | G15 |

| Smoke result | Tester | Date |
|--------------|--------|------|
| Pass / Fail | | |
| Blocking issues | | |

**2.2.3 delta focus:** WinMM audio default, quiet-period capture quality, version-check API, combined note+rating payload.

---

## 26. Test sign-off

| Section | Total cases | Pass | Fail | Blocked | Tester | Date |
|---------|-------------|------|------|---------|--------|------|
| 0 Install | | | | | | |
| 1 Shell | | | | | | |
| 2 Splash | | | | | | |
| 3 Login | | | | | | |
| 4 Registration | | | | | | |
| 5 Navigation | | | | | | |
| 6 Dashboard | | | | | | |
| 7 Dialpad | | | | | | |
| 8 Incoming | | | | | | |
| 9 Call waiting | | | | | | |
| 10 In-call / transfer / record | | | | | | |
| 11 Hotkeys | | | | | | |
| 12 Tray / toasts | | | | | | |
| 13 Wrap-up | | | | | | |
| 14 History | | | | | | |
| 15 Contacts | | | | | | |
| 16 Settings | | | | | | |
| 17 Overlays | | | | | | |
| 18 Crash SMTP | | | | | | |
| 19 Audio regression | | | | | | |
| 20 Security | | | | | | |
| 24 2.2.3 delta | | | | | | |
| 25 Golden Smoke | | | | | | |

| Field | Value |
|-------|-------|
| Build | 2.2.3 |
| Overall result | Pass / Fail |
| Blockers | |
| Tester | |
| Date | |
| Sign-off | |

---

## Appendix A — Recommended run order

| When | What to run |
|------|-------------|
| Every CI / local build | `dotnet test` |
| Every published build | Golden Smoke G1–G15 |
| Audio or SIP media PR | Golden + Section 19 + fragile #1–#3, #11–#12 |
| API / Settings PR | Sections 13, 16.7, 24 |
| Full release candidate | All sections + installer E-01…E-10 |

## Appendix B — Known open issues (do not fail smoke for these unless listed critical)

| Item | Notes |
|------|-------|
| Hangup duration log | `BYE received after Ns` may be followed by `Call hung up after 0s` — history duration may be wrong; track separately |
| SoundWire / virtual devices | Not a supported agent configuration; may still stress WASAPI if PreferWasapi enabled |

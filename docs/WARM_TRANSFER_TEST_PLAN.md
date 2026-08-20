# Warm Transfer Test Plan — CallAnalog Softphone v1.3.9

Exhaustive manual test plan for attended (warm) transfer. Complements MANUAL_TESTING_SOP.md sections **T-06 through T-12** and regression smoke **SMK-12 / SMK-13**.

**Prerequisites**

| Item | Value |
|------|-------|
| Build | v1.3.9 (or branch under test) |
| Extensions | Agent **A** (test phone), **B** (consult target), **C** (external caller) |
| PBX | CallAnalog / OpenSIPS with attended transfer (REFER + re-INVITE) |
| Logs | `%LocalAppData%\CallAnalog\logs\sip.log` (or Settings → Logs folder) |
| Network | Stable; note if testing over VPN |

**UI reference during consult**

- Header: **Warm Transfer**
- Status: `On hold: {primary} · Talking to: {consult}`
- Buttons: **Complete**, **Cancel** (Transfer hidden; Hold disabled)
- Tray: **Warm transfer**

---

## 1. Happy path — start → consult answers → complete

**ID:** WT-HP-01 (maps to T-06, T-07, T-08, SMK-12)

### Steps

1. Sign in as extension **A**. Confirm registered (`[INFO] [REGISTER] Registered extension`).
2. Receive or place a call with **C** (or extension). Answer if inbound. Confirm two-way audio.
3. Tap **Transfer** → select **Warm (attended)** → enter **B** → **Transfer**.
4. Observe UI while consult rings: status shows "Calling B..." briefly, then warm-transfer layout.
5. Answer **B** on second phone. Talk to **B** briefly; confirm **C** is on hold (no audio to C).
6. Tap **Complete**.
7. Confirm agent **A** returns to idle (no active call UI). **C** and **B** are connected to each other.
8. Wait 30 s. Confirm no ghost call UI, no audio from softphone.

### Expected sip.log lines

```
[INFO] Warm transfer started: primary {C} on hold, consulting {B}
[INFO] Creating warm-transfer consult media session: WinMM output=...
[INFO] Warm transfer consult answered (200)
[INFO] Warm transfer consultation connected to {B}
[INFO] Warm transfer consult playback started (WinMM).
[INFO] Warm transfer completed to {B}
[INFO] Call hung up after ...s (Call-ID: ...)
```

### Pass criteria

- [ ] Primary auto-held before consult dials
- [ ] Consult rings and answers
- [ ] Complete drops agent; C↔B connected
- [ ] UI idle; tray Online
- [ ] No errors in sip.log after complete

---

## 2. Cancel warm transfer

**ID:** WT-CAN-01 (maps to T-09, SMK-13)

### Steps

1. Establish call A↔C (same as WT-HP-01 steps 1–2).
2. Start warm transfer to **B**. Wait for **B** to answer.
3. Tap **Cancel**.
4. Confirm UI returns to **On Call** with **C** (not B).
5. Confirm two-way audio with **C** restored.
6. End call normally.

### Expected sip.log lines

```
[INFO] Warm transfer started: primary {C} on hold, consulting {B}
[INFO] Warm transfer consult answered (200)
[INFO] Warm transfer consultation connected to {B}
[INFO] Warm transfer to {B} cancelled
[INFO] Resumed primary call after warm transfer cancel
```

### Pass criteria

- [ ] Cancel restores primary party display (C)
- [ ] Audio with C works
- [ ] Hold state cleared (not stuck on hold)
- [ ] Complete/Cancel buttons hidden; Transfer visible again

---

## 3. Consult no answer / timeout

**ID:** WT-FAIL-01 (maps to T-10)

### Steps

1. Establish call A↔C.
2. Start warm transfer to **B** using a number that rings but is **never answered** (or turn off B).
3. Wait for ring timeout (~90 s per `OutboundRingTimeoutSeconds`).
4. Observe error message in call UI status area.
5. Confirm returned to active call with **C**.

### Expected sip.log lines

```
[INFO] Warm transfer started: primary {C} on hold, consulting {B}
[INFO] Outbound call failed: ... (408 or 487 or timeout message)
[INFO] Warm transfer to {B} cancelled
[INFO] Resumed primary call after warm transfer cancel
```

UI may show: `Consultation call timed out.` or PBX failure text from `InvalidOperationException`.

### Pass criteria

- [ ] No stuck Warm Transfer UI after failure
- [ ] Primary call recoverable with audio
- [ ] No orphan consult leg (verify B does not show phantom call)

---

## 4. Consult busy / rejected

**ID:** WT-FAIL-02 (maps to T-10)

### Steps

1. Establish call A↔C.
2. Ensure **B** will reject (busy, DND, or manually reject).
3. Start warm transfer to **B**.
4. Observe failure message.
5. Confirm primary call with **C** restored.

### Expected sip.log lines

```
[INFO] Warm transfer started: primary {C} on hold, consulting {B}
[INFO] Outbound call failed: ... (486 Busy Here or 603 Decline)
[INFO] Warm transfer to {B} cancelled
[INFO] Resumed primary call after warm transfer cancel
```

### Pass criteria

- [ ] Meaningful error shown (busy / declined / not available)
- [ ] Primary call restored
- [ ] Agent not idle unless C also hung up

---

## 5. Remote hangup on consult leg

**ID:** WT-RH-01 (maps to T-11)

### Steps

1. Establish call A↔C. Start warm transfer; **B** answers.
2. From **B**'s phone, hang up (do not use agent Cancel).
3. Observe agent UI — should auto-return to primary **C** (same as Cancel path).

### Expected sip.log lines

```
[INFO] Warm transfer consultation connected to {B}
[INFO] Warm transfer consultation ended
[INFO] Warm transfer to {B} cancelled
[INFO] Resumed primary call after warm transfer cancel
```

### Pass criteria

- [ ] Auto-recovery without agent action
- [ ] Primary audio restored
- [ ] No Complete/Cancel buttons stuck visible

---

## 6. Remote hangup on primary during consult

**ID:** WT-RH-02 (maps to T-11)

### Steps

1. Establish call A↔C. Start warm transfer; **B** answers.
2. From **C**'s phone, hang up while agent talks to **B**.
3. Observe agent UI and sip.log.

### Expected sip.log lines

```
[INFO] Warm transfer consultation connected to {B}
[INFO] Call hung up after ...s (Call-ID: {primary-call-id})
```

Agent should go idle. **Verify consult leg:** check whether B still shows active call (known risk — see Gap Analysis).

### Pass criteria

- [ ] Agent UI goes idle (not stuck in Warm Transfer)
- [ ] No crash or frozen UI
- [ ] **B** consult leg state documented (pass if B call also ends; fail/note if B orphaned)

---

## 7. Start warm transfer from hold

**ID:** WT-HOLD-01 (maps to T-12)

### Steps

1. Establish call A↔C.
2. Tap **Hold**. Confirm "On Hold" UI.
3. Open Transfer → Warm → target **B** → Transfer.
4. Confirm consult proceeds (primary stays held; no double-hold error).
5. **B** answers → Complete or Cancel per preference.

### Expected sip.log lines

```
[INFO] Warm transfer started: primary {C} on hold, consulting {B}
```

(No duplicate hold errors; `_isOnHold` already true — `PutOnHold` skipped.)

### Pass criteria

- [ ] Warm transfer starts from held state
- [ ] Consult connects normally
- [ ] Complete/Cancel behave as in WT-HP-01 / WT-CAN-01

---

## 8. Blind transfer blocked during warm transfer

**ID:** WT-BLK-01 (maps to T-05)

### Steps

1. Start warm transfer; wait for consult connected state.
2. Attempt blind transfer via Transfer panel (if reachable) or verify Transfer button hidden.
3. If API invoked: expect error `Finish or cancel the warm transfer first.`

### Pass criteria

- [ ] Transfer button hidden during consult (UI)
- [ ] Blind transfer throws if forced (SipService guard)

---

## 9. Hold disabled during warm transfer

**ID:** WT-UI-01 (maps to C-14)

### Steps

1. During active warm transfer consult, confirm **Hold** button is disabled/greyed.

### Pass criteria

- [ ] Hold button not clickable during `WarmTransferConsulting`

---

## 10. Post-complete — new inbound (v1.3.7 regression)

**ID:** WT-POST-01

### Steps

1. Complete warm transfer successfully (WT-HP-01). Agent idle.
2. Within 60 s, place inbound call to **A** from **C** (or third party).
3. Confirm normal incoming UI (ringing, Answer/Decline).
4. Answer. Confirm two-way audio.
5. Check sip.log for stale BYE handling — must **not** reset new call.

### Expected sip.log lines

```
[INFO] Warm transfer completed to {B}
... (possible delayed BYE from old leg) ...
[INFO] [INBOUND] Ignoring stale hangup for Call-ID: {old-id}
   — OR —
[INFO] Ignoring stale BYE for Call-ID: {old-id}
[INFO] [INBOUND] Incoming call from {caller} ...
```

### Pass criteria

- [ ] Inbound rings and answers normally
- [ ] No immediate hangup / idle flicker after answer
- [ ] Stale old-leg signaling ignored (log confirms)
- [ ] Wrap-up overlay behaves normally after end

---

## 11. Post-complete — sign out, new outbound

**ID:** WT-POST-02

### Steps

1. Complete warm transfer. Confirm idle.
2. Sign out (or toggle offline). Sign back in.
3. Place new outbound call. Confirm connect + audio.
4. End call.

### Pass criteria

- [ ] Registration succeeds after sign-in
- [ ] Outbound works; no warm-transfer state residue
- [ ] No consult media session errors in log

---

## 12. DTMF during warm transfer consult

**ID:** WT-DTMF-01

### Steps

1. Warm transfer to **B** (answered). Open keypad.
2. Send DTMF tones (e.g. `1`, `2`, `#`).
3. Confirm **B** side hears tones (not primary **C**).

### Expected sip.log lines

```
[INFO] Sent DTMF '1'
[INFO] Sent DTMF '2'
```

### Pass criteria

- [ ] DTMF routed to consult leg (`_consultUserAgent`)
- [ ] Primary held party does not receive tones

---

## 13. Agent hangup during warm transfer

**ID:** WT-END-01

### Steps

1. Warm transfer consult connected.
2. Agent taps **End Call** (dialpad or call session).
3. Confirm both legs torn down; UI idle.

### Expected sip.log lines

```
[INFO] Hanging up call
```

### Pass criteria

- [ ] Both primary and consult hung up (`HangupAsync` calls both agents)
- [ ] Clean idle state

---

## 14. Mute / speaker mute during consult

**ID:** WT-AUD-01

### Steps

1. Warm transfer consult connected.
2. Toggle **Mute** — confirm consult audio path mutes (B cannot hear agent).
3. Toggle **Speaker mute** — confirm agent cannot hear B.
4. Cancel warm transfer; confirm mute state sensible on return to primary.

### Pass criteria

- [ ] Mute applies to consult `MutingAudioEndPoint`
- [ ] Return to primary restores expected audio routing

---

## 15. Call waiting during warm transfer (edge)

**ID:** WT-CW-01

### Steps

1. A on call with C. Start warm transfer; B answers.
2. While consulting, ring **A** from third party **D**.
3. Document behavior (call waiting eligible per `IsEligibleForCallWaiting`).

### Pass criteria

- [ ] Document actual behavior (may ring/show waiting UI)
- [ ] No crash or permanent stuck state
- [ ] After handling waiting call or dismissing, warm transfer state still coherent

---

## Pass / Fail checklist (summary)

| ID | Scenario | Ref | P | F | Tester | Date | Notes |
|----|----------|-----|---|---|--------|------|-------|
| WT-HP-01 | Happy path complete | T-06–08, SMK-12 | | | | | |
| WT-CAN-01 | Cancel warm transfer | T-09, SMK-13 | | | | | |
| WT-FAIL-01 | Consult no answer / timeout | T-10 | | | | | |
| WT-FAIL-02 | Consult busy / rejected | T-10 | | | | | |
| WT-RH-01 | Remote hangup consult leg | T-11 | | | | | |
| WT-RH-02 | Remote hangup primary during consult | T-11 | | | | | |
| WT-HOLD-01 | Start from hold | T-12 | | | | | |
| WT-BLK-01 | Blind blocked during warm | T-05 | | | | | |
| WT-UI-01 | Hold disabled | C-14 | | | | | |
| WT-POST-01 | Post-complete inbound | v1.3.7 regression | | | | | |
| WT-POST-02 | Post-complete sign-out/outbound | — | | | | | |
| WT-DTMF-01 | DTMF to consult leg | — | | | | | |
| WT-END-01 | Agent hangup both legs | — | | | | | |
| WT-AUD-01 | Mute during consult | — | | | | | |
| WT-CW-01 | Call waiting edge | — | | | | | |

**Release gate:** WT-HP-01, WT-CAN-01, WT-POST-01, and SMK-12/13 must pass before release.

---

## Automated test coverage (unit)

Run: `dotnet test CallAnalog.Softphone.Tests --filter "FullyQualifiedName~WarmTransfer"`

See `CallAnalog.Softphone.Tests/WarmTransferTests.cs` and existing tests in `CallStateConsistencyHelperTests.cs` and `SipCallIdHelperTests`.

---

## Gap analysis — live SIP required

| Area | Unit tested? | Needs live SIP? |
|------|-------------|-----------------|
| `CallStateConsistencyHelper` warm-transfer states | Yes | No |
| `SipCallIdHelper` stale BYE during/after warm transfer | Yes | No |
| Transfer target regex validation | Yes | No |
| Lifecycle state snapshots (theory rows) | Yes | No |
| `StartWarmTransferAsync` hold + consult dial | No | Yes |
| `CompleteWarmTransferAsync` / `AttendedTransfer` REFER | No | Yes |
| `CancelWarmTransferInternalAsync` audio resume | No | Yes |
| Consult media session / WinMM playback | No | Yes |
| Primary remote hangup → consult orphan leg | No | Yes (WT-RH-02) |
| PBX-specific failure codes (486, 408, 603) | No | Yes |
| UI binding (CallSessionView panels) | No | Manual |
| Tray status during warm transfer | No | Manual |
| Call waiting interaction during consult | No | Yes |

### Known risks (code review, v1.3.9)

1. **Primary remote hangup during consult:** `OnCallHungup` on primary calls `CleanupWarmTransferInternal()` which nulls `_consultUserAgent` without sending BYE to consult — consult leg may remain active on B's phone (WT-RH-02).
2. **Consult BYE via SIP transport:** Consult Call-ID ≠ `_activeCallId`; transport-level BYE for consult is treated as stale. Relies on `WireConsultUserAgentEvents.OnCallHungup` instead.
3. **`StartWarmTransferAsync` is blocking:** UI thread awaits consult answer (up to ~95 s). Failure auto-calls cancel; ensure UI remains responsive (status message only).
4. **No duplicate warm transfer guard in UI:** Second start throws `A warm transfer is already in progress` — only if consult agent non-null.

---

*Generated for CallAnalog Softphone v1.3.9 warm-transfer hardening audit.*

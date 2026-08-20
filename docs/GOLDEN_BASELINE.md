# CallAnalog Softphone — Golden Baseline

Frozen behaviors that must pass on every release. New features get a separate delta checklist.

**Baseline version:** 2.2.3 (bugs 2.0.0 → UI 2.1.0 → voice 2.2.0 → audio WinMM + APIs 2.2.3)  
**Gate:** All G1–G15 must pass before shipping.  
**Full checklist:** `docs/MANUAL_TESTING_SOP.md`

| ID | Must always work |
|----|------------------|
| G1 | Login → Online |
| G2 | Outbound → clear two-way audio |
| G3 | Inbound answer / decline |
| G4 | Mute / unmute |
| G5 | Hold / resume |
| G6 | Blind transfer |
| G7 | Call waiting: decline waiting → active call still hears audio |
| G8 | Custom ringtone plays (or default tone if none set) |
| G9 | Hold music: no double-play on agent speaker |
| G10 | Hangup → next call works (audio OK) |
| G11 | Restart → first REGISTER succeeds |
| G12 | Settings audio devices + Test Speaker |
| G13 | Quiet period on call — far end has no loud constant distortion |
| G14 | Default audio backend is WinMM (`Audio:PreferWasapi=false`) |
| G15 | Wrap-up note + rating on same `/public/api/callNote` payload |

## Release rule

1. `dotnet test` passes  
2. Golden Smoke G1–G15 passes on a real PBX with a normal USB headset  
3. Delta checklist for this build’s changes only (`MANUAL_TESTING_SOP.md` §24)

If Golden fails → reject the build even when the new feature works.

## High-risk files

Touching these requires re-running G2–G10 and G13–G14 (and full §19 Audio regression):

- `Services/WinMmAudioOutputManager.cs`
- `Services/CallAnalogWindowsAudioEndPoint.cs`
- `Helpers/PcmFormatConverter.cs`
- `Helpers/AntiAliasLowPassFilter.cs`
- `Services/CallVoiceProcessor.cs`
- `Services/RingtoneService.cs`
- `Services/SipService.cs` (hold / call-waiting / media)
- `Views/CallSessionView.xaml.cs`

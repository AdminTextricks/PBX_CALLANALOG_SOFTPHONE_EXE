using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CallAnalog.Softphone.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyAnswer = 1;
    private const int HotkeyHangup = 2;
    private const int HotkeyMute = 3;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNorepeat = 0x4000;

    private const int WmHotkey = 0x0312;

    private readonly HwndSource _source;
    private bool _registered;

    public event EventHandler? AnswerRequested;
    public event EventHandler? HangupRequested;
    public event EventHandler? MuteRequested;

    public GlobalHotkeyService(IntPtr windowHandle)
    {
        _source = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("Could not attach hotkey listener to window handle.");
        _source.AddHook(WndProc);
    }

    public void Register()
    {
        if (_registered)
        {
            return;
        }

        // Ctrl+Shift+A = Answer, Ctrl+Shift+H = Hangup, Ctrl+Shift+M = Mute
        var modifiers = ModControl | ModShift | ModNorepeat;
        if (!TryRegisterHotKey(_source.Handle, HotkeyAnswer, modifiers, 0x41)
            || !TryRegisterHotKey(_source.Handle, HotkeyHangup, modifiers, 0x48)
            || !TryRegisterHotKey(_source.Handle, HotkeyMute, modifiers, 0x4D))
        {
            App.SipLog.Warn(SipLogTag.General, "One or more global hotkeys could not be registered (may already be in use).");
            Unregister();
            return;
        }

        _registered = true;
    }

    private static bool TryRegisterHotKey(IntPtr handle, int id, uint modifiers, uint vk) =>
        RegisterHotKey(handle, id, modifiers, vk);

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        UnregisterHotKey(_source.Handle, HotkeyAnswer);
        UnregisterHotKey(_source.Handle, HotkeyHangup);
        UnregisterHotKey(_source.Handle, HotkeyMute);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
        {
            return IntPtr.Zero;
        }

        switch (wParam.ToInt32())
        {
            case HotkeyAnswer:
                AnswerRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
            case HotkeyHangup:
                HangupRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
            case HotkeyMute:
                MuteRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

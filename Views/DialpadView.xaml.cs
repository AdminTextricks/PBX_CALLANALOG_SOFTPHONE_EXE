using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CallAnalog.Softphone.Helpers;
using CallAnalog.Softphone.Models;
using CallAnalog.Softphone.Services;

namespace CallAnalog.Softphone.Views;

public partial class DialpadView : UserControl
{
    private DialService? _dialService;
    private SipService? _sipService;
    private bool _isDialing;
    private bool _isUpdatingFormattedText;
    private readonly DispatcherTimer _backspaceHoldTimer;
    private readonly DispatcherTimer _zeroLongPressTimer;
    private bool _backspaceHeld;
    private bool _zeroLongPressFired;
    private string? _pendingNumber;

    public event EventHandler? BackRequested;
    public event EventHandler<string>? OutboundDialFailed;

    public DialpadView()
    {
        InitializeComponent();
        _backspaceHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _backspaceHoldTimer.Tick += (_, _) =>
        {
            _backspaceHeld = true;
            NumberBox.Text = string.Empty;
            _backspaceHoldTimer.Stop();
        };

        _zeroLongPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _zeroLongPressTimer.Tick += (_, _) =>
        {
            _zeroLongPressTimer.Stop();
            if (Mouse.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            _zeroLongPressFired = true;
            _ = HandleDigitAsync("+");
        };

        Loaded += (_, _) =>
        {
            ApplyPendingNumber();
            ActivateInput();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                ApplyPendingNumber();
                ActivateInput();
            }
        };

        UpdateCallState(CallState.Idle);
    }

    public void Initialize(DialService dialService, SipService sipService)
    {
        _dialService = dialService;
        _sipService = sipService;
        UpdateCallState(sipService.CallState);
    }

    public void SetRegistrationStatus(string label, ConnectionStatus status)
    {
        // Status is shown in the main app shell header.
    }

    public void SetNumber(string number)
    {
        _pendingNumber = number ?? string.Empty;
        ApplyPendingNumber();
    }

    public void ActivateInput()
    {
        Dispatcher.BeginInvoke(() =>
        {
            Focus();
            NumberBox.Focus();
            NumberBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void ApplyPendingNumber()
    {
        if (_pendingNumber is null || !IsLoaded || !IsVisible)
        {
            return;
        }

        SetRawNumber(PhoneNumberFormatter.Unformat(_pendingNumber));
        _pendingNumber = null;
    }

    public void UpdateCallState(CallState state)
    {
        if (state is CallState.InCall or CallState.OnHold or CallState.Outgoing)
        {
            CallActionButton.Style = (Style)FindResource("DialPadEndCallButton");
            CallActionText.Text = "End Call";
            CallActionIcon.IconKey = "IconCallEnd";
            return;
        }

        CallActionButton.Style = (Style)FindResource("DialPadCallButton");
        CallActionText.Text = "Call";
        CallActionIcon.IconKey = "IconPhone";

        if (state == CallState.Idle)
        {
            ClearInCallStatus();
        }
    }

    public void ShowDialFailure(string message) =>
        SetInCallStatus(message);

    private void SetInCallStatus(string message, StatusMessageKind kind = StatusMessageKind.Error) =>
        StatusMessageHelper.Apply(InCallStatusText, message, kind);

    private void ClearInCallStatus() => InCallStatusText.Text = string.Empty;

    private void BackButton_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private async void DigitButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string digit)
        {
            if (digit == "0" && _zeroLongPressFired)
            {
                _zeroLongPressFired = false;
                return;
            }

            await HandleDigitAsync(digit);
        }
    }

    private void ZeroButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount > 1)
        {
            return;
        }

        _zeroLongPressFired = false;
        _zeroLongPressTimer.Stop();
        _zeroLongPressTimer.Start();
    }

    private void ZeroButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _zeroLongPressTimer.Stop();
    }

    private async Task HandleDigitAsync(string digit)
    {
        if (digit.Length == 1 && IsDtmfReady() && IsDtmfChar(digit[0]))
        {
            try
            {
                await _sipService!.SendDtmfAsync(digit[0]);
            }
            catch (Exception ex)
            {
                SetInCallStatus(ex.Message);
            }
        }

        AppendDigit(digit);
    }

    private bool IsDtmfReady() =>
        _sipService?.CallState is CallState.InCall or CallState.OnHold or CallState.CallWaitingRinging;

    private static bool IsDtmfChar(char tone) =>
        tone is >= '0' and <= '9' or '*' or '#';

    private void AppendDigit(string digit)
    {
        var raw = PhoneNumberFormatter.Unformat(NumberBox.Text);
        var insertAt = PhoneNumberFormatter.FormattedIndexToRawIndex(NumberBox.Text, NumberBox.CaretIndex);
        insertAt = Math.Clamp(insertAt, 0, raw.Length);
        raw = raw.Insert(insertAt, digit);
        SetRawNumber(raw, caretRawIndex: insertAt + digit.Length);
    }

    private void SetRawNumber(string raw, int? caretRawIndex = null)
    {
        _isUpdatingFormattedText = true;
        try
        {
            NumberBox.Text = PhoneNumberFormatter.FormatForDisplay(raw);
            var caret = caretRawIndex ?? raw.Length;
            try
            {
                NumberBox.CaretIndex = PhoneNumberFormatter.RawIndexToFormattedIndex(raw[..Math.Min(caret, raw.Length)]);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
        finally
        {
            _isUpdatingFormattedText = false;
        }
    }

    private void BackspaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backspaceHeld)
        {
            _backspaceHeld = false;
            return;
        }

        DeleteBackward();
    }

    private void BackspaceButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _backspaceHoldTimer.Start();

    private void BackspaceButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        _backspaceHoldTimer.Stop();

    private void DeleteBackward()
    {
        var raw = PhoneNumberFormatter.Unformat(NumberBox.Text);
        if (raw.Length == 0)
        {
            return;
        }

        var rawCaret = PhoneNumberFormatter.FormattedIndexToRawIndex(NumberBox.Text, NumberBox.CaretIndex);
        if (rawCaret <= 0)
        {
            rawCaret = raw.Length;
        }

        raw = raw.Remove(rawCaret - 1, 1);
        SetRawNumber(raw, rawCaret - 1);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e) =>
        SetRawNumber(string.Empty);

    private void NumberBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingFormattedText)
        {
            return;
        }

        var caret = NumberBox.CaretIndex;
        var raw = PhoneNumberFormatter.Unformat(NumberBox.Text);
        var formatted = PhoneNumberFormatter.FormatForDisplay(raw);
        if (!string.Equals(formatted, NumberBox.Text, StringComparison.Ordinal))
        {
            _isUpdatingFormattedText = true;
            NumberBox.Text = formatted;
            NumberBox.CaretIndex = Math.Min(caret, formatted.Length);
            _isUpdatingFormattedText = false;
        }
    }

    private async void CallActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sipService?.CallState is CallState.InCall or CallState.OnHold or CallState.Outgoing)
        {
            try
            {
                await _sipService.HangupAsync();
            }
            catch (Exception ex)
            {
                SetInCallStatus(ex.Message);
            }

            return;
        }

        await PlaceCallAsync(PhoneNumberFormatter.Unformat(NumberBox.Text));
    }

    private async void RedialButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dialService is null)
        {
            return;
        }

        var last = _dialService.LastDialedNumber;
        if (!string.IsNullOrWhiteSpace(last))
        {
            SetNumber(last);
        }

        await PlaceCallAsync(
            string.IsNullOrWhiteSpace(last) ? PhoneNumberFormatter.Unformat(NumberBox.Text) : last,
            token => _dialService.RedialAsync(token));
    }

    private async Task PlaceCallAsync(string number, Func<CancellationToken, Task<DialResult>>? dialFunc = null)
    {
        if (_dialService is null)
        {
            return;
        }

        if (_isDialing)
        {
            return;
        }

        ClearInCallStatus();
        _isDialing = true;
        try
        {
            var result = await (dialFunc?.Invoke(CancellationToken.None)
                ?? _dialService.PlaceCallAsync(number));

            if (!result.Success)
            {
                var message = $"{result.Message}. {result.Reason}";
                SetInCallStatus(message);
                OutboundDialFailed?.Invoke(this, message);
            }
        }
        finally
        {
            _isDialing = false;
        }
    }

    private async void DialpadView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TryGetDialSymbol(e.Key, out var symbol))
        {
            await HandleDigitAsync(symbol);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back)
        {
            DeleteBackward();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (IsDtmfReady())
            {
                e.Handled = true;
                return;
            }

            _ = PlaceCallAsync(PhoneNumberFormatter.Unformat(NumberBox.Text));
            e.Handled = true;
        }
    }

    private static bool TryGetDialSymbol(Key key, out string symbol)
    {
        symbol = string.Empty;
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (key >= Key.D0 && key <= Key.D9)
        {
            var digit = (int)key - (int)Key.D0;
            if (shift)
            {
                symbol = digit switch
                {
                    8 => "*",
                    3 => "#",
                    _ => digit.ToString()
                };
            }
            else
            {
                symbol = digit.ToString();
            }

            return true;
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            symbol = ((int)key - (int)Key.NumPad0).ToString();
            return true;
        }

        symbol = key switch
        {
            Key.Multiply => "*",
            Key.Oem3 or Key.Oem102 => "#",
            _ => string.Empty
        };

        return symbol.Length > 0;
    }
}

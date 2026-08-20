namespace CallAnalog.Softphone.Models;

public sealed class SettingsSavedEventArgs : EventArgs
{
    public SettingsSavedEventArgs(bool registrationTimingChanged)
    {
        RegistrationTimingChanged = registrationTimingChanged;
    }

    public bool RegistrationTimingChanged { get; }
}

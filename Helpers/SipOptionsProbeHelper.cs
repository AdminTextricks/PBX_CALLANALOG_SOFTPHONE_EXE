namespace CallAnalog.Softphone.Helpers;

/// <summary>
/// Decides when inbound SIP OPTIONS should be answered with 200 OK.
/// SIPSorcery's SIPUserAgent does not answer out-of-dialog OPTIONS; with
/// isTransportExclusive=true it replies 405 MethodNotAllowed instead.
/// OpenSIPS (and similar PBXs) use those probes for contact qualify/reachability.
/// </summary>
internal static class SipOptionsProbeHelper
{
    /// <summary>
    /// Matches SIPSorcery <c>SIPConstants.ALLOWED_SIP_METHODS</c> / the Allow
    /// header already advertised on REGISTER and other responses.
    /// </summary>
    internal const string AllowedMethods =
        "ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, PRACK, REFER, REGISTER, SUBSCRIBE";

    /// <summary>
    /// Out-of-dialog OPTIONS (no To-tag) are PBX qualify/reachability probes.
    /// In-dialog OPTIONS keep a To-tag and are left to the user agent.
    /// </summary>
    internal static bool ShouldAnswerOutOfDialogOptions(bool isOptionsMethod, bool hasToTag) =>
        isOptionsMethod && !hasToTag;
}

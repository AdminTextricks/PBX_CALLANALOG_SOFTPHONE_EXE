using System.Net;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace CallAnalog.Softphone.Services;

/// <summary>
/// VoIP media session that advertises the client's public IP in SDP (required behind NAT, like MicroSIP).
/// </summary>
internal sealed class CallAnalogVoIPMediaSession : VoIPMediaSession
{
    private readonly IPAddress _connectionAddress;

    public CallAnalogVoIPMediaSession(MediaEndPoints mediaEndPoints, IPAddress? publicConnectionAddress)
        : base(mediaEndPoints)
    {
        _connectionAddress = publicConnectionAddress ?? SipNatHelper.GetConnectionAddressForSdp();
        AcceptRtpFromAny = true;
        App.SipLog.Info($"Media session SDP connection address: {_connectionAddress}");
    }

    public override SDP CreateOffer(IPAddress connectionAddress) =>
        base.CreateOffer(ChooseConnectionAddress(connectionAddress));

    public override SDP CreateAnswer(IPAddress connectionAddress) =>
        base.CreateAnswer(ChooseConnectionAddress(connectionAddress));

    private IPAddress ChooseConnectionAddress(IPAddress? requested) =>
        SipNatHelper.CachedPublicIp
        ?? (requested is not null && !SipNatHelper.IsPrivateAddress(requested) ? requested : _connectionAddress);
}

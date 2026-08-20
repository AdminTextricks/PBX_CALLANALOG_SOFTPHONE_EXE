Add-Type -Path "$env:USERPROFILE\.nuget\packages\sipsorcery\10.0.12\lib\net10.0\SIPSorcery.dll"
[SIPSorcery.SIP.App.SIPUserAgent].GetMethods() |
    Where-Object { $_.Name -match 'Initiate|Answer|Call' } |
    ForEach-Object { $_.ToString() }

Add-Type -Path "$env:USERPROFILE\.nuget\packages\sipsorcery\10.0.12\lib\net10.0\SIPSorcery.dll"
[SIPSorcery.Media.VoIPMediaSession].GetConstructors() | ForEach-Object { $_.ToString() }

$asm = [Reflection.Assembly]::LoadFrom("$env:USERPROFILE\.nuget\packages\sipsorcery\10.0.12\lib\net10.0\SIPSorcery.dll")
$asm.GetTypes() | Where-Object { $_.Name -match 'VoIP|MediaSession|RTPSession' } | Select-Object -ExpandProperty FullName

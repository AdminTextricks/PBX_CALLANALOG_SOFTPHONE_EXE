#r "C:\Users\Jatin Tomar\Downloads\softphone-new\.tmp-sipsorcery-win\lib\net10.0-windows10.0.17763\SIPSorceryMedia.Windows.dll"
var t = typeof(SIPSorceryMedia.Windows.WindowsAudioEndPoint);
foreach (var m in t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
{
    Console.WriteLine(m.ToString());
}

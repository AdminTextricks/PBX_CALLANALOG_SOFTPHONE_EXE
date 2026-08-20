using SIPSorceryMedia.Abstractions;

var props = typeof(EncodedAudioFrame).GetProperties();
foreach (var p in props)
{
    Console.WriteLine($"{p.Name}: {p.PropertyType.Name}");
}

foreach (var c in typeof(EncodedAudioFrame).GetConstructors())
{
    Console.WriteLine("ctor: " + string.Join(", ", c.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}")));
}

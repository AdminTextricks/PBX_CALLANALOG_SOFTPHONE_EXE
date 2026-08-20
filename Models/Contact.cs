using System.Text.Json.Serialization;

namespace CallAnalog.Softphone.Models;

public sealed class Contact
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("contact_name")]
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    /// <summary>Maps API field contact_number. Not named Number — API also sends number:null which breaks case-insensitive bind.</summary>
    [JsonPropertyName("contact_number")]
    public string ContactNumber
    {
        get => _contactNumber;
        set => _contactNumber = value ?? string.Empty;
    }

    [JsonIgnore]
    public string Number => ContactNumber;

    private string _name = string.Empty;
    private string _contactNumber = string.Empty;
}

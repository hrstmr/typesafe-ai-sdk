namespace Typesafe.Http;

public sealed class TypeSafeClientOptions
{
    /// <summary>Falls back to the TYPESAFE_API_KEY environment variable when left null.</summary>
    public string? ApiKey { get; set; }

    public Uri BaseUrl { get; set; } = new("https://api.typesafe.ai");

    public string DefaultModel { get; set; } = "jev-latest";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    public IDictionary<string, string> DefaultHeaders { get; } = new Dictionary<string, string>();
}

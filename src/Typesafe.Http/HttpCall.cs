namespace Typesafe.Http;

public sealed class HttpCall<TRequest, TResponse>
{
    public required string Method { get; init; }
    public required string Route { get; init; }
}

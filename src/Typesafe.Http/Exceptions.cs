using System.Net;

namespace Typesafe.Http;

public class TypeSafeException : Exception
{
    public TypeSafeException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>The API returned a non-success status.</summary>
public class ApiException : TypeSafeException
{
    public ApiException(HttpStatusCode status, string? body, string? requestId, string? message = null)
        : base(message ?? $"The API returned {(int)status} ({status}).")
    {
        Status = status;
        Body = body;
        RequestId = requestId;
    }

    public HttpStatusCode Status { get; }

    public string? Body { get; }

    public string? RequestId { get; }

    internal static ApiException FromResponse(
        HttpStatusCode status,
        string? body,
        string? requestId,
        TimeSpan? retryAfter) => status switch
        {
            HttpStatusCode.BadRequest => new BadRequestException(status, body, requestId),
            HttpStatusCode.Unauthorized => new AuthenticationException(status, body, requestId),
            HttpStatusCode.Forbidden => new PermissionDeniedException(status, body, requestId),
            HttpStatusCode.NotFound => new NotFoundException(status, body, requestId),
            HttpStatusCode.UnprocessableEntity => new UnprocessableEntityException(status, body, requestId),
            HttpStatusCode.TooManyRequests => new RateLimitException(status, body, requestId, retryAfter),
            >= HttpStatusCode.InternalServerError => new InternalServerException(status, body, requestId),
            _ => new ApiException(status, body, requestId),
        };
}

public sealed class BadRequestException : ApiException
{
    internal BadRequestException(HttpStatusCode status, string? body, string? requestId)
        : base(status, body, requestId)
    {
    }
}

public sealed class AuthenticationException : ApiException
{
    internal AuthenticationException(HttpStatusCode status, string? body, string? requestId)
        : base(status, body, requestId)
    {
    }
}

public sealed class PermissionDeniedException : ApiException
{
    internal PermissionDeniedException(HttpStatusCode status, string? body, string? requestId)
        : base(status, body, requestId)
    {
    }
}

public sealed class NotFoundException : ApiException
{
    internal NotFoundException(HttpStatusCode status, string? body, string? requestId)
        : base(status, body, requestId)
    {
    }
}

public sealed class UnprocessableEntityException : ApiException
{
    internal UnprocessableEntityException(HttpStatusCode status, string? body, string? requestId)
        : base(status, body, requestId)
    {
    }
}

public sealed class InternalServerException : ApiException
{
    internal InternalServerException(HttpStatusCode status, string? body, string? requestId)
        : base(status, body, requestId)
    {
    }
}

public sealed class RateLimitException : ApiException
{
    internal RateLimitException(HttpStatusCode status, string? body, string? requestId, TimeSpan? retryAfter)
        : base(status, body, requestId) => RetryAfter = retryAfter;

    /// <summary>How long the server asked us to wait, when it said.</summary>
    public TimeSpan? RetryAfter { get; }
}

/// <summary>The request never got a response.</summary>
public class ApiConnectionException : TypeSafeException
{
    public ApiConnectionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class ApiTimeoutException : ApiConnectionException
{
    internal ApiTimeoutException(TimeSpan timeout, Exception? innerException = null)
        : base($"The request timed out after {timeout.TotalSeconds:0.##}s.", innerException) => Timeout = timeout;

    public TimeSpan Timeout { get; }
}

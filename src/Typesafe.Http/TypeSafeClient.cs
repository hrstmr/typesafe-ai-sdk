using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typesafe.Http;

/// <summary>The body posted to SystemOne. Generic so the caller's own state type is serialized directly.</summary>
internal sealed record SystemOneRequest<TState>
{
    [JsonPropertyName("state")]
    public TState? State { get; init; }

    [JsonPropertyName("questions")]
    public required IReadOnlyDictionary<string, Question> Questions { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }
}

public sealed class TypeSafeClient : IDisposable
{
    private const string ApiKeyEnvironmentVariable = "TYPESAFE_API_KEY";
    private const string SystemOnePath = "/v1/systemone";
    private const string RequestIdHeader = "x-request-id";
    private const string SdkVersion = "0.1.0";

    private readonly TypeSafeClientOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _apiKey;

    public TypeSafeClient(TypeSafeClientOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? new TypeSafeClientOptions();

        _apiKey =
            _options.ApiKey
            ?? Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable)
            ?? throw new TypeSafeException(
                $"No API key was supplied. Set {nameof(TypeSafeClientOptions)}.{nameof(TypeSafeClientOptions.ApiKey)} "
                    + $"or the {ApiKeyEnvironmentVariable} environment variable."
            );

        _ownsHttpClient = httpClient is null;

        // Timeouts are enforced per request via a linked token, so a caller-supplied
        // HttpClient keeps whatever timeout its owner configured.
        _http = httpClient ?? new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// Asks the questions declared by <typeparamref name="TQuestions"/> — one property per question, typed
    /// <see cref="NoulAnswer"/>, <see cref="ChoiceAnswer{TEnum}"/> or <see cref="ScoreAnswer{TEnum}"/> — and
    /// returns the same record with every property populated. The instance passed in is only used to infer
    /// the type; its contents are ignored.
    /// </summary>
    public async Task<SystemOneResult<TQuestions>> SystemOneAsync<TState, TQuestions>(
        TState state,
        TQuestions questions,
        string? model = null,
        CancellationToken cancellationToken = default
    )
        where TQuestions : class
    {
        var result = await SystemOneAsync(state, QuestionSchema.Build(typeof(TQuestions)), model, cancellationToken).ConfigureAwait(false);

        return new SystemOneResult<TQuestions>
        {
            Model = result.Model,
            Usage = result.Usage,
            Answers = (TQuestions)QuestionSchema.Bind(typeof(TQuestions), result.Answers),
        };
    }

    public async Task<SystemOneResult> SystemOneAsync<TState>(
        TState state,
        IReadOnlyCollection<Question> questions,
        string? model = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(questions);

        if (questions.Count == 0)
        {
            throw new ArgumentException("Ask at least one question.", nameof(questions));
        }

        var payload = new SystemOneRequest<TState>
        {
            State = state,
            Questions = questions.ToDictionary(question => question.Name),
            Model = model ?? _options.DefaultModel,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseUrl, SystemOnePath))
        {
            Content = JsonContent.Create(payload, options: TypeSafeJsonOptions.Default),
        };

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", $"typesafe-sdk-dotnet/{SdkVersion}");
        request.Headers.TryAddWithoutValidation("X-TypeSafe-SDK", $"typesafe-sdk-dotnet/{SdkVersion}");
        request.Headers.TryAddWithoutValidation("X-TypeSafe-Runtime", RuntimeInformation.FrameworkDescription);

        foreach (var (name, value) in _options.DefaultHeaders)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_options.Timeout);

        HttpStatusCode status;
        string? requestId;
        TimeSpan? retryAfter;
        string body;

        try
        {
            using var response = await _http.SendAsync(request, timeoutSource.Token).ConfigureAwait(false);

            status = response.StatusCode;
            requestId = response.Headers.TryGetValues(RequestIdHeader, out var values) ? values.FirstOrDefault() : null;
            retryAfter = response.Headers.RetryAfter?.Delta;
            body = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiTimeoutException(_options.Timeout);
        }
        catch (HttpRequestException exception)
        {
            throw new ApiConnectionException("The API could not be reached.", exception);
        }

        if ((int)status is < 200 or > 299)
        {
            throw ApiException.FromResponse(status, body, requestId, retryAfter);
        }

        try
        {
            return JsonSerializer.Deserialize<SystemOneResult>(body, TypeSafeJsonOptions.Default)
                ?? throw new TypeSafeException("The API returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new TypeSafeException("The API returned a response this SDK could not read.", exception);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}

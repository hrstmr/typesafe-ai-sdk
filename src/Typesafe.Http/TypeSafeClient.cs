using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typesafe.Http;

public sealed class TypeSafeClient : IDisposable
{
    private const string ApiKeyEnvironmentVariable = "TYPESAFE_API_KEY";
    private const string SystemOnePath = "/v1/systemone";
    private const string RequestIdHeader = "x-request-id";
    private const string SdkVersion = "0.1.0";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TypeSafeClientOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _apiKey;

    public TypeSafeClient(TypeSafeClientOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? new TypeSafeClientOptions();

        _apiKey = _options.ApiKey
            ?? Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable)
            ?? throw new TypeSafeException(
                $"No API key was supplied. Set {nameof(TypeSafeClientOptions)}.{nameof(TypeSafeClientOptions.ApiKey)} "
                + $"or the {ApiKeyEnvironmentVariable} environment variable.");

        _ownsHttpClient = httpClient is null;

        // Timeouts are enforced per request via a linked token, so a caller-supplied
        // HttpClient keeps whatever timeout its owner configured.
        _http = httpClient ?? new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    public async Task<SystemOneResult> SystemOneAsync(
        object? state,
        IReadOnlyCollection<Question> questions,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(questions);

        if (questions.Count == 0)
        {
            throw new ArgumentException("Ask at least one question.", nameof(questions));
        }

        var payload = new Dictionary<string, object?>
        {
            ["state"] = state,
            ["questions"] = questions.ToDictionary(question => question.Name, question => question.ToPayload()),
            ["model"] = model ?? _options.DefaultModel,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseUrl, SystemOnePath))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, SerializerOptions),
                Encoding.UTF8,
                "application/json"),
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

        return Decode(body, questions);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private static SystemOneResult Decode(string body, IReadOnlyCollection<Question> questions)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var answersElement = root.GetProperty("answers");

            var answers = new Dictionary<string, Answer>(questions.Count);

            foreach (var question in questions)
            {
                answers[question.Name] = DecodeAnswer(question, answersElement.GetProperty(question.Name));
            }

            var usageElement = root.GetProperty("usage");
            var usage = new Usage(
                usageElement.GetProperty("input_tokens").GetInt32(),
                usageElement.GetProperty("output_tokens").GetInt32());

            return new SystemOneResult(root.GetProperty("model").GetString() ?? string.Empty, usage, answers);
        }
        catch (Exception exception)
            when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new TypeSafeException("The API returned a response this SDK could not read.", exception);
        }
    }

    private static Answer DecodeAnswer(Question question, JsonElement element) => question switch
    {
        NoulQuestion => new NoulAnswer(element.GetProperty("noul").GetDouble()),

        ChoiceQuestion => new ChoiceAnswer(
            element.GetProperty("choice").GetString() ?? string.Empty,
            element.GetProperty("confidence").GetDouble(),
            ReadLabelledProbabilities(element.GetProperty("probabilities"))),

        ScoreQuestion => new ScoreAnswer(
            element.GetProperty("score").GetDouble(),
            element.GetProperty("confidence").GetDouble(),
            ReadLegend(element.GetProperty("legend")),
            ReadScoredProbabilities(element.GetProperty("probabilities"))),

        _ => throw new TypeSafeException($"Unsupported question type '{question.Type}'."),
    };

    private static Dictionary<string, double> ReadLabelledProbabilities(JsonElement element)
    {
        var probabilities = new Dictionary<string, double>();

        foreach (var property in element.EnumerateObject())
        {
            probabilities[property.Name] = property.Value.GetDouble();
        }

        return probabilities;
    }

    private static Dictionary<int, double> ReadScoredProbabilities(JsonElement element)
    {
        var probabilities = new Dictionary<int, double>();

        foreach (var property in element.EnumerateObject())
        {
            probabilities[int.Parse(property.Name, CultureInfo.InvariantCulture)] = property.Value.GetDouble();
        }

        return probabilities;
    }

    private static Dictionary<int, string?> ReadLegend(JsonElement element)
    {
        var legend = new Dictionary<int, string?>();

        foreach (var property in element.EnumerateObject())
        {
            legend[int.Parse(property.Name, CultureInfo.InvariantCulture)] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.ToString();
        }

        return legend;
    }
}

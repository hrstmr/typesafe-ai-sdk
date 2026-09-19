using System.Net;
using System.Text;
using System.Text.Json;

namespace Typesafe.Http.Tests;

public class QuestionTests
{
    [Fact]
    public void ScoreRejectsRubricShorterThanTwo()
    {
        var thrown = Assert.Throws<ArgumentException>(() => Question.Score("urgency", "How urgent?", "only one"));

        Assert.Equal("rubric", thrown.ParamName);
    }

    [Fact]
    public void ChoiceRejectsEmptyCriteria()
    {
        Assert.Throws<ArgumentException>(() => Question.Choice("tone", "What tone?"));
    }

    [Fact]
    public void BareLabelsBecomeCriteriaWithoutDescriptions()
    {
        var question = Question.Choice("tone", "What tone?", "calm", "angry");

        Assert.Equal(["calm", "angry"], question.Criteria.Keys);
        Assert.All(question.Criteria.Values, Assert.Null);
    }

    [Fact]
    public void NoulOmitsCriteriaUnlessOutcomesAreDescribed()
    {
        Assert.False(Question.Noul("isBilling", "About billing?").ToPayload().ContainsKey("criteria"));

        var described = Question.Noul("isBilling", "About billing?", whenTrue: "yes it is").ToPayload();

        Assert.True(described.ContainsKey("criteria"));
    }
}

public class SystemOneTests
{
    private static readonly TypeSafeClientOptions Options = new() { ApiKey = "test-key" };

    [Fact]
    public async Task PostsQuestionsAndDecodesTypedAnswers()
    {
        var isBilling = Question.Noul("isBilling", "Is this ticket about billing?");
        var sentiment = Question.Choice("sentiment", "What is the tone?", "calm", "frustrated");
        var urgency = Question.Score("urgency", "How urgent?", "can wait", "today", "right now");

        var handler = new StubHandler(
            HttpStatusCode.OK,
            """
            {
              "model": "jev-latest",
              "answers": {
                "isBilling": { "type": "noul", "noul": 0.93 },
                "sentiment": {
                  "type": "choice",
                  "choice": "frustrated",
                  "confidence": 0.81,
                  "probabilities": { "calm": 0.19, "frustrated": 0.81 }
                },
                "urgency": {
                  "type": "score",
                  "score": 1.7,
                  "confidence": 0.62,
                  "legend": { "0": "can wait", "1": "today", "2": "right now" },
                  "probabilities": { "0": 0.1, "1": 0.5, "2": 0.4 }
                }
              },
              "usage": { "input_tokens": 120, "output_tokens": 45 }
            }
            """
        );

        using var client = new TypeSafeClient(Options, new HttpClient(handler));

        var result = await client.SystemOneAsync(new { subject = "Charged twice" }, [isBilling, sentiment, urgency]);

        Assert.Equal("jev-latest", result.Model);
        Assert.Equal(0.93, result[isBilling].Noul);
        Assert.Equal("frustrated", result[sentiment].Choice);
        Assert.Equal(0.81, result[sentiment].Probabilities["frustrated"]);
        Assert.Equal(1.7, result[urgency].Score);
        Assert.Equal("today", result[urgency].Legend[1]);
        Assert.Equal(new Usage(120, 45), result.Usage);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.typesafe.ai/v1/systemone", handler.Request.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", handler.Request.Headers.GetValues("Authorization").Single());

        using var sent = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("jev-latest", sent.RootElement.GetProperty("model").GetString());
        Assert.Equal("choice", sent.RootElement.GetProperty("questions").GetProperty("sentiment").GetProperty("type").GetString());
        Assert.Equal("Charged twice", sent.RootElement.GetProperty("state").GetProperty("subject").GetString());
    }

    [Fact]
    public async Task SendsTheModelOverrideWhenGiven()
    {
        var handler = new StubHandler(HttpStatusCode.OK, EmptyAnswerFor("isBilling"));
        using var client = new TypeSafeClient(Options, new HttpClient(handler));

        await client.SystemOneAsync(null, [Question.Noul("isBilling")], model: "jev-2");

        using var sent = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("jev-2", sent.RootElement.GetProperty("model").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, typeof(AuthenticationException))]
    [InlineData(HttpStatusCode.NotFound, typeof(NotFoundException))]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(RateLimitException))]
    [InlineData(HttpStatusCode.BadGateway, typeof(InternalServerException))]
    public async Task MapsStatusCodesToExceptionTypes(HttpStatusCode status, Type expected)
    {
        var handler = new StubHandler(status, """{"error":"nope"}""");
        using var client = new TypeSafeClient(Options, new HttpClient(handler));

        var thrown = await Assert.ThrowsAnyAsync<ApiException>(() => client.SystemOneAsync(null, [Question.Noul("isBilling")]));

        Assert.IsType(expected, thrown);
        Assert.Equal(status, thrown.Status);
        Assert.Equal("""{"error":"nope"}""", thrown.Body);
    }

    [Fact]
    public async Task RejectsAnEmptyQuestionSet()
    {
        using var client = new TypeSafeClient(Options, new HttpClient(new StubHandler(HttpStatusCode.OK, "{}")));

        await Assert.ThrowsAsync<ArgumentException>(() => client.SystemOneAsync(null, []));
    }

    [Fact]
    public void RequiresAnApiKey()
    {
        Assert.Throws<TypeSafeException>(() => new TypeSafeClient(new TypeSafeClientOptions { ApiKey = null }));
    }

    [Fact]
    public async Task SurfacesAnUnreadableResponse()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"model":"jev-latest"}""");
        using var client = new TypeSafeClient(Options, new HttpClient(handler));

        await Assert.ThrowsAsync<TypeSafeException>(() => client.SystemOneAsync(null, [Question.Noul("isBilling")]));
    }

    private static string EmptyAnswerFor(string name) =>
        $$"""
            {
              "model": "jev-2",
              "answers": { "{{name}}": { "type": "noul", "noul": 0.5 } },
              "usage": { "input_tokens": 1, "output_tokens": 1 }
            }
            """;

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}

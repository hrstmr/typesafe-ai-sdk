using System.ComponentModel;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using Snapshooter.Xunit;

namespace Typesafe.Http.Tests;

[Description("What is the customer's tone?")]
internal enum TestSentiment
{
    [Description("matter of fact, no strong feeling")]
    Calm,

    [Description("annoyed but still civil")]
    Frustrated,

    [Description("openly hostile")]
    Angry,
}

// Underlying values are deliberately sparse, like Buildxact's MemoryKind, so the tests
// prove rubric indices follow declaration order rather than the enum's numbers.
[Description("How urgent is this ticket?")]
internal enum TestUrgency
{
    [Description("can wait")]
    CanWait = 0,

    [Description("this week")]
    ThisWeek = 1,

    [Description("today")]
    Today = 5,

    [Description("right now")]
    RightNow = 9,
}

internal sealed record TicketQuestions(NoulAnswer isBilling, ChoiceAnswer<TestSentiment> sentiment, ScoreAnswer<TestUrgency> urgency)
{
    /// <summary>Instructions live on the instance; sentiment and urgency fall back to their enum's [Description].</summary>
    public static TicketQuestions Declared =>
        new(
            isBilling: new(
                instruction: "Is this ticket about billing?",
                new(isTrueWhen: "Has billing info", isFalseWhen: "does not have billing info")
            ),
            sentiment: default!,
            urgency: default!
        );
}

internal sealed record UnsupportedQuestions(string notAQuestion);

public class SchemaTests
{
    private const string Response = """
        {
          "model": "jev-latest",
          "answers": {
            "isBilling": { "type": "noul", "noul": 0.93 },
            "sentiment": {
              "type": "choice",
              "choice": "Frustrated",
              "confidence": 0.81,
              "probabilities": { "Calm": 0.19, "Frustrated": 0.81, "Angry": 0.0 }
            },
            "urgency": {
              "type": "score",
              "score": 1.7,
              "confidence": 0.62,
              "legend": { "0": "can wait", "1": "this week", "2": "today", "3": "right now" },
              "probabilities": { "0": 0.1, "1": 0.2, "2": 0.6, "3": 0.1 }
            }
          },
          "usage": { "input_tokens": 120, "output_tokens": 45 }
        }
        """;

    private static readonly TypeSafeClientOptions Options = new() { ApiKey = "test-key" };

    private static (TypeSafeClient Client, StubHandler Handler) Stub(string body = Response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler(status, body);
        return (new TypeSafeClient(Options, new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task BindsEveryPropertyToItsTypedAnswer()
    {
        var (client, _) = Stub();

        var response = await client.SystemOneAsync(new Ticket("Charged twice", "Two charges."), TicketQuestions.Declared);
        var answers = response.Answers;

        Assert.Equal(0.93m, answers.isBilling.Noul);
        Assert.Equal(TestSentiment.Frustrated, answers.sentiment.Choice);
        Assert.Equal(0.81m, answers.sentiment.Probabilities[TestSentiment.Frustrated]);
        Assert.Equal(1.7m, answers.urgency.Score);
        Assert.Equal("jev-latest", response.Model);
        Assert.Equal(120, response.Usage.InputTokens);
    }

    [Fact]
    public async Task MapsRubricIndicesByDeclarationOrderNotEnumValue()
    {
        var (client, _) = Stub();

        var response = await client.SystemOneAsync<object?, TicketQuestions>(null, TicketQuestions.Declared);

        // Index 2 is the third declared member (Today = 5), not the member whose value is 2.
        Assert.Equal(TestUrgency.Today, response.Answers.urgency.HighestProbability);
        Assert.Equal("today", response.Answers.urgency.Legend[TestUrgency.Today]);
        Assert.Equal(0.6m, response.Answers.urgency.Probabilities[TestUrgency.Today]);
        Assert.Equal(0.1m, response.Answers.urgency.Probabilities[TestUrgency.CanWait]);
    }

    [Fact]
    public async Task HighestProbabilityPicksTheLargest()
    {
        var (client, _) = Stub();

        var response = await client.SystemOneAsync<object?, TicketQuestions>(null, TicketQuestions.Declared);

        Assert.Equal(TestSentiment.Frustrated, response.Answers.sentiment.HighestProbability);
    }

    [Fact]
    public async Task RejectsAPropertyThatIsNotAnAnswerType()
    {
        var (client, _) = Stub();

        var thrown = await Assert.ThrowsAsync<TypeSafeException>(() => client.SystemOneAsync<object?, UnsupportedQuestions>(null, new(string.Empty)));

        Assert.Contains("notAQuestion", thrown.Message);
    }

    [Fact]
    public async Task RejectsALabelThatIsNotAnEnumMember()
    {
        var body = Response.Replace("\"choice\": \"Frustrated\"", "\"choice\": \"Bewildered\"");
        var (client, _) = Stub(body);

        var thrown = await Assert.ThrowsAsync<TypeSafeException>(() =>
            client.SystemOneAsync<object?, TicketQuestions>(null, TicketQuestions.Declared)
        );

        Assert.Contains("Bewildered", thrown.Message);
    }

    [Fact]
    public async Task RejectsACollectionPassedAsAQuestionsRecord()
    {
        var (client, _) = Stub();
        var asArray = new[] { Question.Noul("isBilling") };

        var thrown = await Assert.ThrowsAsync<TypeSafeException>(() => client.SystemOneAsync<object?, Question[]>(null, asArray));

        Assert.Contains("collection", thrown.Message);
    }

    [Fact]
    public async Task SchemaRequestJson()
    {
        var (client, handler) = Stub();

        await client.SystemOneAsync(new Ticket("Charged twice this month", "Two charges of $49."), TicketQuestions.Declared);

        Snapshot.Match(
            JsonSerializer.Serialize(
                JsonDocument.Parse(handler.RequestBody!).RootElement,
                new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
            )
        );
    }

    [Fact]
    public async Task SchemaBoundAnswers()
    {
        var (client, _) = Stub();

        var response = await client.SystemOneAsync<object?, TicketQuestions>(null, TicketQuestions.Declared);

        Snapshot.Match(response);
    }
}

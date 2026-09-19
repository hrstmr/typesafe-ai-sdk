using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using Snapshooter.Xunit;

namespace Typesafe.Http.Tests;

/// <summary>
/// Pins the JSON on both sides of the wire: what the question classes serialize to,
/// and what a response deserializes into. Both snapshots are the actual bytes/objects
/// the client produced, not hand-written samples.
/// </summary>
public class SerializationSnapshotTests
{
    [Fact]
    public async Task RequestJson()
    {
        var handler = new StubHandler(HttpStatusCode.OK, SystemOneTests.SampleResponse);
        using var client = new TypeSafeClient(new TypeSafeClientOptions { ApiKey = "test-key" }, new HttpClient(handler));

        await client.SystemOneAsync(
            new Ticket("Charged twice this month", "I see two charges of $49 on my card for August."),
            [
                Question.Noul(
                    "isBilling",
                    "Is this ticket about billing?",
                    new NoulCriteria(isTrueWhen: "a charge or invoice", isFalseWhen: "anything else")
                ),
                Question.Choice("sentiment", "What is the customer's tone?", "calm", "frustrated", "angry"),
                Question.Choice(
                    "channel",
                    "Where did this come from?",
                    new Dictionary<string, string?> { ["email"] = "an inbound email", ["chat"] = "the in-app chat widget" }
                ),
                Question.Score("urgency", "How urgent is this ticket?", "can wait", "this week", "today", "right now"),
            ]
        );

        Snapshot.Match(Prettify(handler.RequestBody!));
    }

    [Fact]
    public void ResponseObjectGraph()
    {
        var result = JsonSerializer.Deserialize<SystemOneResult>(SystemOneTests.SampleResponse, TypeSafeJsonOptions.Default);

        Snapshot.Match(result);
    }

    // Re-indents without re-escaping, so the snapshot shows the bytes actually put on the wire.
    private static string Prettify(string json) =>
        JsonSerializer.Serialize(
            JsonDocument.Parse(json).RootElement,
            new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
        );
}

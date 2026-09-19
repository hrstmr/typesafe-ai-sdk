using System.ComponentModel;
using Typesafe.Http;

var ticket = new Ticket(
    Subject: "Charged twice this month",
    Body: "Hi, I see two charges of $49 on my card for August. I only have one account. Please fix this ASAP, I'm pretty frustrated."
);

try
{
    using var client = new TypeSafeClient();

    var response = await client.SystemOneAsync(ticket, new TicketQuestions(default!, default!, default!, default!));
    var answers = response.Answers;

    if (answers.isBilling.Noul > 0.5m)
    {
        Console.WriteLine("billing is true-ish");
    }

    if (answers.sentiment.Choice is Sentiment.Calm)
    {
        Console.WriteLine("sentiment is calm");
    }

    if (answers.urgency.HighestProbability is Urgency.Today)
    {
        Console.WriteLine("urgency is today");
    }

    if (answers.urgency.Probabilities[Urgency.CanWait] < 0.25m)
    {
        Console.WriteLine("this can't wait");
    }

    Console.WriteLine($"refund risk  {answers.refundRisk.Score:0.00} ({answers.refundRisk.Confidence:0.00} confidence)");
    Console.WriteLine($"tokens       {response.Usage.InputTokens} in / {response.Usage.OutputTokens} out");
}
catch (ApiException exception)
{
    Console.Error.WriteLine($"API error {(int)exception.Status} (request {exception.RequestId ?? "unknown"}): {exception.Body}");
}
catch (TypeSafeException exception)
{
    Console.Error.WriteLine(exception.Message);
}

internal sealed record Ticket(string Subject, string Body);

internal sealed record TicketQuestions(
    [property: Description("Is this ticket about billing?")] NoulAnswer isBilling,
    ChoiceAnswer<Sentiment> sentiment,
    ScoreAnswer<Urgency> urgency,
    [property: Description("How likely is the customer to demand a refund?")] ScoreAnswer<RefundRisk> refundRisk
);

[Description("What is the customer's tone?")]
internal enum Sentiment
{
    [Description("matter of fact, no strong feeling")]
    Calm,

    [Description("annoyed but still civil")]
    Frustrated,

    [Description("openly hostile")]
    Angry,
}

[Description("How urgent is this ticket?")]
internal enum Urgency
{
    [Description("can wait")]
    CanWait,

    [Description("this week")]
    ThisWeek,

    [Description("today")]
    Today,

    [Description("right now")]
    RightNow,
}

internal enum RefundRisk
{
    [Description("unlikely")]
    Unlikely,

    [Description("possible")]
    Possible,

    [Description("likely")]
    Likely,
}

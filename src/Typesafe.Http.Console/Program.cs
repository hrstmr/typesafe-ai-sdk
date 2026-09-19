using Typesafe.Http;

var ticket = new
{
    subject = "Charged twice this month",
    body = "Hi, I see two charges of $49 on my card for August. I only have one account. " + "Please fix this ASAP, I'm pretty frustrated.",
};

var isBilling = Question.Noul("isBilling", "Is this ticket about billing?");
var sentiment = Question.Choice("sentiment", "What is the customer's tone?", "calm", "frustrated", "angry");
var urgency = Question.Score("urgency", "How urgent is this ticket?", "can wait", "this week", "today", "right now");
var refundRisk = Question.Score("refundRisk", "How likely is the customer to demand a refund?", "unlikely", "possible", "likely");

try
{
    using var client = new TypeSafeClient();

    var result = await client.SystemOneAsync(ticket, [isBilling, sentiment, urgency, refundRisk]);

    var tone = result[sentiment];
    var refund = result[refundRisk];

    Console.WriteLine($"billing?     {result[isBilling].Noul:0.00}");
    Console.WriteLine($"tone         {tone.Choice} ({tone.Probabilities[tone.Choice]:0.00})");
    Console.WriteLine($"urgency      {result[urgency].Score:0.00} on a 0-3 scale");
    Console.WriteLine($"refund risk  {refund.Score:0.00} ({refund.Confidence:0.00} confidence)");
    Console.WriteLine($"tokens       {result.Usage.InputTokens} in / {result.Usage.OutputTokens} out");
}
catch (ApiException exception)
{
    Console.Error.WriteLine($"API error {(int)exception.Status} (request {exception.RequestId ?? "unknown"}): {exception.Body}");
}
catch (TypeSafeException exception)
{
    Console.Error.WriteLine(exception.Message);
}

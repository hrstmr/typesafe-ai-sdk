using System.Text.Json.Serialization;

namespace Typesafe.Http;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulAnswer), "noul")]
[JsonDerivedType(typeof(ChoiceAnswer), "choice")]
[JsonDerivedType(typeof(ScoreAnswer), "score")]
public abstract record Answer;

/// <summary>
/// Answer to a <see cref="NoulQuestion"/>. <see cref="Noul"/> is a probability in [0, 1],
/// not a boolean — the model reports how strongly it leans yes.
/// </summary>
public sealed record NoulAnswer : Answer
{
    [JsonPropertyName("noul")]
    public required double Noul { get; init; }
}

public sealed record ChoiceAnswer : Answer
{
    /// <summary>The selected label, one of the keys of the question's criteria.</summary>
    [JsonPropertyName("choice")]
    public required string Choice { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    [JsonPropertyName("probabilities")]
    public required IReadOnlyDictionary<string, double> Probabilities { get; init; }
}

public sealed record ScoreAnswer : Answer
{
    /// <summary>Position on the rubric. Fractional, because it is a probability-weighted mean.</summary>
    [JsonPropertyName("score")]
    public required double Score { get; init; }

    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    /// <summary>Rubric index to its description, echoed back so a score can be reported in words.</summary>
    [JsonPropertyName("legend")]
    public required IReadOnlyDictionary<int, string> Legend { get; init; }

    [JsonPropertyName("probabilities")]
    public required IReadOnlyDictionary<int, double> Probabilities { get; init; }
}

public sealed record Usage
{
    [JsonPropertyName("input_tokens")]
    public required int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public required int OutputTokens { get; init; }
}

/// <summary>
/// Answers to one SystemOne call. Index it with the same question objects that were asked:
/// each indexer overload is typed to its question, so the answer type is known at compile time.
/// </summary>
public sealed record SystemOneResult
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>Answers keyed by question name, for when the question objects are not at hand.</summary>
    [JsonPropertyName("answers")]
    public required IReadOnlyDictionary<string, Answer> Answers { get; init; }

    [JsonPropertyName("usage")]
    public required Usage Usage { get; init; }

    public NoulAnswer this[NoulQuestion question] => Lookup<NoulAnswer>(question);

    public ChoiceAnswer this[ChoiceQuestion question] => Lookup<ChoiceAnswer>(question);

    public ScoreAnswer this[ScoreQuestion question] => Lookup<ScoreAnswer>(question);

    private TAnswer Lookup<TAnswer>(Question question)
        where TAnswer : Answer
    {
        ArgumentNullException.ThrowIfNull(question);

        if (!Answers.TryGetValue(question.Name, out var answer))
        {
            throw new KeyNotFoundException($"The response contained no answer named '{question.Name}'.");
        }

        return answer as TAnswer
            ?? throw new InvalidOperationException(
                $"Answer '{question.Name}' came back as {answer.GetType().Name}, which does not match the question asked."
            );
    }
}

using System.Diagnostics.CodeAnalysis;
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
public sealed record NoulAnswer : Answer, IQuestionDeclaration
{
    [JsonConstructor]
    public NoulAnswer() { }

    /// <summary>Declares the question. <see cref="Noul"/> stays unset until the response is bound.</summary>
    [SetsRequiredMembers]
    public NoulAnswer(string? instruction, NoulCriteria? criteria = null)
    {
        Instruction = instruction;
        Criteria = criteria;
    }

    /// <summary>What to ask. Only read when this instance declares a question on a questions record.</summary>
    [JsonIgnore]
    public string? Instruction { get; init; }

    /// <summary>Optional descriptions of what a true and a false answer would mean.</summary>
    [JsonIgnore]
    public NoulCriteria? Criteria { get; init; }

    [JsonPropertyName("noul")]
    public required decimal Noul { get; init; }
}

/// <summary>Choice answer as it arrives on the wire, keyed by label. See <see cref="ChoiceAnswer{TEnum}"/> for the enum-typed form.</summary>
public sealed record ChoiceAnswer : Answer
{
    [JsonPropertyName("choice")]
    public required string Choice { get; init; }

    [JsonPropertyName("confidence")]
    public required decimal Confidence { get; init; }

    [JsonPropertyName("probabilities")]
    public required IReadOnlyDictionary<string, decimal> Probabilities { get; init; }
}

/// <summary>Score answer as it arrives on the wire, keyed by rubric index. See <see cref="ScoreAnswer{TEnum}"/> for the enum-typed form.</summary>
public sealed record ScoreAnswer : Answer
{
    /// <summary>Position on the rubric. Fractional, because it is a probability-weighted mean.</summary>
    [JsonPropertyName("score")]
    public required decimal Score { get; init; }

    [JsonPropertyName("confidence")]
    public required decimal Confidence { get; init; }

    /// <summary>Rubric index to its description, echoed back so a score can be reported in words.</summary>
    [JsonPropertyName("legend")]
    public required IReadOnlyDictionary<int, string> Legend { get; init; }

    [JsonPropertyName("probabilities")]
    public required IReadOnlyDictionary<int, decimal> Probabilities { get; init; }
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

/// <summary>Result of a schema-based call, carrying the answers bound onto <typeparamref name="TQuestions"/>.</summary>
public sealed record SystemOneResult<TQuestions>
    where TQuestions : class
{
    public required string Model { get; init; }

    /// <summary>The questions record, with every property populated by its answer.</summary>
    public required TQuestions Answers { get; init; }

    public required Usage Usage { get; init; }
}

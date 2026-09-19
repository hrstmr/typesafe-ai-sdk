namespace Typesafe.Http;

/// <summary>
/// A choice answer whose labels are the members of <typeparamref name="TEnum"/>.
/// Declare one of these on a questions record to ask a choice question.
/// </summary>
public sealed record ChoiceAnswer<TEnum>
    where TEnum : struct, Enum
{
    public required TEnum Choice { get; init; }

    public required decimal Confidence { get; init; }

    public required IReadOnlyDictionary<TEnum, decimal> Probabilities { get; init; }

    /// <summary>The option the model weighted most heavily. Usually equal to <see cref="Choice"/>.</summary>
    public TEnum HighestProbability => Probabilities.MaxBy(probability => probability.Value).Key;
}

/// <summary>
/// A score answer whose rubric is the members of <typeparamref name="TEnum"/>, lowest first.
/// Declare one of these on a questions record to ask a score question.
/// </summary>
public sealed record ScoreAnswer<TEnum>
    where TEnum : struct, Enum
{
    /// <summary>Position on the rubric. Fractional, because it is a probability-weighted mean.</summary>
    public required decimal Score { get; init; }

    public required decimal Confidence { get; init; }

    /// <summary>Each rubric option and the description that was sent for it.</summary>
    public required IReadOnlyDictionary<TEnum, string> Legend { get; init; }

    public required IReadOnlyDictionary<TEnum, decimal> Probabilities { get; init; }

    /// <summary>The rubric option the model weighted most heavily.</summary>
    public TEnum HighestProbability => Probabilities.MaxBy(probability => probability.Value).Key;
}

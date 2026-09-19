using System.Diagnostics.CodeAnalysis;

namespace Typesafe.Http;

/// <summary>
/// Implemented by every type that can stand in for a question on a questions record, so the
/// schema reader can pull the instruction off the instance the caller supplied.
/// </summary>
internal interface IQuestionDeclaration
{
    string? Instruction { get; }
}

/// <summary>
/// A choice answer whose labels are the members of <typeparamref name="TEnum"/>, each described by
/// a <see cref="System.ComponentModel.DescriptionAttribute"/> on the member.
/// </summary>
public sealed record ChoiceAnswer<TEnum> : IQuestionDeclaration
    where TEnum : struct, Enum
{
    public ChoiceAnswer() { }

    /// <summary>Declares the question. The answer properties stay unset until the response is bound.</summary>
    [SetsRequiredMembers]
    public ChoiceAnswer(string? instruction) => Instruction = instruction;

    /// <summary>What to ask. Falls back to a [Description] on <typeparamref name="TEnum"/> when null.</summary>
    public string? Instruction { get; init; }

    public required TEnum Choice { get; init; }

    public required decimal Confidence { get; init; }

    public required IReadOnlyDictionary<TEnum, decimal> Probabilities { get; init; }

    /// <summary>The option the model weighted most heavily. Usually equal to <see cref="Choice"/>.</summary>
    public TEnum HighestProbability => Probabilities.MaxBy(probability => probability.Value).Key;
}

/// <summary>
/// A score answer whose rubric is the members of <typeparamref name="TEnum"/> in declaration order,
/// lowest first, each described by a <see cref="System.ComponentModel.DescriptionAttribute"/>.
/// </summary>
public sealed record ScoreAnswer<TEnum> : IQuestionDeclaration
    where TEnum : struct, Enum
{
    public ScoreAnswer() { }

    /// <summary>Declares the question. The answer properties stay unset until the response is bound.</summary>
    [SetsRequiredMembers]
    public ScoreAnswer(string? instruction) => Instruction = instruction;

    /// <summary>What to ask. Falls back to a [Description] on <typeparamref name="TEnum"/> when null.</summary>
    public string? Instruction { get; init; }

    /// <summary>Position on the rubric. Fractional, because it is a probability-weighted mean.</summary>
    public required decimal Score { get; init; }

    public required decimal Confidence { get; init; }

    /// <summary>Each rubric option and the description that was sent for it.</summary>
    public required IReadOnlyDictionary<TEnum, string> Legend { get; init; }

    public required IReadOnlyDictionary<TEnum, decimal> Probabilities { get; init; }

    /// <summary>The rubric option the model weighted most heavily.</summary>
    public TEnum HighestProbability => Probabilities.MaxBy(probability => probability.Value).Key;
}

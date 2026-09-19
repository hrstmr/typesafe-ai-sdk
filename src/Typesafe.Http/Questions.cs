using System.Text.Json.Serialization;

namespace Typesafe.Http;

/// <summary>
/// A question sent as part of a SystemOne request. The concrete subtype determines both the
/// wire shape of its criteria and which answer type <see cref="SystemOneResult"/> hands back.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulQuestion), "noul")]
[JsonDerivedType(typeof(ChoiceQuestion), "choice")]
[JsonDerivedType(typeof(ScoreQuestion), "score")]
public abstract class Question
{
    private protected Question(string name, string? instructions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Instructions = instructions;
    }

    /// <summary>Key this question is filed under. Sent as the property name, so never inside the question itself.</summary>
    [JsonIgnore]
    public string Name { get; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; }

    public static NoulQuestion Noul(string name, string? instructions = null, NoulCriteria? criteria = null) => new(name, instructions, criteria);

    public static ChoiceQuestion Choice(string name, string? instructions, params string[] labels) =>
        new(name, instructions, labels.ToDictionary(label => label, _ => (string?)null));

    public static ChoiceQuestion Choice(string name, string? instructions, IReadOnlyDictionary<string, string?> criteria) =>
        new(name, instructions, criteria);

    public static ScoreQuestion Score(string name, string? instructions, params string[] rubric) => new(name, instructions, rubric);
}

/// <summary>Descriptions of what each outcome of a <see cref="NoulQuestion"/> means.</summary>
public sealed record NoulCriteria
{
    public NoulCriteria(string? isTrueWhen = null, string? isFalseWhen = null)
    {
        IsTrueWhen = isTrueWhen;
        IsFalseWhen = isFalseWhen;
    }

    [JsonPropertyName("true")]
    public string? IsTrueWhen { get; init; }

    [JsonPropertyName("false")]
    public string? IsFalseWhen { get; init; }
}

/// <summary>A yes/no question, answered as a probability rather than a boolean.</summary>
public sealed class NoulQuestion : Question
{
    internal NoulQuestion(string name, string? instructions, NoulCriteria? criteria)
        : base(name, instructions) => Criteria = criteria is { IsTrueWhen: null, IsFalseWhen: null } ? null : criteria;

    [JsonPropertyName("criteria")]
    public NoulCriteria? Criteria { get; }
}

/// <summary>A question answered by picking one of a set of named labels.</summary>
public sealed class ChoiceQuestion : Question
{
    internal ChoiceQuestion(string name, string? instructions, IReadOnlyDictionary<string, string?> criteria)
        : base(name, instructions)
    {
        if (criteria.Count == 0)
        {
            throw new ArgumentException("A choice question needs at least one label.", nameof(criteria));
        }

        Criteria = criteria;
    }

    /// <summary>Label to optional description. A null description lets the label speak for itself.</summary>
    [JsonPropertyName("criteria")]
    public IReadOnlyDictionary<string, string?> Criteria { get; }
}

/// <summary>A question answered on an ordered rubric, scored by the rubric entry's index.</summary>
public sealed class ScoreQuestion : Question
{
    internal ScoreQuestion(string name, string? instructions, IReadOnlyList<string> rubric)
        : base(name, instructions)
    {
        if (rubric.Count < 2)
        {
            throw new ArgumentException("A score question needs at least two rubric entries.", nameof(rubric));
        }

        Rubric = rubric;
    }

    /// <summary>Ordered rubric, lowest score first. The score is an index into this list.</summary>
    [JsonPropertyName("criteria")]
    public IReadOnlyList<string> Rubric { get; }
}

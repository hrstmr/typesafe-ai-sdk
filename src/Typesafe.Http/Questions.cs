namespace Typesafe.Http;

/// <summary>
/// A question sent as part of a SystemOne request. The concrete subtype determines
/// which answer type <see cref="SystemOneResult"/> hands back for it.
/// </summary>
public abstract class Question
{
    private protected Question(string name, object? instructions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Instructions = instructions;
    }

    /// <summary>Key this question is filed under in the request and the answers.</summary>
    public string Name { get; }

    public object? Instructions { get; }

    public abstract string Type { get; }

    internal abstract Dictionary<string, object?> ToPayload();

    public static NoulQuestion Noul(
        string name,
        object? instructions = null,
        object? whenTrue = null,
        object? whenFalse = null) => new(name, instructions, whenTrue, whenFalse);

    public static ChoiceQuestion Choice(string name, object? instructions, params string[] labels) =>
        new(name, instructions, labels.ToDictionary(label => label, _ => (object?)null));

    public static ChoiceQuestion Choice(
        string name,
        object? instructions,
        IReadOnlyDictionary<string, object?> criteria) => new(name, instructions, criteria);

    public static ScoreQuestion Score(string name, object? instructions, params string[] rubric) =>
        new(name, instructions, rubric);

    private protected Dictionary<string, object?> BasePayload()
    {
        var payload = new Dictionary<string, object?> { ["type"] = Type };

        if (Instructions is not null)
        {
            payload["instructions"] = Instructions;
        }

        return payload;
    }
}

/// <summary>A yes/no question, answered as a probability rather than a boolean.</summary>
public sealed class NoulQuestion : Question
{
    internal NoulQuestion(string name, object? instructions, object? whenTrue, object? whenFalse)
        : base(name, instructions)
    {
        WhenTrue = whenTrue;
        WhenFalse = whenFalse;
    }

    public object? WhenTrue { get; }

    public object? WhenFalse { get; }

    public override string Type => "noul";

    internal override Dictionary<string, object?> ToPayload()
    {
        var payload = BasePayload();

        if (WhenTrue is not null || WhenFalse is not null)
        {
            payload["criteria"] = new Dictionary<string, object?>
            {
                ["true"] = WhenTrue,
                ["false"] = WhenFalse,
            };
        }

        return payload;
    }
}

/// <summary>A question answered by picking one of a set of named labels.</summary>
public sealed class ChoiceQuestion : Question
{
    internal ChoiceQuestion(string name, object? instructions, IReadOnlyDictionary<string, object?> criteria)
        : base(name, instructions)
    {
        if (criteria.Count == 0)
        {
            throw new ArgumentException("A choice question needs at least one label.", nameof(criteria));
        }

        Criteria = criteria;
    }

    /// <summary>Label to optional description. A null description lets the label speak for itself.</summary>
    public IReadOnlyDictionary<string, object?> Criteria { get; }

    public override string Type => "choice";

    internal override Dictionary<string, object?> ToPayload()
    {
        var payload = BasePayload();
        payload["criteria"] = Criteria;
        return payload;
    }
}

/// <summary>A question answered on an ordered rubric, scored by the rubric entry's index.</summary>
public sealed class ScoreQuestion : Question
{
    internal ScoreQuestion(string name, object? instructions, IReadOnlyList<string> rubric)
        : base(name, instructions)
    {
        if (rubric.Count < 2)
        {
            throw new ArgumentException("A score question needs at least two rubric entries.", nameof(rubric));
        }

        Rubric = rubric;
    }

    /// <summary>Ordered rubric, lowest score first. The score is an index into this list.</summary>
    public IReadOnlyList<string> Rubric { get; }

    public override string Type => "score";

    internal override Dictionary<string, object?> ToPayload()
    {
        var payload = BasePayload();
        payload["criteria"] = Rubric;
        return payload;
    }
}

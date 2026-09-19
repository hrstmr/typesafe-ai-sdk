namespace Typesafe.Http;

public abstract class Answer
{
    private protected Answer(string type) => Type = type;

    public string Type { get; }
}

/// <summary>
/// Answer to a <see cref="NoulQuestion"/>. <see cref="Noul"/> is a probability in [0, 1],
/// not a boolean — the model reports how strongly it leans yes.
/// </summary>
public sealed class NoulAnswer : Answer
{
    internal NoulAnswer(double noul)
        : base("noul") => Noul = noul;

    public double Noul { get; }
}

public sealed class ChoiceAnswer : Answer
{
    internal ChoiceAnswer(string choice, double confidence, IReadOnlyDictionary<string, double> probabilities)
        : base("choice")
    {
        Choice = choice;
        Confidence = confidence;
        Probabilities = probabilities;
    }

    /// <summary>The selected label, one of the keys of the question's criteria.</summary>
    public string Choice { get; }

    public double Confidence { get; }

    public IReadOnlyDictionary<string, double> Probabilities { get; }
}

public sealed class ScoreAnswer : Answer
{
    internal ScoreAnswer(
        double score,
        double confidence,
        IReadOnlyDictionary<int, string?> legend,
        IReadOnlyDictionary<int, double> probabilities)
        : base("score")
    {
        Score = score;
        Confidence = confidence;
        Legend = legend;
        Probabilities = probabilities;
    }

    /// <summary>Position on the rubric. Fractional, because it is a probability-weighted mean.</summary>
    public double Score { get; }

    public double Confidence { get; }

    /// <summary>Rubric index to its description, echoed back so a score can be reported in words.</summary>
    public IReadOnlyDictionary<int, string?> Legend { get; }

    public IReadOnlyDictionary<int, double> Probabilities { get; }
}

public sealed record Usage(int InputTokens, int OutputTokens);

/// <summary>
/// Answers to one SystemOne call. Index it with the same question objects that were asked:
/// each indexer overload is typed to its question, so the answer type is known at compile time.
/// </summary>
public sealed class SystemOneResult
{
    private readonly IReadOnlyDictionary<string, Answer> _answers;

    internal SystemOneResult(string model, Usage usage, IReadOnlyDictionary<string, Answer> answers)
    {
        Model = model;
        Usage = usage;
        _answers = answers;
    }

    public string Model { get; }

    public Usage Usage { get; }

    /// <summary>Answers keyed by question name, for when the question objects are not at hand.</summary>
    public IReadOnlyDictionary<string, Answer> Answers => _answers;

    public NoulAnswer this[NoulQuestion question] => Lookup<NoulAnswer>(question);

    public ChoiceAnswer this[ChoiceQuestion question] => Lookup<ChoiceAnswer>(question);

    public ScoreAnswer this[ScoreQuestion question] => Lookup<ScoreAnswer>(question);

    private TAnswer Lookup<TAnswer>(Question question)
        where TAnswer : Answer
    {
        ArgumentNullException.ThrowIfNull(question);

        if (!_answers.TryGetValue(question.Name, out var answer))
        {
            throw new KeyNotFoundException($"The response contained no answer named '{question.Name}'.");
        }

        return answer as TAnswer
            ?? throw new InvalidOperationException(
                $"Answer '{question.Name}' is a {answer.Type} answer, which does not match the question asked.");
    }
}

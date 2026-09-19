using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Typesafe.Http;

/// <summary>
/// Reads a questions record type — one property per question — into the questions to ask,
/// and binds the answers back onto a new instance of it.
/// </summary>
internal static class QuestionSchema
{
    private const BindingFlags EnumMembers = BindingFlags.Public | BindingFlags.Static;

    private static readonly MethodInfo ChoiceBinder = Method(nameof(BindChoice));
    private static readonly MethodInfo ScoreBinder = Method(nameof(BindScore));

    public static IReadOnlyList<Question> Build(Type questionsType) => [.. Members(questionsType).Select(BuildQuestion)];

    public static object Bind(Type questionsType, IReadOnlyDictionary<string, Answer> answers)
    {
        var members = Members(questionsType);
        var values = new object?[members.Count];

        for (var index = 0; index < members.Count; index++)
        {
            var member = members[index];

            if (!answers.TryGetValue(member.Name, out var answer))
            {
                throw new TypeSafeException($"The response contained no answer named '{member.Name}'.");
            }

            values[index] = BindAnswer(member, answer);
        }

        return Activator.CreateInstance(questionsType, values)
            ?? throw new TypeSafeException($"Could not construct {questionsType.Name} from the response.");
    }

    private sealed record Member(string Name, Type Type, string? Instructions);

    private static IReadOnlyList<Member> Members(Type questionsType)
    {
        if (questionsType.IsAssignableTo(typeof(IEnumerable)))
        {
            throw new TypeSafeException(
                $"{questionsType.Name} is a collection. Pass a questions record to this overload, or a collection of "
                    + $"{nameof(Question)} objects to the overload that takes one."
            );
        }

        var parameters =
            questionsType.GetConstructors().OrderByDescending(constructor => constructor.GetParameters().Length).FirstOrDefault()?.GetParameters()
            ?? throw new TypeSafeException($"{questionsType.Name} has no public constructor to read questions from.");

        if (parameters.Length == 0)
        {
            throw new TypeSafeException($"{questionsType.Name} declares no questions.");
        }

        return
        [
            .. parameters.Select(parameter => new Member(
                parameter.Name!,
                parameter.ParameterType,
                Describe(questionsType.GetProperty(parameter.Name!))
                    ?? Describe(parameter)
                    ?? Describe(EnumOf(parameter.ParameterType, typeof(ChoiceAnswer<>)) ?? EnumOf(parameter.ParameterType, typeof(ScoreAnswer<>)))
            )),
        ];
    }

    private static Question BuildQuestion(Member member)
    {
        if (member.Type == typeof(NoulAnswer))
        {
            return Question.Noul(member.Name, member.Instructions);
        }

        if (EnumOf(member.Type, typeof(ChoiceAnswer<>)) is { } choiceEnum)
        {
            return Question.Choice(
                member.Name,
                member.Instructions,
                Options(choiceEnum).ToDictionary(option => option.Name, option => option.Description)
            );
        }

        if (EnumOf(member.Type, typeof(ScoreAnswer<>)) is { } scoreEnum)
        {
            return Question.Score(member.Name, member.Instructions, [.. Options(scoreEnum).Select(option => option.Description ?? option.Name)]);
        }

        throw new TypeSafeException(
            $"Question '{member.Name}' is declared as {member.Type.Name}. It must be "
                + $"{nameof(NoulAnswer)}, {nameof(ChoiceAnswer)}<TEnum> or {nameof(ScoreAnswer)}<TEnum>."
        );
    }

    private static object BindAnswer(Member member, Answer answer)
    {
        if (member.Type == typeof(NoulAnswer))
        {
            return answer as NoulAnswer ?? throw Mismatch(member, answer);
        }

        if (EnumOf(member.Type, typeof(ChoiceAnswer<>)) is { } choiceEnum)
        {
            return Invoke(ChoiceBinder, choiceEnum, answer as ChoiceAnswer ?? throw Mismatch(member, answer));
        }

        if (EnumOf(member.Type, typeof(ScoreAnswer<>)) is { } scoreEnum)
        {
            return Invoke(ScoreBinder, scoreEnum, answer as ScoreAnswer ?? throw Mismatch(member, answer));
        }

        throw Mismatch(member, answer);
    }

    // Reflection wraps anything the binder throws in a TargetInvocationException, which would
    // bury the message explaining what was actually wrong with the response.
    private static object Invoke(MethodInfo binder, Type enumType, Answer wire)
    {
        try
        {
            return binder.MakeGenericMethod(enumType).Invoke(null, [wire])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static ChoiceAnswer<TEnum> BindChoice<TEnum>(ChoiceAnswer wire)
        where TEnum : struct, Enum =>
        new()
        {
            Choice = ParseLabel<TEnum>(wire.Choice),
            Confidence = wire.Confidence,
            Probabilities = wire.Probabilities.ToDictionary(entry => ParseLabel<TEnum>(entry.Key), entry => entry.Value),
        };

    private static ScoreAnswer<TEnum> BindScore<TEnum>(ScoreAnswer wire)
        where TEnum : struct, Enum
    {
        // The rubric was sent in declaration order, so the API's indices are positions in that
        // list — not the enum's underlying values, which may be sparse.
        var declared = Declared<TEnum>();

        TEnum At(int index) =>
            index >= 0 && index < declared.Count
                ? declared[index]
                : throw new TypeSafeException($"The API returned rubric index {index}, which is outside {typeof(TEnum).Name}.");

        return new ScoreAnswer<TEnum>
        {
            Score = wire.Score,
            Confidence = wire.Confidence,
            Legend = wire.Legend.ToDictionary(entry => At(entry.Key), entry => entry.Value),
            Probabilities = wire.Probabilities.ToDictionary(entry => At(entry.Key), entry => entry.Value),
        };
    }

    private static IReadOnlyList<TEnum> Declared<TEnum>()
        where TEnum : struct, Enum => [.. typeof(TEnum).GetFields(EnumMembers).Select(field => (TEnum)field.GetValue(null)!)];

    private static IReadOnlyList<(string Name, string? Description)> Options(Type enumType) =>
        [.. enumType.GetFields(EnumMembers).Select(field => (field.Name, Describe(field)))];

    private static string? Describe(MemberInfo? member) => member?.GetCustomAttribute<DescriptionAttribute>()?.Description;

    private static string? Describe(ParameterInfo parameter) => parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;

    private static Type? EnumOf(Type? type, Type openGeneric) =>
        type is { IsGenericType: true } && type.GetGenericTypeDefinition() == openGeneric ? type.GetGenericArguments()[0] : null;

    private static TEnum ParseLabel<TEnum>(string label)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(label, ignoreCase: true, out var value)
            ? value
            : throw new TypeSafeException($"The API answered '{label}', which is not a member of {typeof(TEnum).Name}.");

    private static MethodInfo Method(string name) => typeof(QuestionSchema).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static TypeSafeException Mismatch(Member member, Answer answer) =>
        new($"Question '{member.Name}' is declared as {member.Type.Name} but the API answered with {answer.GetType().Name}.");
}

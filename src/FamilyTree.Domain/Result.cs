namespace FamilyTree.Domain;

/// <summary>
/// The outcome of something that can be refused for an expected reason, such as invalid
/// input. Rules break often enough in normal use that they aren't exceptions. Errors are
/// keyed by the field they concern, so callers can show each one next to that field.
/// </summary>
public sealed class Result<T>
{
    private readonly T? value;

    private Result(T? value, IReadOnlyDictionary<string, string[]> errors)
    {
        this.value = value;
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public bool IsSuccess => Errors.Count == 0;

    public T Value => IsSuccess ? value! : throw new InvalidOperationException("A failed result has no value.");

    public static Result<T> Success(T value) => new(value, new Dictionary<string, string[]>());

    public static Result<T> Failure(IReadOnlyDictionary<string, string[]> errors) => new(default, errors);
}

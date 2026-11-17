namespace SnackCounter.Domain.Common;

public enum ErrorType { Validation, NotFound, Conflict, BusinessRule, Forbidden }

/// <summary>An expected, business-level failure. Exceptions are reserved for programming errors.</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Rule(string code, string message) => new(code, message, ErrorType.BusinessRule);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}

public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => !IsSuccess;

    public static Result Ok() => new(null);
    public static Result Fail(Error error) => new(error);

    public static implicit operator Result(Error error) => Fail(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error) : base(error) => _value = value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result has no value: {Error!.Code}");

    public static Result<T> Success(T value) => new(value, null);
    public static new Result<T> Fail(Error error) => new(default, error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Fail(error);
}

/// <summary>Result type for commands that return nothing.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value;
}

using SnackCounter.Domain.Common;

namespace SnackCounter.Api.Http;

/// <summary>
/// The signed-in employee. Demo: taken from the X-Employee-Id header (the web app's "act as" picker).
/// Production: read the employee id claim from the company SSO (OIDC) token instead. Only this type changes.
/// </summary>
public readonly record struct Me(EmployeeId Id)
{
    public const string Header = "X-Employee-Id";

    public static ValueTask<Me> BindAsync(HttpContext context)
    {
        var raw = context.Request.Headers[Header].ToString();
        return Guid.TryParse(raw, out var id)
            ? ValueTask.FromResult(new Me(new EmployeeId(id)))
            : throw new BadHttpRequestException($"Send the {Header} header to say who you are.", StatusCodes.Status401Unauthorized);
    }
}

public static class ResultHttpExtensions
{
    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult>? onSuccess = null) =>
        result.IsSuccess ? onSuccess?.Invoke(result.Value) ?? TypedResults.Ok(result.Value) : result.Error!.ToProblem();

    /// <summary>RFC 7807 problem details. The stable errorCode lets the UI and chat show friendly text.</summary>
    public static IResult ToProblem(this Error error) =>
        TypedResults.Problem(
            statusCode: error.Type switch
            {
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
                ErrorType.Forbidden => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status500InternalServerError,
            },
            title: error.Message,
            extensions: new Dictionary<string, object?> { ["errorCode"] = error.Code });
}

/// <summary>Dev/demo clock that can be moved forward, so a 2-hour offer timeout can be shown live.</summary>
public sealed class AdjustableTimeProvider : TimeProvider
{
    private long _offsetTicks;

    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Offset;

    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        Interlocked.Add(ref _offsetTicks, by.Ticks);
    }
}

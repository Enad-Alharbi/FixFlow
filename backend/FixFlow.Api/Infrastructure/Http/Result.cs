namespace FixFlow.Api.Infrastructure.Http;

/// <summary>
/// An expected failure from a feature service: the HTTP status to answer with, a stable error code (localized by the
/// frontend), optional per-field validation codes, and optional extra fields (e.g. lockoutEnd).
/// </summary>
public record ServiceError(
    int Status,
    string Code,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    IReadOnlyDictionary<string, object?>? Extensions = null)
{
    public static ServiceError NotFound(string code = "resource.not_found") => new(StatusCodes.Status404NotFound, code);
    public static ServiceError Conflict(string code) => new(StatusCodes.Status409Conflict, code);
    public static ServiceError Concurrency() => new(StatusCodes.Status409Conflict, "concurrency.conflict");

    /// <summary>400 with per-field codes: { "email": ["user.email_taken"] }.</summary>
    public static ServiceError Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new(StatusCodes.Status400BadRequest, "validation.failed", errors);
}

/// <summary>Either a value or a <see cref="ServiceError"/>. Services return it; controllers turn it into HTTP.</summary>
public record Result<T>(T? Value, ServiceError? Error)
{
    public bool IsSuccess => Error is null;

    // Lets a service simply `return value;` or `return ServiceError.NotFound();`.
    public static implicit operator Result<T>(T value) => new(value, null);
    public static implicit operator Result<T>(ServiceError error) => new(default, error);
}

/// <summary>Collects per-field validation codes; empty means valid.</summary>
public class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = new();

    public bool Any => _errors.Count > 0;

    public FieldErrors Add(string field, string code)
    {
        if (!_errors.TryGetValue(field, out var codes)) _errors[field] = codes = [];
        if (!codes.Contains(code)) codes.Add(code);
        return this;
    }

    public ServiceError ToError() => ServiceError.Validation(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
}

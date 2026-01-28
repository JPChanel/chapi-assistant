namespace Chapi.Domain.Common;

/// <summary>
/// Representa el resultado de una operación que puede fallar.
/// Usa este patrón en lugar de excepciones para flujo de control.
/// </summary>
public class Result
{
    public bool IsSuccess { get; protected set; }
    public string Error { get; protected set; } = string.Empty;

    public static Result Success() => new() { IsSuccess = true };
    public static Result Fail(string error) => new() { IsSuccess = false, Error = error };
}

/// <summary>
/// Resultado con datos de retorno.
/// </summary>
public class Result<T> : Result
{
    public T? Data { get; set; }

    public static Result<T> Success(T data) => new() { IsSuccess = true, Data = data };
    public new static Result<T> Fail(string error) => new() { IsSuccess = false, Error = error };
}

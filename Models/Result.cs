using System.Net;

namespace SearchOrchestrator.Models;

public class Result
{
    public bool IsSuccess { get; }
    public string? Error { get; }
    public HttpStatusCode? StatusCode { get; }

    protected Result(bool isSuccess, string? error, HttpStatusCode? statusCode)
    {
        IsSuccess = isSuccess;
        Error = error;
        StatusCode = statusCode;
    }

    public static Result Success() => new(true, null, null);
    public static Result Failure(string error, HttpStatusCode? statusCode = null) => new(false, error, statusCode);
}

public class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool isSuccess, T? value, string? error, HttpStatusCode? statusCode)
        : base(isSuccess, error, statusCode)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new(true, value, null, null);
    public new static Result<T> Failure(string error, HttpStatusCode? statusCode = null) => new(false, default, error, statusCode);
}

namespace SearchOrchestrator.Middleware;

/// <summary>
/// Middleware that ensures every request has a correlation ID.
/// If the client sends X-Correlation-Id, it is reused; otherwise a new one is generated.
/// The correlation ID is available via <see cref="CorrelationIdAccessor"/> and returned in the response.
/// </summary>
public class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, CorrelationIdAccessor accessor)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault()
                            ?? Guid.NewGuid().ToString("N");

        accessor.CorrelationId = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (context.RequestServices.GetRequiredService<ILoggerFactory>()
                   .CreateLogger("CorrelationId")
                   .BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}

/// <summary>
/// Scoped service that holds the correlation ID for the current request.
/// </summary>
public class CorrelationIdAccessor
{
    public string CorrelationId { get; set; } = string.Empty;
}

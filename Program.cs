using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Prometheus;
using SearchOrchestrator.Configuration;
using SearchOrchestrator.Middleware;
using SearchOrchestrator.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SearchEngineOptions>(
    builder.Configuration.GetSection(SearchEngineOptions.SectionName));
builder.Services.Configure<PolicySettings>(
    builder.Configuration.GetSection(PolicySettings.SectionName));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<CorrelationIdAccessor>();

builder.Services.AddSingleton<IIndexingSourceRepository, InMemoryIndexingSourceRepository>();
builder.Services.AddSingleton<IIndexingTaskRepository, InMemoryIndexingTaskRepository>();

builder.Services.AddSingleton<IPolicyService, PolicyService>();

builder.Services.AddHttpClient(HttpClientNames.SearchEngine, (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<SearchEngineOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

builder.Services.AddSingleton<ISearchEngineClient, HttpSearchEngineClient>();

builder.Services.AddSingleton<IndexingTaskQueue>(sp =>
{
    var options = sp.GetRequiredService<IOptions<SearchEngineOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<IndexingTaskQueue>>();
    return new IndexingTaskQueue(options.QueueCapacity, logger);
});

builder.Services.AddSingleton<IndexingOrchestrator>();

builder.Services.AddHostedService<IndexingBackgroundService>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        var exception = exceptionFeature?.Error;

        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(exception, "Unhandled exception occurred");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(new
        {
            error = "An internal server error occurred",
            traceId = context.TraceIdentifier
        });
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapMetrics();

app.Run();

using Prometheus;

namespace SearchOrchestrator.Services;

public static class SearchEngineMetrics
{
    public static readonly Counter HttpRequestsTotal = Metrics.CreateCounter(
        "search_engine_http_requests_total",
        "Total HTTP requests to the external search engine",
        new CounterConfiguration { LabelNames = ["endpoint", "status_code"] });

    public static readonly Counter HttpErrorsTotal = Metrics.CreateCounter(
        "search_engine_http_errors_total",
        "Total HTTP errors from the external search engine",
        new CounterConfiguration { LabelNames = ["endpoint", "status_code"] });
}

namespace SearchOrchestrator.Configuration;

public class PollyRetrySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Comma-separated delays in seconds between retries. E.g. "1,2,3"
    /// </summary>
    public string RetryDelays { get; set; } = "1,2,3";
}

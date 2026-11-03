namespace DatasetHub;
public sealed class ReliabilityOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxRetries { get; set; } = 2;
    public int RetryBaseSeconds { get; set; } = 10;
    public int RetryMaxSeconds { get; set; } = 120;
    public int NoProgressTimeoutSeconds { get; set; } = 300;
    public int JobTimeoutSeconds { get; set; } = 7200;
    public int FullScanIntervalHours { get; set; } = 24;
    public void Validate()
    {
        if (MaxRetries is < 0 or > 10 || RetryBaseSeconds is < 1 or > 3600 || RetryMaxSeconds < RetryBaseSeconds || RetryMaxSeconds > 86400 ||
            NoProgressTimeoutSeconds is < 1 or > 86400 || JobTimeoutSeconds < NoProgressTimeoutSeconds || JobTimeoutSeconds > 604800 || FullScanIntervalHours is < 1 or > 720)
            throw new ArgumentException("Invalid reliability retry/timeout/full-scan settings.");
    }
    public TimeSpan RetryDelay(int attempt) => TimeSpan.FromSeconds(Math.Min(RetryMaxSeconds, RetryBaseSeconds * Math.Pow(2, attempt)) * (0.8 + Random.Shared.NextDouble() * 0.4));
}

namespace Flux.Ui;

/// <summary>Human-friendly byte sizes and transfer rates.</summary>
public static class ByteFormat
{
    public static string Bytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} bytes",
    };

    public static string Rate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024.0 * 1024 * 1024 => $"{bytesPerSecond / (1024.0 * 1024 * 1024):F2} GB/s",
        >= 1024.0 * 1024 => $"{bytesPerSecond / (1024.0 * 1024):F1} MB/s",
        >= 1024.0 => $"{bytesPerSecond / 1024.0:F1} KB/s",
        _ => $"{bytesPerSecond:0} B/s",
    };
}

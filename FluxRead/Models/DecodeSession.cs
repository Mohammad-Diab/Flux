using System.Text.Json.Serialization;

namespace FluxRead.Models;

/// <summary>
/// Represents a decoder session with progress tracking.
/// </summary>
public class DecodeSession
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("inputMode")]
public InputMode InputMode { get; set; }

    [JsonPropertyName("sourceFolder")]
    public string? SourceFolder { get; set; }

    [JsonPropertyName("outputPath")]
    public string? OutputPath { get; set; }

    [JsonPropertyName("progress")]
public DecodeProgress Progress { get; set; } = new();

    [JsonPropertyName("metadata")]
    public DecodedMetadata? Metadata { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();

    [JsonPropertyName("logs")]
  public List<string> Logs { get; set; } = new();
}

public class DecodeProgress
{
    [JsonPropertyName("totalFrames")]
    public int TotalFrames { get; set; }

    [JsonPropertyName("decodedFrames")]
    public int DecodedFrames { get; set; }

  [JsonPropertyName("failedFrames")]
    public int FailedFrames { get; set; }

    [JsonPropertyName("eccCorrections")]
    public int EccCorrections { get; set; }

    [JsonPropertyName("crcFailures")]
    public int CrcFailures { get; set; }

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("endTime")]
    public DateTime? EndTime { get; set; }
}

public class DecodedMetadata
{
    public byte[] Sha256 { get; set; } = Array.Empty<byte>();
    public int TileSize { get; set; }
    public int EccLevel { get; set; }
    public string PayloadType { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public long OriginalLength { get; set; }
}

public enum InputMode
{
    Folder,
    ScreenCapture
}

public enum DecodeStatus
{
    Idle,
    Scanning,
    Decoding,
    Assembling,
    Verifying,
 Completed,
    Failed
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluxCast.Models;

/// <summary>
/// Represents the complete session state for encoding.
/// </summary>
public class SessionData
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("status")]
    public string Status { get; set; } = "encoding";

    [JsonPropertyName("encodingConfig")]
    public EncodingConfig EncodingConfig { get; set; } = new();

    [JsonPropertyName("progress")]
    public ProgressInfo Progress { get; set; } = new();

    [JsonPropertyName("frames")]
    public FramesInfo Frames { get; set; } = new();

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}

public class EncodingConfig
{
    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = string.Empty;

    [JsonPropertyName("isFolder")]
    public bool IsFolder { get; set; }

  [JsonPropertyName("tileSize")]
    public int TileSize { get; set; }

    [JsonPropertyName("eccLevel")]
    public int EccLevel { get; set; }

    [JsonPropertyName("enableCompression")]
    public bool EnableCompression { get; set; }

    [JsonPropertyName("frameWidthPx")]
    public int FrameWidthPx { get; set; }

    [JsonPropertyName("frameHeightPx")]
    public int FrameHeightPx { get; set; }
}

public class ProgressInfo
{
    [JsonPropertyName("totalFrames")]
    public int TotalFrames { get; set; }

    [JsonPropertyName("encodedFrames")]
    public int EncodedFrames { get; set; }

    [JsonPropertyName("currentFrameIndex")]
    public int CurrentFrameIndex { get; set; }

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("lastUpdateTime")]
    public DateTime LastUpdateTime { get; set; } = DateTime.UtcNow;
}

public class FramesInfo
{
 [JsonPropertyName("storedInMemory")]
    public bool StoredInMemory { get; set; }

    [JsonPropertyName("tempFolder")]
  public string TempFolder { get; set; } = string.Empty;

    [JsonPropertyName("frameFiles")]
 public List<FrameFileInfo> FrameFiles { get; set; } = new();
}

public class FrameFileInfo
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

using FluxCore.Ecc;
using FluxCore.Framing;
using FluxCore.Hashing;
using FluxCore.Imaging;
using SkiaSharp;

namespace FluxCore.Decoding;

/// <summary>
/// Capture-tolerant frame decoder: registers the frame via its corner finder patterns,
/// resolves orientation with the timing pattern, samples and classifies every tile, then
/// recovers header and payload with full CRC verification. When recovery fails on a capture
/// with many low-confidence tiles, the failure is reported as CaptureUnstable so callers know
/// to wait for the remote-desktop codec to converge and recapture. Also offers a cheap probe
/// for click-confirmation polling.
/// </summary>
public sealed class FrameDecoder
{
    private const double MinTimingMatchRatio = 0.95;
    private const double MaxLowConfidenceFraction = 0.08;

    private readonly PaletteClassifier _classifier;

    /// <summary>Creates a decoder for the given data/header tile palette.</summary>
    public FrameDecoder(ColorMap colorMap)
    {
        ArgumentNullException.ThrowIfNull(colorMap);
        _classifier = new PaletteClassifier(colorMap);
    }

    /// <summary>
    /// Decodes one captured frame image.
    /// </summary>
    /// <param name="capture">Captured image containing the frame (any scale/offset).</param>
    /// <param name="previousFrameId">Frame id of the last successful decode; a matching id short-circuits to SameFrameAsBefore.</param>
    /// <param name="expectedFrameId">Frame id the caller expects; any other decoded id returns WrongFrame.</param>
    public FrameDecodeResult Decode(SKBitmap capture, uint? previousFrameId = null, uint? expectedFrameId = null)
    {
        ArgumentNullException.ThrowIfNull(capture);

        if (!TryRegister(capture, out var registration))
            return registration.FailureResult!;

        var sampler = registration.Sampler!;
        var samples = sampler.SampleAll();

        var (classifications, lowConfidence, meanDistance, maxDistance) = ClassifyDataTiles(samples);
        var baseDiagnostics = new DecodeDiagnostics
        {
            FinderPoints = registration.Corners,
            TimingMatchRatio = registration.TimingMatchRatio,
            LowConfidenceDataTiles = lowConfidence,
            MeanPaletteDistance = meanDistance,
            MaxPaletteDistance = maxDistance,
        };

        bool unstable = lowConfidence > FrameFormat.DataTileCount * MaxLowConfidenceFraction;

        if (!TryRecoverHeader(samples, out var header, out int copiesAgreeing))
        {
            return Undecodable(
                unstable ? DecodeFailureReason.CaptureUnstable : DecodeFailureReason.HeaderUnreadable,
                baseDiagnostics);
        }

        var diagnostics = With(baseDiagnostics, copiesAgreeing: copiesAgreeing);

        if (previousFrameId.HasValue && header.FrameId == previousFrameId.Value)
        {
            return new FrameDecodeResult
            {
                Status = DecodeStatus.SameFrameAsBefore,
                Header = header,
                Diagnostics = diagnostics,
            };
        }

        if (expectedFrameId.HasValue && header.FrameId != expectedFrameId.Value)
        {
            return new FrameDecodeResult
            {
                Status = DecodeStatus.WrongFrame,
                Header = header,
                Diagnostics = diagnostics,
            };
        }

        var codewords = new byte[ReedSolomonBlockCodec.EncodedFrameLength];
        for (int t = 0; t < FrameFormat.DataTileCount; t++)
        {
            var (codeword, symbol) = FrameFormat.ToCodewordSymbol(t);
            codewords[codeword * FrameFormat.CodewordLength + symbol] = classifications[t];
        }

        var payload = new byte[header.EccLevel.PayloadBytesPerFrame()];
        if (!ReedSolomonBlockCodec.TryDecodePayload(codewords, header.EccLevel, payload, out int correctedErrors))
        {
            return Undecodable(
                unstable ? DecodeFailureReason.CaptureUnstable : DecodeFailureReason.EccFailure,
                With(diagnostics, correctedErrors: correctedErrors));
        }

        diagnostics = With(diagnostics, correctedErrors: correctedErrors);

        var realPayload = payload[..header.PayloadLength];
        if (!Crc32Helper.Verify(realPayload, header.PayloadCrc32))
        {
            return Undecodable(DecodeFailureReason.CrcMismatch, diagnostics);
        }

        return new FrameDecodeResult
        {
            Status = DecodeStatus.Success,
            Header = header,
            Payload = realPayload,
            Diagnostics = diagnostics,
        };
    }

    /// <summary>
    /// Decodes the metadata frame (frame 0). The caller identifies frame 0 by position (first
    /// frame). Uses cube-corner threshold classification independent of the payload palette, so
    /// the frame is decodable even before the palette is known. Returns the serialized metadata
    /// bytes as the payload for the caller to deserialize.
    /// </summary>
    /// <param name="capture">Captured image of frame 0.</param>
    public FrameDecodeResult DecodeMetadataFrame(SKBitmap capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        if (!TryRegister(capture, out var registration))
            return registration.FailureResult!;

        var sampler = registration.Sampler!;
        var diagnostics = new DecodeDiagnostics
        {
            FinderPoints = registration.Corners,
            TimingMatchRatio = registration.TimingMatchRatio,
        };

        var stream = new byte[FrameFormat.MetadataEncodedBytes];
        var positions = FrameFormat.MetadataFrameTiles;
        for (int t = 0; t < FrameFormat.MetadataTilesUsed; t++)
        {
            var (x, y) = positions[t];
            var sample = sampler.Sample(x, y);
            int value = CubeCornerColors.Classify(sample.R, sample.G, sample.B);
            WriteCubeCornerBits(stream, t, value);
        }

        var content = new byte[FrameFormat.MetadataContentBytes];
        int correctedErrors = 0;
        Span<byte> block = stackalloc byte[FrameFormat.CodewordLength];

        for (int c = 0; c < FrameFormat.MetadataCodewordCount; c++)
        {
            for (int s = 0; s < FrameFormat.CodewordLength; s++)
            {
                block[s] = stream[s * FrameFormat.MetadataCodewordCount + c];
            }

            if (!ReedSolomonBlockCodec.TryDecodeBlock(
                    block, FrameFormat.MetadataParitySymbols,
                    content.AsSpan(c * FrameFormat.MetadataCodewordDataBytes, FrameFormat.MetadataCodewordDataBytes),
                    out int corrected))
            {
                return Undecodable(DecodeFailureReason.EccFailure, With(diagnostics, correctedErrors: correctedErrors));
            }

            correctedErrors += corrected;
        }

        var header = new FrameHeader(0, 0, 0, 0, EccLevel.Max, isMetadataFrame: true);
        return new FrameDecodeResult
        {
            Status = DecodeStatus.Success,
            Header = header,
            Payload = content,
            Diagnostics = With(diagnostics, correctedErrors: correctedErrors),
        };
    }

    private static void WriteCubeCornerBits(byte[] stream, int tileIndex, int value)
    {
        int baseBit = tileIndex * 3;
        for (int k = 0; k < 3; k++)
        {
            int bit = (value >> (2 - k)) & 1;
            int globalBit = baseBit + k;
            if (bit != 0)
                stream[globalBit >> 3] |= (byte)(1 << (7 - (globalBit & 7)));
        }
    }

    /// <summary>
    /// Cheap poll: registration, beacon parity, and header only. No payload sampling or ECC.
    /// </summary>
    /// <param name="capture">Captured image.</param>
    public ProbeResult TryProbe(SKBitmap capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        if (!TryRegister(capture, out var registration))
            return new ProbeResult { Registered = false };

        var sampler = registration.Sampler!;

        double beaconLuma = 0;
        foreach (var (x, y) in FrameFormat.BeaconTiles)
        {
            beaconLuma += sampler.Sample(x, y).Luma;
        }

        bool beaconIsBlack = beaconLuma / FrameFormat.BeaconTiles.Count < registration.Luma!.Threshold;

        FrameHeader? header = null;
        if (TryRecoverHeader(sampler, out var recovered, out _))
            header = recovered;

        return new ProbeResult
        {
            Registered = true,
            BeaconIsBlack = beaconIsBlack,
            Header = header,
        };
    }

    private bool TryRegister(SKBitmap capture, out Registration registration)
    {
        var luma = LumaImage.FromBitmap(capture);

        if (!FiducialDetector.TryDetect(luma, out var corners))
        {
            registration = Registration.Failed(
                Undecodable(DecodeFailureReason.FiducialsNotFound, new DecodeDiagnostics()));
            return false;
        }

        var detected = corners.Select(c => (c.X, c.Y)).ToArray();
        (double X, double Y)[][] assignments =
        [
            detected,
            [detected[3], detected[2], detected[1], detected[0]],
        ];

        double bestRatio = 0;
        foreach (var assignment in assignments)
        {
            var homography = Homography.FromPoints(FrameFormat.FinderCentersTiles.ToArray(), assignment);
            var sampler = new TileSampler(capture, homography);

            double ratio = MeasureTimingMatch(sampler, luma.Threshold);
            bestRatio = Math.Max(bestRatio, ratio);

            if (ratio >= MinTimingMatchRatio)
            {
                registration = new Registration
                {
                    Sampler = sampler,
                    Luma = luma,
                    Corners = corners,
                    TimingMatchRatio = ratio,
                };
                return true;
            }
        }

        registration = Registration.Failed(Undecodable(
            DecodeFailureReason.GeometryInvalid,
            new DecodeDiagnostics { FinderPoints = corners, TimingMatchRatio = bestRatio }));
        return false;
    }

    private static double MeasureTimingMatch(TileSampler sampler, byte threshold)
    {
        int matches = 0;
        int total = 0;

        for (int y = 0; y < FrameFormat.GridHeightTiles; y++)
        {
            for (int x = 0; x < FrameFormat.GridWidthTiles; x++)
            {
                if (FrameFormat.GetRole(x, y) != TileRole.Timing)
                    continue;

                bool expectedBlack = FrameFormat.IsStructuralBlack(x, y);
                bool sampledBlack = sampler.Sample(x, y).Luma < threshold;
                if (expectedBlack == sampledBlack)
                    matches++;
                total++;
            }
        }

        return total == 0 ? 0 : (double)matches / total;
    }

    private (byte[] Values, int LowConfidence, double MeanDistance, double MaxDistance) ClassifyDataTiles(
        TileSample[] samples)
    {
        var values = new byte[FrameFormat.DataTileCount];
        int lowConfidence = 0;
        double totalDistance = 0;
        double maxDistance = 0;

        for (int t = 0; t < FrameFormat.DataTileCount; t++)
        {
            var (x, y) = FrameFormat.DataTiles[t];
            var sample = samples[y * FrameFormat.GridWidthTiles + x];
            var classification = _classifier.Classify(sample.R, sample.G, sample.B);

            values[t] = classification.PaletteIndex;
            totalDistance += classification.NearestDistance;
            maxDistance = Math.Max(maxDistance, classification.NearestDistance);
            if (classification.IsLowConfidence)
                lowConfidence++;
        }

        return (values, lowConfidence, totalDistance / FrameFormat.DataTileCount, maxDistance);
    }

    private bool TryRecoverHeader(TileSample[] samples, out FrameHeader header, out int copiesAgreeing) =>
        TryRecoverHeader((x, y) => samples[y * FrameFormat.GridWidthTiles + x], out header, out copiesAgreeing);

    private bool TryRecoverHeader(TileSampler sampler, out FrameHeader header, out int copiesAgreeing) =>
        TryRecoverHeader(sampler.Sample, out header, out copiesAgreeing);

    private bool TryRecoverHeader(Func<int, int, TileSample> sampleAt, out FrameHeader header, out int copiesAgreeing)
    {
        var candidates = new List<FrameHeader>();

        for (int copy = 0; copy < FrameFormat.HeaderCopyCount; copy++)
        {
            var symbols = new byte[FrameFormat.HeaderCopyLength];
            var positions = FrameFormat.GetHeaderCopyTiles(copy);
            for (int i = 0; i < symbols.Length; i++)
            {
                var (x, y) = positions[i];
                var sample = sampleAt(x, y);
                symbols[i] = _classifier.Classify(sample.R, sample.G, sample.B).PaletteIndex;
            }

            if (ReedSolomonBlockCodec.TryDecodeHeader(symbols, out var candidate))
                candidates.Add(candidate);
        }

        return VoteOnHeader(candidates, out header, out copiesAgreeing);
    }

    private static bool VoteOnHeader(List<FrameHeader> candidates, out FrameHeader header, out int copiesAgreeing)
    {
        foreach (var candidate in candidates)
        {
            int agreeing = candidates.Count(c => c == candidate);
            if (agreeing >= 2 && candidate.IsPlausible())
            {
                header = candidate;
                copiesAgreeing = agreeing;
                return true;
            }
        }

        foreach (var candidate in candidates)
        {
            if (candidate.IsPlausible())
            {
                header = candidate;
                copiesAgreeing = 1;
                return true;
            }
        }

        header = default;
        copiesAgreeing = 0;
        return false;
    }

    private static FrameDecodeResult Undecodable(DecodeFailureReason reason, DecodeDiagnostics diagnostics) =>
        new()
        {
            Status = DecodeStatus.Undecodable,
            FailureReason = reason,
            Diagnostics = diagnostics,
        };

    private static DecodeDiagnostics With(
        DecodeDiagnostics source, int? copiesAgreeing = null, int? correctedErrors = null) =>
        new()
        {
            FinderPoints = source.FinderPoints,
            TimingMatchRatio = source.TimingMatchRatio,
            LowConfidenceDataTiles = source.LowConfidenceDataTiles,
            MeanPaletteDistance = source.MeanPaletteDistance,
            MaxPaletteDistance = source.MaxPaletteDistance,
            HeaderCopiesAgreeing = copiesAgreeing ?? source.HeaderCopiesAgreeing,
            CorrectedErrors = correctedErrors ?? source.CorrectedErrors,
        };

    private sealed class Registration
    {
        public TileSampler? Sampler { get; init; }

        public LumaImage? Luma { get; init; }

        public FinderPoint[] Corners { get; init; } = [];

        public double TimingMatchRatio { get; init; }

        public FrameDecodeResult? FailureResult { get; init; }

        public static Registration Failed(FrameDecodeResult result) => new() { FailureResult = result };
    }
}

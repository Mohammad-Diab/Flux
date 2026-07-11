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
    /// <param name="bitsPerTile">Colour depth the payload was packed at; 8 is one palette byte per tile.</param>
    /// <param name="layout">Grid layout; defaults to the canonical 160×90.</param>
    public FrameDecodeResult Decode(
        SKBitmap capture, uint? previousFrameId = null, uint? expectedFrameId = null, int bitsPerTile = 8,
        FrameLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        layout ??= FrameLayout.Default;

        if (!TryRegister(capture, layout, out var registration))
            return registration.FailureResult!;

        var sampler = registration.Sampler!;
        var samples = sampler.SampleAll();

        var (classifications, lowConfidence, meanDistance, maxDistance) = ClassifyDataTiles(samples, layout);
        var baseDiagnostics = new DecodeDiagnostics
        {
            FinderPoints = registration.Corners,
            TimingMatchRatio = registration.TimingMatchRatio,
            LowConfidenceDataTiles = lowConfidence,
            MeanPaletteDistance = meanDistance,
            MaxPaletteDistance = maxDistance,
        };

        bool unstable = lowConfidence > layout.DataTileCount * MaxLowConfidenceFraction;

        if (!TryRecoverHeader(samples, layout, out var header, out int copiesAgreeing))
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

        if (!TryDecodePayloadTiles(classifications, header.EccLevel, bitsPerTile, out var payload, out int correctedErrors, layout))
        {
            return Undecodable(
                unstable ? DecodeFailureReason.CaptureUnstable : DecodeFailureReason.EccFailure,
                With(diagnostics, correctedErrors: correctedErrors));
        }

        diagnostics = With(diagnostics, correctedErrors: correctedErrors);

        // PayloadLength is bounded here (against the grid's capacity), not in the grid-agnostic header.
        if (header.PayloadLength > payload.Length)
            return Undecodable(DecodeFailureReason.HeaderUnreadable, diagnostics);

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

        // Frame 0 is always the canonical layout — the bootstrap anchor before the grid is known.
        if (!TryRegister(capture, FrameLayout.Default, out var registration))
            return registration.FailureResult!;

        var sampler = registration.Sampler!;
        var diagnostics = new DecodeDiagnostics
        {
            FinderPoints = registration.Corners,
            TimingMatchRatio = registration.TimingMatchRatio,
        };

        var positions = FrameFormat.MetadataFrameTiles;
        var tileValues = new ushort[FrameFormat.MetadataTilesUsed];
        for (int t = 0; t < FrameFormat.MetadataTilesUsed; t++)
        {
            var (x, y) = positions[t];
            var sample = sampler.Sample(x, y);
            tileValues[t] = (ushort)CubeCornerColors.Classify(sample.R, sample.G, sample.B);
        }

        var stream = TileBitPacker.Unpack(tileValues, CubeCornerColors.BitsPerTile, FrameFormat.MetadataEncodedBytes);

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

    /// <summary>
    /// Cheap poll: registration, beacon parity, and header only. No payload sampling or ECC.
    /// </summary>
    /// <param name="capture">Captured image.</param>
    /// <param name="layout">Grid layout; defaults to the canonical 160×90.</param>
    public ProbeResult TryProbe(SKBitmap capture, FrameLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(capture);
        layout ??= FrameLayout.Default;

        if (!TryRegister(capture, layout, out var registration))
            return new ProbeResult { Registered = false };

        var sampler = registration.Sampler!;

        double beaconLuma = 0;
        foreach (var (x, y) in layout.BeaconTiles)
        {
            beaconLuma += sampler.Sample(x, y).Luma;
        }

        bool beaconIsBlack = beaconLuma / layout.BeaconTiles.Count < registration.Luma!.Threshold;

        FrameHeader? header = null;
        if (TryRecoverHeader(sampler, layout, out var recovered, out _))
            header = recovered;

        return new ProbeResult
        {
            Registered = true,
            BeaconIsBlack = beaconIsBlack,
            Header = header,
        };
    }

    private bool TryRegister(SKBitmap capture, FrameLayout layout, out Registration registration)
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
            var homography = Homography.FromPoints(layout.FinderCentersTiles.ToArray(), assignment);
            var sampler = new TileSampler(capture, homography, layout);

            double ratio = MeasureTimingMatch(sampler, luma.Threshold, layout);
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

    private static double MeasureTimingMatch(TileSampler sampler, byte threshold, FrameLayout layout)
    {
        int matches = 0;
        int total = 0;

        for (int y = 0; y < layout.GridHeightTiles; y++)
        {
            for (int x = 0; x < layout.GridWidthTiles; x++)
            {
                if (layout.GetRole(x, y) != TileRole.Timing)
                    continue;

                bool expectedBlack = layout.IsStructuralBlack(x, y);
                bool sampledBlack = sampler.Sample(x, y).Luma < threshold;
                if (expectedBlack == sampledBlack)
                    matches++;
                total++;
            }
        }

        return total == 0 ? 0 : (double)matches / total;
    }

    private (ushort[] Values, int LowConfidence, double MeanDistance, double MaxDistance) ClassifyDataTiles(
        TileSample[] samples, FrameLayout layout)
    {
        var values = new ushort[layout.DataTileCount];
        int lowConfidence = 0;
        double totalDistance = 0;
        double maxDistance = 0;

        for (int t = 0; t < layout.DataTileCount; t++)
        {
            var (x, y) = layout.DataTiles[t];
            var sample = samples[y * layout.GridWidthTiles + x];
            var classification = _classifier.Classify(sample.R, sample.G, sample.B);

            values[t] = classification.PaletteIndex;
            totalDistance += classification.NearestDistance;
            maxDistance = Math.Max(maxDistance, classification.NearestDistance);
            if (classification.IsLowConfidence)
                lowConfidence++;
        }

        return (values, lowConfidence, totalDistance / layout.DataTileCount, maxDistance);
    }

    /// <summary>
    /// Recovers a payload frame from its classified data-tile values: unpacks the tiles at the
    /// colour depth, de-interleaves, and RS-decodes. The inverse of the encoder's payload packing.
    /// </summary>
    /// <param name="dataTileValues">Classified data-tile values in scan order (at least the tiles used at this depth).</param>
    /// <param name="eccLevel">ECC level the payload was encoded at.</param>
    /// <param name="bitsPerTile">Colour depth the payload was packed at.</param>
    /// <param name="payload">Recovered payload bytes (frame capacity at this level/depth).</param>
    /// <param name="correctedErrors">Total symbols corrected across all codewords.</param>
    /// <param name="layout">Grid layout; defaults to the canonical 160×90.</param>
    public static bool TryDecodePayloadTiles(
        ReadOnlySpan<ushort> dataTileValues, EccLevel eccLevel, int bitsPerTile,
        out byte[] payload, out int correctedErrors, FrameLayout? layout = null)
    {
        layout ??= FrameLayout.Default;
        int codewordCount = layout.CodewordsForBits(bitsPerTile);
        int encodedLength = codewordCount * FrameFormat.CodewordLength;
        int tilesUsed = TileBitPacker.TileCount(encodedLength, bitsPerTile);

        if (dataTileValues.Length < tilesUsed)
            throw new ArgumentException($"Need at least {tilesUsed} tile values, got {dataTileValues.Length}.", nameof(dataTileValues));

        var stream = TileBitPacker.Unpack(dataTileValues[..tilesUsed], bitsPerTile, encodedLength);

        var codewords = new byte[encodedLength];
        for (int c = 0; c < codewordCount; c++)
            for (int s = 0; s < FrameFormat.CodewordLength; s++)
                codewords[c * FrameFormat.CodewordLength + s] = stream[s * codewordCount + c];

        payload = new byte[eccLevel.PayloadBytesPerFrame(codewordCount)];
        return ReedSolomonBlockCodec.TryDecodePayload(codewords, eccLevel, payload, out correctedErrors, codewordCount);
    }

    private bool TryRecoverHeader(TileSample[] samples, FrameLayout layout, out FrameHeader header, out int copiesAgreeing) =>
        TryRecoverHeader((x, y) => samples[y * layout.GridWidthTiles + x], layout, out header, out copiesAgreeing);

    private bool TryRecoverHeader(TileSampler sampler, FrameLayout layout, out FrameHeader header, out int copiesAgreeing) =>
        TryRecoverHeader(sampler.Sample, layout, out header, out copiesAgreeing);

    private bool TryRecoverHeader(Func<int, int, TileSample> sampleAt, FrameLayout layout, out FrameHeader header, out int copiesAgreeing)
    {
        var candidates = new List<FrameHeader>();

        for (int copy = 0; copy < FrameFormat.HeaderCopyCount; copy++)
        {
            var symbols = new byte[FrameFormat.HeaderCopyLength];
            var positions = layout.GetHeaderCopyTiles(copy);
            for (int i = 0; i < symbols.Length; i++)
            {
                var (x, y) = positions[i];
                var sample = sampleAt(x, y);
                // Header symbols are RS bytes; palettes ≥256 only use indices 0-255 for the header.
                symbols[i] = (byte)_classifier.Classify(sample.R, sample.G, sample.B).PaletteIndex;
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

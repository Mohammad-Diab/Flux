using FluxCore.Imaging;
using Xunit;

namespace FluxCore.Tests.Imaging;

public class PaletteGeneratorTests
{
    private static readonly int[] SupportedCounts = [8, 16, 32, 64, 128, 256];

    [Theory]
    [InlineData(7)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(2048)]
    [InlineData(255)]
    public void Generate_InvalidCount_Throws(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PaletteGenerator.Generate(count));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void Generate_ProducesExactCount_UniqueNonWhiteColors(int count)
    {
        var palette = PaletteGenerator.Generate(count);

        Assert.Equal(count, palette.Colors.Length);
        Assert.Equal(count, palette.Colors.Distinct().Count());
        Assert.DoesNotContain(palette.Colors, c => c.IsWhite);
    }

    [Fact]
    public void Generate_IsDeterministic()
    {
        foreach (var count in SupportedCounts)
            Assert.Equal(PaletteGenerator.Generate(count).Colors, PaletteGenerator.Generate(count).Colors);
    }

    [Fact]
    public void Generate_AllSupportedCounts_StayAboveJpegSafeFloor()
    {
        // 36 is the minimum pairwise distance the degradation suite pins as JPEG-q85-safe.
        foreach (var count in SupportedCounts)
            Assert.True(PaletteGenerator.Generate(count).MinimumDistance >= 36 - 1e-6,
                $"{count} colours: min distance {PaletteGenerator.Generate(count).MinimumDistance} < 36");
    }

    [Fact]
    public void Generate_FewerColors_AreAtLeastAsRobust()
    {
        // Minimum distance is weakly decreasing as the colour count grows (denser = less robust).
        for (int i = 1; i < SupportedCounts.Length; i++)
        {
            double smaller = PaletteGenerator.Generate(SupportedCounts[i - 1]).MinimumDistance;
            double larger = PaletteGenerator.Generate(SupportedCounts[i]).MinimumDistance;
            Assert.True(smaller >= larger - 1e-6, $"{SupportedCounts[i - 1]} less robust than {SupportedCounts[i]}");
        }
    }

    [Fact]
    public void Generate_256_MinimumDistanceIs36()
    {
        Assert.Equal(36, PaletteGenerator.Generate(256).MinimumDistance, precision: 6);
    }

    [Fact]
    public void Generate_256_ReproducesHistoricalDefaultPalette()
    {
        // Independently rebuild the pre-generator 8×8×4 lattice and assert an exact match, so this
        // pins the palette even though ColorMap.Default now delegates to the generator.
        ReadOnlySpan<byte> redGreen = [0, 36, 73, 109, 146, 182, 219, 255];
        ReadOnlySpan<byte> blue = [0, 85, 170, 255];
        var expected = new Rgb24[256];
        for (int r = 0; r < 8; r++)
            for (int g = 0; g < 8; g++)
                for (int b = 0; b < 4; b++)
                    expected[(r * 8 + g) * 4 + b] = new Rgb24(redGreen[r], redGreen[g], blue[b]);
        expected[255] = new Rgb24(18, 18, 43);

        Assert.Equal(expected, PaletteGenerator.Generate(256).Colors);
    }

    [Fact]
    public void Generate_Rugged_ProducesTheGrayscaleLadder()
    {
        byte[] expected = [0, 32, 64, 96, 128, 159, 191, 223];

        var palette = PaletteGenerator.Generate(8, PaletteKind.Rugged);

        Assert.Equal(expected.Length, palette.Colors.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(new Rgb24(expected[i], expected[i], expected[i]), palette.Colors[i]);
    }

    [Fact]
    public void Generate_Rugged_IsGrayscaleNonWhiteAndUnique()
    {
        var colors = PaletteGenerator.Generate(8, PaletteKind.Rugged).Colors;

        Assert.Equal(8, colors.Distinct().Count());
        Assert.DoesNotContain(colors, c => c.IsWhite);
        Assert.All(colors, c => Assert.True(c.R == c.G && c.G == c.B, $"{c} is not gray"));
    }

    [Fact]
    public void Generate_Rugged_LumaGapsStayWideIncludingUpToWhite()
    {
        // The rugged guarantee: every entry (and reserved white at 255) is well separated in luma,
        // unlike the standard 8-colour palette whose gap-colour sits 9 luma from blue.
        var grays = PaletteGenerator.Generate(8, PaletteKind.Rugged).Colors.Select(c => (int)c.R).ToArray();
        var levels = grays.Append(255).ToArray(); // reserved white is the 9th level

        for (int i = 1; i < levels.Length; i++)
            Assert.True(levels[i] - levels[i - 1] >= 31, $"luma gap {levels[i - 1]}→{levels[i]} too narrow");
    }

    [Fact]
    public void Generate_Rugged_MinimumDistanceReflectsTheLumaGap()
    {
        // Narrowest gray gap is 31 (128→159); as a diagonal RGB step that is 31·√3.
        Assert.Equal(31 * Math.Sqrt(3), PaletteGenerator.Generate(8, PaletteKind.Rugged).MinimumDistance, precision: 6);
    }

    [Fact]
    public void Generate_Standard8_IsUnchangedByTheKindOverload()
    {
        Assert.Equal(PaletteGenerator.Generate(8).Colors, PaletteGenerator.Generate(8, PaletteKind.Standard).Colors);
        Assert.NotEqual(PaletteGenerator.Generate(8).Colors, PaletteGenerator.Generate(8, PaletteKind.Rugged).Colors);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(256)]
    public void Generate_Rugged_RejectsAnyCountButEight(int count)
    {
        Assert.False(PaletteGenerator.IsSupportedCount(count, PaletteKind.Rugged));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaletteGenerator.Generate(count, PaletteKind.Rugged));
    }

    [Fact]
    public void IsSupportedCount_KindAware()
    {
        Assert.True(PaletteGenerator.IsSupportedCount(8, PaletteKind.Rugged));
        Assert.True(PaletteGenerator.IsSupportedCount(8, PaletteKind.Standard));
        Assert.True(PaletteGenerator.IsSupportedCount(1024, PaletteKind.Standard));
    }
}

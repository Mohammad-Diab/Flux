using FluxCore.Imaging;
using Xunit;

namespace FluxCore.Tests.Imaging;

public class ColorMapTests
{
    [Fact]
    public void Constructor_WithValidPalette_Succeeds()
    {
        var colorMap = new ColorMap(CreateTestPalette(256));

        Assert.Equal(256, colorMap.Count);
    }

    [Fact]
    public void Constructor_WithNullPalette_ThrowsArgumentNullException()
    {
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
        Assert.Throws<ArgumentNullException>(() => new ColorMap(null));
#pragma warning restore CS8625
    }

    [Fact]
    public void Constructor_WithUnsupportedSize_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ColorMap(new Rgb24[100]));
        Assert.Contains("1024", ex.Message);
    }

    [Theory]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void Constructor_AcceptsSupportedCounts(int count)
    {
        var colorMap = new ColorMap(PaletteGenerator.Generate(count).Colors);
        Assert.Equal(count, colorMap.Count);
    }

    [Fact]
    public void Constructor_WithWhiteInPalette_ThrowsInvalidColorException()
    {
        var palette = CreateTestPalette(256);
        palette[50] = Rgb24.White;

        var ex = Assert.Throws<InvalidColorException>(() => new ColorMap(palette));
        Assert.Contains("white", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_WithDuplicates_ThrowsInvalidColorException()
    {
        var palette = CreateTestPalette(256);
        palette[100] = palette[50];

        var ex = Assert.Throws<InvalidColorException>(() => new ColorMap(palette));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public void GetColor_ReturnsCorrectColor()
    {
        var palette = CreateTestPalette(256);
        var colorMap = new ColorMap(palette);

        Assert.Equal(palette[42], colorMap.GetColor(42));
    }

    [Theory]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void FromCount_ProducesCountUniqueColors(int count)
    {
        var colorMap = ColorMap.FromCount(count);

        var colors = new HashSet<Rgb24>();
        for (int i = 0; i < count; i++)
            colors.Add(colorMap.GetColor(i));

        Assert.Equal(count, colorMap.Count);
        Assert.Equal(count, colors.Count);
    }

    [Fact]
    public void MinimumDistance_DecreasesWithDensity()
    {
        double d256 = ColorMap.FromCount(256).MinimumDistance;
        double d512 = ColorMap.FromCount(512).MinimumDistance;
        double d1024 = ColorMap.FromCount(1024).MinimumDistance;

        // 256 stays at 36; denser palettes are less robust (the reserved-white fill also lands
        // nearer a neighbour in the finer grid, so 512 is ~26, not 36).
        Assert.Equal(36, d256, precision: 0);
        Assert.True(d256 > d512 && d512 > d1024, $"expected 256({d256:F1}) > 512({d512:F1}) > 1024({d1024:F1}).");
        Assert.True(d512 is > 24 and < 30, $"512 min-distance {d512:F1} expected ~26.");
        Assert.True(d1024 is > 15 and < 20, $"1024 min-distance {d1024:F1} expected ~17.");
    }

    [Fact]
    public void Default_Is256AndDoesNotContainWhite()
    {
        var defaultMap = ColorMap.Default;

        Assert.Equal(256, defaultMap.Count);
        for (int i = 0; i < defaultMap.Count; i++)
            Assert.False(defaultMap.GetColor(i).IsWhite, $"Color at index {i} is white.");
    }

    [Fact]
    public void Palette_ReturnsCorrectSpan()
    {
        var palette = CreateTestPalette(256);
        var colorMap = new ColorMap(palette);

        var paletteSpan = colorMap.Palette;
        Assert.Equal(256, paletteSpan.Length);
        for (int i = 0; i < 256; i++)
            Assert.Equal(palette[i], paletteSpan[i]);
    }

    /// <summary>Creates a palette of unique, non-white colours.</summary>
    private static Rgb24[] CreateTestPalette(int count)
    {
        var palette = new Rgb24[count];
        var used = new HashSet<Rgb24>();

        for (int i = 0; i < count; i++)
        {
            Rgb24 color;
            int attempt = 0;
            do
            {
                int seed = i + attempt * count;
                byte r = (byte)((seed * 7) % 256);
                byte g = (byte)((seed * 13) % 256);
                byte b = (byte)((seed * 19) % 256);
                color = new Rgb24(r, g, b);
                attempt++;
            } while (color.IsWhite || !used.Add(color));

            palette[i] = color;
        }

        return palette;
    }
}

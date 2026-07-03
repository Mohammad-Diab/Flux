using FluxCore.Imaging;
using Xunit;

namespace FluxCore.Tests.Imaging;

public class ColorMapTests
{
    [Fact]
    public void Constructor_WithValidPalette_Succeeds()
    {
        // Arrange
     var palette = CreateTestPalette();

        // Act
   var colorMap = new ColorMap(palette);

        // Assert
        Assert.NotNull(colorMap);
    }

    [Fact]
    public void Constructor_WithNullPalette_ThrowsArgumentNullException()
{
 // Act & Assert
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
   Assert.Throws<ArgumentNullException>(() => new ColorMap(null));
#pragma warning restore CS8625
    }

    [Fact]
    public void Constructor_WithWrongSize_ThrowsArgumentException()
    {
        // Arrange
   var palette = new Rgb24[100];

// Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => new ColorMap(palette));
        Assert.Contains("256", ex.Message);
    }

 [Fact]
    public void Constructor_WithWhiteInPalette_ThrowsInvalidColorException()
    {
      // Arrange
        var palette = CreateTestPalette();
      palette[50] = Rgb24.White; // Inject white

      // Act & Assert
      var ex = Assert.Throws<InvalidColorException>(() => new ColorMap(palette));
        Assert.Contains("white", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

 [Fact]
 public void Constructor_WithDuplicates_ThrowsInvalidColorException()
    {
        // Arrange
        var palette = CreateTestPalette();
   palette[100] = palette[50]; // Create duplicate

    // Act & Assert
        var ex = Assert.Throws<InvalidColorException>(() => new ColorMap(palette));
        Assert.Contains("Duplicate", ex.Message);
    }

[Fact]
    public void GetColor_ReturnsCorrectColor()
    {
   // Arrange
   var palette = CreateTestPalette();
        var colorMap = new ColorMap(palette);

        // Act
    var color = colorMap.GetColor(42);

        // Assert
        Assert.Equal(palette[42], color);
    }

  [Fact]
    public void GetByte_ReturnsCorrectByte()
{
     // Arrange
        var palette = CreateTestPalette();
   var colorMap = new ColorMap(palette);
   var testColor = palette[123];

  // Act
  var value = colorMap.GetByte(testColor);

        // Assert
        Assert.Equal(123, value);
    }

    [Fact]
    public void GetByte_WithWhite_ThrowsInvalidColorException()
    {
   // Arrange
    var colorMap = ColorMap.Default;

   // Act & Assert
   var ex = Assert.Throws<InvalidColorException>(() => colorMap.GetByte(Rgb24.White));
   Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetByte_WithUnknownColor_ThrowsInvalidColorException()
{
        // Arrange
     var colorMap = ColorMap.Default;
        var unknownColor = new Rgb24(1, 2, 3); // Unlikely to be in palette

        // Act & Assert
        var ex = Assert.Throws<InvalidColorException>(() => colorMap.GetByte(unknownColor));
 Assert.Contains("not in the palette", ex.Message);
    }

    [Fact]
    public void TryGetByte_WithValidColor_ReturnsTrue()
    {
  // Arrange
      var palette = CreateTestPalette();
        var colorMap = new ColorMap(palette);
      var testColor = palette[78];

        // Act
  var result = colorMap.TryGetByte(testColor, out byte value);

   // Assert
    Assert.True(result);
        Assert.Equal(78, value);
    }

 [Fact]
    public void TryGetByte_WithWhite_ReturnsFalse()
    {
   // Arrange
 var colorMap = ColorMap.Default;

  // Act
     var result = colorMap.TryGetByte(Rgb24.White, out byte value);

    // Assert
        Assert.False(result);
        Assert.Equal(0, value);
    }

    [Fact]
    public void TryGetByte_WithUnknownColor_ReturnsFalse()
  {
        // Arrange
        var colorMap = ColorMap.Default;
 var unknownColor = new Rgb24(1, 2, 3);

        // Act
var result = colorMap.TryGetByte(unknownColor, out byte value);

     // Assert
        Assert.False(result);
    }

    [Fact]
    public void Contains_WithValidColor_ReturnsTrue()
    {
  // Arrange
        var palette = CreateTestPalette();
    var colorMap = new ColorMap(palette);

        // Act & Assert
     Assert.True(colorMap.Contains(palette[100]));
    }

  [Fact]
    public void Contains_WithWhite_ReturnsFalse()
    {
        // Arrange
  var colorMap = ColorMap.Default;

// Act & Assert
        Assert.False(colorMap.Contains(Rgb24.White));
    }

  [Fact]
    public void Serialize_ReturnsCorrectSize()
    {
        // Arrange
   var colorMap = ColorMap.Default;

        // Act
     var serialized = colorMap.Serialize();

        // Assert
        Assert.Equal(768, serialized.Length); // 256 * 3
    }

    [Fact]
    public void Serialize_Deserialize_RoundTrip()
{
        // Arrange
        var originalPalette = CreateTestPalette();
 var original = new ColorMap(originalPalette);

  // Act
        var serialized = original.Serialize();
   var deserialized = ColorMap.Deserialize(serialized);

        // Assert - verify all 256 colors match
 for (int i = 0; i < 256; i++)
   {
       Assert.Equal(original.GetColor((byte)i), deserialized.GetColor((byte)i));
        }
    }

    [Fact]
    public void Deserialize_WithNullData_ThrowsArgumentNullException()
    {
// Act & Assert
#pragma warning disable CS8625
   Assert.Throws<ArgumentNullException>(() => ColorMap.Deserialize(null));
#pragma warning restore CS8625
    }

    [Fact]
  public void Deserialize_WithWrongSize_ThrowsArgumentException()
  {
        // Arrange
var data = new byte[500];

  // Act & Assert
    var ex = Assert.Throws<ArgumentException>(() => ColorMap.Deserialize(data));
        Assert.Contains("768", ex.Message);
    }

    [Fact]
    public void Default_IsNotNull()
    {
   // Act
        var defaultMap = ColorMap.Default;

   // Assert
        Assert.NotNull(defaultMap);
    }

[Fact]
    public void Default_Has256UniqueColors()
    {
   // Arrange
var defaultMap = ColorMap.Default;
        var colors = new HashSet<Rgb24>();

  // Act
        for (int i = 0; i < 256; i++)
  {
    colors.Add(defaultMap.GetColor((byte)i));
        }

   // Assert
      Assert.Equal(256, colors.Count);
    }

    [Fact]
    public void Default_DoesNotContainWhite()
    {
        // Arrange
        var defaultMap = ColorMap.Default;

      // Act & Assert
    for (int i = 0; i < 256; i++)
   {
            var color = defaultMap.GetColor((byte)i);
       Assert.False(color.IsWhite, $"Color at index {i} is white: {color}");
    }
}

    [Fact]
    public void Palette_ReturnsCorrectSpan()
    {
        // Arrange
   var palette = CreateTestPalette();
   var colorMap = new ColorMap(palette);

 // Act
        var paletteSpan = colorMap.Palette;

        // Assert
   Assert.Equal(256, paletteSpan.Length);
     for (int i = 0; i < 256; i++)
        {
    Assert.Equal(palette[i], paletteSpan[i]);
        }
    }

    /// <summary>
    /// Creates a test palette with 256 unique non-white colors.
    /// </summary>
    private static Rgb24[] CreateTestPalette()
    {
        var palette = new Rgb24[256];
        var used = new HashSet<Rgb24>();

        for (int i = 0; i < 256; i++)
 {
            Rgb24 color;
            int attempt = 0;

 // Keep trying until we find a unique non-white color
       do
         {
      // Generate deterministic but varied colors
          int seed = i + attempt * 256;
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

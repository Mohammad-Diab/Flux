using FluxCore.Imaging;
using Xunit;

namespace FluxCore.Tests.Imaging;

public class Rgb24Tests
{
    [Fact]
    public void Constructor_SetsComponentsCorrectly()
    {
    // Arrange & Act
      var color = new Rgb24(100, 150, 200);

        // Assert
        Assert.Equal(100, color.R);
        Assert.Equal(150, color.G);
        Assert.Equal(200, color.B);
    }

    [Fact]
    public void White_IsDefinedCorrectly()
    {
        // Arrange & Act
        var white = Rgb24.White;

   // Assert
        Assert.Equal(255, white.R);
        Assert.Equal(255, white.G);
        Assert.Equal(255, white.B);
        Assert.True(white.IsWhite);
    }

    [Fact]
  public void IsWhite_ReturnsTrueForWhiteColor()
    {
        // Arrange
        var white = new Rgb24(255, 255, 255);

        // Act & Assert
 Assert.True(white.IsWhite);
    }

    [Fact]
    public void IsWhite_ReturnsFalseForNonWhiteColor()
    {
        // Arrange
        var color = new Rgb24(254, 255, 255);

        // Act & Assert
        Assert.False(color.IsWhite);
    }

    [Fact]
    public void Equals_ReturnsTrueForSameColors()
  {
        // Arrange
  var color1 = new Rgb24(100, 150, 200);
    var color2 = new Rgb24(100, 150, 200);

        // Act & Assert
        Assert.True(color1.Equals(color2));
        Assert.True(color1 == color2);
    }

    [Fact]
    public void Equals_ReturnsFalseForDifferentColors()
    {
 // Arrange
        var color1 = new Rgb24(100, 150, 200);
    var color2 = new Rgb24(100, 150, 201);

 // Act & Assert
        Assert.False(color1.Equals(color2));
        Assert.True(color1 != color2);
    }

    [Fact]
    public void GetHashCode_IsSameForEqualColors()
 {
        // Arrange
      var color1 = new Rgb24(100, 150, 200);
        var color2 = new Rgb24(100, 150, 200);

        // Act & Assert
        Assert.Equal(color1.GetHashCode(), color2.GetHashCode());
    }

    [Fact]
public void ToString_ReturnsFormattedString()
{
      // Arrange
     var color = new Rgb24(100, 150, 200);

        // Act
        var result = color.ToString();

     // Assert
        Assert.Equal("RGB(100, 150, 200)", result);
    }
}

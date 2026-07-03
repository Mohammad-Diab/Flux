using FluxCore.Hashing;
using Xunit;

namespace FluxCore.Tests.Hashing;

public class Sha256HelperTests
{
    [Fact]
 public void ComputeHash_WithByteArray_ReturnsCorrectHash()
  {
        // Arrange
  var data = "Hello, World!"u8.ToArray();

    // Act
        var hash = Sha256Helper.ComputeHash(data);

        // Assert
     Assert.NotNull(hash);
   Assert.Equal(32, hash.Length);
    }

    [Fact]
public void ComputeHash_WithSpan_ReturnsCorrectHash()
    {
   // Arrange
     var data = "Hello, World!"u8.ToArray();

        // Act
   var hash = Sha256Helper.ComputeHash(data.AsSpan());

   // Assert
        Assert.NotNull(hash);
        Assert.Equal(32, hash.Length);
    }

    [Fact]
 public void ComputeHash_SameData_ReturnsSameHash()
    {
        // Arrange
   var data = "Test data"u8.ToArray();

   // Act
        var hash1 = Sha256Helper.ComputeHash(data);
   var hash2 = Sha256Helper.ComputeHash(data);

  // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
  public async Task ComputeHashAsync_WithStream_ReturnsCorrectHash()
    {
        // Arrange
    var data = "Hello, World!"u8.ToArray();
        using var stream = new MemoryStream(data);

      // Act
   var hash = await Sha256Helper.ComputeHashAsync(stream);

// Assert
   Assert.NotNull(hash);
   Assert.Equal(32, hash.Length);
    }

    [Fact]
    public void ToHexString_ConvertsHashCorrectly()
    {
  // Arrange
   var hash = new byte[32];
   Array.Fill(hash, (byte)0xFF);

        // Act
   var hexString = Sha256Helper.ToHexString(hash);

      // Assert
  Assert.Equal(64, hexString.Length);
        Assert.All(hexString, c => Assert.True(char.IsAsciiHexDigitLower(c)));
  }

    [Fact]
    public void FromHexString_ConvertsCorrectly()
    {
   // Arrange
        var hexString = new string('f', 64);

   // Act
  var hash = Sha256Helper.FromHexString(hexString);

        // Assert
        Assert.Equal(32, hash.Length);
     Assert.All(hash, b => Assert.Equal(0xFF, b));
    }

    [Fact]
    public void FromHexString_WithInvalidLength_ThrowsArgumentException()
    {
   // Arrange
        var hexString = "abc";

   // Act & Assert
  Assert.Throws<ArgumentException>(() => Sha256Helper.FromHexString(hexString));
    }

    [Fact]
    public void HexString_RoundTrip_PreservesHash()
  {
   // Arrange
   var originalHash = Sha256Helper.ComputeHash("Test"u8.ToArray());

   // Act
   var hexString = Sha256Helper.ToHexString(originalHash);
     var restoredHash = Sha256Helper.FromHexString(hexString);

        // Assert
     Assert.Equal(originalHash, restoredHash);
    }

    [Fact]
    public void Verify_WithMatchingHash_ReturnsTrue()
    {
   // Arrange
        var data = "Test data"u8.ToArray();
   var expectedHash = Sha256Helper.ComputeHash(data);

        // Act
        var result = Sha256Helper.Verify(data, expectedHash);

        // Assert
   Assert.True(result);
    }

    [Fact]
    public void Verify_WithDifferentHash_ReturnsFalse()
    {
        // Arrange
   var data = "Test data"u8.ToArray();
        var wrongHash = new byte[32];

        // Act
        var result = Sha256Helper.Verify(data, wrongHash);

   // Assert
Assert.False(result);
    }

 [Fact]
    public void Verify_WithInvalidHashLength_ThrowsArgumentException()
    {
   // Arrange
        var data = "Test"u8.ToArray();
   var invalidHash = new byte[16];

   // Act & Assert
        Assert.Throws<ArgumentException>(() => Sha256Helper.Verify(data, invalidHash));
    }

    [Fact]
    public void ComputeHash_WithNullData_ThrowsArgumentNullException()
    {
        // Act & Assert
#pragma warning disable CS8625
   Assert.Throws<ArgumentNullException>(() => Sha256Helper.ComputeHash((byte[])null));
#pragma warning restore CS8625
    }
}

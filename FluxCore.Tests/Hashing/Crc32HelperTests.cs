using FluxCore.Hashing;
using Xunit;

namespace FluxCore.Tests.Hashing;

public class Crc32HelperTests
{
    [Fact]
    public void ComputeChecksum_WithByteArray_ReturnsChecksum()
  {
        // Arrange
     var data = "Hello, World!"u8.ToArray();

     // Act
   var checksum = Crc32Helper.ComputeChecksum(data);

   // Assert
   Assert.NotEqual(0u, checksum);
 }

  [Fact]
  public void ComputeChecksum_WithSpan_ReturnsChecksum()
    {
        // Arrange
   var data = "Hello, World!"u8.ToArray();

  // Act
        var checksum = Crc32Helper.ComputeChecksum(data.AsSpan());

        // Assert
   Assert.NotEqual(0u, checksum);
    }

[Fact]
    public void ComputeChecksum_SameData_ReturnsSameChecksum()
    {
   // Arrange
        var data = "Test data"u8.ToArray();

        // Act
   var checksum1 = Crc32Helper.ComputeChecksum(data);
     var checksum2 = Crc32Helper.ComputeChecksum(data);

   // Assert
        Assert.Equal(checksum1, checksum2);
    }

    [Fact]
    public void ComputeChecksum_DifferentData_ReturnsDifferentChecksum()
  {
   // Arrange
     var data1 = "Test 1"u8.ToArray();
     var data2 = "Test 2"u8.ToArray();

        // Act
     var checksum1 = Crc32Helper.ComputeChecksum(data1);
var checksum2 = Crc32Helper.ComputeChecksum(data2);

   // Assert
     Assert.NotEqual(checksum1, checksum2);
    }

    [Fact]
    public void Verify_WithMatchingChecksum_ReturnsTrue()
    {
        // Arrange
   var data = "Test data"u8.ToArray();
   var expectedChecksum = Crc32Helper.ComputeChecksum(data);

     // Act
   var result = Crc32Helper.Verify(data, expectedChecksum);

   // Assert
        Assert.True(result);
    }

  [Fact]
    public void Verify_WithDifferentChecksum_ReturnsFalse()
    {
   // Arrange
   var data = "Test data"u8.ToArray();
 var wrongChecksum = 0u;

// Act
   var result = Crc32Helper.Verify(data, wrongChecksum);

     // Assert
        Assert.False(result);
    }

    [Fact]
    public void Verify_WithSpan_WorksCorrectly()
    {
   // Arrange
        var data = "Test data"u8.ToArray();
     var expectedChecksum = Crc32Helper.ComputeChecksum(data);

   // Act
  var result = Crc32Helper.Verify(data.AsSpan(), expectedChecksum);

        // Assert
   Assert.True(result);
    }

    [Fact]
    public void ComputeChecksum_WithNullData_ThrowsArgumentNullException()
    {
   // Act & Assert
#pragma warning disable CS8625
   Assert.Throws<ArgumentNullException>(() => Crc32Helper.ComputeChecksum((byte[])null));
#pragma warning restore CS8625
    }

    [Fact]
    public void ComputeChecksum_EmptyData_ReturnsChecksum()
    {
    // Arrange
   var data = Array.Empty<byte>();

   // Act
   var checksum = Crc32Helper.ComputeChecksum(data);

        // Assert
        // CRC-32 of empty data is a known value
     Assert.Equal(0u, checksum);
    }
}

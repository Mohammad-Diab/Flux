using FluxCore.Framing;
using FluxCore.Imaging;
using Xunit;

namespace FluxCore.Tests.Framing;

public class MetadataPayloadTests
{
    [Fact]
    public void Constructor_WithValidParameters_Succeeds()
    {
   // Arrange
  var sha256 = new byte[32];
        var colorMap = ColorMap.Default;

        // Act
   var metadata = new MetadataPayload(
         sha256,
   tileSize: 8,
     eccPer16: 4,
   separatorEvery: 8,
         algorithm: 1,
  PayloadType.SevenZip,
            "TestFile.txt",
            originalLength: 1024,
          colorMap);

        // Assert
    Assert.NotNull(metadata);
        Assert.Equal(8, metadata.TileSize);
 Assert.Equal(4, metadata.EccPer16);
 }

    [Fact]
    public void Constructor_WithInvalidSha256Length_ThrowsArgumentException()
 {
   // Arrange
var sha256 = new byte[16]; // Wrong size
   var colorMap = ColorMap.Default;

  // Act & Assert
  Assert.Throws<ArgumentException>(() => new MetadataPayload(
            sha256, 8, 4, 8, 1, PayloadType.Raw, "Test", 1024, colorMap));
    }

    [Fact]
    public void Constructor_WithInvalidEccPer16_ThrowsArgumentException()
  {
        // Arrange
        var sha256 = new byte[32];
     var colorMap = ColorMap.Default;

     // Act & Assert
     Assert.Throws<ArgumentException>(() => new MetadataPayload(
  sha256, 8, eccPer16: 9, 8, 1, PayloadType.Raw, "Test", 1024, colorMap));
    }

    [Fact]
    public void Serialize_ProducesValidByteArray()
{
  // Arrange
        var sha256 = new byte[32];
        Array.Fill(sha256, (byte)0xAB);
  var colorMap = ColorMap.Default;
   var metadata = new MetadataPayload(
          sha256, 8, 4, 8, 1, PayloadType.SevenZip, "TestFile.txt", 123456, colorMap);

// Act
   var serialized = metadata.Serialize();

    // Assert
        Assert.NotNull(serialized);
   Assert.True(serialized.Length > 800); // At least version + sha256 + fields + colormap
    }

    [Fact]
    public void Serialize_Deserialize_RoundTrip()
    {
   // Arrange
     var sha256 = new byte[32];
 for (int i = 0; i < 32; i++) sha256[i] = (byte)i;
        var colorMap = ColorMap.Default;
        var original = new MetadataPayload(
            sha256, 6, 3, 8, 1, PayloadType.Raw, "MyDocument.pdf", 987654321, colorMap);

     // Act
        var serialized = original.Serialize();
        var deserialized = MetadataPayload.Deserialize(serialized);

        // Assert
        Assert.Equal(original.Version, deserialized.Version);
   Assert.Equal(original.Sha256, deserialized.Sha256);
      Assert.Equal(original.TileSize, deserialized.TileSize);
  Assert.Equal(original.EccPer16, deserialized.EccPer16);
        Assert.Equal(original.SeparatorEvery, deserialized.SeparatorEvery);
      Assert.Equal(original.Algorithm, deserialized.Algorithm);
        Assert.Equal(original.PayloadType, deserialized.PayloadType);
        Assert.Equal(original.OriginalName, deserialized.OriginalName);
        Assert.Equal(original.OriginalLength, deserialized.OriginalLength);
 
  // Verify color map
     for (int i = 0; i < 256; i++)
  {
  Assert.Equal(original.ColorMap.GetColor((byte)i), deserialized.ColorMap.GetColor((byte)i));
        }
    }

    [Fact]
    public void Deserialize_WithInvalidVersion_ThrowsNotSupportedException()
    {
   // Arrange
  var sha256 = new byte[32];
        var colorMap = ColorMap.Default;
var metadata = new MetadataPayload(
     sha256, 8, 4, 8, 1, PayloadType.Raw, "Test", 1024, colorMap);
        var serialized = metadata.Serialize();
  
// Corrupt version
  serialized[0] = 99;

    // Act & Assert
Assert.Throws<NotSupportedException>(() => MetadataPayload.Deserialize(serialized));
    }

    [Fact]
    public void Deserialize_WithTruncatedData_ThrowsArgumentException()
    {
   // Arrange
        var tooShort = new byte[50];

   // Act & Assert
  Assert.Throws<ArgumentException>(() => MetadataPayload.Deserialize(tooShort));
    }

    [Fact]
    public void Serialize_WithLongName_Succeeds()
 {
  // Arrange
  var sha256 = new byte[32];
        var colorMap = ColorMap.Default;
   var longName = new string('A', 1000);
        var metadata = new MetadataPayload(
 sha256, 8, 4, 8, 1, PayloadType.Raw, longName, 1024, colorMap);

        // Act
  var serialized = metadata.Serialize();
var deserialized = MetadataPayload.Deserialize(serialized);

        // Assert
   Assert.Equal(longName, deserialized.OriginalName);
    }

    [Fact]
    public void Serialize_WithUnicodeCharacters_PreservesName()
    {
  // Arrange
   var sha256 = new byte[32];
   var colorMap = ColorMap.Default;
   var unicodeName = "???.txt";
  var metadata = new MetadataPayload(
   sha256, 8, 4, 8, 1, PayloadType.Raw, unicodeName, 1024, colorMap);

   // Act
        var serialized = metadata.Serialize();
     var deserialized = MetadataPayload.Deserialize(serialized);

  // Assert
        Assert.Equal(unicodeName, deserialized.OriginalName);
    }

    [Theory]
    [InlineData(PayloadType.Raw)]
    [InlineData(PayloadType.SevenZip)]
    public void Serialize_PreservesPayloadType(PayloadType payloadType)
  {
    // Arrange
   var sha256 = new byte[32];
        var colorMap = ColorMap.Default;
        var metadata = new MetadataPayload(
       sha256, 8, 4, 8, 1, payloadType, "Test", 1024, colorMap);

  // Act
        var serialized = metadata.Serialize();
    var deserialized = MetadataPayload.Deserialize(serialized);

   // Assert
        Assert.Equal(payloadType, deserialized.PayloadType);
    }
}

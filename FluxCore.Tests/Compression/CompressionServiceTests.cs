using FluxCore.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxCore.Tests.Compression;

public class CompressionServiceTests
{
    [Fact]
    public void Constructor_DetectsCompressionMethod()
    {
        // Arrange & Act
        var service = new CompressionService(logger: NullLogger<CompressionService>.Instance);

 // Assert
     Assert.NotNull(service);
        Assert.Contains("ratio", service.CompressionMethod, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateRaw_ReturnsIdenticalData()
    {
        // Arrange
        var service = new CompressionService();
    var data = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var result = service.CreateRaw(data);

        // Assert
        Assert.Equal(data, result.Data);
        Assert.Equal(data.Length, result.OriginalSize);
        Assert.Equal(1.0, result.CompressionRatio);
    }

    [Fact]
  public async Task CompressAsync_WithFile_Succeeds()
    {
 // Arrange
        var service = new CompressionService();
        var tempFile = Path.GetTempFileName();
        var testData = "Hello, World! This is a test file for compression."u8.ToArray();
 await File.WriteAllBytesAsync(tempFile, testData);

        try
        {
  // Act
      var result = await service.CompressAsync(tempFile);

    // Assert
   Assert.NotNull(result);
      Assert.NotNull(result.Data);
  Assert.True(result.Data.Length > 0);
            Assert.Equal(testData.Length, result.OriginalSize);
       // Compressed size should be less than original (in most cases)
 }
   finally
   {
  if (File.Exists(tempFile))
      File.Delete(tempFile);
    }
    }

    [Fact]
  public async Task CompressAsync_WithDirectory_Succeeds()
    {
        // Arrange
    var service = new CompressionService();
        var tempDir = Path.Combine(Path.GetTempPath(), $"flux_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        // Create test files
   await File.WriteAllTextAsync(Path.Combine(tempDir, "file1.txt"), "Content 1");
   await File.WriteAllTextAsync(Path.Combine(tempDir, "file2.txt"), "Content 2");

   try
        {
// Act
       var result = await service.CompressAsync(tempDir);

  // Assert
      Assert.NotNull(result);
     Assert.NotNull(result.Data);
       Assert.True(result.Data.Length > 0);
  Assert.True(result.OriginalSize > 0);
     }
   finally
   {
if (Directory.Exists(tempDir))
  Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Decompress_RoundTrip_WithFile()
  {
   // Arrange
        var service = new CompressionService();
        var tempFile = Path.GetTempFileName();
   var testData = "Round trip test data for compression and decompression."u8.ToArray();
await File.WriteAllBytesAsync(tempFile, testData);

var tempExtractDir = Path.Combine(Path.GetTempPath(), $"flux_extract_{Guid.NewGuid():N}");

  try
    {
            // Act - Compress
       var compressed = await service.CompressAsync(tempFile);

      // Act - Decompress
       await service.DecompressAsync(compressed.Data, tempExtractDir);

 // Assert
      Assert.True(Directory.Exists(tempExtractDir));
     var extractedFile = Directory.GetFiles(tempExtractDir).Single();
    var extractedData = await File.ReadAllBytesAsync(extractedFile);

      Assert.Equal(testData, extractedData);
   }
  finally
    {
            if (File.Exists(tempFile))
       File.Delete(tempFile);

    if (Directory.Exists(tempExtractDir))
   Directory.Delete(tempExtractDir, true);
        }
    }

    [Fact]
    public async Task Decompress_RoundTrip_WithDirectory()
  {
   // Arrange
 var service = new CompressionService();
 var tempDir = Path.Combine(Path.GetTempPath(), $"flux_test_{Guid.NewGuid():N}");
   Directory.CreateDirectory(tempDir);

        var file1Content = "File 1 content";
   var file2Content = "File 2 content";
await File.WriteAllTextAsync(Path.Combine(tempDir, "file1.txt"), file1Content);
        await File.WriteAllTextAsync(Path.Combine(tempDir, "file2.txt"), file2Content);

        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"flux_extract_{Guid.NewGuid():N}");

     try
        {
   // Act - Compress
       var compressed = await service.CompressAsync(tempDir);

       // Act - Decompress
         await service.DecompressAsync(compressed.Data, tempExtractDir);

  // Assert
            Assert.True(Directory.Exists(tempExtractDir));
            var extractedFiles = Directory.GetFiles(tempExtractDir, "*", SearchOption.AllDirectories);
  Assert.Equal(2, extractedFiles.Length);

     // The top-level folder name is preserved in the archive, so files land under it.
     var folderName = Path.GetFileName(tempDir);
     var extracted1 = await File.ReadAllTextAsync(Path.Combine(tempExtractDir, folderName, "file1.txt"));
   var extracted2 = await File.ReadAllTextAsync(Path.Combine(tempExtractDir, folderName, "file2.txt"));

      Assert.Equal(file1Content, extracted1);
      Assert.Equal(file2Content, extracted2);
 }
finally
        {
  if (Directory.Exists(tempDir))
Directory.Delete(tempDir, true);

       if (Directory.Exists(tempExtractDir))
      Directory.Delete(tempExtractDir, true);
 }
    }

    [Fact]
  public async Task CompressAsync_WithNonExistentPath_ThrowsFileNotFoundException()
    {
   // Arrange
   var service = new CompressionService();
   var nonExistentPath = Path.Combine(Path.GetTempPath(), "does_not_exist.txt");

        // Act & Assert
     await Assert.ThrowsAsync<FileNotFoundException>(() => service.CompressAsync(nonExistentPath));
    }

[Fact]
    public void CompressionResult_CalculatesRatioCorrectly()
    {
   // Arrange
   var originalSize = 1000L;
 var compressedData = new byte[500];

  // Act
        var result = new CompressionResult(compressedData, originalSize);

   // Assert
 Assert.Equal(500, result.CompressedSize);
   Assert.Equal(0.5, result.CompressionRatio);
    }

    [Theory]
    [InlineData(true)]  // Prefer 7z.exe
    [InlineData(false)] // Force built-in
    public void Constructor_WithPreference_SetsMethodCorrectly(bool prefer7z)
    {
// Arrange & Act
   var service = new CompressionService(prefer7zExe: prefer7z);

 // Assert
        Assert.NotNull(service.CompressionMethod);
    }
}

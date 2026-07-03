using ZXing.Common.ReedSolomon;

namespace FluxCore.Ecc;

/// <summary>
/// Provides Reed-Solomon error correction with configurable redundancy.
/// Supports 1-8 ECC symbols per 16 data symbols.
/// </summary>
public sealed class ReedSolomonEcc
{
    private readonly int _eccSymbolsPer16;
    private readonly ReedSolomonEncoder _encoder;
    private readonly ReedSolomonDecoder _decoder;

    /// <summary>
    /// Gets the number of ECC symbols added per 16 data symbols.
    /// </summary>
    public int EccSymbolsPer16 => _eccSymbolsPer16;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReedSolomonEcc"/> class.
    /// </summary>
    /// <param name="eccSymbolsPer16">Number of ECC symbols per 16 data symbols (1-8).</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when ECC count is out of range.</exception>
    public ReedSolomonEcc(int eccSymbolsPer16)
    {
        if (eccSymbolsPer16 < 1 || eccSymbolsPer16 > 8)
            throw new ArgumentOutOfRangeException(nameof(eccSymbolsPer16), "ECC symbols must be between 1 and 8.");

 _eccSymbolsPer16 = eccSymbolsPer16;
        
        // Use GF(256) for byte-level encoding
        var field = GenericGF.QR_CODE_FIELD_256;
 _encoder = new ReedSolomonEncoder(field);
        _decoder = new ReedSolomonDecoder(field);
    }

    /// <summary>
    /// Encodes data with Reed-Solomon error correction.
    /// </summary>
    /// <param name="data">Data to encode (must be non-empty).</param>
    /// <returns>Encoded data with ECC symbols appended.</returns>
    /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
    /// <exception cref="EccException">Thrown when encoding fails.</exception>
    public byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length == 0)
         throw new ArgumentException("Data cannot be empty.", nameof(data));

        try
        {
      // Calculate total ECC symbols needed
            int dataBlocks = (data.Length + 15) / 16; // Round up
     int totalEccSymbols = dataBlocks * _eccSymbolsPer16;

  // Convert to int[] for RS encoder
            var dataInts = new int[data.Length + totalEccSymbols];
         for (int i = 0; i < data.Length; i++)
            {
       dataInts[i] = data[i] & 0xFF;
     }

      // Encode with Reed-Solomon
       _encoder.encode(dataInts, totalEccSymbols);

      // Convert back to byte[]
            var result = new byte[dataInts.Length];
         for (int i = 0; i < dataInts.Length; i++)
 {
  result[i] = (byte)(dataInts[i] & 0xFF);
         }

   return result;
        }
        catch (Exception ex)
    {
 throw new EccException($"Failed to encode data with Reed-Solomon: {ex.Message}", ex);
        }
    }

    /// <summary>
 /// Decodes data and attempts to correct errors using Reed-Solomon.
    /// </summary>
    /// <param name="encodedData">Encoded data with ECC symbols.</param>
 /// <param name="originalDataLength">Expected length of original data (without ECC).</param>
    /// <returns>Decoded data with errors corrected.</returns>
    /// <exception cref="ArgumentNullException">Thrown when encodedData is null.</exception>
    /// <exception cref="EccException">Thrown when decoding fails or errors are uncorrectable.</exception>
    public byte[] Decode(byte[] encodedData, int originalDataLength)
    {
        ArgumentNullException.ThrowIfNull(encodedData);

   if (originalDataLength <= 0)
     throw new ArgumentException("Original data length must be positive.", nameof(originalDataLength));

   if (encodedData.Length < originalDataLength)
            throw new ArgumentException("Encoded data is shorter than original data length.", nameof(encodedData));

  try
        {
  // Calculate expected ECC symbols
          int dataBlocks = (originalDataLength + 15) / 16;
 int totalEccSymbols = dataBlocks * _eccSymbolsPer16;
            int expectedLength = originalDataLength + totalEccSymbols;

            if (encodedData.Length < expectedLength)
        throw new EccException($"Encoded data length {encodedData.Length} is less than expected {expectedLength}.");

 // Convert to int[] for RS decoder
    var dataInts = new int[expectedLength];
      for (int i = 0; i < expectedLength; i++)
  {
            dataInts[i] = encodedData[i] & 0xFF;
     }

         // Decode with Reed-Solomon
    _decoder.decode(dataInts, totalEccSymbols);

            // Extract original data (first originalDataLength bytes)
    var result = new byte[originalDataLength];
            for (int i = 0; i < originalDataLength; i++)
            {
      result[i] = (byte)(dataInts[i] & 0xFF);
   }

     return result;
        }
        catch (Exception ex) when (ex.GetType().Name == "ReedSolomonException")
    {
throw new EccException($"Failed to decode data: {ex.Message}. Data may be corrupted beyond repair.", ex);
     }
        catch (Exception ex) when (ex is not EccException)
        {
        throw new EccException($"Unexpected error during decoding: {ex.Message}", ex);
      }
    }

    /// <summary>
    /// Tries to decode data and correct errors.
    /// </summary>
    /// <param name="encodedData">Encoded data with ECC symbols.</param>
    /// <param name="originalDataLength">Expected length of original data.</param>
    /// <param name="decodedData">Decoded data if successful.</param>
    /// <returns>True if decoding succeeded; false otherwise.</returns>
    public bool TryDecode(byte[] encodedData, int originalDataLength, out byte[]? decodedData)
    {
 try
        {
        decodedData = Decode(encodedData, originalDataLength);
return true;
        }
        catch
        {
        decodedData = null;
         return false;
 }
    }

    /// <summary>
    /// Calculates the total size of encoded data (original + ECC).
    /// </summary>
    /// <param name="originalDataLength">Length of original data.</param>
    /// <returns>Total length including ECC symbols.</returns>
  public int CalculateEncodedSize(int originalDataLength)
    {
        if (originalDataLength <= 0)
            return 0;

  int dataBlocks = (originalDataLength + 15) / 16;
        int totalEccSymbols = dataBlocks * _eccSymbolsPer16;
        return originalDataLength + totalEccSymbols;
    }

    /// <summary>
    /// Calculates the overhead ratio (ECC size / total size).
    /// </summary>
    /// <param name="originalDataLength">Length of original data.</param>
    /// <returns>Overhead ratio (0.0 to 1.0).</returns>
    public double CalculateOverheadRatio(int originalDataLength)
    {
        if (originalDataLength <= 0)
            return 0;

int totalSize = CalculateEncodedSize(originalDataLength);
   int eccSize = totalSize - originalDataLength;
    return (double)eccSize / totalSize;
    }
}

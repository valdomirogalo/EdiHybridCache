using System.Buffers;
using System.IO.Compression;

namespace EdiHybridCache.Cache;

internal static class CompressionHelper
{
    // Limits sourced from Constants.cs — single source of truth
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        // Rent from ArrayPool to avoid LOH allocation for large values (>85 KB).
        // The pooled buffer is sized for the worst case (incompressible data + overhead).
        var buffer = ArrayPool<byte>.Shared.Rent(data.Length + Constants.GzipOverhead);
        try
        {
            // MemoryStream(byte[], writable: true) uses the provided array directly
            // without allocating its own internal buffer.
            using var output = new MemoryStream(buffer, 0, buffer.Length, writable: true);
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write(data);
            }

            var compressedLength = (int)output.Position;
            var result = new byte[compressedLength];
            buffer.AsSpan(0, compressedLength).CopyTo(result);
            return result;
        }
        catch (NotSupportedException)
        {
            // Edge case: compressed output exceeded buffer.Length (extremely
            // incompressible data). Fall back to the allocating approach.
            using var fallback = new MemoryStream(data.Length);
            using (var gzip = new GZipStream(fallback, CompressionLevel.Fastest))
            {
                gzip.Write(data);
            }
            return fallback.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static bool TryDecompress(ReadOnlySpan<byte> compressedData, out byte[] result)
    {
        result = [];

        if (compressedData.Length == 0)
            return true;

        var buffer = ArrayPool<byte>.Shared.Rent(
            Math.Min(compressedData.Length * 10, Constants.MaxDecompressedBytes));

        try
        {
            var (totalRead, finalBuffer) = DecompressToBuffer(compressedData, buffer);

            if (totalRead >= Constants.MaxDecompressedBytes)
                return false; // CWE-409: ZIP Bomb — hard cap reached

            result = new byte[totalRead];
            finalBuffer.AsSpan(0, totalRead).CopyTo(result);
            return true;
        }
        catch (InvalidDataException)
        {
            result = [];
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static (int TotalRead, byte[] Buffer) DecompressToBuffer(
        ReadOnlySpan<byte> compressedData, byte[] buffer)
    {
        // Use ReadOnlySpan<byte> directly — avoid the compressedData.ToArray() copy.
        // The byte[] array passed to MemoryStream is not written to, so writable: false.
        var compressedCopy = compressedData.ToArray();
        using var input = new MemoryStream(compressedCopy, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);

        var totalRead = 0;
        int bytesRead;

        do
        {
            var remaining = buffer.Length - totalRead;

            if (remaining == 0)
            {
                // Expand buffer (doubles) up to the hard cap
                var newSize = Math.Min(buffer.Length * 2, Constants.MaxDecompressedBytes);
                if (newSize <= buffer.Length)
                    break;

                var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
                buffer.AsSpan(0, totalRead).CopyTo(newBuffer);
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = newBuffer;
                remaining = buffer.Length - totalRead;
            }

            bytesRead = gzip.Read(buffer, totalRead, remaining);
            totalRead += bytesRead;
        }
        while (bytesRead > 0 && totalRead < Constants.MaxDecompressedBytes);

        return (totalRead, buffer);
    }
}

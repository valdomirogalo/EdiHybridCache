using System.Buffers;
using System.IO.Compression;

namespace EdiHybridCache.Cache;

internal static class CompressionHelper
{
    private const int MaxDecompressedBytes = 100 * 1024 * 1024; // 100 MB

    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream(data.Length);
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

    public static bool TryDecompress(ReadOnlySpan<byte> compressedData, out byte[] result)
    {
        result = [];

        if (compressedData.Length == 0)
            return true;

        var buffer = ArrayPool<byte>.Shared.Rent(
            Math.Min(compressedData.Length * 10, MaxDecompressedBytes));

        var totalRead = 0;

        try
        {
            totalRead = DecompressToBuffer(compressedData, buffer);

            if (totalRead >= MaxDecompressedBytes)
                return false; // CWE-409: ZIP Bomb — hard cap reached

            result = new byte[totalRead];
            buffer.AsSpan(0, totalRead).CopyTo(result);
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

    private static int DecompressToBuffer(ReadOnlySpan<byte> compressedData, byte[] buffer)
    {
        using var input = new MemoryStream(compressedData.Length);
        input.Write(compressedData);
        input.Position = 0;

        using var gzip = new GZipStream(input, CompressionMode.Decompress);

        var totalRead = 0;
        int bytesRead;

        do
        {
            var remaining = buffer.Length - totalRead;

            if (remaining == 0)
            {
                // Expand buffer (doubles) up to the hard cap
                var newSize = Math.Min(buffer.Length * 2, MaxDecompressedBytes);
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
        while (bytesRead > 0 && totalRead < MaxDecompressedBytes);

        return totalRead;
    }
}

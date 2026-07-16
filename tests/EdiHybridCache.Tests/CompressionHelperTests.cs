using Xunit;
using EdiHybridCache.Cache;

namespace EdiHybridCache.Tests;

public class CompressionHelperTests
{
    [Fact]
    public void Compress_And_Decompress_SequentialData_Roundtrips()
    {
        var data = new byte[10_000];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(i % 256);

        var compressed = CompressionHelper.Compress(data);
        Assert.True(compressed.Length < data.Length,
            $"Compressed size ({compressed.Length}) should be < original ({data.Length})");

        var ok = CompressionHelper.TryDecompress(compressed, out var result);
        Assert.True(ok);
        Assert.Equal(data, result);
    }

    [Fact]
    public void Compress_And_Decompress_SmallData_Roundtrips()
    {
        var data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello"
        var compressed = CompressionHelper.Compress(data);
        var ok = CompressionHelper.TryDecompress(compressed, out var result);
        Assert.True(ok);
        Assert.Equal(data, result);
    }

    [Fact]
    public void Compress_And_Decompress_LargeRepetitiveData_Roundtrips()
    {
        var data = new byte[100_000];
        data.AsSpan().Fill(0x42);

        var compressed = CompressionHelper.Compress(data);
        Assert.True(compressed.Length < data.Length,
            $"Large repetitive data should compress well ({compressed.Length} vs {data.Length})");

        var ok = CompressionHelper.TryDecompress(compressed, out var result);
        Assert.True(ok);
        Assert.Equal(data, result);
    }

    [Fact]
    public void Compress_And_Decompress_RandomData_Roundtrips()
    {
        var data = new byte[1000];
        new Random(42).NextBytes(data);

        var compressed = CompressionHelper.Compress(data);
        // Random data may not compress — but must round-trip
        var ok = CompressionHelper.TryDecompress(compressed, out var result);
        Assert.True(ok);
        Assert.Equal(data, result);
    }

    [Fact]
    public void Decompress_WithEmptyData_ReturnsTrueWithEmptyResult()
    {
        var ok = CompressionHelper.TryDecompress([], out var result);
        Assert.True(ok);
        Assert.Empty(result);
    }

    [Fact]
    public void Decompress_WithCorruptData_ReturnsFalse()
    {
        var corrupt = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        var ok = CompressionHelper.TryDecompress(corrupt, out _);
        Assert.False(ok);
    }

    [Fact]
    public void Decompress_WithGzipHeaderOnly_ReturnsEmptyData()
    {
        // Minimal GZip header with no body → valid empty GZip member
        var header = new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03 };
        var ok = CompressionHelper.TryDecompress(header, out var result);
        Assert.True(ok);
        Assert.Empty(result);
    }

    [Fact]
    public void Compress_IsIdempotent()
    {
        var data = new byte[5000];
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(i % 256);

        var c1 = CompressionHelper.Compress(data);
        var c2 = CompressionHelper.Compress(data);
        Assert.Equal(c1.Length, c2.Length);
    }
}

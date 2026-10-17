using Engine.Protocol;
using Xunit;

namespace Engine.Gateway.Tests;

public class UploadAssemblerTests
{
    private static ConnectionLimitsOptions Limits(long maxBytes = 1_000, int maxChunks = 50) =>
        new() { MaxUploadBytes = maxBytes, MaxUploadChunks = maxChunks, UploadTimeout = TimeSpan.FromMinutes(1) };

    private static List<UploadChunkMessage> Split(string text, int size, Guid? id = null)
    {
        var uploadId = id ?? Guid.NewGuid();
        var pieces = Enumerable.Range(0, (text.Length + size - 1) / size)
            .Select(i => text.Substring(i * size, Math.Min(size, text.Length - i * size)))
            .ToList();
        return pieces.Select((piece, i) => new UploadChunkMessage(uploadId, i, pieces.Count, piece)).ToList();
    }

    [Fact]
    public void InOrderChunks_ReassembleTheExactText()
    {
        using var assembler = new UploadAssembler(Limits(), new UploadBudget(10_000));
        const string text = "{\"type\":\"createSession\",\"datasetType\":\"matrix\"}";
        var chunks = Split(text, 7);

        foreach (var chunk in chunks.Take(chunks.Count - 1))
            Assert.Equal(UploadStatus.InProgress, assembler.Accept(chunk).Status);

        var done = assembler.Accept(chunks[chunks.Count - 1]);
        Assert.Equal(UploadStatus.Complete, done.Status);
        Assert.Equal(text, done.Json);
    }

    [Fact]
    public void SingleChunkUpload_CompletesImmediately()
    {
        using var assembler = new UploadAssembler(Limits(), new UploadBudget(10_000));
        var done = assembler.Accept(new UploadChunkMessage(Guid.NewGuid(), 0, 1, "{}"));
        Assert.Equal(UploadStatus.Complete, done.Status);
        Assert.Equal("{}", done.Json);
    }

    [Fact]
    public void SkippedChunk_IsRejected_AndDiscardsTheUpload()
    {
        var budget = new UploadBudget(10_000);
        using var assembler = new UploadAssembler(Limits(), budget);
        var chunks = Split("abcdefghij", 2);

        assembler.Accept(chunks[0]);
        var result = assembler.Accept(chunks[2]); // chunk 1 never arrived

        Assert.Equal(UploadStatus.Rejected, result.Status);
        Assert.Equal("upload-out-of-order", result.Code);
        Assert.False(assembler.InProgress);
        Assert.Equal(0, budget.InFlightBytes);
    }

    [Fact]
    public void ChunkWithoutAStart_IsRejected()
    {
        using var assembler = new UploadAssembler(Limits(), new UploadBudget(10_000));
        var result = assembler.Accept(new UploadChunkMessage(Guid.NewGuid(), 3, 5, "x"));
        Assert.Equal("upload-out-of-order", result.Code);
    }

    [Fact]
    public void ChunkFromAnotherUpload_IsRejected()
    {
        using var assembler = new UploadAssembler(Limits(), new UploadBudget(10_000));
        assembler.Accept(new UploadChunkMessage(Guid.NewGuid(), 0, 3, "a"));
        var result = assembler.Accept(new UploadChunkMessage(Guid.NewGuid(), 1, 3, "b"));
        Assert.Equal("upload-out-of-order", result.Code);
    }

    [Fact]
    public void ChangingTheTotalMidUpload_IsRejected()
    {
        using var assembler = new UploadAssembler(Limits(), new UploadBudget(10_000));
        var id = Guid.NewGuid();
        assembler.Accept(new UploadChunkMessage(id, 0, 3, "a"));
        Assert.Equal("upload-out-of-order", assembler.Accept(new UploadChunkMessage(id, 1, 4, "b")).Code);
    }

    [Fact]
    public void StartingOver_DiscardsThePartialUpload_AndReleasesItsBytes()
    {
        var budget = new UploadBudget(10_000);
        using var assembler = new UploadAssembler(Limits(), budget);
        assembler.Accept(new UploadChunkMessage(Guid.NewGuid(), 0, 3, "aaaa"));
        Assert.Equal(4 * UploadAssembler.MemoryFactor, budget.InFlightBytes);

        var fresh = Split("xyz", 2);
        assembler.Accept(fresh[0]);
        var done = assembler.Accept(fresh[1]);

        Assert.Equal("xyz", done.Json); // nothing left over from the abandoned upload
        Assert.Equal(3 * UploadAssembler.MemoryFactor, budget.InFlightBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public void InvalidTotal_IsRejected(int total)
    {
        using var assembler = new UploadAssembler(Limits(maxChunks: 50), new UploadBudget(10_000));
        Assert.Equal("invalid-upload", assembler.Accept(new UploadChunkMessage(Guid.NewGuid(), 0, total, "a")).Code);
    }

    [Fact]
    public void NullData_IsRejected()
    {
        using var assembler = new UploadAssembler(Limits(), new UploadBudget(10_000));
        Assert.Equal("invalid-upload", assembler.Accept(new UploadChunkMessage(Guid.NewGuid(), 0, 1, null!)).Code);
    }

    [Fact]
    public void ExceedingTheUploadLimit_IsRejected_AndNothingStaysReserved()
    {
        var budget = new UploadBudget(10_000);
        using var assembler = new UploadAssembler(Limits(maxBytes: 10), budget);
        var chunks = Split(new string('a', 12), 4);

        Assert.Equal(UploadStatus.InProgress, assembler.Accept(chunks[0]).Status);
        Assert.Equal(UploadStatus.InProgress, assembler.Accept(chunks[1]).Status);
        var result = assembler.Accept(chunks[2]);

        Assert.Equal("upload-too-large", result.Code);
        Assert.Equal(0, budget.InFlightBytes);
    }

    [Fact]
    public void SizeIsCountedInUtf8Bytes_NotCharacters()
    {
        using var assembler = new UploadAssembler(Limits(maxBytes: 5), new UploadBudget(10_000));
        // Two characters, six bytes.
        var result = assembler.Accept(new UploadChunkMessage(Guid.NewGuid(), 0, 1, "€€"));
        Assert.Equal("upload-too-large", result.Code);
    }

    [Fact]
    public void SharedBudgetExhausted_IsRejectedAsServerBusy()
    {
        var budget = new UploadBudget(4 * UploadAssembler.MemoryFactor + 8); // room for "aaaa" but not "bb" as well
        using var first = new UploadAssembler(Limits(), budget);
        using var second = new UploadAssembler(Limits(), budget);

        first.Accept(new UploadChunkMessage(Guid.NewGuid(), 0, 2, "aaaa"));
        var result = second.Accept(new UploadChunkMessage(Guid.NewGuid(), 0, 2, "bb"));

        Assert.Equal("server-busy", result.Code);
    }

    [Fact]
    public void Dispose_ReturnsReservedBytes()
    {
        var budget = new UploadBudget(10_000);
        var assembler = new UploadAssembler(Limits(), budget);
        assembler.Accept(new UploadChunkMessage(Guid.NewGuid(), 0, 2, "abcd"));
        Assert.Equal(4 * UploadAssembler.MemoryFactor, budget.InFlightBytes);

        assembler.Dispose();

        Assert.Equal(0, budget.InFlightBytes);
    }

    [Fact]
    public void CompletedUpload_StaysReservedUntilReset()
    {
        var budget = new UploadBudget(10_000);
        using var assembler = new UploadAssembler(Limits(), budget);
        assembler.Accept(new UploadChunkMessage(Guid.NewGuid(), 0, 1, "abcd"));
        Assert.Equal(4 * UploadAssembler.MemoryFactor, budget.InFlightBytes);

        assembler.Reset();

        Assert.Equal(0, budget.InFlightBytes);
    }

    [Fact]
    public void TimeRemaining_IsNullWhenIdle_AndShrinksDuringAnUpload()
    {
        using var assembler = new UploadAssembler(Limits(), new UploadBudget(10_000));
        Assert.Null(assembler.TimeRemaining);

        assembler.Accept(new UploadChunkMessage(Guid.NewGuid(), 0, 2, "a"));

        Assert.InRange(assembler.TimeRemaining!.Value, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Budget_NeverGoesOverItsLimit_UnderContention()
    {
        var budget = new UploadBudget(1_000);
        int granted = 0;

        Parallel.For(0, 200, _ =>
        {
            if (budget.TryReserve(10)) Interlocked.Increment(ref granted);
        });

        Assert.Equal(100, granted);
        Assert.Equal(1_000, budget.InFlightBytes);
    }

    [Fact]
    public void Budget_IsChargedForMemoryCost_NotJustWireSize()
    {
        var budget = new UploadBudget(10_000);
        using var assembler = new UploadAssembler(Limits(), budget);

        assembler.Accept(new UploadChunkMessage(Guid.NewGuid(), 0, 2, "aaaa"));

        Assert.Equal(4 * UploadAssembler.MemoryFactor, budget.InFlightBytes);
    }

    [Fact]
    public void MaxUploadBytes_StillLimitsTheRawWireSize()
    {
        // 8 bytes would fit MaxUploadBytes = 10 but costs 64 against a 10,000 budget: the raw limit is what rejects it.
        using var assembler = new UploadAssembler(Limits(maxBytes: 7), new UploadBudget(10_000));

        var result = assembler.Accept(new UploadChunkMessage(Guid.NewGuid(), 0, 1, "12345678"));

        Assert.Equal("upload-too-large", result.Code);
    }
}

using ImageResize.Core.Cache;
using ImageResize.Core.Configuration;
using ImageResize.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace ImageResize.Tests;

/// <summary>
/// Confirms that cancellation tokens are honoured in the cache writer and that orphaned
/// <c>.tmp</c> files are cleaned up on cancellation.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class CancellationTests
{
    private string _tempDir = null!;
    private FileSystemImageCache _cache = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = TestPaths.AllocateTempDir(nameof(CancellationTests));

        var opts = new ImageResizeOptions
        {
            CacheRoot = _tempDir,
            Cache = new ImageResizeOptions.CacheOptions { FolderSharding = 0 }
        };
        var optsMock = new Mock<IOptions<ImageResizeOptions>>();
        optsMock.Setup(x => x.Value).Returns(opts);

        _cache = new FileSystemImageCache(optsMock.Object, Mock.Of<ILogger<FileSystemImageCache>>());
    }

    [TearDown]
    public void TearDown() => TestPaths.SafeDeleteDir(_tempDir);

    [Test]
    public async Task WriteAtomicallyAsync_CancelledBeforeCopy_ThrowsAndLeavesNoTempFile()
    {
        var target = Path.Combine(_tempDir, "cancelled.jpg");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using var data = new MemoryStream(new byte[1024]);

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _cache.WriteAtomicallyAsync(target, data, cts.Token));

        File.Exists(target).ShouldBeFalse();
        File.Exists(target + ".tmp").ShouldBeFalse();
    }

    [Test]
    public async Task WriteAtomicallyAsync_FailureDuringCopy_RemovesTempFile()
    {
        var target = Path.Combine(_tempDir, "broken.jpg");
        var failing = new ThrowingStream();

        await Should.ThrowAsync<IOException>(
            async () => await _cache.WriteAtomicallyAsync(target, failing, CancellationToken.None));

        File.Exists(target + ".tmp").ShouldBeFalse();
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => 4096;
        public override long Position { get; set; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("disk died");
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

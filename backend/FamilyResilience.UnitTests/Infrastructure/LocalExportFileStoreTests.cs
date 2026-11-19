using System.Text;
using FamilyResilience.Infrastructure.Exports;

namespace FamilyResilience.UnitTests.Infrastructure;

public sealed class LocalExportFileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "azwah-unit-exports", Guid.NewGuid().ToString("N"));

    private static string Key() => $"exports/{Guid.NewGuid()}.csv";

    private static Func<Stream, CancellationToken, Task> Text(string text) =>
        (stream, token) => stream.WriteAsync(Encoding.UTF8.GetBytes(text), token).AsTask();

    private static async Task<string> ReadAsync(Stream? stream)
    {
        Assert.NotNull(stream);
        await using (stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task A_written_file_reads_back_and_a_second_write_replaces_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new LocalExportFileStore(_root);
        var key = Key();

        await store.WriteAsync(key, Text("first"), cancellationToken);
        await store.WriteAsync(key, Text("second"), cancellationToken);

        Assert.True(store.IsConfigured);
        Assert.Equal("second", await ReadAsync(await store.OpenReadAsync(key, cancellationToken)));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "exports")));
    }

    [Fact]
    public async Task A_write_that_fails_part_way_leaves_the_earlier_file_and_no_temporary_one()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new LocalExportFileStore(_root);
        var key = Key();
        await store.WriteAsync(key, Text("complete"), cancellationToken);

        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(
            key,
            async (stream, token) =>
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes("partial"), token);
                throw new IOException("A synthetic crash in the middle of a write.");
            },
            cancellationToken));

        Assert.Equal("complete", await ReadAsync(await store.OpenReadAsync(key, cancellationToken)));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "exports")));
    }

    [Fact]
    public async Task A_missing_file_reads_as_null_and_deletes_quietly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new LocalExportFileStore(_root);
        var key = Key();

        Assert.Null(await store.OpenReadAsync(key, cancellationToken));
        await store.DeleteAsync(key, cancellationToken);

        await store.WriteAsync(key, Text("x"), cancellationToken);
        await store.DeleteAsync(key, cancellationToken);
        Assert.Null(await store.OpenReadAsync(key, cancellationToken));
    }

    [Fact]
    public async Task A_root_written_with_a_trailing_separator_works()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new LocalExportFileStore(_root + Path.DirectorySeparatorChar);
        var key = Key();

        await store.WriteAsync(key, Text("rows"), cancellationToken);

        Assert.Equal("rows", await ReadAsync(await store.OpenReadAsync(key, cancellationToken)));
    }

    [Fact]
    public async Task Deleting_a_key_also_removes_temporary_files_a_crash_left_behind()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new LocalExportFileStore(_root);
        var key = Key();
        await store.WriteAsync(key, Text("rows"), cancellationToken);
        var leftover = Path.Combine(_root, key) + $".{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(leftover, "partial", cancellationToken);
        var neighbour = Key();
        await store.WriteAsync(neighbour, Text("other"), cancellationToken);

        await store.DeleteAsync(key, cancellationToken);

        Assert.Equal([Path.GetFileName(neighbour)], Directory.GetFiles(Path.Combine(_root, "exports")).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("../outside.csv")]
    [InlineData("exports/../../outside.csv")]
    [InlineData("exports/not-a-guid.csv")]
    [InlineData("/etc/passwd")]
    [InlineData("exports/00000000-0000-0000-0000-000000000000.txt")]
    public async Task A_key_that_is_not_an_export_key_is_refused(string key)
    {
        var store = new LocalExportFileStore(_root);

        await Assert.ThrowsAsync<ArgumentException>(() => store.OpenReadAsync(key, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(key, Text("x"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Without_a_directory_the_store_is_not_configured_and_refuses_every_call()
    {
        var store = new UnconfiguredExportFileStore();

        Assert.False(store.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync(Key(), Text("x"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.OpenReadAsync(Key(), TestContext.Current.CancellationToken));
    }
}

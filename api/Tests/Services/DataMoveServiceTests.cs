using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Services;

public class DataMoveServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"DataMoveTest_{Guid.NewGuid()}");
    private readonly ActionsContext _context;

    public DataMoveServiceTests()
    {
        Directory.CreateDirectory(_root);
        _context = new ActionsContext(new DbContextOptionsBuilder<ActionsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose()
    {
        _context.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private Task Move(string from, string to) =>
        new DataMoveService().MoveAsync(_context, from, to, CancellationToken.None);

    [Fact]
    public async Task Move_WhenSourceIsFile_MovesFileSuccessfully()
    {
        var source = Path.Combine(_root, "old_file.txt");
        var dest = Path.Combine(_root, "new_file.txt");
        await File.WriteAllTextAsync(source, "content");

        await Move(source, dest);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(dest));
        Assert.Equal("content", await File.ReadAllTextAsync(dest));
    }

    [Fact]
    public async Task Move_WhenSourceIsDirectory_MovesDirectorySuccessfully()
    {
        var source = Path.Combine(_root, "SourceFolder");
        var dest = Path.Combine(_root, "DestFolder");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "data.txt"), "hello");

        await Move(source, dest);

        Assert.False(Directory.Exists(source));
        Assert.True(File.Exists(Path.Combine(dest, "data.txt")));
    }

    [Fact]
    public async Task Move_WhenTargetDirectoryDoesNotExist_CreatesDirectoryAndMovesFile()
    {
        var source = Path.Combine(_root, "source.txt");
        var dest = Path.Combine(_root, "deep", "path", "target.txt");
        await File.WriteAllTextAsync(source, "data");

        await Move(source, dest);

        Assert.True(File.Exists(dest));
    }

    [Fact]
    public async Task Move_WhenDestinationExists_DoesNotMoveOrRecord()
    {
        var source = Path.Combine(_root, "source.txt");
        var dest = Path.Combine(_root, "dest.txt");
        await File.WriteAllTextAsync(source, "source");
        await File.WriteAllTextAsync(dest, "already exists");

        await Move(source, dest);

        Assert.True(File.Exists(source));
        Assert.Equal("already exists", await File.ReadAllTextAsync(dest));
        Assert.Empty(_context.Actions);
    }

    [Fact]
    public async Task Move_WhenSourceMissing_DoesNotRecord()
    {
        await Move(Path.Combine(_root, "ghost.txt"), Path.Combine(_root, "dest.txt"));
        Assert.Empty(_context.Actions);
    }

    [Fact]
    public async Task Move_RecordsDataMovedActionWithFromAndTo()
    {
        var source = Path.Combine(_root, "a.txt");
        var dest = Path.Combine(_root, "sub", "b.txt");
        await File.WriteAllTextAsync(source, "x");

        await Move(source, dest);

        var record = Assert.IsType<DataMovedActionRecord>(Assert.Single(_context.Actions));
        Assert.Equal(source, record.From);
        Assert.Equal(dest, record.To);
    }
}

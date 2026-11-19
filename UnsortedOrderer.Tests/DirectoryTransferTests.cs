using UnsortedOrderer.Services;
using Xunit;

namespace UnsortedOrderer.Tests;

public sealed class DirectoryTransferTests : IDisposable
{
    private readonly string _name = "uno-transfer-" + Guid.NewGuid().ToString("N");
    private string Root => Path.Combine(Path.GetTempPath(), _name);

    [Fact]
    public void Cross_root_move_preserves_directory_structure_and_contents()
    {
        var otherRoot = OperatingSystem.IsWindows() ? AppContext.BaseDirectory : "/dev/shm";
        if (!Directory.Exists(otherRoot) || OperatingSystem.IsWindows() &&
            string.Equals(Path.GetPathRoot(otherRoot), Path.GetPathRoot(Root), StringComparison.OrdinalIgnoreCase))
            return;

        var other = Path.Combine(otherRoot, _name);
        var source = Directory.CreateDirectory(Path.Combine(Root, "source")).FullName;
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllText(Path.Combine(source, "nested", "data.txt"), "payload");
        try
        {
            Directory.CreateDirectory(other);
            var destination = Path.Combine(other, "dataset");
            DirectoryTransfer.Move(source, destination);
            Assert.False(Directory.Exists(source));
            Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
            Assert.Equal("payload", File.ReadAllText(Path.Combine(destination, "nested", "data.txt")));
        }
        finally
        {
            if (Directory.Exists(other))
                Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void Existing_target_is_not_overwritten()
    {
        var source = Directory.CreateDirectory(Path.Combine(Root, "source")).FullName;
        var destination = Directory.CreateDirectory(Path.Combine(Root, "target")).FullName;
        File.WriteAllText(Path.Combine(source, "payload.txt"), "new");
        File.WriteAllText(Path.Combine(destination, "payload.txt"), "old");

        Assert.ThrowsAny<IOException>(() => DirectoryTransfer.Move(source, destination));

        Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "payload.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(source, "payload.txt")));
    }

    [Fact]
    public void Descendant_target_is_rejected_before_writing()
    {
        var source = Directory.CreateDirectory(Path.Combine(Root, "source")).FullName;
        var destination = Path.Combine(source, "target");

        Assert.ThrowsAny<IOException>(() => DirectoryTransfer.Move(source, destination));
        Assert.False(Directory.Exists(destination));
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}

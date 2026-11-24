using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Services.Desktop;
using Xunit;

namespace UnsortedOrderer.Tests;

public sealed class DesktopShortcutDeduplicationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uno-dedup-" + Guid.NewGuid().ToString("N"));
    private readonly PlainShortcutStore _store = new();

    [Fact]
    public void Existing_links_on_personal_and_public_desktops_are_collapsed_without_creating_new_links()
    {
        var personal = Directory.CreateDirectory(Path.Combine(_root, "Personal")).FullName;
        var shared = Directory.CreateDirectory(Path.Combine(_root, "Public")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(_root, "Source")).FullName;
        var sorted = Directory.CreateDirectory(Path.Combine(_root, "Sorted")).FullName;
        var first = Path.Combine(shared, "Unsorted.lnk");
        var duplicate = Path.Combine(personal, "_Unsorted — ярлык.lnk");
        _store.CreateFolderLink(first, source);
        _store.CreateFolderLink(duplicate, source);
        var organizer = new DesktopShortcutOrganizer(_store, new SilentLog());

        for (var run = 0; run < 2; run++)
            organizer.EnsureFolderLink(personal, "Unsorted", source, shared, sorted);

        Assert.True(File.Exists(duplicate));
        Assert.False(File.Exists(first));
        Assert.Single(Directory.GetFiles(personal, "*.lnk"));
        Assert.Empty(Directory.GetFiles(shared, "*.lnk"));
        Assert.Single(Directory.GetFiles(Path.Combine(sorted, "_ToDelete"), "*.lnk"));
    }

    [Fact]
    public void Existing_public_desktop_link_is_reused_instead_of_creating_a_private_copy()
    {
        var personal = Directory.CreateDirectory(Path.Combine(_root, "Personal")).FullName;
        var shared = Directory.CreateDirectory(Path.Combine(_root, "Public")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(_root, "Source")).FullName;
        var sorted = Directory.CreateDirectory(Path.Combine(_root, "Sorted")).FullName;
        _store.CreateFolderLink(Path.Combine(shared, "_Unsorted.lnk"), source);
        var organizer = new DesktopShortcutOrganizer(_store, new SilentLog());

        organizer.EnsureFolderLink(personal, "Unsorted", source, shared, sorted);

        Assert.Empty(Directory.GetFiles(personal, "*.lnk"));
        Assert.Single(Directory.GetFiles(shared, "*.lnk"));
    }

    [Fact]
    public void Distinct_existing_shortcut_targets_are_not_quarantined()
    {
        var personal = Directory.CreateDirectory(Path.Combine(_root, "Personal")).FullName;
        var shared = Directory.CreateDirectory(Path.Combine(_root, "Public")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(_root, "Source")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(_root, "Other")).FullName;
        var sorted = Directory.CreateDirectory(Path.Combine(_root, "Sorted")).FullName;
        _store.CreateFolderLink(Path.Combine(shared, "Unsorted.lnk"), other);
        var organizer = new DesktopShortcutOrganizer(_store, new SilentLog());

        organizer.EnsureFolderLink(personal, "Unsorted", source, shared, sorted);

        Assert.Equal(2, Directory.GetFiles(personal, "*.lnk").Length + Directory.GetFiles(shared, "*.lnk").Length);
        Assert.False(Directory.Exists(Path.Combine(sorted, "_ToDelete")));
    }

    [Fact]
    public void Duplicate_epson_launchers_in_existing_group_are_quarantined_not_destroyed()
    {
        var desktop = Directory.CreateDirectory(Path.Combine(_root, "Desktop")).FullName;
        var photo = Directory.CreateDirectory(Path.Combine(desktop, "PhotoEditors")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(_root, "Source")).FullName;
        var sorted = Directory.CreateDirectory(Path.Combine(_root, "Sorted")).FullName;
        _store.CreateFolderLink(Path.Combine(photo, "Epson Photo+.lnk"), @"C:\Epson\EPPlus.exe");
        _store.CreateFolderLink(Path.Combine(photo, "Epson Photo+ (2).lnk"), @"C:\Epson\Epson Photo+.exe");
        var third = Path.Combine(desktop, "Epson Photo+ — ярлык.lnk");
        _store.CreateFolderLink(third, @"C:\Epson\Epson Photo+.exe");
        var organizer = new DesktopShortcutOrganizer(_store, new SilentLog());

        var grouped = organizer.Group(desktop, source, sorted, [third]);
        var errors = organizer.DeduplicateGroup(desktop, "PhotoEditors", sorted);

        Assert.Equal(0, grouped.Errors);
        Assert.Equal(0, errors);
        Assert.Single(Directory.GetFiles(photo, "*.lnk"));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(sorted, "_ToDelete"), "*.lnk").Length);
        Assert.False(File.Exists(third));
    }

    [Fact]
    public void A_second_cleanup_is_idempotent_for_normal_group_shortcuts()
    {
        var desktop = Directory.CreateDirectory(Path.Combine(_root, "Desktop")).FullName;
        var source = Directory.CreateDirectory(Path.Combine(_root, "Source")).FullName;
        var sorted = Directory.CreateDirectory(Path.Combine(_root, "Sorted")).FullName;
        var first = Path.Combine(desktop, "QLoader.lnk");
        _store.CreateFolderLink(first, @"C:\VR\QLoader.exe");
        var organizer = new DesktopShortcutOrganizer(_store, new SilentLog());

        Assert.Equal(1, organizer.Group(desktop, source, sorted, [first]).Moved);
        Assert.True(File.Exists(Path.Combine(desktop, "VR", "QLoader.lnk")));
        Assert.Empty(Directory.GetFiles(desktop, "*.lnk"));
        Assert.Equal(0, organizer.DeduplicateGroup(desktop, "VR", sorted));
        Assert.Single(Directory.GetFiles(Path.Combine(desktop, "VR"), "*.lnk"));
    }

    [Fact]
    public void Same_executable_and_arguments_with_different_working_folders_are_not_duplicates()
    {
        var desktop = Directory.CreateDirectory(Path.Combine(_root, "Desktop")).FullName;
        var group = Directory.CreateDirectory(Path.Combine(desktop, "Development")).FullName;
        var sorted = Directory.CreateDirectory(Path.Combine(_root, "Sorted")).FullName;
        File.WriteAllText(Path.Combine(group, "Project A.lnk"), "a");
        File.WriteAllText(Path.Combine(group, "Project B.lnk"), "b");
        var organizer = new DesktopShortcutOrganizer(new WorkingDirectoryStore(), new SilentLog());

        Assert.Equal(0, organizer.DeduplicateGroup(desktop, "Development", sorted));

        Assert.Equal(2, Directory.GetFiles(group, "*.lnk").Length);
        Assert.False(Directory.Exists(Path.Combine(sorted, "_ToDelete")));
    }

    [Fact]
    public void Working_replacement_for_a_broken_epson_shortcut_is_preserved()
    {
        var desktop = Directory.CreateDirectory(Path.Combine(_root, "Desktop")).FullName;
        var photo = Directory.CreateDirectory(Path.Combine(desktop, "PhotoEditors")).FullName;
        var sorted = Directory.CreateDirectory(Path.Combine(_root, "Sorted")).FullName;
        var stale = Path.Combine(photo, "Epson Photo+.lnk");
        File.WriteAllText(stale, @"C:\Epson\EPPlus.exe");
        var replacement = Path.Combine(desktop, "Epson Photo+ new.lnk");
        File.WriteAllText(replacement, @"C:\Epson\Epson Photo+.exe");
        var organizer = new DesktopShortcutOrganizer(new BrokenEpsonStore(), new SilentLog());

        var grouped = organizer.Group(desktop, Path.Combine(_root, "Source"), sorted, [replacement]);
        var errors = organizer.DeduplicateGroup(desktop, "PhotoEditors", sorted);

        Assert.Equal(1, grouped.Moved);
        Assert.Equal(0, errors);
        Assert.True(File.Exists(Path.Combine(photo, "Epson Photo+ new.lnk")));
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(Path.Combine(sorted, "_ToDelete", "Epson Photo+.lnk")));
    }

    private sealed class WorkingDirectoryStore : IDesktopShortcutStore
    {
        public DesktopShortcut Read(string path) => new(@"C:\Apps\Code.exe", ".", Path.GetFileNameWithoutExtension(path));
        public void CreateFolderLink(string path, string targetDirectory) => File.WriteAllText(path, targetDirectory);
    }

    private sealed class BrokenEpsonStore : IDesktopShortcutStore
    {
        public DesktopShortcut Read(string path) => new(File.ReadAllText(path));
        public bool IsBroken(string path, DesktopShortcut shortcut) =>
            Path.GetFileName(path).Equals("Epson Photo+.lnk", StringComparison.OrdinalIgnoreCase);
        public void CreateFolderLink(string path, string targetDirectory) => File.WriteAllText(path, targetDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class PlainShortcutStore : IDesktopShortcutStore
    {
        public DesktopShortcut? Read(string path) => File.Exists(path) ? new DesktopShortcut(File.ReadAllText(path)) : null;
        public void CreateFolderLink(string path, string targetDirectory) => File.WriteAllText(path, targetDirectory);
    }

    private sealed class SilentLog : IMessageWriter
    {
        public void WriteLine(string message) { }
    }
}

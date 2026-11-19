using UnsortedOrderer.Models;
using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Services;

namespace UnsortedOrderer.Application.Application;

public sealed class FileOrganizerApplication
{
    public const string QuarantineFolderName = DeletionQuarantine.FolderName;

    private readonly AppSettings _settings;
    private readonly FileOrganizerService _organizer;
    private readonly IMessageWriter _messageWriter;

    private readonly IDesktopCleanupService _desktop;
    private readonly IDownloadsCleanupService _downloads;

    public FileOrganizerApplication(
        AppSettings settings,
        FileOrganizerService organizer,
        IMessageWriter messageWriter,
        IDesktopCleanupService desktop,
        IDownloadsCleanupService downloads)
    {
        _settings = settings;
        _organizer = organizer;
        _messageWriter = messageWriter;
        _desktop = desktop;
        _downloads = downloads;
    }

    public int Run(
        bool cleanDesktop = false,
        bool cleanDownloads = false,
        Action<CleanupStage>? onStage = null)
    {
        if (ApplicationLocation.ContainsPath(_settings.DestinationRoot, _settings.SourceDirectory))
            throw new IOException("Папка разбора не может находиться внутри папки назначения.");

        var desktopWasCleaned = cleanDesktop && _desktop.CanClean(_settings.SourceDirectory);
        if (desktopWasCleaned)
        {
            onStage?.Invoke(CleanupStage.Desktop);
            _desktop.Clean(_settings.SourceDirectory, _settings.DestinationRoot);
        }

        if (cleanDownloads && _downloads.CanClean(_settings.SourceDirectory))
        {
            onStage?.Invoke(CleanupStage.Downloads);
            _downloads.Clean(_settings.SourceDirectory);
        }

        onStage?.Invoke(CleanupStage.Sorting);
        var quarantineExtensions = FileUtilities
            .NormalizeExtensions(_settings.DeletedExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _messageWriter.WriteLine($"Extensions moved to {DeletionQuarantine.FolderName}: " +
            string.Join(", ", quarantineExtensions));

        _messageWriter.WriteLine(string.Empty);

        _messageWriter.WriteLine("Loaded settings:");
        _messageWriter.WriteLine($"  SourceDirectory: {_settings.SourceDirectory}");
        _messageWriter.WriteLine($"  DestinationRoot: {_settings.DestinationRoot}");
        _messageWriter.WriteLine($"  SoftFolderName: {_settings.SoftFolderName}");
        _messageWriter.WriteLine($"  ArchiveFolderName: {_settings.ArchiveFolderName}");
        _messageWriter.WriteLine($"  ImagesFolderName: {_settings.ImagesFolderName}");
        _messageWriter.WriteLine($"  PhotosFolderName: {_settings.PhotosFolderName}");
        _messageWriter.WriteLine($"  MusicFolderName: {_settings.MusicFolderName}");
        _messageWriter.WriteLine($"  MusicalInstrumentsFolderName: {_settings.MusicalInstrumentsFolderName}");
        _messageWriter.WriteLine($"  EBooksFolderName: {_settings.EBooksFolderName}");
        _messageWriter.WriteLine($"  RepositoriesFolderName: {_settings.RepositoriesFolderName}");
        _messageWriter.WriteLine($"  DriversFolderName: {_settings.DriversFolderName}");
        _messageWriter.WriteLine($"  FirmwareFolderName: {_settings.FirmwareFolderName}");
        _messageWriter.WriteLine($"  MetadataFolderName: {_settings.MetadataFolderName}");
        _messageWriter.WriteLine(string.Empty);
        _messageWriter.WriteLine("Starting organization...");

        var failedEntries = _organizer.Organize();

        if (desktopWasCleaned)
        {
            _desktop.CreateFolderShortcuts(_settings.SourceDirectory, _settings.DestinationRoot);
        }

        return failedEntries;
    }

    public void ClearQuarantine()
    {
        _organizer.ClearQuarantine();
        _messageWriter.WriteLine($"Корзина {QuarantineFolderName} очищена.");
    }
}

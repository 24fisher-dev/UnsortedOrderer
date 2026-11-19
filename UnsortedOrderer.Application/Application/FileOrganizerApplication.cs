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
        Action<CleanupStage>? onStage = null,
        bool groupCommonDesktop = false)
    {
        if (ApplicationLocation.ContainsPath(_settings.DestinationRoot, _settings.SourceDirectory))
            throw new IOException("Папка разбора не может находиться внутри папки назначения.");

        var desktopErrors = 0;
        var desktopAvailable = cleanDesktop && _desktop.CanClean(_settings.SourceDirectory);
        if (desktopAvailable || groupCommonDesktop)
            onStage?.Invoke(CleanupStage.Desktop);
        if (groupCommonDesktop)
            desktopErrors += _desktop.CleanCommon(_settings.SourceDirectory, _settings.DestinationRoot);
        if (desktopAvailable)
            desktopErrors += _desktop.Clean(_settings.SourceDirectory, _settings.DestinationRoot);
        else if (cleanDesktop)
        {
            _messageWriter.WriteLine("Личный рабочий стол недоступен или совпадает с папкой разбора. Перенос пропущен.");
            desktopErrors++;
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

        if (desktopAvailable)
        {
            try
            {
                _desktop.CreateFolderShortcuts(_settings.SourceDirectory, _settings.DestinationRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                desktopErrors++;
                _messageWriter.WriteLine($"Не удалось создать ярлыки Unsorted и Sorted: {ex.Message}");
            }
        }

        return failedEntries + desktopErrors;
    }

    public void ClearQuarantine()
    {
        _organizer.ClearQuarantine();
        _messageWriter.WriteLine($"Корзина {QuarantineFolderName} очищена.");
    }
}

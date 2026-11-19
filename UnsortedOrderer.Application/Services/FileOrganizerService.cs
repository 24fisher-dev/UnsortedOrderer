using UnsortedOrderer.Application.Contracts.Services.Categories;
using UnsortedOrderer.Application.Contracts.Services.Categories.Photo;
using UnsortedOrderer.Application.Services.Categories;
using UnsortedOrderer.Cache;
using UnsortedOrderer.Categories;
using UnsortedOrderer.Contracts.Categories;
using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;

namespace UnsortedOrderer.Services;

public sealed class FileOrganizerService
{
    private readonly AppSettings _settings;
    private readonly ApplicationLocation _application;
    private readonly IArchiveService _archiveService;
    private readonly IPhotoService _photoService;
    private readonly IMessengerPathService _messengerPathService;
    private readonly IMessageWriter _messageWriter;
    private readonly IStatisticsService _statisticsService;
    private readonly IReadOnlyCollection<ICategory> _categories;
    private readonly IReadOnlyCollection<INonSplittableDirectoryCategory> _nonSplittableCategories;
    private readonly IReadOnlyCollection<ImageCategoryBase> _imageCategories;
    private readonly DriversCategory? _driversCategory;
    private readonly PhotosCategory? _photosCategory;
    private readonly ThreeDPrintingCategory? _printingCategory;
    private readonly ImagesCategory? _imagesCategory;
    private readonly IReadOnlyCollection<ICategoryParsingService> _categoryParsingServices;
    private readonly CategoryCache _categoryCache;
    private readonly UnknownCategory _unknownCategory;
    private readonly HashSet<string> _quarantineExtensions;
    private readonly DeletionQuarantine _quarantine;
    private readonly string[] _softwareArchiveKeywords;
    private int _failedEntries;

    public FileOrganizerService(
        AppSettings settings,
        IArchiveService archiveService,
        IPhotoService photoService,
        IMessengerPathService messengerPathService,
        IEnumerable<ICategory> categories,
        IEnumerable<ICategoryParsingService> categoryParsingServices,
        IStatisticsService statisticsService,
        IMessageWriter messageWriter,
        ApplicationLocation? application = null)
    {
        _settings = settings;
        _application = application ?? ApplicationLocation.Current;
        _archiveService = archiveService;
        _photoService = photoService;
        _messengerPathService = messengerPathService;
        _statisticsService = statisticsService;
        _messageWriter = messageWriter;
        _categories = categories.ToArray();
        _nonSplittableCategories = _categories.OfType<INonSplittableDirectoryCategory>().ToArray();
        _imageCategories = _categories.OfType<ImageCategoryBase>().ToArray();
        _driversCategory = _categories.OfType<DriversCategory>().FirstOrDefault();
        _photosCategory = _imageCategories.OfType<PhotosCategory>().FirstOrDefault();
        _printingCategory = _categories.OfType<ThreeDPrintingCategory>().FirstOrDefault();
        _imagesCategory = _imageCategories.OfType<ImagesCategory>().FirstOrDefault();
        _categoryParsingServices = categoryParsingServices.ToArray();
        _categoryCache = new CategoryCache(_categories);
        _quarantine = new DeletionQuarantine(settings.DestinationRoot);
        _quarantineExtensions = FileUtilities
            .NormalizeExtensions(settings.DeletedExtensions)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _softwareArchiveKeywords = settings.SoftwareArchiveKeywords
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .ToArray();

        _unknownCategory = _categories.OfType<UnknownCategory>().FirstOrDefault()
            ?? throw new InvalidOperationException("Unknown category is missing.");

        ValidateCategoryExtensions();
    }

    internal void ClearQuarantine() => _quarantine.Clear(_application, _settings.SourceDirectory);

    public int Organize()
    {
        _failedEntries = 0;
        if (!Directory.Exists(_settings.SourceDirectory))
        {
            Directory.CreateDirectory(_settings.SourceDirectory);
            _messageWriter.WriteLine($"Created missing source directory at '{_settings.SourceDirectory}'.");
        }

        var createdDestinationDirectory = !Directory.Exists(_settings.DestinationRoot);
        Directory.CreateDirectory(_settings.DestinationRoot);

        var destinationMessage = createdDestinationDirectory
            ? $"Created destination root at '{_settings.DestinationRoot}'."
            : $"Ensured destination root exists at '{_settings.DestinationRoot}'.";
        _messageWriter.WriteLine(destinationMessage);

        new LegacyCategoryMigration(_settings, _application, _categories, _messageWriter).Run();
        TryProcessEntry(_settings.SourceDirectory, () => ProcessDirectory(_settings.SourceDirectory));

        TryProcessEntry(_settings.SourceDirectory, PrintStatistics);
        return _failedEntries;
    }

    private void ProcessDirectory(string directory)
    {
        _messageWriter.WriteLine($"Scanning directory: {directory}");

        foreach (var file in Directory.GetFiles(directory))
        {
            TryProcessEntry(file, () => ProcessFile(file));
        }

        foreach (var subDirectory in Directory.GetDirectories(directory))
        {
            TryProcessEntry(subDirectory, () => ProcessSubdirectory(subDirectory));
        }
    }

    private void ProcessSubdirectory(string subDirectory)
    {
        if (ShouldSkipDirectory(subDirectory))
            return;

        var nonSplittableCategory = _nonSplittableCategories
            .FirstOrDefault(category => category.IsNonSplittableDirectory(subDirectory));

        if (nonSplittableCategory is not null)
        {
            MoveNonSplittableDirectory(subDirectory, nonSplittableCategory);
            return;
        }

        ProcessDirectory(subDirectory);
        if (IsDirectoryEmpty(subDirectory))
        {
            Directory.Delete(subDirectory);
            _statisticsService.RecordDeletedDirectory(subDirectory);
        }
    }

    private void TryProcessEntry(string path, Action process)
    {
        try
        {
            process();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _failedEntries++;
            _messageWriter.WriteLine($"Не удалось обработать «{path}»: {ex.Message}");
        }
    }

    private void ProcessFile(string filePath)
    {
        if (_application.ProtectsFile(filePath))
        {
            return;
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (_quarantineExtensions.Contains(extension))
        {
            var quarantined = _quarantine.MoveFile(filePath);
            _statisticsService.RecordMovedFile(quarantined, DeletionQuarantine.FolderName);
            return;
        }

        var category = ResolveCategory(filePath, extension);
        if (category is null)
        {
            _statisticsService.RecordUnknownFile(extension);
            MoveToUnknown(filePath, extension);
            return;
        }

        switch (category)
        {
            case PhotosCategory:
                var photoDestination = _photoService.MovePhoto(
                    filePath,
                    _settings.DestinationRoot,
                    _settings.PhotosFolderName);
                _statisticsService.RecordMovedFile(photoDestination, category.FolderName);
                break;
            case ImagesCategory:
                var imagesDirectory = Path.Combine(_settings.DestinationRoot, category.FolderName);
                var messengerFolder = _messengerPathService.GetMessengerFolder(filePath);

                if (!string.IsNullOrWhiteSpace(messengerFolder))
                {
                    imagesDirectory = Path.Combine(imagesDirectory, messengerFolder);
                }

                var imageDestination = FileUtilities.MoveFile(filePath, imagesDirectory);
                _statisticsService.RecordMovedFile(imageDestination, category.FolderName);
                break;
            case ThreeDPrintingCategory printing:
                var printingDestination = printing.GetDestinationDirectory(_settings.DestinationRoot, filePath);
                _statisticsService.RecordMovedFile(FileUtilities.MoveFile(filePath, printingDestination), category.FolderName);
                _archiveService.RecordCompanionFileMove(filePath, printingDestination, printing.FolderName);
                break;
            case ArchivesCategory:
                var archiveResult = _archiveService.HandleArchiveFile(filePath, category.FolderName);
                _statisticsService.RecordMovedFile(archiveResult.DestinationPath, archiveResult.CategoryName);
                break;
            default:
                var destinationDirectory = category is INonSplittableDirectoryCategory nonSplittableCategory
                    ? nonSplittableCategory.GetFileDestination(_settings.DestinationRoot, filePath)
                    : Path.Combine(_settings.DestinationRoot, category.FolderName);
                var destination = FileUtilities.MoveFile(filePath, destinationDirectory);
                _statisticsService.RecordMovedFile(destination, category.FolderName);
                if (extension == ".gpx")
                {
                    _archiveService.RecordCompanionFileMove(filePath, destinationDirectory, category.FolderName);
                }
                break;
        }
    }

    private ICategory? ResolveCategory(string filePath, string extension)
    {
        if (_driversCategory is not null && _driversCategory.IsDriverFile(filePath))
        {
            return _driversCategory;
        }

        if (extension == ".json" && _printingCategory?.IsMaterialProfile(filePath) == true)
        {
            return _printingCategory;
        }

        if (extension == ".gpx")
        {
            return FileFormatProbe.ResolveGpxCategory(filePath, _categories);
        }

        if (extension == ".scr" && FileFormatProbe.IsWindowsExecutable(filePath))
        {
            return _categories.OfType<SoftCategory>().FirstOrDefault();
        }

        var parsedCategory = TryResolveWithParsingServices(filePath, extension);
        if (parsedCategory is not null)
        {
            return parsedCategory;
        }

        if (_categoryCache.ImageCategoryExtensions.Contains(extension))
        {
            if (_photosCategory is null || _imagesCategory is null)
            {
                return _imageCategories.FirstOrDefault(category => category.Matches(extension));
            }

            return _photoService.IsPhoto(filePath)
                ? _photosCategory
                : _imagesCategory;
        }

        return _categoryCache.CategoriesByExtension.TryGetValue(extension, out var category)
            ? category
            : null;
    }

    private ICategory? TryResolveWithParsingServices(string filePath, string extension)
    {
        var matchingCategories = _categories
            .Where(category => category.Matches(extension) || IsDocumentImageCategoryCandidate(category, extension))
            .ToArray();

        if (matchingCategories.Length == 0)
        {
            return null;
        }

        foreach (var parsingService in _categoryParsingServices)
        {
            foreach (var category in matchingCategories)
            {
                if (IsFileOfCategory(parsingService, category, filePath))
                {
                    return category;
                }
            }
        }

        return null;
    }

    private bool IsDocumentImageCategoryCandidate(ICategory category, string extension)
    {
        return category is DocumentsCategory && _categoryCache.ImageCategoryExtensions.Contains(extension);
    }

    private static bool IsFileOfCategory(
        ICategoryParsingService parsingService,
        ICategory category,
        string filePath)
    {
        var methodInfo = parsingService
            .GetType()
            .GetMethod(nameof(ICategoryParsingService.IsFileOfCategory));

        if (methodInfo is null)
        {
            return false;
        }

        var genericMethod = methodInfo.MakeGenericMethod(category.GetType());
        var result = genericMethod.Invoke(parsingService, new object[] { filePath });
        return result is bool matched && matched;
    }

    private void MoveNonSplittableDirectory(string directory, INonSplittableDirectoryCategory category)
    {
        var sourceDirectory = category is RepositoriesCategory repositoriesCategory
            ? repositoriesCategory.GetRepositoryRoot(directory) ?? directory
            : directory;

        var destinationPath = category.GetDirectoryDestination(_settings.DestinationRoot, sourceDirectory);
        var fileCount = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories).Count();
        DirectoryTransfer.Move(sourceDirectory, destinationPath);

        _statisticsService.RecordMovedNonSplittableDirectory(category, destinationPath, fileCount);
    }

    private bool ShouldSkipDirectory(string directory) =>
        _application.ProtectsDirectory(directory)
        || ApplicationLocation.ContainsPath(directory, _settings.DestinationRoot)
        || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;

    private static bool IsDirectoryEmpty(string path)
    {
        return !Directory.EnumerateFileSystemEntries(path).Any();
    }

    private void MoveToUnknown(string filePath, string extension)
    {
        var extensionFolderName = string.IsNullOrWhiteSpace(extension)
            ? "no-extension"
            : extension.TrimStart('.')
                .Replace(Path.DirectorySeparatorChar, '-')
            .Replace(Path.AltDirectorySeparatorChar, '-');

        var unknownDirectory = Path.Combine(_settings.DestinationRoot, _unknownCategory.FolderName, extensionFolderName);
        var destination = FileUtilities.MoveFile(filePath, unknownDirectory);
        _statisticsService.RecordMovedFile(destination, _unknownCategory.FolderName);
    }

    private void ValidateCategoryExtensions()
    {
        var extensionToCategories = new Dictionary<string, List<ICategory>>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in _categories)
        {
            foreach (var extension in category.Extensions)
            {
                if (!extensionToCategories.TryGetValue(extension, out var categories))
                {
                    categories = new List<ICategory>();
                    extensionToCategories[extension] = categories;
                }

                categories.Add(category);
            }
        }

        var overlaps = extensionToCategories
            .Where(kvp => kvp.Value.Count > 1)
            .Where(kvp => !kvp.Value.All(category => category is ImageCategoryBase))
            .ToArray();

        if (overlaps.Length == 0)
        {
            return;
        }

        var overlapDetails = overlaps
            .Select(kvp =>
            {
                var names = kvp.Value
                    .Select(c => c.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);

                return $"{kvp.Key}: {string.Join(", ", names)}";
            });

        throw new InvalidOperationException($"Category extensions overlap: {string.Join("; ", overlapDetails)}");
    }

    private void PrintStatistics()
    {
        _statisticsService.PrintStatistics(_settings.SourceDirectory);
    }
}

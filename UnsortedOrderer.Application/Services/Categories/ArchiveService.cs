using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnsortedOrderer.Application.Contracts.Services.Categories;
using UnsortedOrderer.Categories;
using UnsortedOrderer.Contracts.Categories;
using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;
using UnsortedOrderer.Services;

namespace UnsortedOrderer.Application.Services.Categories;

public sealed class ArchiveService : IArchiveService
{
    private readonly AppSettings _settings;
    private readonly IReadOnlyCollection<ICategory> _categories;
    private readonly IReadOnlyCollection<INonSplittableDirectoryCategory> _nonSplittableCategories;
    private readonly IStatisticsService _statisticsService;
    private readonly DeletionQuarantine _quarantine;
    private readonly ApplicationLocation _application;
    private readonly string[] _softwareArchiveKeywords;
    private readonly Dictionary<string, List<CompanionFileMove>> _companionMovesBySourceDirectory =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public ArchiveService(
        AppSettings settings,
        IEnumerable<ICategory> categories,
        IStatisticsService statisticsService,
        ApplicationLocation? application = null)
    {
        _settings = settings;
        _categories = categories.ToArray();
        _nonSplittableCategories = _categories
            .OfType<INonSplittableDirectoryCategory>()
            .ToArray();
        _statisticsService = statisticsService;
        _quarantine = new DeletionQuarantine(settings.DestinationRoot);
        _application = application ?? ApplicationLocation.Current;
        _softwareArchiveKeywords = settings.SoftwareArchiveKeywords ?? Array.Empty<string>();
    }

    public ArchiveHandlingResult HandleArchiveFile(string filePath, string categoryName)
    {
        if (IsSoftwareArchive(filePath))
        {
            RemoveExistingDistributionDirectory(filePath);
            var softArchiveDestination = Path.Combine(_settings.DestinationRoot, _settings.SoftFolderName);
            var movedArchive = HandleArchive(filePath, softArchiveDestination);
            return new ArchiveHandlingResult(movedArchive, _settings.SoftFolderName);
        }

        var sibling = FindMatchingSibling(filePath);
        var companion = GetLiveCompanionDestination(sibling) ?? FindMovedCompanion(filePath)?.Destination;
        if (companion is { } target)
        {
            // An identified companion from this run takes precedence over older same-named files.
            var archive = FileUtilities.MoveFile(filePath, target.Directory);
            return new ArchiveHandlingResult(archive, target.CategoryName);
        }

        RemoveExistingDistributionDirectory(filePath);
        if (sibling is { } matching)
        {
            var matchingDestinationDirectory = Path.Combine(_settings.DestinationRoot, matching.Category.FolderName);
            var matchingArchiveDestination = HandleArchive(filePath, matchingDestinationDirectory);
            return new ArchiveHandlingResult(matchingArchiveDestination, matching.Category.FolderName);
        }

        var archiveDestinationDirectory = Path.Combine(_settings.DestinationRoot, _settings.ArchiveFolderName);
        var archiveDestination = HandleArchive(filePath, archiveDestinationDirectory);
        return new ArchiveHandlingResult(archiveDestination, categoryName);
    }

    private (string Directory, string CategoryName)? GetLiveCompanionDestination((ICategory Category, string Path)? sibling)
    {
        if (sibling is not { } file)
            return null;

        if (file.Category is ThreeDPrintingCategory printing)
            return (printing.GetDestinationDirectory(_settings.DestinationRoot, file.Path), printing.FolderName);

        if (file.Category is GpsTracksCategory ||
            file.Category is MusicalInstrumentsCategory && Path.GetExtension(file.Path).Equals(".gpx", StringComparison.OrdinalIgnoreCase))
            return (Path.Combine(_settings.DestinationRoot, file.Category.FolderName), file.Category.FolderName);

        return null;
    }

    public void RecordCompanionFileMove(string sourcePath, string destinationDirectory, string categoryName)
    {
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        if (!_companionMovesBySourceDirectory.TryGetValue(sourceDirectory, out var moves))
        {
            moves = [];
            _companionMovesBySourceDirectory.Add(sourceDirectory, moves);
        }

        moves.Add(new CompanionFileMove(Path.GetFileNameWithoutExtension(sourcePath), (destinationDirectory, categoryName)));
    }

    private CompanionFileMove? FindMovedCompanion(string archivePath)
    {
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(archivePath))!;
        if (!_companionMovesBySourceDirectory.TryGetValue(sourceDirectory, out var moves))
        {
            return null;
        }

        var archiveStem = Path.GetFileNameWithoutExtension(archivePath);
        return (moves.Find(move => string.Equals(archiveStem, move.Stem, StringComparison.OrdinalIgnoreCase))
            ?? moves.Find(move => IsHalfNameMatch(archiveStem, move.Stem)));
    }

    private sealed record CompanionFileMove(string Stem, (string Directory, string CategoryName) Destination);

    public string HandleArchive(string archivePath, string destinationDirectory)
    {
        var finalDestinationDirectory = FindMatchingDestinationDirectory(archivePath)
            ?? destinationDirectory;

        Directory.CreateDirectory(finalDestinationDirectory);

        var destinationPath = Path.Combine(finalDestinationDirectory, Path.GetFileName(archivePath));
        if (File.Exists(destinationPath))
        {
            if (ApplicationLocation.SamePath(archivePath, destinationPath))
                return destinationPath;
            var preserved = _quarantine.MoveFile(destinationPath);
            _statisticsService.RecordMovedFile(preserved, DeletionQuarantine.FolderName);
        }

        File.Move(archivePath, destinationPath);

        return destinationPath;
    }

    private void RemoveExistingDistributionDirectory(string archivePath)
    {
        var archiveName = Path.GetFileNameWithoutExtension(archivePath) ?? string.Empty;
        var potentialDistributionDirectory = Path.Combine(
            _settings.DestinationRoot,
            _settings.SoftFolderName,
            archiveName);

        if (Directory.Exists(potentialDistributionDirectory)
            && !_application.ProtectsDirectory(potentialDistributionDirectory)
            && (File.GetAttributes(potentialDistributionDirectory) & FileAttributes.ReparsePoint) == 0
            && !ApplicationLocation.ContainsPath(potentialDistributionDirectory, archivePath))
        {
            var preserved = _quarantine.MoveDirectory(potentialDistributionDirectory);
            _statisticsService.RecordMovedFile(preserved, DeletionQuarantine.FolderName);
        }
    }

    private (ICategory Category, string Path)? FindMatchingSibling(string archivePath)
    {
        var directory = Path.GetDirectoryName(archivePath);
        var archiveName = Path.GetFileNameWithoutExtension(archivePath);

        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(archiveName))
        {
            return null;
        }

        foreach (var siblingPath in Directory.EnumerateFileSystemEntries(directory))
        {
            if (string.Equals(siblingPath, archivePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var siblingName = Directory.Exists(siblingPath)
                ? Path.GetFileName(siblingPath)
                : Path.GetFileNameWithoutExtension(siblingPath);

            if (!IsHalfNameMatch(archiveName, siblingName))
            {
                continue;
            }

            if (Directory.Exists(siblingPath))
            {
                var siblingCategory = _nonSplittableCategories
                    .FirstOrDefault(category => category.IsNonSplittableDirectory(siblingPath));

                if (siblingCategory is not null)
                {
                    return (siblingCategory, siblingPath);
                }

                continue;
            }

            var matchedExtension = Path.GetExtension(siblingPath).ToLowerInvariant();
            var category = matchedExtension == ".gpx" ? FileFormatProbe.ResolveGpxCategory(siblingPath, _categories)
                : MapTrackProbe.IsTrack(siblingPath) && _categories.OfType<GpsTracksCategory>().FirstOrDefault() is { } gps
                    ? gps
                : _categories.FirstOrDefault(c => c.Matches(matchedExtension)
                || (matchedExtension == ".json" && c is ThreeDPrintingCategory printing && printing.IsMaterialProfile(siblingPath)));
            if (category is not null)
            {
                return (category, siblingPath);
            }
        }

        return null;
    }

    private bool IsSoftwareArchive(string archivePath)
    {
        if (_softwareArchiveKeywords.Length == 0)
        {
            return false;
        }

        var archiveName = Path.GetFileNameWithoutExtension(archivePath) ?? string.Empty;
        return _softwareArchiveKeywords.Any(keyword =>
            archiveName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private string? FindMatchingDestinationDirectory(string archivePath)
    {
        var archiveName = Path.GetFileNameWithoutExtension(archivePath);
        if (string.IsNullOrWhiteSpace(archiveName))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(_settings.DestinationRoot) && Directory.Exists(_settings.DestinationRoot))
        {
            var destinationSiblingDirectory = FindMatchingDirectoryInDestinationRoot(archiveName);
            if (destinationSiblingDirectory is not null)
            {
                return destinationSiblingDirectory;
            }
        }

        return null;
    }

    private string? FindMatchingDirectoryInDestinationRoot(string archiveName)
    {
        var pending = new Stack<string>();
        pending.Push(_settings.DestinationRoot);
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        while (pending.TryPop(out var folder))
        {
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
            {
                if (ApplicationLocation.SamePath(entry.FullName, _quarantine.DirectoryPath))
                    continue;

                var isDirectory = entry is DirectoryInfo;
                var siblingName = isDirectory ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name);
                if (IsHalfNameMatch(archiveName, siblingName))
                    return isDirectory ? entry.FullName : folder;

                if (isDirectory)
                    pending.Push(entry.FullName);
            }
        }

        return null;
    }

    private static bool IsHalfNameMatch(string archiveName, string siblingName)
    {
        if (string.IsNullOrWhiteSpace(archiveName) || string.IsNullOrWhiteSpace(siblingName))
        {
            return false;
        }

        var compareLength = Math.Min(archiveName.Length, siblingName.Length) / 2;
        if (compareLength == 0)
        {
            return false;
        }

        return archiveName
            .AsSpan(0, compareLength)
            .Equals(siblingName.AsSpan(0, compareLength), StringComparison.OrdinalIgnoreCase);
    }
}

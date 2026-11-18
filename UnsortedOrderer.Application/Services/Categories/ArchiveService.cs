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
    private readonly string[] _softwareArchiveKeywords;
    private readonly ThreeDPrintingCategory? _printingCategory;
    private readonly Dictionary<string, List<PrintingFileMove>> _printingMovesBySourceDirectory =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public ArchiveService(
        AppSettings settings,
        IEnumerable<ICategory> categories,
        IStatisticsService statisticsService)
    {
        _settings = settings;
        _categories = categories.ToArray();
        _printingCategory = _categories.OfType<ThreeDPrintingCategory>().FirstOrDefault();
        _nonSplittableCategories = _categories
            .OfType<INonSplittableDirectoryCategory>()
            .ToArray();
        _statisticsService = statisticsService;
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
        if (_printingCategory is not null)
        {
            var printingDestination = sibling?.Category is ThreeDPrintingCategory
                ? _printingCategory.GetDestinationDirectory(_settings.DestinationRoot, sibling.Value.Path)
                : FindMovedPrintingSiblingDirectory(filePath);
            if (printingDestination is not null)
            {
                // A sibling from this run takes precedence over old same-named files in the destination.
                var archive = FileUtilities.MoveFile(filePath, printingDestination);
                return new ArchiveHandlingResult(archive, _printingCategory.FolderName);
            }
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

    public void RecordPrintingFileMove(string sourcePath, string destinationDirectory)
    {
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        if (!_printingMovesBySourceDirectory.TryGetValue(sourceDirectory, out var moves))
        {
            moves = [];
            _printingMovesBySourceDirectory.Add(sourceDirectory, moves);
        }

        moves.Add(new PrintingFileMove(Path.GetFileNameWithoutExtension(sourcePath), destinationDirectory));
    }

    private string? FindMovedPrintingSiblingDirectory(string archivePath)
    {
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(archivePath))!;
        if (!_printingMovesBySourceDirectory.TryGetValue(sourceDirectory, out var moves))
        {
            return null;
        }

        var archiveStem = Path.GetFileNameWithoutExtension(archivePath);
        return (moves.Find(move => string.Equals(archiveStem, move.Stem, StringComparison.OrdinalIgnoreCase))
            ?? moves.Find(move => IsHalfNameMatch(archiveStem, move.Stem)))?.DestinationDirectory;
    }

    private sealed record PrintingFileMove(string Stem, string DestinationDirectory);

    public string HandleArchive(string archivePath, string destinationDirectory)
    {
        var finalDestinationDirectory = FindMatchingDestinationDirectory(archivePath)
            ?? destinationDirectory;

        Directory.CreateDirectory(finalDestinationDirectory);

        var destinationPath = Path.Combine(finalDestinationDirectory, Path.GetFileName(archivePath));
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
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

        if (Directory.Exists(potentialDistributionDirectory))
        {
            _statisticsService.RecordDeletedDirectory(potentialDistributionDirectory);
            Directory.Delete(potentialDistributionDirectory, recursive: true);
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
            var category = _categories.FirstOrDefault(c => c.Matches(matchedExtension)
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
        foreach (var entry in Directory.EnumerateFileSystemEntries(_settings.DestinationRoot, "*", SearchOption.AllDirectories))
        {
            var siblingName = Directory.Exists(entry)
                ? Path.GetFileName(entry)
                : Path.GetFileNameWithoutExtension(entry);

            if (!IsHalfNameMatch(archiveName, siblingName))
            {
                continue;
            }

            return Directory.Exists(entry)
                ? entry
                : Path.GetDirectoryName(entry);
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

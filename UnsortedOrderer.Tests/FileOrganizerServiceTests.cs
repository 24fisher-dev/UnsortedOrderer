using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnsortedOrderer.Application.Contracts.Services.Categories;
using UnsortedOrderer.Application.Contracts.Services.Categories.Photo;
using UnsortedOrderer.Application.Services.Categories;
using UnsortedOrderer.Categories;
using UnsortedOrderer.Contracts.Categories;
using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;
using UnsortedOrderer.Services;
using Xunit;

namespace UnsortedOrderer.Tests;

public partial class FileOrganizerServiceTests
{
    [Fact]
    public void RepositoryInsideWrapperDirectories_IsMovedAndEmptyWrappersAreRemoved()
    {
        using var source = new TempDirectory();
        using var destination = new TempDirectory();

        var outer = System.IO.Path.Combine(source.Path, "outer");
        var inner = System.IO.Path.Combine(outer, "inner");
        var repositoryRoot = System.IO.Path.Combine(inner, "my-repo");

        Directory.CreateDirectory(repositoryRoot);
        Directory.CreateDirectory(System.IO.Path.Combine(repositoryRoot, ".git"));
        // A wrapper must meet the recursive code threshold to be recognized before traversal.
        File.WriteAllText(System.IO.Path.Combine(repositoryRoot, "Program.cs"), "class Program { }");
        File.WriteAllText(System.IO.Path.Combine(repositoryRoot, "Input.cs"), "class Input { }");
        File.WriteAllText(System.IO.Path.Combine(repositoryRoot, "Output.cs"), "class Output { }");

        var settings = new AppSettings(
            source.Path,
            destination.Path,
            softFolderName: "Soft",
            archiveFolderName: "Archives",
            imagesFolderName: "Images",
            photosFolderName: "Photos",
            musicFolderName: "Music",
            musicalInstrumentsFolderName: "Instruments",
            eBooksFolderName: "EBooks",
            repositoriesFolderName: "Repositories",
            driversFolderName: "Drivers",
            firmwareFolderName: "Firmware",
            metadataFolderName: "Metadata",
            webFolderName: "Web",
            graphicsFolderName: "Graphics",
            unknownFolderName: "Unknown",
            deletedExtensions: Array.Empty<string>(),
            documentImageKeywords: Array.Empty<string>(),
            cameraFileNamePatterns: Array.Empty<DeviceBrandPattern>(),
            softwareArchiveKeywords: Array.Empty<string>());

        var categories = new ICategory[]
        {
            new RepositoriesCategory(settings.RepositoriesFolderName, new RepositoriesService(RepositoriesCategory.CodeExtensions)),
            new UnknownCategory(settings.UnknownFolderName)
        };

        var organizer = new FileOrganizerService(
            settings,
            new StubArchiveService(),
            new StubPhotoService(isPhoto: false),
            new StubMessengerPathService(),
            categories,
            Array.Empty<ICategoryParsingService>(),
            new RecordingStatisticsService(),
            new StubMessageWriter());

        Assert.Equal(0, organizer.Organize());

        var repositoriesDestination = System.IO.Path.Combine(destination.Path, settings.RepositoriesFolderName);
        var expectedRepositoryPath = System.IO.Path.Combine(repositoriesDestination, "my-repo");

        Assert.True(Directory.Exists(expectedRepositoryPath));
        Assert.False(Directory.Exists(System.IO.Path.Combine(repositoriesDestination, "outer")));
        Assert.False(Directory.Exists(outer));
    }

    [Fact]
    public void MusicDirectory_IsMovedWhole()
    {
        using var source = new TempDirectory();
        using var destination = new TempDirectory();

        var albumDirectory = System.IO.Path.Combine(source.Path, "Album");
        Directory.CreateDirectory(albumDirectory);
        File.WriteAllText(System.IO.Path.Combine(albumDirectory, "track01.mp3"), "");
        File.WriteAllText(System.IO.Path.Combine(albumDirectory, "cover.png"), "");

        var settings = new AppSettings(
            source.Path,
            destination.Path,
            softFolderName: "Soft",
            archiveFolderName: "Archives",
            imagesFolderName: "Images",
            photosFolderName: "Photos",
            musicFolderName: "Music",
            musicalInstrumentsFolderName: "Instruments",
            eBooksFolderName: "EBooks",
            repositoriesFolderName: "Repositories",
            driversFolderName: "Drivers",
            firmwareFolderName: "Firmware",
            metadataFolderName: "Metadata",
            webFolderName: "Web",
            graphicsFolderName: "Graphics",
            unknownFolderName: "Unknown",
            deletedExtensions: Array.Empty<string>(),
            documentImageKeywords: Array.Empty<string>(),
            cameraFileNamePatterns: Array.Empty<DeviceBrandPattern>(),
            softwareArchiveKeywords: Array.Empty<string>());

        var categories = new ICategory[]
        {
            new MusicCategory(settings.MusicFolderName, new MusicService()),
            new UnknownCategory(settings.UnknownFolderName)
        };

        var organizer = new FileOrganizerService(
            settings,
            new StubArchiveService(),
            new StubPhotoService(isPhoto: false),
            new StubMessengerPathService(),
            categories,
            Array.Empty<ICategoryParsingService>(),
            new RecordingStatisticsService(),
            new StubMessageWriter());

        InvokeProcessDirectory(organizer, source.Path);

        var expectedAlbumPath = System.IO.Path.Combine(destination.Path, settings.MusicFolderName, "Album");
        Assert.True(Directory.Exists(expectedAlbumPath));
        Assert.False(Directory.Exists(albumDirectory));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ArchiveFollowsPrintingModel_WithoutOverwritingOtherArchives(bool archiveFirst, bool sameMonthCollision)
    {
        using var source = new TempDirectory();
        using var destination = new TempDirectory();

        var stlPath = System.IO.Path.Combine(source.Path, "oneup plug.stl");
        var archivePath = System.IO.Path.Combine(source.Path, "oneup plug.zip");

        File.WriteAllText(stlPath, "dummy-stl");
        File.WriteAllText(archivePath, "dummy-zip");
        var date = File.GetCreationTime(stlPath);
        var printingRoot = Path.Combine(destination.Path, "3DPrinting");
        var monthFolder = Path.Combine(printingRoot, date.Year.ToString("D4"), date.Month.ToString("D2"));
        var olderDate = date.AddMonths(-1);
        var previousMonthFolder = Path.Combine(printingRoot, olderDate.Year.ToString("D4"), olderDate.Month.ToString("D2"));
        Directory.CreateDirectory(previousMonthFolder);
        var oldArchive = Path.Combine(previousMonthFolder, "oneup plug.zip");
        File.WriteAllText(oldArchive, "previous archive");
        File.WriteAllText(Path.Combine(previousMonthFolder, "oneup plug.stl"), "previous model");
        var existingCurrentArchive = Path.Combine(monthFolder, "oneup plug.zip");
        if (sameMonthCollision)
        {
            Directory.CreateDirectory(monthFolder);
            File.WriteAllText(existingCurrentArchive, "current existing archive");
        }


        var settings = new AppSettings(
            source.Path,
            destination.Path,
            softFolderName: "Soft",
            archiveFolderName: "Archives",
            imagesFolderName: "Images",
            photosFolderName: "Photos",
            musicFolderName: "Music",
            musicalInstrumentsFolderName: "Instruments",
            eBooksFolderName: "EBooks",
            repositoriesFolderName: "Repositories",
            driversFolderName: "Drivers",
            firmwareFolderName: "Firmware",
            metadataFolderName: "Metadata",
            webFolderName: "Web",
            graphicsFolderName: "Graphics",
            unknownFolderName: "Unknown",
            deletedExtensions: Array.Empty<string>(),
            documentImageKeywords: Array.Empty<string>(),
            cameraFileNamePatterns: Array.Empty<DeviceBrandPattern>(),
            softwareArchiveKeywords: Array.Empty<string>());

        var categories = new ICategory[]
        {
            new ThreeDPrintingCategory(),
            new ArchivesCategory(settings.ArchiveFolderName),
            new UnknownCategory(settings.UnknownFolderName)
        };

        var statistics = new RecordingStatisticsService();

        var organizer = new FileOrganizerService(
            settings,
            new ArchiveService(settings, categories, statistics),
            new StubPhotoService(isPhoto: false),
            new StubMessengerPathService(),
            categories,
            Array.Empty<ICategoryParsingService>(),
            statistics,
            new StubMessageWriter());

        InvokeProcessFile(organizer, archiveFirst ? archivePath : stlPath);
        InvokeProcessFile(organizer, archiveFirst ? stlPath : archivePath);

        var movedModel = Path.Combine(monthFolder, "oneup plug.stl");
        var movedArchive = Path.Combine(monthFolder, sameMonthCollision ? "oneup plug(1).zip" : "oneup plug.zip");

        Assert.Equal("dummy-stl", File.ReadAllText(movedModel));
        Assert.Equal("dummy-zip", File.ReadAllText(movedArchive));
        Assert.Equal("previous archive", File.ReadAllText(oldArchive));
        if (sameMonthCollision)
        {
            Assert.Equal("current existing archive", File.ReadAllText(existingCurrentArchive));
        }
    }

    [Fact]
    public void ArchiveUsesDestinationDirectory_WhenSiblingOnlyExistsInSource()
    {
        using var source = new TempDirectory();
        using var destination = new TempDirectory();

        var siblingPath = System.IO.Path.Combine(source.Path, "demo.exe");
        var archivePath = System.IO.Path.Combine(source.Path, "demo.zip");

        File.WriteAllText(siblingPath, "exe");
        File.WriteAllText(archivePath, "zip");

        var settings = new AppSettings(
            source.Path,
            destination.Path,
            softFolderName: "Soft",
            archiveFolderName: "Archives",
            imagesFolderName: "Images",
            photosFolderName: "Photos",
            musicFolderName: "Music",
            musicalInstrumentsFolderName: "Instruments",
            eBooksFolderName: "EBooks",
            repositoriesFolderName: "Repositories",
            driversFolderName: "Drivers",
            firmwareFolderName: "Firmware",
            metadataFolderName: "Metadata",
            webFolderName: "Web",
            graphicsFolderName: "Graphics",
            unknownFolderName: "Unknown",
            deletedExtensions: Array.Empty<string>(),
            documentImageKeywords: Array.Empty<string>(),
            cameraFileNamePatterns: Array.Empty<DeviceBrandPattern>(),
            softwareArchiveKeywords: Array.Empty<string>());

        var categories = new ICategory[]
        {
            new ArchivesCategory(settings.ArchiveFolderName),
            new UnknownCategory(settings.UnknownFolderName)
        };

        var statistics = new RecordingStatisticsService();

        var organizer = new FileOrganizerService(
            settings,
            new ArchiveService(settings, categories, statistics),
            new StubPhotoService(isPhoto: false),
            new StubMessengerPathService(),
            categories,
            Array.Empty<ICategoryParsingService>(),
            statistics,
            new StubMessageWriter());

        InvokeProcessFile(organizer, archivePath);

        var archiveDestination = System.IO.Path.Combine(destination.Path, settings.ArchiveFolderName, "demo.zip");

        Assert.False(File.Exists(archivePath));
        Assert.True(File.Exists(archiveDestination));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mixed_3d_printing_project_with_model_photos_moves_as_one_folder(bool includeWindowsMetadata)
    {
        using var source = new TempDirectory();
        using var destination = new TempDirectory();

        var projectName = "A4Tech KR-85 legs";
        var project = Path.Combine(source.Path, projectName);
        Directory.CreateDirectory(project);
        var files = new Dictionary<string, string>
        {
            ["KR85_pair.3mf"] = "project-file",
            ["KR85_pair.stl"] = "print-model",
            ["photo_2026-11-25_13-02-16.jpg"] = "photo-1",
            ["photo_2026-11-25_13-02-23.jpg"] = "photo-2",
            ["photo_2026-11-25_13-05-59.jpg"] = "photo-3",
            ["photo_2026-11-25_13-38-30.jpg"] = "photo-4",
            ["Деталь ножки.png"] = "preview",
            ["Вид снизу.png"] = "render"
        };
        if (includeWindowsMetadata)
        {
            files["desktop.ini"] = "system metadata";
            files["Thumbs.db"] = "thumbnail cache";
        }
        foreach (var file in files)
            File.WriteAllText(Path.Combine(project, file.Key), file.Value);

        var model = Path.Combine(project, "KR85_pair.3mf");
        var date = File.GetCreationTime(model);
        var settings = CreatePrintingSettings(source.Path, destination.Path);
        var categories = new ICategory[]
        {
            new ThreeDPrintingCategory(),
            new UnknownCategory(settings.UnknownFolderName)
        };
        var organizer = CreatePrintingOrganizer(settings, categories);

        Assert.Equal(0, organizer.Organize());

        var movedProject = Path.Combine(destination.Path, "3DPrinting",
            date.ToString("yyyy"), date.ToString("MM"), projectName);
        Assert.True(Directory.Exists(movedProject));
        Assert.False(Directory.Exists(project));
        Assert.Equal(files.Keys.OrderBy(x => x), Directory.GetFiles(movedProject)
            .Select(Path.GetFileName).OrderBy(x => x));
        foreach (var file in files)
            Assert.Equal(file.Value, File.ReadAllText(Path.Combine(movedProject, file.Key)));
        Assert.False(Directory.Exists(Path.Combine(destination.Path, "Photos")));
        Assert.False(Directory.Exists(Path.Combine(destination.Path, "Images")));
    }

    [Fact]
    public void Printing_project_does_not_swallow_unrelated_installer_or_parent_folder()
    {
        using var source = new TempDirectory();
        using var destination = new TempDirectory();
        var parent = Path.Combine(source.Path, "Mixed");
        var project = Path.Combine(parent, "A4Tech KR-85 legs");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "part.stl"), "model");
        File.WriteAllText(Path.Combine(project, "photo.jpg"), "photo");
        File.WriteAllText(Path.Combine(parent, "installer.exe"), "different app");
        var printing = new ThreeDPrintingCategory();
        Assert.True(printing.IsNonSplittableDirectory(project));
        Assert.False(printing.IsNonSplittableDirectory(parent));

        File.WriteAllText(Path.Combine(project, "installer.exe"), "not part of a print");
        Assert.False(printing.IsNonSplittableDirectory(project));
    }

    [Fact]
    public void Printing_project_with_an_existing_destination_does_not_overwrite_old_project()
    {
        using var source = new TempDirectory();
        using var destination = new TempDirectory();
        var project = Path.Combine(source.Path, "A4Tech KR-85 legs");
        Directory.CreateDirectory(project);
        var model = Path.Combine(project, "part.3mf");
        File.WriteAllText(model, "new model");
        File.WriteAllText(Path.Combine(project, "photo.png"), "new photo");
        var date = File.GetCreationTime(model);
        var destinationMonth = Path.Combine(destination.Path, "3DPrinting", date.ToString("yyyy"), date.ToString("MM"));
        var existingProject = Path.Combine(destinationMonth, "A4Tech KR-85 legs");
        Directory.CreateDirectory(existingProject);
        File.WriteAllText(Path.Combine(existingProject, "part.3mf"), "old model");

        var settings = CreatePrintingSettings(source.Path, destination.Path);
        var organizer = CreatePrintingOrganizer(settings, [new ThreeDPrintingCategory(), new UnknownCategory(settings.UnknownFolderName)]);
        Assert.Equal(0, organizer.Organize());

        Assert.Equal("old model", File.ReadAllText(Path.Combine(existingProject, "part.3mf")));
        var newProject = Path.Combine(destinationMonth, "A4Tech KR-85 legs(1)");
        Assert.Equal("new model", File.ReadAllText(Path.Combine(newProject, "part.3mf")));
        Assert.Equal("new photo", File.ReadAllText(Path.Combine(newProject, "photo.png")));
    }

    private static AppSettings CreatePrintingSettings(string source, string destination) =>
        new(source, destination, softFolderName: "Soft", archiveFolderName: "Archives",
            imagesFolderName: "Images", photosFolderName: "Photos",
            musicFolderName: "Music", musicalInstrumentsFolderName: "Instruments",
            eBooksFolderName: "EBooks", repositoriesFolderName: "Repositories",
            driversFolderName: "Drivers", firmwareFolderName: "Firmware",
            metadataFolderName: "Metadata", webFolderName: "Web", graphicsFolderName: "Graphics",
            unknownFolderName: "Unknown", deletedExtensions: [],
            documentImageKeywords: [], cameraFileNamePatterns: [],
            softwareArchiveKeywords: []);

    private static FileOrganizerService CreatePrintingOrganizer(AppSettings settings, ICategory[] categories) =>
        new(settings, new StubArchiveService(), new StubPhotoService(isPhoto: true),
            new StubMessengerPathService(), categories, [],
            new RecordingStatisticsService(), new StubMessageWriter());

    private static void InvokeProcessFile(FileOrganizerService organizer, string filePath)
    {
        var method = typeof(FileOrganizerService)
            .GetMethod("ProcessFile", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ProcessFile method not found.");

        method.Invoke(organizer, new object[] { filePath });
    }

    private static void InvokeProcessDirectory(FileOrganizerService organizer, string directoryPath)
    {
        var method = typeof(FileOrganizerService)
            .GetMethod("ProcessDirectory", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ProcessDirectory method not found.");

        method.Invoke(organizer, new object[] { directoryPath });
    }

    private sealed class StubArchiveService : IArchiveService
    {
        public void RecordCompanionFileMove(string sourcePath, string destinationDirectory, string categoryName) { }

        public ArchiveHandlingResult HandleArchiveFile(string archivePath, string categoryName)
        {
            var destinationDirectory = Path.Combine(
                Path.GetDirectoryName(archivePath) ?? string.Empty,
                categoryName);
            var destination = HandleArchive(archivePath, destinationDirectory);
            return new ArchiveHandlingResult(destination, categoryName);
        }

        public string HandleArchive(string archivePath, string destinationDirectory)
        {
            return FileUtilities.MoveFile(archivePath, destinationDirectory);
        }
    }

    private sealed class StubMessengerPathService : IMessengerPathService
    {
        public string? GetMessengerFolder(string filePath) => null;
    }

    private sealed class RecordingStatisticsService : IStatisticsService
    {
        private readonly List<(string destination, string category)> _movedFiles = new();

        public IReadOnlyCollection<(string destination, string category)> MovedFiles => _movedFiles;

        public void RecordMovedFile(string destinationPath, string category)
        {
            _movedFiles.Add((destinationPath, category));
        }

        public void RecordMovedFiles(int count, string category)
        {
        }

        public void RecordMovedNonSplittableDirectory(INonSplittableDirectoryCategory category, string destinationPath, int fileCount)
        {
        }

        public void RecordDeletedDirectory(string directory)
        {
        }

        public void RecordUnknownFile(string extension)
        {
        }

        public void PrintStatistics(string sourceDirectory)
        {
        }
    }

    private sealed class StubMessageWriter : IMessageWriter
    {
        public void WriteLine(string message)
        {
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup errors in tests
            }
        }
    }
}

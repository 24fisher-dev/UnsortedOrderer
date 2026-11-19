# UnsortedOrderer

Standalone Windows Forms application for organizing a folder full of unsorted files. The published executable includes .NET and the default configuration. The default source is `C:\_Unsorted` and the destination is `C:\_Sorted`. To use custom settings, put a complete `appsettings.json` next to the executable. A full example is available in [UnsortedOrderer.WinForms/appsettings.json](UnsortedOrderer.WinForms/appsettings.json).

## Configuration

- `SourceDirectory` — path to the folder you want to tidy up.
- `DestinationRoot` — root directory where sorted data will be moved.
- `SoftFolderName` — subfolder name for installers and software packages.
- `ArchiveFolderName` — subfolder name for archives.
- `ImagesFolderName` — subfolder name for icons, logos, and other small images.
- `PhotosFolderName` — subfolder name for large photos; within it, the app creates year and month subdirectories based on photo capture data.
- `RepositoriesFolderName` — subfolder name for repositories and source folders.
- `FirmwareFolderName` — subfolder name for firmware files (`.bin`, `.dat`).
- `MetadataFolderName` — subfolder name for metadata and sidecar files (for example, `.hprj`, `.xmp`).
- `WebFolderName` — subfolder name for web shortcuts and saved HTML pages.
- `GraphicsFolderName` — subfolder name for layered graphics and illustration project files.
- `EBooksFolderName` — subfolder name for e-books and comics.

## Sorting logic

- Large photos (including RAW) are moved to `PhotosFolderName` with a year/month breakdown determined from EXIF or file creation date. If EXIF dates are earlier than 1980, the file creation date is used instead.
- Small images (icons, previews, logos) go to the `images` subfolder inside `ImagesFolderName` without date breakdown. Size is determined by resolution (up to 512 px on the longer side) and file size (up to ~300 KB for small images).
- Documents, videos, 3D models, archives, digital certificates, firmware files, metadata, and software are sent to dedicated folders.
- STL, 3MF, G-code and binary G-code go to `3DPrinting/<year>/<month>` using the file creation date (last modified date if creation is older than 1980). Recognized Bambu Studio and OrcaSlicer filament preset JSONs follow the same rule. Other JSON files go to `Data`. Generic 3D formats such as OBJ and STEP, and Cura profiles, also go to `3DPrinting`.
- Excel (`.xls`, `.xlsx`, `.xlsm`, `.xlsb`), `.csv` and `.ods` go to `Data`. Folders containing data XML files and optionally spreadsheets or ordinary folder metadata are moved intact to `Data`. Existing software/driver package rules and routing of loose XML are unchanged.
- Guitar Pro 6 `.gpx` containers (`BCFS`/`BCFZ`) go to `Guitar Tabs`, GPS Exchange XML goes to `GPX`, and unrecognized `.gpx` stays in the unknown category.
- Fusion and SketchUp (`.f3d`, `.skp`) go to `3DPrinting`, `.svg` and `.tgs` go to the graphics category, and `.thm` and `.winmd` go to metadata. `.scr` goes to software only when it has a Windows PE header. Generic `.data`, `.prn`, and files without extensions are not guessed.
- Common fonts, diagrams, standalone databases, maps and project plans go to `Fonts`, `Diagrams`, `Databases`, `Maps` and `ProjectPlans`. Added more CAD, resin-slicer, structured-data and archive formats.
- OpenDocument presentations (`.odp`), Aseprite projects (`.aseprite`), and archives (`.tgz`, `.zst`) are recognized.
- HTML files and browser shortcuts (`.html`, `.htm`, `.xhtml`, `.mhtml`, `.mht`, `.url`, `.webloc`, `.website`) move to `WebFolderName`.
- Graphics editor project files (for example, `.psd`, `.psb`, `.ai`, `.eps`, `.xcf`, `.kra`, `.ora`, `.clip`, `.afphoto`, `.afdesign`, `.afpub`, `.cdr`, `.sai`, `.mdp`, `.pdn`, `.pspimage`, `.reb`, `.sketch`, `.fig`) move to `GraphicsFolderName`.
- E-books (`.epub`, `.fb2`, `.mobi`, `.azw3`, `.djvu`, `.cbr`, `.cbz`, `.ibooks`, `.kfx`) move to the `EBooksFolderName` folder.
- Video files go to `Videos` with automatic grouping by year, and within that by camera/phone make (`DJI Action`, `GoPro`, `Sony`, `Canon`, `Nikon`, `Ricoh`, `iPhone`, `Samsung`, `Google Pixel`, `Xiaomi`, `Realme`, `Nokia`, `Huawei`, `OnePlus`, `Motorola`, `Oppo`, `Vivo`, `LG`, `Asus`, `ZTE`) based on filename patterns. If metadata dates resolve to earlier than 1980, the file creation date is used to determine the year.
- If installer names (archives, `exe`, `msi`) differ only by version, files are gathered into a shared folder named after the program within the destination directory.
- Disk images (`.iso`, `.cso`, `.dmg`, `.img`, `.mdf`, `.mds`, `.nrg`, `.ccd`, `.isz`, `.vhd`, `.vhdx`) are placed in the `_Disk images` subfolder inside the software directory.
- Standalone configuration files (`.conf`) are stored in `Configurations`, and XML files (`.xml`) go to `Soft/_Xml_files` within the software directory.
- Repositories and source folders are moved whole into `RepositoriesFolderName` without per-file sorting. Detection reacts to `.git`, common manifests (`package.json`, `pyproject.toml`, `go.mod`, `Cargo.toml`, etc.), or the presence of several source files (`.cs`, `.ts`, `.js`, `.py`, `.java`, `.cpp`, `.rs`, and more).
- Music album directories that contain only audio files, album artwork images, and optional track lists are moved intact into `MusicFolderName` even if they include hidden files like `thumbs.db`.
- When a repository folder is wrapped in one or more parent directories (for example, after extracting an archive), the sorter unwraps those shells and moves only the repository directory itself into `RepositoriesFolderName`.
- Files matching the legacy `DeletedExtensions` setting (always including `.crdownload`) move to `_ToDelete` under the configured destination instead of being deleted. Previously overwritten archives and replaced software folders are also preserved there with unique names.
- Installer directories are detected separately and moved intact into the software folder without unpacking.
- Device firmware directories (with documentation and helper files) are moved intact into `FirmwareFolderName`. Detected driver and software bundles take precedence over embedded generic .bin or .dat payloads. Install bundles of known products take precedence over embedded source files unless a version-control marker is present.
- When an archive name matches an already moved software folder, the folder is removed and the archive is placed in its spot.
- When an archive shares the first half of its name with an unarchived file or folder in the same source directory, the archive is moved to that item's destination category so it ends up beside it.
- Empty directories are removed after processing.

## Run and publish

- Download the latest `UnsortedOrderer_v*.exe` from [GitHub Releases](https://github.com/24fisher-dev/UnsortedOrderer/releases/latest) and launch it on Windows x64. No installer or separate .NET installation is required.
- The window shows the source and destination folders and lists extensions moved to `_ToDelete`. After sorting, **Открыть корзину** opens this folder for review. **Удалить файлы из корзины навсегда** explicitly and permanently clears only this folder, including entries from previous runs. Sorting itself never deletes files. If a file or folder is inaccessible, the journal identifies the path and processing continues with other items.
- The optional checkboxes import the contents of Desktop and Downloads into the source before sorting. Available checkboxes are checked by default. Nothing is moved until you click **Начать очистку**. Desktop import reads the current user’s Windows Desktop folder regardless of where the program is launched. The journal shows the actual path, the number of objects moved, the number of shortcuts grouped, and any errors. The separate, unchecked **Обработать ярлыки общего рабочего стола** option groups recognized shortcuts within the shared folder and moves broken shortcuts to the quarantine. Windows permissions still apply. The running executable and its settings stay in place, and folders containing the running app are not moved. Old executables elsewhere on the desktop are sorted normally. Known game and launcher shortcuts are grouped into `Games`, and known image/vector editors into `PhotoEditors`, on the desktop. Common diagnostics shortcuts such as AIDA64, MSI Afterburner and CPU-Z are grouped into `Utilities`. Unknown shortcuts stay where they are. Desktop `.bat` scripts are left in place. Shortcuts with definitively missing local targets or explicitly empty Internet shortcut targets go to `Sorted/_ToDelete`; network, shell and other ambiguous targets are not assumed broken. The desktop and Downloads import continue after individual filesystem errors and report affected paths in the log. Transfers between volumes do not replace existing files or follow junctions. On Windows a completed desktop cleanup leaves links to the configured `Unsorted` and `Sorted` folders without replacing existing links. Downloads import is unavailable if that folder does not exist or is already the source.
- The window title shows its own version. The app checks GitHub Releases in the background on startup and offers to open the release page if a newer version is available.
- Click **Начать очистку** to start. The window stays responsive and shows the current stage and journal. When finished or stopped by an error, click **Новая очистка** to return to the options or **Выход** to quit.

A successful build from `main` publishes the tested executable in GitHub Releases. The release tag and executable use the same version. Pull requests do not publish releases.

To run from source on Windows:

```powershell
dotnet run --project UnsortedOrderer.WinForms
```

To publish a single self-contained executable:

```powershell
dotnet publish UnsortedOrderer.WinForms -c Release -r win-x64 -p:PublishProfile=FolderProfile -o out
```

After cleanup, **Открыть Sorted** opens the configured destination folder. Legacy `3DModels` and standard `Instruments` folders are consolidated without replacing existing files. Known misplaced packages may be moved intact to Soft or Drivers. Generic JSON goes to Data, Arduino sketches to Repositories, .download to IncompleteDownloads, .rom to ROMs, and .winmd to Metadata. Existing _Unknown is not automatically rescanned.

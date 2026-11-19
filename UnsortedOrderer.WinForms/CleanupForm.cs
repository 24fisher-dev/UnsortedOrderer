using System.Diagnostics;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using UnsortedOrderer.Infrastructure.Updates;
using UnsortedOrderer.Application.Application;
using UnsortedOrderer.Application.DependencyInjection;
using UnsortedOrderer.Contracts.Services;
using UnsortedOrderer.Models;

namespace UnsortedOrderer.WinForms;

internal sealed class CleanupForm : Form
{
    private readonly AppSettings _settings;
    private readonly WindowLog _log = new();
    private readonly CheckBox _desktop = new() { Text = "Перенести содержимое рабочего стола на сортировку", AutoSize = true };
    private readonly CheckBox _downloads = new() { Text = "Перенести содержимое папки «Загрузки» на сортировку", AutoSize = true };
    private readonly Label _trashStatus = new() { AutoSize = true, Visible = false };
    private readonly FlowLayoutPanel _trashActions = new() { Dock = DockStyle.Fill, AutoSize = true, Visible = false };
    private readonly Button _openTrash = new() { Text = "Открыть корзину", AutoSize = true };
    private readonly Button _deleteTrash = new() { Text = "Удалить файлы из корзины навсегда", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 18 };
    private readonly TextBox _journal = new()
    {
        AccessibleName = "Журнал очистки",
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical
    };
    private readonly LinkLabel _updateLink = new() { AutoSize = true, Visible = false };
    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly Button _start = new() { Text = "Начать очистку", AutoSize = true };
    private readonly Button _openSorted = new() { Text = "Открыть Sorted", AutoSize = true, Visible = false };
    private readonly Button _restart = new() { Text = "Новая очистка", AutoSize = true };
    private readonly Button _exit = new() { Text = "Выход", AutoSize = true };
    private readonly System.Windows.Forms.Timer _logTimer = new() { Interval = 150 };
    private readonly Icon? _applicationIcon;
    private bool _running;
    private string TrashPath => Path.Combine(_settings.DestinationRoot, FileOrganizerApplication.QuarantineFolderName);

    public CleanupForm(AppSettings settings, Version currentVersion)
    {
        _settings = settings;
        using var iconResource = typeof(CleanupForm).Assembly.GetManifestResourceStream("UnsortedOrderer.ApplicationIcon");
        if (iconResource is not null)
        {
            _applicationIcon = new Icon(iconResource);
            Icon = _applicationIcon;
        }

        Text = $"UnsortedOrderer v{currentVersion} — очистка и сортировка";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(720, 520);
        MinimumSize = new Size(610, 410);
        StartPosition = FormStartPosition.CenterScreen;

        var paths = new TextBox
        {
            Text = $"Папка для разбора   {_settings.SourceDirectory}\r\nРазложить по папкам в   {_settings.DestinationRoot}",
            AccessibleName = "Папки очистки",
            Multiline = true,
            WordWrap = false,
            ReadOnly = true,
            ScrollBars = ScrollBars.Horizontal,
            Height = 57,
            Dock = DockStyle.Fill
        };
        var quarantineExtensions = _settings.DeletedExtensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension)).ToArray();
        var warning = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.HotTrack,
            Text = $"В корзину Sorted\\_ToDelete перемещаются файлы: {string.Join(", ", quarantineExtensions)}."
        };

        _trashActions.Controls.AddRange([_openTrash, _deleteTrash]);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        buttons.Controls.AddRange([_start, _openSorted, _restart, _exit]);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 11, Dock = DockStyle.Fill, Padding = new Padding(14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < layout.RowCount; row++)
        {
            layout.RowStyles.Add(row == 7 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
        }
        Control[] controls = [_updateLink, paths, _desktop, _downloads, warning, _status, _progress, _journal, _trashStatus, _trashActions, buttons];
        for (var row = 0; row < controls.Length; row++)
        {
            layout.Controls.Add(controls[row], 0, row);
        }
        Controls.Add(layout);

        Shown += async (_, _) => await UpdateNotification.CheckAndShowAsync(
            _updateLink, currentVersion, _updateCancellation.Token);
        _start.Click += async (_, _) => await StartCleanupAsync();
        _openSorted.Click += (_, _) => OpenFolder(_settings.DestinationRoot, "Sorted");
        _openTrash.Click += (_, _) => OpenFolder(TrashPath, "Корзина Sorted");
        _deleteTrash.Click += async (_, _) => await ClearTrashAsync();
        _restart.Click += (_, _) => ResetForm();
        _exit.Click += (_, _) => Close();
        _logTimer.Tick += (_, _) => FlushLog();
        ResetForm();
    }

    private async Task StartCleanupAsync()
    {
        if (_running || !_start.Enabled)
        {
            return;
        }

        var cleanDesktop = _desktop.Enabled && _desktop.Checked;
        var cleanDownloads = _downloads.Enabled && _downloads.Checked;
        _running = true;
        _desktop.Enabled = _downloads.Enabled = _start.Enabled = _exit.Enabled = false;
        _progress.Style = ProgressBarStyle.Marquee;
        _status.Text = "Подготовка к очистке…";
        _logTimer.Start();

        var stage = new Progress<CleanupStage>(value =>
        {
            if (_running)
            {
                _status.Text = value switch
                {
                    CleanupStage.Desktop => "Перенос содержимого рабочего стола…",
                    CleanupStage.Downloads => "Перенос содержимого папки «Загрузки»…",
                    _ => "Сортировка файлов…"
                };
            }
        });

        int? failedEntries = null;
        try
        {
            failedEntries = await Task.Run(() =>
            {
                // Services and statistics are scoped to one run.
                using var provider = BuildServices();
                return provider.GetRequiredService<FileOrganizerApplication>()
                    .Run(cleanDesktop, cleanDownloads, ((IProgress<CleanupStage>)stage).Report);
            });
        }
        catch (Exception exception)
        {
            _log.WriteLine($"Ошибка очистки: {exception.Message}");
        }
        finally
        {
            _running = false;
            _logTimer.Stop();
            FlushLog();
            _progress.Style = ProgressBarStyle.Blocks;
            _progress.Value = failedEntries.HasValue ? _progress.Maximum : 0;
            _status.Text = failedEntries switch
            {
                0 => "Очистка завершена.",
                int count => $"Очистка завершена с ошибками ({count}). См. журнал.",
                null => "Ошибка очистки. Часть файлов могла быть обработана. См. журнал."
            };
            _start.Visible = false;
            _restart.Visible = _exit.Enabled = true;
            _openSorted.Visible = Directory.Exists(_settings.DestinationRoot);
            ShowTrashActions();
            _restart.Focus();
        }
    }

    private void ResetForm()
    {
        _desktop.Enabled = new Services.DesktopCleanupService(_log).CanClean(_settings.SourceDirectory);
        _downloads.Enabled = new Services.DownloadsCleanupService(_log).CanClean(_settings.SourceDirectory);
        _desktop.Checked = _desktop.Enabled;
        _downloads.Checked = _downloads.Enabled;
        _desktop.Text = _desktop.Enabled
            ? "Перенести содержимое рабочего стола на сортировку"
            : "Рабочий стол — доступно при запуске программы с рабочего стола";
        _downloads.Text = _downloads.Enabled
            ? "Перенести содержимое папки «Загрузки» на сортировку"
            : "Папка «Загрузки» уже выбрана для разбора или недоступна";
        _trashActions.Visible = _trashStatus.Visible = false;
        _journal.Clear();
        _progress.Value = 0;
        _status.Text = "Выберите нужные опции и начните очистку.";
        _restart.Visible = _openSorted.Visible = false;
        _start.Visible = _start.Enabled = true;
        _start.Focus();
    }

    private ServiceProvider BuildServices() =>
        new ServiceCollection().AddSingleton<IMessageWriter>(_log)
            .AddUnsortedOrdererApplication(_settings).BuildServiceProvider();

    private void ShowTrashActions()
    {
        var hasFiles = false;
        try
        {
            hasFiles = Directory.Exists(TrashPath) && Directory.EnumerateFileSystemEntries(TrashPath).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.WriteLine($"Не удалось проверить корзину «{TrashPath}»: {ex.Message}");
            FlushLog();
            hasFiles = true;
        }

        _trashStatus.Visible = true;
        _trashActions.Visible = hasFiles;
        _trashStatus.Text = hasFiles
            ? "Файлы не удалены. Внимательно проверьте содержимое корзины перед удалением."
            : "Файлы не удалены. В корзине нет файлов к удалению.";
    }

    private async Task ClearTrashAsync()
    {
        if (_running || !_trashActions.Visible)
            return;

        _running = true;
        _trashActions.Enabled = _restart.Enabled = _exit.Enabled = false;
        _trashStatus.Text = "Удаление файлов из корзины…";
        try
        {
            await Task.Run(() =>
            {
                using var provider = BuildServices();
                provider.GetRequiredService<FileOrganizerApplication>().ClearQuarantine();
            });
            _trashActions.Visible = false;
            _trashStatus.Text = "Корзина очищена.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.WriteLine($"Не удалось очистить корзину «{TrashPath}»: {ex.Message}");
            _trashStatus.Text = "Корзину не удалось очистить полностью. См. журнал.";
        }
        finally
        {
            _running = false;
            FlushLog();
            _trashActions.Enabled = _restart.Enabled = _exit.Enabled = true;
            _restart.Focus();
        }
    }

    private void OpenFolder(string path, string title)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                MessageBox.Show(this, $"Папка не найдена: {path}", title);
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось открыть папку");
        }
    }

    private void FlushLog()
    {
        var text = _log.Drain();
        if (text.Length == 0)
        {
            return;
        }

        var overflow = _journal.TextLength + text.Length - WindowLog.MaxLength;
        if (overflow > 0)
        {
            // Trim in chunks so a busy scan doesn't rewrite the journal on every tick.
            _journal.Select(0, Math.Max(overflow, Math.Min(6_000, _journal.TextLength)));
            _journal.SelectedText = string.Empty;
        }
        _journal.AppendText(text);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_running && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            _updateCancellation.Cancel();
            _updateCancellation.Dispose();
            _logTimer.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            _applicationIcon?.Dispose();
        }
    }
}

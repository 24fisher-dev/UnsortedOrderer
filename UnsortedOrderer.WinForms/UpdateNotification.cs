using System.ComponentModel;
using System.Diagnostics;
using UnsortedOrderer.Infrastructure.Updates;

namespace UnsortedOrderer.WinForms;

internal static class UpdateNotification
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(4),
        MaxResponseContentBufferSize = 1024 * 1024
    };

    public static async Task CheckAndShowAsync(LinkLabel link, Version currentVersion, CancellationToken token)
    {
        var update = await new GitHubReleaseChecker(Http).CheckAsync(currentVersion, token);
        if (update is null || token.IsCancellationRequested || link.IsDisposed)
        {
            return;
        }

        link.Text = $"Доступна версия v{update.Version} — скачать обновление";
        link.LinkClicked += (_, _) => OpenRelease(link, update.ReleasePage);
        link.Visible = true;
    }

    private static void OpenRelease(LinkLabel owner, Uri url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(owner, $"Не удалось открыть браузер. Страница релиза:\n{url}", "Обновление",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}

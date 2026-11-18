using System.Reflection;
using Microsoft.Extensions.Configuration;
using UnsortedOrderer.Infrastructure.Mappers;
using UnsortedOrderer.Models;

namespace UnsortedOrderer.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            using var configStream = File.Exists(configPath)
                ? File.OpenRead(configPath)
                : typeof(Program).Assembly.GetManifestResourceStream("UnsortedOrderer.DefaultSettings")
                    ?? throw new InvalidOperationException("Встроенные настройки не найдены.");
            var configuration = new ConfigurationBuilder().AddJsonStream(configStream).Build();
            var settings = AppSettingsMapper.Map(configuration.Get<AppSettingsDto>() ?? new AppSettingsDto());
            var assembly = typeof(Program).Assembly;
            var fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            var currentVersion = Version.TryParse(fileVersion, out var version)
                ? version
                : assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            System.Windows.Forms.Application.Run(new CleanupForm(settings, currentVersion));
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Не удалось запустить UnsortedOrderer",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

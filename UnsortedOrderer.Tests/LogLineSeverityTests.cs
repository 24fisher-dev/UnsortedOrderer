using UnsortedOrderer.Services.Desktop;
using Xunit;

namespace UnsortedOrderer.Tests;

public sealed class LogLineSeverityTests
{
    [Theory]
    [InlineData("Ошибка очистки: доступ запрещён", true)]
    [InlineData("Не удалось перенести \"a.lnk\": Access denied", true)]
    [InlineData("Недостаточно прав для перемещения ярлыка", true)]
    [InlineData("Ярлыков оставлено — 9. Ошибок переноса: 2.", true)]
    [InlineData("Ошибок переноса: 0.", false)]
    [InlineData("Очистка завершена.", false)]
    [InlineData("Ярлыков сгруппировано — 12.", false)]
    [InlineData("Перенесено в папку разбора: example", false)]
    public void Distinguishes_real_errors_from_zero_error_counters(string message, bool expected) =>
        Assert.Equal(expected, LogLineSeverity.IsError(message));
}

namespace UnsortedOrderer.Services.Desktop;

public static class LogLineSeverity
{
    public static bool IsError(string message)
    {
        var line = message.TrimStart();
        if (line.StartsWith("Ошибка", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Не удалось", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Недостаточно прав", StringComparison.OrdinalIgnoreCase))
            return true;

        const string marker = "Ошибок переноса:";
        var position = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (position < 0)
            return false;
        var number = line[(position + marker.Length)..].TrimStart();
        return number.Length > 0 && char.IsAsciiDigit(number[0]) &&
            int.TryParse(new string(number.TakeWhile(char.IsAsciiDigit).ToArray()), out var count) && count > 0;
    }
}

using System.Text;
using UnsortedOrderer.Contracts.Services;

namespace UnsortedOrderer.WinForms;

internal sealed class WindowLog : IMessageWriter
{
    public const int MaxLength = 60_000;
    private readonly StringBuilder _pending = new();

    public void WriteLine(string message)
    {
        lock (_pending)
        {
            _pending.AppendLine(message);
            if (_pending.Length > MaxLength)
            {
                _pending.Remove(0, _pending.Length - MaxLength);
            }
        }
    }

    public string Drain()
    {
        lock (_pending)
        {
            var text = _pending.ToString();
            _pending.Clear();
            return text;
        }
    }
}

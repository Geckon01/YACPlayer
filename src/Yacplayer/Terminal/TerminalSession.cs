namespace Yacplayer.Terminal;

public sealed class TerminalSession : IDisposable
{
    private const string BeginSynchronizedUpdate = "\u001b[?2026h";
    private const string EndSynchronizedUpdate = "\u001b[?2026l";
    private readonly bool _interactive = !Console.IsOutputRedirected;

    public TerminalSession()
    {
        if (_interactive)
        {
            Console.Write("\u001b[?1049h\u001b[?7l\u001b[?25l\u001b[2J");
            Console.Out.Flush();
        }
    }

    public void Render(string value)
    {
        if (!_interactive || value.Length == 0)
        {
            return;
        }

        Console.Write(WrapSynchronized(value));
        Console.Out.Flush();
    }

    internal static string WrapSynchronized(string value)
        => value.Length == 0 ? "" : BeginSynchronizedUpdate + value + EndSynchronizedUpdate;

    public void Dispose()
    {
        if (_interactive)
        {
            Console.Write($"{EndSynchronizedUpdate}\u001b[0m\u001b[?7h\u001b[?25h\u001b[?1049l");
            Console.Out.Flush();
        }
    }
}

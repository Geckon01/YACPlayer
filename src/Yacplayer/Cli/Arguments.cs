namespace Yacplayer.Cli;

public sealed record Arguments(
    string? Path,
    bool Recursive,
    int? Fps,
    bool NoVisualizer,
    bool Help,
    bool Version,
    bool IsValid,
    string? Error)
{
    public static Arguments Parse(string[] args)
    {
        string? path = null;
        string? error = null;
        bool recursive = false;
        bool noVisual = false;
        bool help = false;
        bool version = false;
        int? fps = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help" or "-h":
                    help = true;
                    break;

                case "--version":
                    version = true;
                    break;

                case "--recursive" or "-r":
                    recursive = true;
                    break;

                case "--no-visualizer":
                    noVisual = true;
                    break;

                case "--fps":
                    if (++i >= args.Length ||
                        !int.TryParse(args[i], out var value) ||
                        value is not (30 or 60))
                    {
                        error = "--fps must be 30 or 60.";
                    }
                    else
                    {
                        fps = value;
                    }

                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        error = $"Unknown option: {arg}";
                    }
                    else if (path is not null)
                    {
                        error = "Only one file or directory path may be supplied.";
                    }
                    else
                    {
                        path = System.IO.Path.GetFullPath(arg);
                    }

                    break;
            }

            if (error is not null)
            {
                break;
            }
        }

        return new(path, recursive, fps, noVisual, help, version, error is null, error);
    }

    public const string HelpText = """
Yacplayer 1.26.5.11 - local terminal music player for Windows
Usage: yacplayer [path] [--recursive] [--fps 30|60] [--no-visualizer]

Options:
  -h, --help       Show help without initializing audio
  --version        Show version
  -r, --recursive  Scan folders recursively (reparse points are skipped)
  --fps 30|60      UI frame rate
  --no-visualizer  Start with visualization disabled

Keys: Space pause/resume; X stop; N/P next/previous; arrows seek/select;
      +/- volume; M mute; F browser; Tab focus; Enter play/open; A append;
      Delete remove from queue (never disk); / search; S shuffle; R repeat;
      V visual mode; B realtime audio analysis; T reactive UI mode; O settings; ? help; Q quit with confirmation.

Playback: WAV PCM and MP3. M4A/AAC/WMA/FLAC depend on Windows Media Foundation codecs.
OGG/Opus playback is not supported by the current release.
""";
}

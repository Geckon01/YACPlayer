using System.Text;
using Yacplayer;
using Yacplayer.Cli;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
var parsed = Arguments.Parse(args);
if (!parsed.IsValid) { Console.Error.WriteLine(parsed.Error); Console.Error.WriteLine("Try --help."); return 2; }
if (parsed.Help) { Console.WriteLine(Arguments.HelpText); return 0; }
if (parsed.Version) { Console.WriteLine($"Yacplayer {typeof(App).Assembly.GetName().Version?.ToString(4)}"); return 0; }
if (parsed.Path is not null && !File.Exists(parsed.Path) && !Directory.Exists(parsed.Path)) { Console.Error.WriteLine($"Path does not exist: {parsed.Path}"); return 3; }
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
try { return await new App(parsed).RunAsync(cts.Token); }
catch (Exception ex) { Console.Error.WriteLine($"Startup failure: {ex.Message}"); return 3; }

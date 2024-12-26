using System.Diagnostics;
using System.Reactive.Linq;
using System.Text;
using CliWrap;
using CliWrap.EventStream;

namespace tiktok_automation_wrapper;

class Program
{
    static CancellationTokenSource cts = new CancellationTokenSource();
    static CancellationTokenSource streamLinkCts = new CancellationTokenSource();

    private static string User = "";
    static async Task Main(string[] args)
    {
        var errorCounter = 0;
        User = args.Length > 0 ? args[0] : "hofzeitprojekt";
        Console.WriteLine($"Running tiktok automation wrapper! For user: {User}");
        Console.CancelKeyPress += (s, ea) => cts.Cancel();
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var cmd = Cli.Wrap(Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd.exe" : "/bin/bash")
                    .WithArguments(
                        $"-c {(Environment.OSVersion.Platform == PlatformID.Win32NT ?
                            "conda activate tiktoklive" :
                            "\"source ~/anaconda3/bin/activate tiktoklive")} " +
                        $"&& python -u check_live.py -n {User}");
                await ListenToProcess(cmd, "check_live.py", cts.Token);
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
            catch(Exception ex)
            {
                errorCounter++;
                Console.WriteLine($"#{errorCounter} Error: " + ex);
                if(errorCounter >= 50) throw;
            }
        }
    }

    private static void HandleLine(string line, CancellationToken token)
    {
        if (line.StartsWith('~'))
        {
            switch (line)
            {
                case "~LIVE":
                    HandleLive();
                    break;
                case "~LIVE_ENDED":
                    HandleLiveEnd();
                    break;
                case "~DISCONNECTED":
                    Console.WriteLine("Disconnected");
                    break;
                case "~STARTED":
                    Console.WriteLine("Running LiveCheck");
                    break;
                default:
                    Console.Error.WriteLine($"Unknown command: {line}");
                    break;
            }
        }
    }

    private static void HandleLiveEnd()
    {
        Console.WriteLine("LIVE ENDED");
        streamLinkCts.Cancel();
    }

    private static void HandleLive()
    {
        streamLinkCts = new CancellationTokenSource();
        Console.WriteLine("LIVE DETECTED");
        Task.Run(async () =>
        {
            var errorCounter = 0;
            while (!streamLinkCts.IsCancellationRequested)
            {
                Console.WriteLine("Starting streamlink");
                var cmd = Cli.Wrap("streamlink")
                    .WithArguments($"https://www.tiktok.com/@{User}/live best --player-external-http --player-external-http-port 1312 -l all")
                    .WithValidation(CommandResultValidation.None);
                await ListenToProcess(cmd, "streamlink", streamLinkCts.Token);
                Console.WriteLine($"#{errorCounter} streamlink crashed - waiting 30 seconds before retrying");
                await Task.Delay(TimeSpan.FromSeconds(30));
                errorCounter++;
                if (errorCounter >= 5)
                {
                    Console.WriteLine($"#{errorCounter} Error - Need fallback no stream found");
                    break;
                }
            }
        });
    }

    private static async Task ListenToProcess(Command cmd, string name,
        CancellationToken cancellationToken = default, int forceCancelAfterSeconds = 30)
    {
        using var forcefulCts = new CancellationTokenSource();
        await using var link = cancellationToken.Register(() => { forcefulCts.CancelAfter(forceCancelAfterSeconds); });
        await cmd.Observe().ForEachAsync((cmdEvent) =>
        {
            switch (cmdEvent)
            {
                case StartedCommandEvent started:
                    Console.Out.WriteLine($"[{name}] Process started; ID: {started.ProcessId}");
                    break;
                case StandardOutputCommandEvent stdOut:
                    if (!stdOut.Text.StartsWith('~'))
                        Console.Out.WriteLine($"[{name}]Out> {stdOut.Text}");
                    HandleLine(stdOut.Text, cancellationToken);
                    break;
                case StandardErrorCommandEvent stdErr:
                    if (!stdErr.Text.StartsWith('~'))
                        Console.Error.WriteLine($"[{name}]Err> {stdErr.Text}");
                    HandleLine(stdErr.Text, cancellationToken);
                    break;
                case ExitedCommandEvent exited:
                    Console.WriteLine($"[{name}] Process exited; Code: {exited.ExitCode}");
                    break;
            }
        });
    }

    private static void ConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        cts.Cancel();
    }

    static void ProcessOnOutputDataReceived(DataReceivedEventArgs dataReceivedEventArgs)
    {
        var value = dataReceivedEventArgs.Data;
        if (value is null) return;
        if (value.StartsWith("frame"))
        {
            Console.SetCursorPosition(0, Console.CursorTop - 1);
            Console.WriteLine(dataReceivedEventArgs.Data);
        }
        else
        {
            Console.WriteLine(dataReceivedEventArgs.Data);
        }
    }
}
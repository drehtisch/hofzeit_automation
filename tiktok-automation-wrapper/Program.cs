using System.Net.Http.Headers;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using CliWrap;
using CliWrap.EventStream;

namespace tiktok_automation_wrapper;

// ReSharper disable once ClassNeverInstantiated.Global
class Program
{
    private static readonly CancellationTokenSource LiveCheckCts = new();
    private static CancellationTokenSource StreamLinkCts = new();

    private static string _user = "";
    private const string StreamerBotUrl = "http://localhost:7474";
    private const string BotToken = "";

    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri(StreamerBotUrl),
        DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", BotToken) },
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static int _isLive = 0; // 0 = false, 1 = true
    private static bool IsLive => Interlocked.CompareExchange(ref _isLive, 0, 0) == 1;
    private static void SetLive() => Interlocked.Exchange(ref _isLive, 1);
    private static void SetNotLive() => Interlocked.Exchange(ref _isLive, 0);

    static async Task Main(string[] args)
    {
        try
        {
            await MainAsync(args);
        }
        finally
        {
            LiveCheckCts.Dispose();
            StreamLinkCts.Dispose();
            HttpClient.Dispose();
        }
    }

    private static async Task MainAsync(string[] args)
    {
        _user = args.Length > 0 ? args[0] : "hofzeitprojekt";
        Console.WriteLine($"Running tiktok automation wrapper! For user: {_user}");
        Console.CancelKeyPress += OnConsoleOnCancelKeyPress;
        while (!LiveCheckCts.IsCancellationRequested)
        {
            try
            {
                var cmd = Cli.Wrap(Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd.exe" : "/bin/bash")
                    .WithArguments(
                        $"-c {(Environment.OSVersion.Platform == PlatformID.Win32NT ?
                            "conda activate tiktoklive" :
                            "\"source ~/anaconda3/bin/activate tiktoklive")} " +
                        $"&& python -u live_notify.py -n {_user}");
                await ListenToProcess(cmd, "live_notify.py", cancellationToken: LiveCheckCts.Token);
                await Task.Delay(TimeSpan.FromSeconds(30), LiveCheckCts.Token);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Live check task has been cancelled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running live check: {ex}");
            }
        }
    }

    private static async Task HandleLine(string line)
    {
        if (line.StartsWith('~'))
        {
            switch (line)
            {
                case "~LIVE":
                    await HandleLiveAsync();
                    break;
                case "~LIVE_ENDED":
                    await HandleLiveEndAsync();
                    break;
                case "~DISCONNECTED":
                    Console.WriteLine("Disconnected");
                    break;
                case "~STARTED":
                    Console.WriteLine("Running LiveCheck");
                    break;
                default:
                    await Console.Error.WriteLineAsync($"Unknown command: {line}");
                    break;
            }
        }
    }

    private static async Task HandleLiveEndAsync()
    {
        if (!IsLive)
        {
            Console.WriteLine("Is not live anymore. Skipping...");
            return;
        }

        SetNotLive();
        Console.WriteLine("LIVE ENDED");
        await StreamLinkCts.CancelAsync();
        await ExecuteStreamerBotActionAsync("LiveEnd", CancellationToken.None);
    }

    private static async Task HandleLiveAsync()
    {
        if (IsLive)
        {
            Console.WriteLine("Is already live. Skipping...");
            return;
        }

        SetLive();
        await StreamLinkCts.CancelAsync();
        StreamLinkCts = new CancellationTokenSource();
        StreamLinkCts.Token.Register(() => Console.WriteLine("Quitting streamlink"));
        Console.WriteLine("LIVE DETECTED");
        _ = Task.Run(async () =>
        {
            try
            {
                while (!StreamLinkCts.IsCancellationRequested)
                {
                    Console.WriteLine("Starting streamlink");
                    var cmd = Cli.Wrap("streamlink")
                        .WithArguments(
                            $"https://www.tiktok.com/@{_user}/live best --player-external-http --player-external-http-port 1312")
                        .WithValidation(CommandResultValidation.None);
                    await ListenToProcess(cmd, "streamlink", cancellationToken: StreamLinkCts.Token);
                    Console.WriteLine("streamlink exited - waiting 10 seconds before retrying");
                    await Task.Delay(TimeSpan.FromSeconds(10), StreamLinkCts.Token);
                }
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Waiting for streamlink restart canceled");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[streamlink] Error handling live: {e}");
            }
        });
        await ExecuteStreamerBotActionAsync("LiveStart", StreamLinkCts.Token);
    }

    private static async Task ExecuteStreamerBotActionAsync(string action, CancellationToken token)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { action = new { name = action } });
            var content = new StringContent(payload, Encoding.Default, "application/json");
            var response = await HttpClient.PostAsync("/DoAction", content, token);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Action '{action}' sent successfully.");
            }
            else
            {
                Console.WriteLine($"Failed to send action '{action}': {response.StatusCode} - {response.ReasonPhrase}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending action '{action}': {ex}");
        }
    }

    private static async Task ListenToProcess(Command cmd, string name, int forceCancelAfterSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var forcefulCts = new CancellationTokenSource();
            await using var link = cancellationToken.Register(() =>
            {
                forcefulCts.CancelAfter(TimeSpan.FromSeconds(forceCancelAfterSeconds));
            });

            await cmd.Observe(Encoding.Default, Encoding.Default, forcefulCts.Token, cancellationToken)
                .ForEachAsync(async (cmdEvent) => await HandleProcessEvent(cmdEvent, name),
                    cancellationToken);
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine($"[{name}] Process canceled");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[{name}] Error while listening to process: {e}");
        }
    }

    private static async Task HandleProcessEvent(CommandEvent cmdEvent, string name)
    {
        try
        {
            switch (cmdEvent)
            {
                case StartedCommandEvent started:
                    await OnProcessStarted(started, name);
                    break;
                case StandardOutputCommandEvent stdOut:
                    await OnProcessStandardOutput(stdOut, name);
                    break;
                case StandardErrorCommandEvent stdErr:
                    await OnProcessStandardError(stdErr, name);
                    break;
                case ExitedCommandEvent exited:
                    OnProcessExited(exited, name);
                    break;
                default:
                    Console.WriteLine($"[{name}] Unknown process event type: {cmdEvent.GetType()}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{name}] Error handling commandEvent {cmdEvent.GetType()} {cmdEvent}:  {ex}");
        }
    }

    private static Task OnProcessStarted(StartedCommandEvent started, string name)
    {
        Console.WriteLine($"[{name}] Process started; ID: {started.ProcessId}");
        return Task.CompletedTask;
    }

    private static async Task OnProcessStandardOutput(StandardOutputCommandEvent stdOut, string name)
    {
        if (!stdOut.Text.StartsWith('~'))
            await Console.Out.WriteLineAsync($"[{name}]Out> {stdOut.Text}");
        await HandleLine(stdOut.Text);
    }

    private static async Task OnProcessStandardError(StandardErrorCommandEvent stdErr, string name)
    {
        if (!stdErr.Text.StartsWith('~'))
            await Console.Error.WriteLineAsync($"[{name}]Err> {stdErr.Text}");
        await HandleLine(stdErr.Text);
    }

    private static void OnProcessExited(ExitedCommandEvent exited, string name)
    {
        Console.WriteLine($"[{name}] Process exited; Code: {exited.ExitCode}");
    }

    private static void OnConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        LiveCheckCts.Cancel();
        StreamLinkCts.Cancel();
        Console.WriteLine("Cancellation requested. Cleaning up resources.");
    }
}
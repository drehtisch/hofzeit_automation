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
    private static CancellationTokenSource _streamLinkCts = new();

    private static string _user = "";
    private const string StreamerBotUrl = "http://localhost:7474";
    private const string BotToken = "";
    
    private static bool IsLive = false;
    static async Task Main(string[] args)
    {
        _user = args.Length > 0 ? args[0] : "hofzeitprojekt";
        Console.WriteLine($"Running tiktok automation wrapper! For user: {_user}");
        Console.CancelKeyPress += ConsoleOnCancelKeyPress;
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
        IsLive = false;
        Console.WriteLine("LIVE ENDED");
        await _streamLinkCts.CancelAsync();
        await ExecuteStreamerBotActionAsync("LiveEnd", CancellationToken.None);
    }

    private static async Task HandleLiveAsync()
    {
        if (IsLive)
        {
            Console.WriteLine("Is already live. Skipping...");
            return;
        }
        await _streamLinkCts.CancelAsync();
        _streamLinkCts = new CancellationTokenSource();
        _streamLinkCts.Token.Register(() => Console.WriteLine("Quitting streamlink"));
        Console.WriteLine("LIVE DETECTED");
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_streamLinkCts.IsCancellationRequested)
                {
                    Console.WriteLine("Starting streamlink");
                    var cmd = Cli.Wrap("streamlink")
                        .WithArguments(
                            $"https://www.tiktok.com/@{_user}/live best --player-external-http --player-external-http-port 1312")
                        .WithValidation(CommandResultValidation.None);
                    await ListenToProcess(cmd, "streamlink", cancellationToken:_streamLinkCts.Token);
                    Console.WriteLine("streamlink exited - waiting 10 seconds before retrying");
                    await Task.Delay(TimeSpan.FromSeconds(10), _streamLinkCts.Token);
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
        await ExecuteStreamerBotActionAsync("LiveStart", _streamLinkCts.Token);
    }

    private static async Task ExecuteStreamerBotActionAsync(string action, CancellationToken token)
    {
        try
        {
            Console.WriteLine($"Sending action '{action}' to StreamerBot");
            using var client = new HttpClient();
            client.BaseAddress = new Uri(StreamerBotUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BotToken);
            var payload = new
            {
                action = new
                {
                    name = action
                }
            };
            var payLoadString = JsonSerializer.Serialize(payload);
            var content = new StringContent(payLoadString, Encoding.Default, "application/json");
            var response = await client.PostAsync("/DoAction", content, token);
            var responseString = await response.Content.ReadAsStringAsync(token);
            Console.WriteLine(
                $"Streamer bot action send. Response: {response.StatusCode} {response.ReasonPhrase} -> {responseString}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Sending streamer bot action failed: {ex}");
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
            // any exception lambda is handled
            await cmd.Observe(Encoding.Default, Encoding.Default, forcefulCts.Token, cancellationToken).ForEachAsync(
                async void (cmdEvent) =>
                {
                    try
                    {
                        switch (cmdEvent)
                        {
                            case StartedCommandEvent started:
                                await Console.Out.WriteLineAsync($"[{name}] Process started; ID: {started.ProcessId}");
                                break;
                            case StandardOutputCommandEvent stdOut:
                                if (!stdOut.Text.StartsWith('~'))
                                    await Console.Out.WriteLineAsync($"[{name}]Out> {stdOut.Text}");
                                await HandleLine(stdOut.Text);
                                break;
                            case StandardErrorCommandEvent stdErr:
                                if (!stdErr.Text.StartsWith('~'))
                                    await Console.Error.WriteLineAsync($"[{name}]Err> {stdErr.Text}");
                                await HandleLine(stdErr.Text);
                                break;
                            case ExitedCommandEvent exited:
                                Console.WriteLine($"[{name}] Process exited; Code: {exited.ExitCode}");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{name}] Error handling command: {ex}");
                    }
                }, cancellationToken);
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

    private static void ConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        LiveCheckCts.Cancel();
        _streamLinkCts.Cancel();
    }
}
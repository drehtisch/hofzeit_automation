using System.Diagnostics;

while (true)
{
    var arguments = string.Join(" ",args);
    Console.WriteLine("Running ffmpeg with '{0}'", arguments);
    var startInfo = new ProcessStartInfo
    {
        FileName = "ffmpeg",
        Arguments = arguments,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    var process = new Process { StartInfo = startInfo };
    
    process.OutputDataReceived += (s, ea) =>  ProcessOnOutputDataReceived(ea);
    process.ErrorDataReceived += (s, ea) => ProcessOnOutputDataReceived(ea);
    
    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    
    // Wait for the process to exit
    process.WaitForExit();

    // Check if the process exited normally or was terminated
    if (process.ExitCode != 0)
    {
        Console.WriteLine("FFmpeg crashed. Waiting 15 seconds...");
        await Task.Delay(TimeSpan.FromSeconds(15));
        Console.WriteLine("Restarting...");
    }
    else
    {
        Console.WriteLine("FFmpeg completed successfully.");
        break; // Exit the loop if FFmpeg completed successfully
    }
}

return;


void ProcessOnOutputDataReceived(DataReceivedEventArgs dataReceivedEventArgs)
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
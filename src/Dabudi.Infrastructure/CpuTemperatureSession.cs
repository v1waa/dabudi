using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text.Json;
using Dabudi.Core;

namespace Dabudi.Infrastructure;

/// <summary>A user-requested, read-only sensor process; the UI and input sender stay unelevated.</summary>
[SupportedOSPlatform("windows")]
public sealed class CpuTemperatureSession(AppLog log) : IDisposable
{
    private NamedPipeServerStream? _pipe;
    private CancellationTokenSource? _lifetime;
    private Process? _process;
    private Task? _reader;
    public int? ProcessId => _process?.Id;
    public event Action<CpuTemperatureReading>? Updated;

    public async Task StartAsync(bool smokeTest = false)
    {
        Stop();
        var lifetime = _lifetime = new CancellationTokenSource();
        var pipeName = "dabudi-cpu-" + Guid.NewGuid().ToString("N");
        var pipe = _pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _reader = ReadAsync(pipe, connected, lifetime.Token);
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("Не найден dabudi.exe."))
            {
                UseShellExecute = true,
                Verb = smokeTest ? "" : "runas",
                Arguments = (smokeTest ? "--cpu-sensor-smoke " : "--cpu-sensor ") + pipeName
            };
            // The UAC dialog must not block the main dispatcher.
            var process = await Task.Run(() => Process.Start(start), lifetime.Token).ConfigureAwait(false)
                ?? throw new IOException("Не удалось запустить чтение датчиков.");
            if (lifetime.IsCancellationRequested) { process.Dispose(); lifetime.Token.ThrowIfCancellationRequested(); }
            _process = process;
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(15), lifetime.Token).ConfigureAwait(false);
        }
        catch { Stop(); throw; }
    }

    private async Task ReadAsync(NamedPipeServerStream pipe, TaskCompletionSource connected, CancellationToken token)
    {
        try
        {
            await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
            connected.TrySetResult();
            using var reader = new StreamReader(pipe, leaveOpen: true);
            while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
            {
                var reading = JsonSerializer.Deserialize<CpuTemperatureReading>(line);
                if (!Enum.IsDefined(reading.Status) || reading.Temperature is { } value && (!double.IsFinite(value) || value is < -20 or > 150))
                    throw new IOException("Некорректный ответ датчика CPU.");
                if (!token.IsCancellationRequested) Updated?.Invoke(reading);
            }
            if (!token.IsCancellationRequested) throw new IOException("Процесс чтения датчиков завершился.");
        }
        catch (Exception exception) when (token.IsCancellationRequested && exception is OperationCanceledException or IOException or ObjectDisposedException) { }
        catch (Exception exception)
        {
            connected.TrySetException(exception);
            log.Write("CPU sensor connection failed", exception);
            if (!token.IsCancellationRequested) Updated?.Invoke(new(null, CpuTemperatureStatus.Failed));
        }
        finally { connected.TrySetCanceled(token); }
    }

    public void Stop()
    {
        var lifetime = _lifetime;
        _lifetime = null;
        lifetime?.Cancel();
        _pipe?.Dispose();
        _pipe = null;
        _process?.Dispose();
        _process = null;
        // Closing the pipe stops the child, including when the parent crashes. Never wait on the UI.
        if (lifetime != null && _reader != null)
            _ = _reader.ContinueWith(_ => lifetime.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        else lifetime?.Dispose();
        _reader = null;
    }

    public void Dispose() => Stop();
}

[SupportedOSPlatform("windows")]
public static class CpuSensorWorker
{
    public static async Task<int> RunAsync(string pipeName, AppLog log, bool smokeTest)
    {
        if (!pipeName.StartsWith("dabudi-cpu-", StringComparison.Ordinal)
            || !Guid.TryParseExact(pipeName[11..], "N", out _)) return 1;
        using var lifetime = new CancellationTokenSource();
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(5000, lifetime.Token).ConfigureAwait(false);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var gate = new object();
            var latest = new CpuTemperatureReading(null, CpuTemperatureStatus.Checking);
            void Publish(CpuTemperatureReading reading) { lock (gate) latest = reading; }
            using var monitor = new HardwareMonitor(log, cpuOnly: true);
            monitor.Updated += snapshot => Publish(new(snapshot.CpuTemperature, snapshot.CpuStatus));
            async Task InitializeAsync()
            {
                try
                {
                    if (smokeTest) { Publish(new(61.5, CpuTemperatureStatus.Ready)); return; }
                    if (!CpuSensorAccess.IsElevated) throw new UnauthorizedAccessException("Нет доступа администратора к датчикам.");
                    if (CpuSensorAccess.Probe() == CpuTemperatureStatus.DriverMissing)
                    {
                        Publish(new(null, CpuTemperatureStatus.Installing));
                        if (await CpuSensorAccess.InstallAsync(lifetime.Token).ConfigureAwait(false))
                        { Publish(new(null, CpuTemperatureStatus.RestartRequired)); return; }
                    }
                    monitor.SetEnabled(true);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    log.Write("CPU sensor setup failed", exception);
                    Publish(new(null, CpuTemperatureStatus.Failed));
                }
            }
            // Heartbeats continue even if a hardware driver is slow, so parent exit always disconnects us.
            var initialize = Task.Run(InitializeAsync);
            while (true)
            {
                CpuTemperatureReading reading;
                lock (gate) reading = latest;
                await writer.WriteLineAsync(JsonSerializer.Serialize(reading)).ConfigureAwait(false);
                await Task.Delay(500, lifetime.Token).ConfigureAwait(false);
                // Observe the task; its failures are reported as readings above.
                if (initialize.IsCompleted) await initialize.ConfigureAwait(false);
            }
        }
        catch (IOException) { return 0; } // Parent closed its pipe: stop this process with it.
        catch (Exception exception) { log.Write("CPU sensor worker failed", exception); return 1; }
        finally { lifetime.Cancel(); }
    }
}

using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace MarkdownViewer.Services;

/// <summary>
/// Per-user single-instance coordination over a named mutex + named pipe. The
/// first process owns the mutex and runs the pipe server (raising
/// <see cref="Received"/> for each incoming file path); a later process detects
/// the held mutex, hands its argument to the owner via <see cref="TrySignal"/>,
/// and exits. If signalling fails (the owner is mid-exit), the caller falls back
/// to launching normally — so the worst case is a second window, never a hang.
/// </summary>
public sealed class SingleInstanceServer : IDisposable
{
    // Local\ scopes these to the current login session (per-user single instance).
    public const string MutexName = @"Local\MarkdownViewer.singleinstance.mutex";
    private const string PipeName = "MarkdownViewer.singleinstance.pipe";

    /// <summary>Raised on a background thread with the incoming file path (or null
    /// for a bare focus signal). Marshal to the UI thread before touching UI.</summary>
    public event Action<string?>? Received;

    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    /// <summary>Start accepting hand-offs from later instances.</summary>
    public void Start() => _loop = Task.Run(() => ListenLoopAsync(_cts.Token));

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server);
                var msg = (await reader.ReadToEndAsync(ct)).Trim();
                Received?.Invoke(string.IsNullOrEmpty(msg) ? null : msg);
            }
            catch (OperationCanceledException) { break; }
            catch { /* a malformed/aborted client shouldn't kill the listener */ }
        }
    }

    /// <summary>
    /// Send <paramref name="payload"/> (a file path, or "" to just focus) to the
    /// running instance. Returns false if no instance answered in time.
    /// </summary>
    public static bool TrySignal(string payload)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.Write(payload);
            return true;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        _cts.Dispose();
    }
}

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using JsxCore.Compilation;
using Microsoft.Extensions.Logging;

namespace JsxCore.Hosting;

public interface IJsxHotReloadState
{
    bool Enabled { get; }
}

public sealed class JsxHotReloadService(
    bool enabled,
    JsxCompilationService compilation,
    JsxServerRendererReset reset,
    ILogger<JsxHotReloadService> logger)
    : IJsxHotReloadState, IDisposable
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private readonly ILogger<JsxHotReloadService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly JsxCompilationService _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
    private readonly JsxServerRendererReset _reset = reset ?? throw new ArgumentNullException(nameof(reset));
    private bool _subscribed;

    public bool Enabled { get; } = enabled;

    public void Start()
    {
        if (_subscribed)
        {
            return;
        }
        _subscribed = true;
        _compilation.BuildCompleted += OnBuildCompleted;
    }

    private void OnBuildCompleted(BuildState state)
    {
        // Server-side engines hold a parsed graph of the previous build; drop them.
        _reset.Reset();

        var message = state.Result.Succeeded
            ? JsonSerializer.Serialize(new { type = "update", version = state.BuildId })
            : JsonSerializer.Serialize(new
            {
                type = "error",
                title = "TypeScript compilation failed",
                detail = state.Result.FormatDiagnostics()
            });

        _ = BroadcastAsync(message);
    }

    public async Task AcceptAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var id = Guid.NewGuid();
        _clients[id] = socket;
        _logger.LogDebug("JsxCore hot reload client {ClientId} connected.", id);

        try
        {
            var buffer = new byte[256];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                // The client never sends anything meaningful; this just waits for the close frame.
                var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown or client disconnect.
        }
        catch (WebSocketException)
        {
            // Abrupt disconnects are routine during development.
        }
        finally
        {
            _clients.TryRemove(id, out _);
            _logger.LogDebug("JsxCore hot reload client {ClientId} disconnected.", id);
        }
    }

    private async Task BroadcastAsync(string message)
    {
        if (_clients.IsEmpty)
        {
            return;
        }

        var payload = Encoding.UTF8.GetBytes(message);

        foreach (var (id, socket) in _clients)
        {
            if (socket.State != WebSocketState.Open)
            {
                _clients.TryRemove(id, out _);
                continue;
            }

            try
            {
                await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "JsxCore could not notify hot reload client {ClientId}.", id);
                _clients.TryRemove(id, out _);
            }
        }
    }

    public void Dispose()
    {
        if (_subscribed)
        {
            _compilation.BuildCompleted -= OnBuildCompleted;
            _subscribed = false;
        }
        _clients.Clear();
    }
}

public sealed class JsxServerRendererReset
{
    private Action? _reset;
    public void Bind(Action reset) => _reset = reset;
    public void Reset() => _reset?.Invoke();
}

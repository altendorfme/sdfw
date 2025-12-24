using Sdfw.Core.Ipc;

namespace Sdfw.Service.Services;

/// <summary>
/// Service for handling IPC communication with the UI client.
/// </summary>
public interface IIpcServerService
{
    /// <summary>
    /// Gets whether the server is running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the count of connected clients.
    /// </summary>
    int ConnectedClients { get; }

    /// <summary>
    /// Starts the IPC server.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the IPC server.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts a notification to all connected clients.
    /// </summary>
    Task BroadcastAsync(IpcMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a client connects.
    /// </summary>
    event EventHandler? ClientConnected;

    /// <summary>
    /// Event raised when a client disconnects.
    /// </summary>
    event EventHandler? ClientDisconnected;
}

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AppToMic;

/// <summary>
/// Tiny HTTP server on 127.0.0.1 so a Stream Deck (or anything else) can control the app:
///   http://127.0.0.1:8765/toggle   /on   /off   /status
/// Only accepts connections from this computer.
/// </summary>
public sealed class RemoteControl : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<string, bool> _handler;
    private readonly CancellationTokenSource _cts = new();

    /// <param name="handler">Receives a command ("toggle", "on", "off", "status", "show") and returns the active state.</param>
    public RemoteControl(int port, Func<string, bool> handler)
    {
        _handler = handler;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        _ = AcceptLoop();
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch
            {
                return;
            }
            _ = Handle(client);
        }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 2000;
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                string? requestLine = await reader.ReadLineAsync(_cts.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                // e.g. "GET /toggle HTTP/1.1"
                var parts = requestLine?.Split(' ') ?? Array.Empty<string>();
                string path = parts.Length > 1 ? parts[1] : "/status";
                string command = path.Split('?')[0].Trim('/').ToLowerInvariant();
                if (command.Length == 0) command = "status";

                bool known = command is "toggle" or "on" or "off" or "status" or "show";
                string body;
                string status;
                if (known)
                {
                    bool active = _handler(command);
                    body = $"{{\"active\":{(active ? "true" : "false")}}}";
                    status = "200 OK";
                }
                else
                {
                    body = "{\"error\":\"use /toggle, /on, /off or /status\"}";
                    status = "404 Not Found";
                }

                var bodyBytes = Encoding.UTF8.GetBytes(body);
                var header = $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nAccess-Control-Allow-Origin: *\r\n" +
                             $"Content-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
                await stream.WriteAsync(bodyBytes);
            }
            catch
            {
                // Ignore broken clients.
            }
        }
    }

    /// <summary>Used by a second launch (e.g. "AppToMic.exe --toggle") to forward the command to the running app.</summary>
    public static bool Send(int port, string command)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            http.GetAsync($"http://127.0.0.1:{port}/{command}").GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}

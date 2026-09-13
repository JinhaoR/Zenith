using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Zenith.App.Tests.Navigation;

/// <summary>Records requests at the server, independently of WebView2 event handlers.</summary>
internal sealed class LoopbackSite : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _run;
    internal ConcurrentQueue<string> Requests { get; } = new();
    internal sealed record TestRequest(string Method, string Path, string Body);
    internal ConcurrentQueue<TestRequest> Received { get; } = new();
    internal Func<TestRequest, (int Status, string Headers, string Body)>? DetailedResponse { get; set; }
    internal Func<string, (int Status, string Headers, string Body)> Response { get; set; } =
        _ => (200, "", "<html><body>Loopback test</body></html>");
    internal string Origin { get; }
    internal string ListenerOrigin { get; }

    internal LoopbackSite(string address, string? host = null)
    {
        var ip = IPAddress.Parse(address);
        if (!IPAddress.IsLoopback(ip)) throw new ArgumentException("Test servers must bind only to loopback.", nameof(address));
        _listener = new TcpListener(ip, 0);
        _listener.Start();
        ListenerOrigin = $"http://{address}:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        Origin = host is null ? ListenerOrigin : $"http://{host}:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        _run = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var line = await reader.ReadLineAsync(deadline.Token);
                    if (line is null) continue;
                    var parts = line.Split(' ');
                    if (parts.Length < 2) continue;
                    var size = line.Length;
                    var contentLength = 0;
                    while (await reader.ReadLineAsync(deadline.Token) is { Length: > 0 } header)
                    {
                        size += header.Length;
                        if (size > 16384) throw new IOException("Oversized test request.");
                        if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!int.TryParse(header[15..].Trim(), out contentLength) || contentLength is < 0 or > 16384)
                                throw new IOException("Oversized test body.");
                        }
                    }
                    // Only ASCII fixture form values are submitted in these tests.
                    var buffer = new char[contentLength];
                    var offset = 0;
                    while (offset < buffer.Length)
                    {
                        var count = await reader.ReadAsync(buffer.AsMemory(offset), deadline.Token);
                        if (count == 0) throw new IOException("Incomplete test body.");
                        offset += count;
                    }
                    var received = new TestRequest(parts[0], parts[1], new string(buffer));
                    Requests.Enqueue(parts[1]);
                    Received.Enqueue(received);
                    var response = DetailedResponse?.Invoke(received) ?? Response(parts[1]);
                    var body = Encoding.UTF8.GetBytes(response.Body);
                    var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {response.Status} Test\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n{response.Headers}\r\n");
                    await stream.WriteAsync(headers, deadline.Token);
                    await stream.WriteAsync(body, deadline.Token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
            }
        }
        catch (Exception ex) when (_stop.IsCancellationRequested && ex is OperationCanceledException or SocketException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await _run;
        _stop.Dispose();
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;
using MimeKit;

namespace Identity.Infrastructure;

internal sealed record ReceivedEmail(string From, string To, string Subject, string Body);

/// <summary>Serveur SMTP minimal sur un port local : accepte tout et conserve ce qu'il a reçu.</summary>
internal sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<ReceivedEmail> _received = [];
    private readonly Task _accepting;

    public FakeSmtpServer()
    {
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public async Task<ReceivedEmail> WaitForAsync(Func<ReceivedEmail, bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            lock (_received)
            {
                if (_received.FirstOrDefault(match) is { } found)
                {
                    return found;
                }
            }

            await Task.Delay(50);
        }

        lock (_received)
        {
            throw new TimeoutException(
                $"No matching email. Received: {string.Join(" | ", _received.Select(m => $"{m.To} / {m.Subject}"))}");
        }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = ServeAsync(client);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            using var reader = new StreamReader(stream, Encoding.Latin1);
            await using var writer = new StreamWriter(stream, Encoding.Latin1) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 fake ESMTP");
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 go ahead");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                    {
                        data.Append(dataLine.StartsWith("..", StringComparison.Ordinal) ? dataLine[1..] : dataLine).Append("\r\n");
                    }

                    using var raw = new MemoryStream(Encoding.Latin1.GetBytes(data.ToString()));
                    var message = await MimeMessage.LoadAsync(raw);
                    lock (_received)
                    {
                        _received.Add(new ReceivedEmail(
                            message.From.ToString(), message.To.ToString(), message.Subject ?? string.Empty, message.TextBody ?? string.Empty));
                    }

                    await writer.WriteLineAsync("250 queued");
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 bye");
                    return;
                }
                else
                {
                    await writer.WriteLineAsync("250 ok");
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accepting;
    }
}

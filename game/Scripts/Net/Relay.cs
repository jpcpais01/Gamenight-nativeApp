using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GameNight.Net;

/// <summary>
/// One end of an online game's line through the relay (relay/ in the repository): a WebSocket
/// to the room named by the game's code. Everything runs off the game's thread; the game sends
/// with <see cref="Send"/> and takes what came in with <see cref="TryReceive"/>.
/// </summary>
public sealed class Relay : IDisposable
{
    public enum State { Connecting, Open, Closed }

    /// <summary>The relay's own "your friend is here" note to the host.</summary>
    public const byte Joined = 0xff;
    /// <summary>Closing codes from the relay: the code's taken, no such game, the game's full, the other side left.</summary>
    public const int Taken = 4001, NoGame = 4004, Full = 4009, Left = 4010;

    public volatile State Status = State.Connecting;
    /// <summary>Why it closed: a relay code above, or 0 for a network failure (see Error).</summary>
    public int CloseCode;
    public string Error = "";

    readonly ClientWebSocket _ws = new();
    readonly CancellationTokenSource _stop = new();
    readonly ConcurrentQueue<byte[]> _in = new();
    readonly Channel<byte[]> _out = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Opens the room `code` as its host, or joins it.</summary>
    public Relay(string url, bool host, string code)
    {
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        var uri = new Uri($"{url}/{(host ? "host" : "join")}/{code}");
        Task.Run(() => Run(uri));
    }

    async Task Run(Uri uri)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            await _ws.ConnectAsync(uri, timeout.Token);
            Status = State.Open;
            _ = Task.Run(Pump);
            var buf = new byte[1 << 16];
            var msg = new System.IO.MemoryStream();
            while (!_stop.IsCancellationRequested)
            {
                var r = await _ws.ReceiveAsync(buf, _stop.Token);
                if (r.MessageType == WebSocketMessageType.Close) break;
                msg.Write(buf, 0, r.Count);
                if (!r.EndOfMessage) continue;
                _in.Enqueue(msg.ToArray());
                msg.SetLength(0);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or System.Net.Http.HttpRequestException or ObjectDisposedException or InvalidOperationException)
        {
            if (!_stop.IsCancellationRequested) Error = e is OperationCanceledException ? "No answer from the server" : e.Message;
        }
        CloseCode = (int?)_ws.CloseStatus ?? 0;
        Status = State.Closed;
    }

    /// <summary>The sending side: one message at a time, in order.</summary>
    async Task Pump()
    {
        try
        {
            while (await _out.Reader.WaitToReadAsync(_stop.Token))
                while (_out.Reader.TryRead(out var m))
                    await _ws.SendAsync(m, WebSocketMessageType.Binary, true, _stop.Token);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
    }

    public void Send(byte[] msg)
    {
        if (Status != State.Closed) _out.Writer.TryWrite(msg);
    }

    public bool TryReceive(out byte[] msg) => _in.TryDequeue(out msg);

    public void Dispose()
    {
        if (Status == State.Open)
        {
            try
            {
                _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(300);
            }
            catch (Exception) { }
        }
        Status = State.Closed;
        _stop.Cancel();
        _out.Writer.TryComplete();
        _ws.Dispose();
    }
}

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace GameNight.Link;

/// <summary>
/// The controller for phones without the app (an iPhone, a tablet): the PC serves a small web
/// page on the home network, and the page talks back over a WebSocket (browsers can't send
/// UDP). The page sends the same INPUT packets as the app, the PC answers each with STATUS.
/// A tiny server of its own, one thread per connection, Nagle off so nothing waits.
/// </summary>
public sealed class WebLink
{
    public const int Port = 47821;
    readonly Host _host;
    readonly TcpListener _listener;
    volatile bool _running = true;

    public bool Serving { get; }

    public WebLink(Host host)
    {
        _host = host;
        try
        {
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            Serving = true;
            new Thread(Accept) { IsBackground = true, Name = "WebLink" }.Start();
        }
        catch (Exception e)
        {
            Godot.GD.Print($"Web controller: can't listen on port {Port} ({e.Message})");
        }
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); }
        catch (Exception) { }
    }

    void Accept()
    {
        while (_running)
        {
            TcpClient c;
            try { c = _listener.AcceptTcpClient(); }
            catch (Exception)
            {
                if (!_running) return;
                continue;
            }
            new Thread(() => Serve(c)) { IsBackground = true, Name = "WebLinkClient", Priority = ThreadPriority.AboveNormal }.Start();
        }
    }

    void Serve(TcpClient c)
    {
        using (c)
        {
            try
            {
                c.NoDelay = true;
                var s = c.GetStream();
                var (path, key) = ReadRequest(s);
                if (path == null) return;
                if (key != null) Socket(s, key, (IPEndPoint)c.Client.RemoteEndPoint);
                else if (path == "/manifest.json") Reply(s, "application/manifest+json", WebPage.Manifest);
                else if (path == "/" || path.StartsWith("/?")) Reply(s, "text/html; charset=utf-8", WebPage.Html);
                else Reply(s, "text/plain", "Not here", "404 Not Found");
            }
            catch (Exception) { }
        }
    }

    /// <summary>The request line's path and the WebSocket key (null for a plain page request).</summary>
    static (string, string) ReadRequest(NetworkStream s)
    {
        var sb = new StringBuilder();
        while (sb.Length < 8192)
        {
            int b = s.ReadByte();
            if (b < 0) return (null, null);
            sb.Append((char)b);
            if (b == '\n' && sb.Length >= 4 && sb[^2] == '\r' && sb[^3] == '\n' && sb[^4] == '\r') break;
        }
        var lines = sb.ToString().Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2) return (null, null);
        string key = null;
        foreach (var l in lines)
            if (l.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)) key = l[18..].Trim();
        return (first[1], key);
    }

    static void Reply(NetworkStream s, string type, string body, string status = "200 OK")
    {
        var b = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {b.Length}\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n");
        s.Write(head);
        s.Write(b);
    }

    /// <summary>The WebSocket: handshake, then binary frames until the page goes. Each INPUT
    /// in is answered by a STATUS out.</summary>
    void Socket(NetworkStream s, string key, IPEndPoint from)
    {
        string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        s.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"));
        var rx = new Wire.Pad();
        var head = new byte[14];
        var payload = new byte[1024];
        var outBuf = new byte[256];
        var frame = new byte[258];
        while (_running)
        {
            if (!Fill(s, head, 0, 2)) return;
            int op = head[0] & 15;
            bool masked = (head[1] & 128) != 0;
            long len = head[1] & 127;
            if (len == 126)
            {
                if (!Fill(s, head, 2, 2)) return;
                len = head[2] << 8 | head[3];
            }
            else if (len == 127)
            {
                if (!Fill(s, head, 2, 8)) return;
                len = 0;
                for (int i = 0; i < 8; i++) len = len << 8 | head[2 + i];
            }
            if (len > payload.Length) return;
            var mask = new byte[4];
            if (masked && !Fill(s, mask, 0, 4)) return;
            if (!Fill(s, payload, 0, (int)len)) return;
            if (masked)
                for (int i = 0; i < len; i++) payload[i] ^= mask[i & 3];
            switch (op)
            {
                case 8:
                    return;
                case 9:
                    Send(s, frame, 0xA, payload, (int)len);
                    break;
                case 2:
                    if (Wire.ReadPad(payload, (int)len, rx))
                    {
                        int n = _host.Answer(rx, from, outBuf);
                        Send(s, frame, 0x2, outBuf, n);
                    }
                    break;
            }
        }
    }

    static void Send(NetworkStream s, byte[] frame, int op, byte[] data, int n)
    {
        n = Math.Min(n, 125);
        frame[0] = (byte)(0x80 | op);
        frame[1] = (byte)n;
        Array.Copy(data, 0, frame, 2, n);
        s.Write(frame, 0, n + 2);
    }

    static bool Fill(Stream s, byte[] b, int at, int n)
    {
        while (n > 0)
        {
            int r = s.Read(b, at, n);
            if (r <= 0) return false;
            at += r;
            n -= r;
        }
        return true;
    }
}

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SCUMLab {
public sealed class ServerStatusResult {
    public bool online;
    public int players = -1, maxPlayers = -1;
    public string name;
    public string Summary {
        get {
            if (!online) return "Офлайн";
            return players >= 0 ? "Онлайн · " + players + (maxPlayers > 0 ? "/" + maxPlayers : "") + " игр." : "Онлайн";
        }
    }
}

/// Server status via Steam A2S_INFO (UDP), falling back to a TCP connect on the game's query port.
public static class ServerStatus {
    public const string DefaultAddress = "100.125.34.42:7779";

    public static bool TryParseAddress(string text, out string host, out int port) {
        host = null; port = 0;
        if (String.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        int colon = text.LastIndexOf(':');
        if (colon <= 0 || !Int32.TryParse(text.Substring(colon + 1), out port) || port < 1 || port > 65535) return false;
        host = text.Substring(0, colon);
        return host.Length > 0;
    }

    public static ServerStatusResult Query(string address, int timeoutMs = 1500) {
        string host; int port;
        if (!TryParseAddress(address, out host, out port)) return new ServerStatusResult();
        IPAddress ip;
        try { ip = IPAddress.TryParse(host, out ip) ? ip : Dns.GetHostAddresses(host)[0]; }
        catch { return new ServerStatusResult(); }
        // SCUM: game port 7777, Steam query usually game+1, direct-connect port game+2.
        int[] candidates = { port - 1, port, port + 1, 27015 };
        foreach (int p in candidates) {
            if (p < 1 || p > 65535) continue;
            ServerStatusResult r = A2SInfo(new IPEndPoint(ip, p), timeoutMs);
            if (r != null) return r;
        }
        return new ServerStatusResult { online = TcpOpen(new IPEndPoint(ip, port), timeoutMs) };
    }

    static readonly byte[] InfoRequest = Build(null);
    static byte[] Build(byte[] challenge) {
        byte[] head = { 0xFF, 0xFF, 0xFF, 0xFF, 0x54 };
        byte[] text = Encoding.ASCII.GetBytes("Source Engine Query\0");
        byte[] result = new byte[head.Length + text.Length + (challenge == null ? 0 : 4)];
        Buffer.BlockCopy(head, 0, result, 0, head.Length);
        Buffer.BlockCopy(text, 0, result, head.Length, text.Length);
        if (challenge != null) Buffer.BlockCopy(challenge, 0, result, head.Length + text.Length, 4);
        return result;
    }

    internal static ServerStatusResult A2SInfo(IPEndPoint endpoint, int timeoutMs) {
        try {
            using (UdpClient udp = new UdpClient()) {
                udp.Client.ReceiveTimeout = timeoutMs;
                udp.Connect(endpoint);
                byte[] request = InfoRequest;
                for (int attempt = 0; attempt < 2; attempt++) {
                    udp.Send(request, request.Length);
                    IPEndPoint from = null;
                    byte[] reply = udp.Receive(ref from);
                    if (reply.Length >= 9 && reply[4] == 0x41) {
                        byte[] challenge = new byte[4];
                        Buffer.BlockCopy(reply, 5, challenge, 0, 4);
                        request = Build(challenge);
                        continue;
                    }
                    return ParseInfo(reply);
                }
            }
        } catch (SocketException) { } catch (ObjectDisposedException) { }
        return null;
    }

    internal static ServerStatusResult ParseInfo(byte[] reply) {
        if (reply == null || reply.Length < 6 || reply[0] != 0xFF || reply[1] != 0xFF || reply[2] != 0xFF || reply[3] != 0xFF || reply[4] != 0x49) return null;
        int p = 6; // header(4) + 'I' + protocol
        string name = Str(reply, ref p); Str(reply, ref p); Str(reply, ref p); Str(reply, ref p);
        if (p + 4 > reply.Length) return null;
        p += 2; // app id
        return new ServerStatusResult { online = true, name = name, players = reply[p], maxPlayers = reply[p + 1] };
    }

    static string Str(byte[] b, ref int p) {
        int start = p;
        while (p < b.Length && b[p] != 0) p++;
        string s = Encoding.UTF8.GetString(b, start, Math.Max(0, p - start));
        if (p < b.Length) p++;
        return s;
    }

    static bool TcpOpen(IPEndPoint endpoint, int timeoutMs) {
        try {
            using (TcpClient tcp = new TcpClient()) {
                IAsyncResult r = tcp.BeginConnect(endpoint.Address, endpoint.Port, null, null);
                bool ok = r.AsyncWaitHandle.WaitOne(timeoutMs) && tcp.Connected;
                if (ok) tcp.EndConnect(r);
                return ok;
            }
        } catch { return false; }
    }
}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using GameNight.Club;
using GameNight.Sim;

namespace GameNight.Net;

/// <summary>
/// An online match with a friend, from the lobby to full time. The host's game runs the match
/// (both sides' controls go into its engine) and streams every frame to the friend's game,
/// which draws it and sends its controls back. Both go through the relay, in a room named by a
/// 4-digit code the host reads out. Messages are one byte of type, then the body:
/// C the friend's club (JSON), S kick-off (JSON: the whole set-up), I the friend's controls (a
/// Wire packet, as a phone controller sends), F a frame (FrameCodec), Q the ping's echo, K skip
/// the replay or walk-out, L left. Coach mode adds O a manager's order (friend to host, JSON)
/// and B the friend's side as their manager sees it (host to friend, JSON).
/// </summary>
public sealed class Online : IDisposable
{
    public const byte Club = (byte)'C', Start = (byte)'S', Input = (byte)'I', Frame = (byte)'F', Pong = (byte)'Q', Skip = (byte)'K', Leave = (byte)'L',
        Order = (byte)'O', Bench = (byte)'B', Sim = (byte)'M';

    /// <summary>The online game under way (the lobby or a match), or null.</summary>
    public static Online Current { get; private set; }

    public enum Stage
    {
        /// <summary>Reaching the relay.</summary>
        Connecting,
        /// <summary>Host: the room is open, waiting for the friend. Friend: in, waiting for kick-off.</summary>
        Waiting,
        /// <summary>Host: the friend is in (their club has arrived).</summary>
        Ready,
        Playing,
        /// <summary>It's over: the line dropped, the code was wrong, or someone left (see Problem).</summary>
        Failed,
    }

    public readonly bool IsHost;
    public string Code { get; private set; }
    public Stage Now { get; private set; } = Stage.Connecting;
    public string Problem = "";

    /// <summary>Host: the friend's line-up and the bits that make their side theirs.</summary>
    public ClubInfo Friend;
    /// <summary>Friend: the match as the host set it up.</summary>
    public Kickoff Match;
    /// <summary>Host: the match will be coach mode (picked in the lobby).</summary>
    public bool CoachMode;

    readonly ClubState _club;
    Relay _relay;
    int _tries;

    static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    /// <summary>A side's club, as it travels: line-up (with its player cards), crest and goal explosion.</summary>
    public sealed class ClubInfo
    {
        public TeamSetup Team;
        public Card[] Cards, BenchCards;
        public Crest Crest;
        public int GoalFx;
        /// <summary>The club's formation id (for the coach's board).</summary>
        public string Formation = "433";

        public static ClubInfo Of(ClubState c)
        {
            var t = c.TeamSetup();
            return new ClubInfo
            {
                Team = t, Crest = c.S.Crest, GoalFx = c.S.GoalFx, Formation = c.S.Lineup.Formation,
                Cards = t.Players.Select(p => p.Source as Card).ToArray(),
                BenchCards = t.Bench?.Select(p => p.Source as Card).ToArray(),
            };
        }

        /// <summary>The cards back onto the players (the engine's line-up doesn't carry them).</summary>
        public TeamSetup Restore()
        {
            for (int i = 0; i < Team.Players.Count && Cards != null && i < Cards.Length; i++) Team.Players[i].Source = Cards[i];
            for (int i = 0; Team.Bench != null && BenchCards != null && i < Team.Bench.Count && i < BenchCards.Length; i++) Team.Bench[i].Source = BenchCards[i];
            return Team;
        }
    }

    /// <summary>Everything the friend needs to put the same match on screen.</summary>
    public sealed class Kickoff
    {
        public int Seed;
        public string Ground = "big";
        public ClubInfo Home, Away;
        /// <summary>The host's own stadium, when the ground is "custom".</summary>
        public GameNight.Grounds.Build.StadiumPlan Plan;
        /// <summary>Coach mode: the computer plays both sides, the two friends manage them.</summary>
        public bool Coach;
    }

    Online(ClubState club, bool host, string code)
    {
        _club = club;
        IsHost = host;
        Code = code;
    }

    /// <summary>Opens a room with a fresh code and waits for a friend.</summary>
    public static Online Host(ClubState club)
    {
        Current?.Dispose();
        Current = new Online(club, true, NewCode());
        Current.Connect();
        return Current;
    }

    /// <summary>Joins the friend's room.</summary>
    public static Online Join(ClubState club, string code)
    {
        Current?.Dispose();
        Current = new Online(club, false, code);
        Current.Connect();
        return Current;
    }

    /// <summary>Online play needs the relay's address (set when the game was built).</summary>
    public static bool Available => RelayConfig.Url.Length > 0;

    static string NewCode() => Random.Shared.Next(1000, 10000).ToString();

    void Connect()
    {
        Now = Stage.Connecting;
        _relay?.Dispose();
        _relay = new Relay(RelayConfig.Url, IsHost, Code);
    }

    /// <summary>The lobby's side of the line (call every frame until Playing): handles the
    /// relay's state and the set-up messages.</summary>
    public void Poll()
    {
        if (Now == Stage.Failed || Now == Stage.Playing) return;
        if (_relay.Status == Relay.State.Open && Now == Stage.Connecting)
        {
            Now = Stage.Waiting;
            _tries = 0;
            if (!IsHost) Send(Club, JsonSerializer.SerializeToUtf8Bytes(ClubInfo.Of(_club), Json));
        }
        while (_relay.TryReceive(out var m))
        {
            if (m.Length == 0) continue;
            if (IsHost && m[0] == Club)
            {
                try
                {
                    Friend = JsonSerializer.Deserialize<ClubInfo>(new ReadOnlySpan<byte>(m, 1, m.Length - 1), Json);
                    Now = Friend?.Team != null ? Stage.Ready : Now;
                }
                catch (JsonException) { }
            }
            else if (!IsHost && m[0] == Start)
            {
                try
                {
                    Match = JsonSerializer.Deserialize<Kickoff>(new ReadOnlySpan<byte>(m, 1, m.Length - 1), Json);
                    if (Match?.Home?.Team != null && Match.Away?.Team != null) Now = Stage.Playing;
                }
                catch (JsonException) { }
            }
        }
        if (_relay.Status != Relay.State.Closed) return;
        int why = _relay.CloseCode;
        if (IsHost && (why == Relay.Taken || (why == Relay.Left && Now != Stage.Failed)))
        {
            // The code was taken: another one. The friend left the lobby: the room opens again.
            if (why == Relay.Taken) Code = NewCode();
            Friend = null;
            if (++_tries < 5)
            {
                Connect();
                return;
            }
        }
        Now = Stage.Failed;
        Problem = why switch
        {
            Relay.NoGame => $"No game with the code {Code}",
            Relay.Full => "That game already has two players",
            Relay.Left => IsHost ? "Your friend left" : "The host left",
            _ => _relay.Error.Length > 0 ? "Can't reach the online server" : "The connection dropped",
        };
    }

    /// <summary>Host: the match is on. The friend gets the whole set-up; returns it for this side too.</summary>
    public MatchSetup KickOff(int seed, string ground, out Kickoff k)
    {
        var home = ClubInfo.Of(_club);
        var away = Friend;
        // Two shirts too alike: the friend changes into the away kit.
        var hk = home.Team.Info.Kit;
        var ak = away.Team.Info.Kit;
        if (ColorDist(hk.Shirt, ak.Shirt) < 120)
        {
            (ak.Shirt, ak.Shirt2, ak.Shorts, ak.Socks) = (0xf1ebdc, 0x23345e, 0x23345e, 0xf1ebdc);
            if (ColorDist(hk.Shirt, ak.Shirt) < 120) (ak.Shirt, ak.Shirt2, ak.Shorts, ak.Socks) = (0x2a2440, 0xffd447, 0x2a2440, 0x2a2440);
        }
        k = new Kickoff { Seed = seed, Ground = ground, Home = home, Away = away, Plan = ground == "custom" ? _club.S.Stadium : null, Coach = CoachMode };
        Send(Start, JsonSerializer.SerializeToUtf8Bytes(k, Json));
        Now = Stage.Playing;
        return new MatchSetup { Teams = new[] { home.Restore(), away.Restore() } };
    }

    static double ColorDist(int a, int b) =>
        Math.Sqrt(Math.Pow(((a >> 16) & 255) - ((b >> 16) & 255), 2) + Math.Pow(((a >> 8) & 255) - ((b >> 8) & 255), 2) + Math.Pow((a & 255) - (b & 255), 2));

    // ---------------------------------------------------------------- in the match

    /// <summary>The line's still up.</summary>
    public bool Connected => _relay?.Status == Relay.State.Open;

    /// <summary>The line dropped or the other side left (in a match).</summary>
    public bool Lost => _relay == null || _relay.Status == Relay.State.Closed;

    public void Send(byte type, byte[] body)
    {
        var m = new byte[body.Length + 1];
        m[0] = type;
        Buffer.BlockCopy(body, 0, m, 1, body.Length);
        _relay?.Send(m);
    }

    /// <summary>A whole message (type byte included), as FrameCodec makes them.</summary>
    public void SendRaw(byte[] msg) => _relay?.Send(msg);

    public bool TryReceive(out byte[] msg)
    {
        msg = null;
        return _relay != null && _relay.TryReceive(out msg);
    }

    /// <summary>Done with it: says goodbye and hangs up.</summary>
    public void Dispose()
    {
        if (_relay?.Status == Relay.State.Open) Send(Leave, Array.Empty<byte>());
        _relay?.Dispose();
        _relay = null;
        if (Current == this) Current = null;
    }

    /// <summary>A JSON message (coach orders and boards).</summary>
    public void SendJson<T>(byte type, T body) => Send(type, JsonSerializer.SerializeToUtf8Bytes(body, Json));

    public static T ReadJson<T>(byte[] m)
    {
        try { return JsonSerializer.Deserialize<T>(m.AsSpan(1), Json); }
        catch (JsonException) { return default; }
    }

    public static string Utf8(byte[] m, int from) => Encoding.UTF8.GetString(m, from, m.Length - from);
}

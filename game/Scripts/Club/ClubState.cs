using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GameNight.Sim;

namespace GameNight.Club;

public sealed class Lineup
{
    public string Formation = "433";
    /// <summary>Card id per slot (11).</summary>
    public string[] Slots = new string[11];
    /// <summary>Custom slot placement per index (null = formation default).</summary>
    public FSlot[] Custom = new FSlot[11];
}

/// <summary>The club's own kit: shirt design and colours.</summary>
public sealed class ClubKit
{
    public int Pattern;
    public int Main = 0xc8393b, Secondary = 0x8f1f24, Shorts = 0xf3ede0;
}

public sealed class Record
{
    public int Played, Won, Drawn, Lost, Gf, Ga;
}

/// <summary>Everything saved on the device.</summary>
public sealed class ClubSave
{
    public int V = 1;
    public string Name = "Rossoneri Athletic";
    /// <summary>Three-letter code on the scoreboard (empty = from the name).</summary>
    public string Short = "";
    public int Coins;
    public List<Card> Cards = new();
    public Lineup Lineup = new();
    public Record Record = new();
    public int PacksOpened;
    public ClubKit Kit = new();
    public Crest Crest = new();
    public Banner Banner = new();
    /// <summary>You, the manager on the touchline.</summary>
    public Coach Coach = new();
    /// <summary>The captain's card (unset, or not in the XI: the best-rated starter).</summary>
    public string Captain = "";
    /// <summary>The ground last played at: preselected next time.</summary>
    public string Ground = "big";
    /// <summary>The club's own stadium: a stand set per slot (the stadium builder).</summary>
    public GameNight.Grounds.Build.StadiumPlan Stadium = new();
    /// <summary>Unix ms when the free pack is next available.</summary>
    public long FreePackAt;
    /// <summary>Best streak per training drill.</summary>
    public Dictionary<string, int> DrillBest = new();
}

/// <summary>
/// The player's club (the PWA's src/meta/club.ts): collection, line-up, coins, record. Saved
/// as JSON in the app's own storage after every change.
/// </summary>
public sealed class ClubState
{
    public const int StartCoins = 6000;
    public const int FreePackHours = 4;
    const string SaveFile = "club.json";

    public ClubSave S;
    public event Action Changed;
    readonly string _path;

    static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    public ClubState(string dir)
    {
        _path = Path.Combine(dir, SaveFile);
        S = Load() ?? Fresh();
        Repair();
        Save();
    }

    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // ---------------------------------------------------------------- persistence

    ClubSave Fresh()
    {
        var rng = new Rng((uint)(Now ^ 0x5bd1e995) & 0x7fffffff);
        // Starter squad: a full, mostly-common team with a couple of rares to build around.
        (Position, Rarity)[] want =
        {
            (Position.GK, Rarity.Common), (Position.GK, Rarity.Common),
            (Position.CB, Rarity.Common), (Position.CB, Rarity.Common), (Position.CB, Rarity.Rare), (Position.LB, Rarity.Common), (Position.RB, Rarity.Common),
            (Position.CDM, Rarity.Common), (Position.CM, Rarity.Common), (Position.CM, Rarity.Common), (Position.CAM, Rarity.Common), (Position.LM, Rarity.Common), (Position.RM, Rarity.Common),
            (Position.LW, Rarity.Common), (Position.RW, Rarity.Common), (Position.ST, Rarity.Rare), (Position.ST, Rarity.Common),
        };
        var k = TeamData.Default[0].Kit;
        S = new ClubSave
        {
            Name = TeamData.Default[0].Name,
            Coins = StartCoins,
            Cards = want.Select(w => Club.Cards.Generate(rng, w.Item2, w.Item1)).ToList(),
            Kit = new ClubKit { Main = k.Shirt, Secondary = k.Shirt2, Shorts = k.Shorts },
        };
        AutoPick(save: false);
        return S;
    }

    ClubSave Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var s = JsonSerializer.Deserialize<ClubSave>(File.ReadAllText(_path), Json);
            return s != null && s.V == 1 && s.Cards != null ? s : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Keep a loaded save consistent (missing cards, wrong lengths).</summary>
    void Repair()
    {
        S.Kit ??= new ClubKit();
        S.Crest ??= new Crest();
        S.Banner ??= new Banner();
        S.Coach ??= new Coach();
        S.Record ??= new Record();
        S.DrillBest ??= new();
        S.Stadium ??= new();
        if (S.Stadium.Sets == null || S.Stadium.Sets.Length != 8) S.Stadium.Sets = new GameNight.Grounds.Build.StadiumPlan().Sets;
        S.Lineup ??= new Lineup();
        var l = S.Lineup;
        Array.Resize(ref l.Slots, 11);
        Array.Resize(ref l.Custom, 11);
        var ids = new HashSet<string>(S.Cards.Select(c => c.Id));
        var seen = new HashSet<string>();
        for (int i = 0; i < 11; i++)
            if (l.Slots[i] == null || !ids.Contains(l.Slots[i]) || !seen.Add(l.Slots[i])) l.Slots[i] = null;
        if (l.Slots.Any(s => s == null)) FillGaps();
    }

    public void Save()
    {
        try
        {
            // Write a fresh file then swap it in, so a crash mid-write never loses the club.
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(S, Json));
            File.Move(tmp, _path, true);
        }
        catch
        {
            /* storage unavailable: keep playing in memory */
        }
        Changed?.Invoke();
    }

    public void Reset()
    {
        S = Fresh();
        Save();
    }

    // ---------------------------------------------------------------- queries

    public Card Card(string id) => id == null ? null : S.Cards.Find(c => c.Id == id);

    public Formation Formation => Formations.ById(S.Lineup.Formation);

    /// <summary>Effective slot (custom placement or the formation default).</summary>
    public FSlot Slot(int i) => S.Lineup.Custom[i] ?? Formation.Slots[i];

    public Card[] Starters() => S.Lineup.Slots.Select(Card).ToArray();

    public bool IsStarter(string id) => Array.IndexOf(S.Lineup.Slots, id) >= 0;

    /// <summary>Shirt index of the captain: the chosen one if he's in the XI, else the best-rated starter.</summary>
    public int CaptainIndex()
    {
        var s = Starters();
        int chosen = Array.FindIndex(s, c => c != null && c.Id == S.Captain);
        if (chosen >= 0) return chosen;
        int best = 0;
        for (int i = 0; i < 11; i++)
            if (s[i] != null && s[i].Overall > (s[best]?.Overall ?? -1)) best = i;
        return best;
    }

    public void SetCaptain(string id)
    {
        S.Captain = id;
        Save();
    }

    public List<Card> Bench() => S.Cards.Where(c => !IsStarter(c.Id)).OrderByDescending(c => c.Overall).ToList();

    /// <summary>Team rating: average of the starters in their slots.</summary>
    public int TeamRating()
    {
        var s = Starters();
        double sum = 0;
        for (int i = 0; i < 11; i++) sum += s[i] != null ? Club.Cards.RatingIn(s[i], Slot(i).Pos) : 30;
        return Club.Cards.JsRound(sum / 11);
    }

    // ---------------------------------------------------------------- line-up edits

    int TakeBest(List<Card> pool, Position pos)
    {
        int best = -1, bestR = -1;
        for (int k = 0; k < pool.Count; k++)
        {
            int r = Club.Cards.RatingIn(pool[k], pos);
            if (r > bestR)
            {
                bestR = r;
                best = k;
            }
        }
        return best;
    }

    public void SetFormation(string id)
    {
        if (!Formations.All.Any(f => f.Id == id)) return;
        var pool = Starters().Where(c => c != null).ToList();
        S.Lineup.Formation = id;
        S.Lineup.Custom = new FSlot[11];
        // Re-seat the same eleven where they fit best (keeper first, so he stays in goal).
        var slots = new string[11];
        var f = Formation;
        foreach (int i in Enumerable.Range(0, 11).OrderBy(i => f.Slots[i].Pos == Position.GK ? 0 : 1))
        {
            int b = TakeBest(pool, f.Slots[i].Pos);
            if (b < 0) continue;
            slots[i] = pool[b].Id;
            pool.RemoveAt(b);
        }
        S.Lineup.Slots = slots;
        Save();
    }

    /// <summary>Put a card in a slot. If it was already in another slot, the two swap.</summary>
    public void Assign(int slot, string cardId)
    {
        var l = S.Lineup.Slots;
        int from = Array.IndexOf(l, cardId);
        var prev = l[slot];
        l[slot] = cardId;
        if (from >= 0 && from != slot) l[from] = prev;
        Save();
    }

    public void SwapSlots(int a, int b)
    {
        var l = S.Lineup.Slots;
        (l[a], l[b]) = (l[b], l[a]);
        Save();
    }

    /// <summary>Move a slot on the board; its position label follows the zone it lands in.</summary>
    public void MoveSlot(int i, double x, double z)
    {
        if (Formation.Slots[i].Pos == Position.GK) return;
        x = Math.Clamp(x, Formations.MinX, Formations.MaxX);
        z = Math.Clamp(z, Formations.MinZ, Formations.MaxZ);
        S.Lineup.Custom[i] = new FSlot(Formations.Zone(x, z), x, z);
        Save();
    }

    public bool HasCustom => S.Lineup.Custom.Any(c => c != null);

    public void ResetPositions()
    {
        S.Lineup.Custom = new FSlot[11];
        Save();
    }

    /// <summary>Best available eleven for the current formation.</summary>
    public void AutoPick(bool save = true)
    {
        var slots = new string[11];
        var pool = new List<Card>(S.Cards);
        // Hardest-to-fill slots first: keeper, then by scarcity of natural players.
        var order = Enumerable.Range(0, 11)
            .Select(i => (i, pos: Slot(i).Pos, n: pool.Count(c => c.Position == Slot(i).Pos)))
            .OrderBy(o => o.pos == Position.GK ? -1 : 0).ThenBy(o => o.pos == Position.GK ? 0 : o.n).ToList();
        foreach (var (i, pos, _) in order)
        {
            int b = TakeBest(pool, pos);
            if (b < 0) continue;
            slots[i] = pool[b].Id;
            pool.RemoveAt(b);
        }
        // Polish the greedy pick: swap any two starters, or a starter with a bench player,
        // while it raises the total (natural positions win ties, so nobody plays out of
        // position for no gain).
        double Score(Card c, int i) => c == null ? 0 : Club.Cards.RatingIn(c, Slot(i).Pos) + (c.Position == Slot(i).Pos ? 0.5 : 0);
        bool improved = true;
        for (int pass = 0; improved && pass < 8; pass++)
        {
            improved = false;
            for (int a = 0; a < 11; a++)
            {
                for (int b = a + 1; b < 11; b++)
                {
                    var ca = Card(slots[a]);
                    var cb = Card(slots[b]);
                    if (Score(cb, a) + Score(ca, b) > Score(ca, a) + Score(cb, b) + 0.01)
                    {
                        (slots[a], slots[b]) = (slots[b], slots[a]);
                        improved = true;
                    }
                }
                for (int k = 0; k < pool.Count; k++)
                {
                    var ca = Card(slots[a]);
                    if (Score(pool[k], a) > Score(ca, a) + 0.01)
                    {
                        slots[a] = pool[k].Id;
                        if (ca != null) pool[k] = ca;
                        else pool.RemoveAt(k--);
                        improved = true;
                    }
                }
            }
        }
        S.Lineup.Slots = slots;
        if (save) Save();
    }

    void FillGaps()
    {
        var l = S.Lineup.Slots;
        var pool = S.Cards.Where(c => !l.Contains(c.Id)).ToList();
        for (int i = 0; i < 11; i++)
        {
            if (l[i] != null) continue;
            int b = TakeBest(pool, Slot(i).Pos);
            if (b < 0) continue;
            l[i] = pool[b].Id;
            pool.RemoveAt(b);
        }
    }

    // ---------------------------------------------------------------- collection

    public void AddCards(IEnumerable<Card> cards)
    {
        S.Cards.AddRange(cards);
        Save();
    }

    /// <summary>Quick-sell a card from the bench (starters can't be sold).</summary>
    public int Sell(string id)
    {
        if (IsStarter(id)) return 0;
        var c = Card(id);
        if (c == null) return 0;
        int v = Club.Cards.SellValue(c);
        S.Cards.Remove(c);
        S.Coins += v;
        Save();
        return v;
    }

    /// <summary>Coins earned outside a match result (league bonuses, prize money).</summary>
    public void Earn(int coins)
    {
        if (coins <= 0) return;
        S.Coins += coins;
        Save();
    }

    public bool Spend(int coins)
    {
        if (S.Coins < coins) return false;
        S.Coins -= coins;
        Save();
        return true;
    }

    public long FreePackIn => Math.Max(0, S.FreePackAt - Now);

    /// <summary>Buy (or claim) a pack; null when it can't be had yet. The cards are in the club
    /// straight away: closing the app mid-reveal never loses a pull.</summary>
    public List<Card> BuyPack(PackDef p)
    {
        if (p.Price == 0)
        {
            if (FreePackIn > 0) return null;
            S.FreePackAt = Now + FreePackHours * 3600_000L;
        }
        else if (S.Coins < p.Price) return null;
        else S.Coins -= p.Price;
        var cards = Packs.Open(p, Now);
        S.PacksOpened++;
        AddCards(cards);
        return cards;
    }

    public void Rename(string name)
    {
        name = name.Trim();
        if (name.Length > 24) name = name[..24];
        if (name.Length > 0) S.Name = name;
        Save();
    }

    public void SetShort(string code)
    {
        S.Short = new string(code.ToUpperInvariant().Where(char.IsLetterOrDigit).Take(3).ToArray());
        Save();
    }

    public void SetKit(ClubKit k)
    {
        S.Kit = k;
        Save();
    }

    public void SetCrest(Crest c)
    {
        S.Crest = c;
        Save();
    }

    public void SetBanner(Banner b)
    {
        S.Banner = b;
        Save();
    }

    public void SetCoach(Coach c)
    {
        S.Coach = c;
        Save();
    }

    /// <summary>Banner colours: painted in the chosen club colour, lettering in whatever reads on it.</summary>
    public (string text, int bg, int fg) BannerColors()
    {
        var b = S.Banner;
        var k = S.Kit;
        int bg = b.Color == "main" ? k.Main : b.Color == "secondary" ? k.Secondary : 0x14123a;
        int other = b.Color == "main" ? k.Secondary : b.Color == "secondary" ? k.Main : 0xffd447;
        static double Lum(int c) => 0.299 * ((c >> 16) & 255) + 0.587 * ((c >> 8) & 255) + 0.114 * (c & 255);
        int fg = Math.Abs(Lum(other) - Lum(bg)) > 70 ? other : Lum(bg) > 140 ? 0x14121c : 0xf3eee2;
        return (b.Text, bg, fg);
    }

    public void SetStadium(GameNight.Grounds.Build.Slot slot, int set)
    {
        S.Stadium.Set(slot, set);
        Save();
    }

    /// <summary>What's round the ground (Surroundings.Names).</summary>
    public void SetStadiumArea(int area)
    {
        S.Stadium.Area = area;
        Save();
    }

    /// <summary>A stand set's main colour, everywhere it's built (0 its own).</summary>
    public void SetStadiumPaint(int set, uint col)
    {
        S.Stadium.SetPaint(set, col);
        Save();
    }

    public void SetGround(string g)
    {
        S.Ground = g;
        Save();
    }

    public int DrillBest(DrillKind k) => S.DrillBest.GetValueOrDefault(k.ToString());

    public void SaveDrillBest(DrillKind k, int best)
    {
        if (DrillBest(k) >= best) return;
        S.DrillBest[k.ToString()] = best;
        Save();
    }

    // ---------------------------------------------------------------- matches

    public string ShortName
    {
        get
        {
            if (!string.IsNullOrEmpty(S.Short)) return S.Short;
            var words = new string(S.Name.ToUpperInvariant().Where(ch => char.IsLetter(ch) || ch == ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 3) return string.Concat(words.Take(3).Select(w => w[0]));
            var w0 = words.Length > 0 ? words[0] : "GNC";
            return w0.Length > 3 ? w0[..3] : w0;
        }
    }

    public TeamInfo Info()
    {
        var d = TeamData.Default[0].Kit;
        var k = S.Kit;
        return new TeamInfo
        {
            Name = S.Name,
            Short = ShortName,
            Kit = new Kit { Shirt = k.Main, Shirt2 = k.Secondary, Shorts = k.Shorts, Socks = k.Main, GkShirt = d.GkShirt, GkShorts = d.GkShorts, Pattern = k.Pattern },
        };
    }

    static double ColorDist(int a, int b) =>
        Math.Sqrt(Math.Pow(((a >> 16) & 255) - ((b >> 16) & 255), 2) + Math.Pow(((a >> 8) & 255) - ((b >> 8) & 255), 2) + Math.Pow((a & 255) - (b & 255), 2));

    /// <summary>The opponent's kit: their usual one, or the change kit if it would clash with ours.</summary>
    public TeamInfo OpponentInfo()
    {
        var b = TeamData.Default[1];
        var ours = S.Kit;
        var k = b.Kit;
        var info = new TeamInfo { Name = b.Name, Short = b.Short, Kit = new Kit { Shirt = k.Shirt, Shirt2 = k.Shirt2, Shorts = k.Shorts, Socks = k.Socks, GkShirt = k.GkShirt, GkShorts = k.GkShorts, Pattern = k.Pattern } };
        if (ColorDist(ours.Main, k.Shirt) > 120 && ColorDist(ours.Shorts, k.Shorts) > 60) return info;
        var c = info.Kit;
        (c.Shirt, c.Shirt2, c.Shorts, c.Socks) = (0x1f6b4a, 0xf1ebdc, 0xf1ebdc, 0x1f6b4a);
        if (ColorDist(ours.Main, c.Shirt) < 120) (c.Shirt, c.Shirt2, c.Shorts, c.Socks) = (0x2a2440, 0xffd447, 0x2a2440, 0x2a2440);
        return info;
    }

    static SetupPlayer ToSetup(SimPlayer p, FSlot slot) => new()
    {
        Name = p.Name, Number = p.Number, Attrs = p.Attrs, Look = p.Look, Foot = p.Foot,
        Role = Club.Cards.RoleOf(slot.Pos), X = slot.X, Z = slot.Z,
    };

    public TeamSetup TeamSetup()
    {
        var s = Starters();
        var t = new TeamSetup { Info = Info(), Captain = CaptainIndex() };
        for (int i = 0; i < 11; i++)
        {
            var slot = Slot(i);
            var c = s[i] ?? Club.Cards.Generate(new Rng(i + 1), Rarity.Common, slot.Pos, 45);
            t.Players.Add(ToSetup(Club.Cards.ToSim(c, slot.Pos), slot));
        }
        return t;
    }

    /// <summary>Today's opponent: built around our level so matches stay competitive.</summary>
    public int OpponentLevel(int seed) => (int)Math.Clamp(TeamRating() + Club.Cards.JsRound(new Rng(seed).Gauss() * 2) + 1, 55, 92);

    public TeamSetup Opponent(int seed)
    {
        var rng = new Rng(seed);
        int level = OpponentLevel(seed);
        rng.Gauss();
        var f = Formations.All[(int)(rng.Next() * 3)];
        static Rarity RarityFor(int o) => o >= 88 ? Rarity.Icon : o >= 82 ? Rarity.Legendary : o >= 74 ? Rarity.Epic : o >= 64 ? Rarity.Rare : Rarity.Common;
        // Their captain: the best-rated man in their XI.
        var t = new TeamSetup { Info = OpponentInfo() };
        int top = -1;
        for (int i = 0; i < 11; i++)
        {
            var slot = f.Slots[i];
            int o = (int)Math.Clamp(Club.Cards.JsRound(level + rng.Gauss() * 3), 45, 95);
            var c = Club.Cards.Generate(rng, RarityFor(o), slot.Pos, o);
            if (c.Overall > top)
            {
                top = c.Overall;
                t.Captain = i;
            }
            t.Players.Add(ToSetup(Club.Cards.ToSim(c, slot.Pos), slot));
        }
        return t;
    }

    public MatchSetup MatchSetup(int seed) => new() { Teams = new[] { TeamSetup(), Opponent(seed) } };

    /// <summary>Walked off: booked as a 0-3 defeat, no coins.</summary>
    public void RecordForfeit()
    {
        var r = S.Record;
        r.Played++;
        r.Lost++;
        r.Ga += 3;
        Save();
    }

    /// <summary>Book a finished match; returns the coins earned and W / D / L.</summary>
    public (int coins, char result) RecordResult(int gf, int ga)
    {
        var r = S.Record;
        r.Played++;
        r.Gf += gf;
        r.Ga += ga;
        char result = gf > ga ? 'W' : gf == ga ? 'D' : 'L';
        if (result == 'W') r.Won++;
        else if (result == 'D') r.Drawn++;
        else r.Lost++;
        int coins = (result == 'W' ? 1500 : result == 'D' ? 800 : 400) + gf * 150;
        S.Coins += coins;
        Save();
        return (coins, result);
    }
}

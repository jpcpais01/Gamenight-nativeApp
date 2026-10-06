using System;
using System.Collections.Generic;
using GameNight.Sim;

namespace GameNight.Club;

// Player cards: the club's collection (the PWA's src/meta/cards.ts). Stats are 1..99 (FIFA
// style) plus a real body (height / weight), and every one of them feeds the simulation (ToSim).

public enum Position { GK, CB, LB, RB, CDM, CM, CAM, LM, RM, LW, RW, ST }

public enum Rarity { Common, Rare, Epic, Legendary, Icon }

public enum Stat { Pace, Accel, Agility, Stamina, Strength, Jumping, Power, Passing, Shooting, Dribbling, Defending, Keeping }

public sealed class Stats
{
    public int Pace, Accel, Agility, Stamina, Strength, Jumping, Power, Passing, Shooting, Dribbling, Defending, Keeping;

    public int this[Stat k]
    {
        get => k switch
        {
            Stat.Pace => Pace, Stat.Accel => Accel, Stat.Agility => Agility, Stat.Stamina => Stamina,
            Stat.Strength => Strength, Stat.Jumping => Jumping, Stat.Power => Power, Stat.Passing => Passing,
            Stat.Shooting => Shooting, Stat.Dribbling => Dribbling, Stat.Defending => Defending, _ => Keeping,
        };
        set
        {
            switch (k)
            {
                case Stat.Pace: Pace = value; break;
                case Stat.Accel: Accel = value; break;
                case Stat.Agility: Agility = value; break;
                case Stat.Stamina: Stamina = value; break;
                case Stat.Strength: Strength = value; break;
                case Stat.Jumping: Jumping = value; break;
                case Stat.Power: Power = value; break;
                case Stat.Passing: Passing = value; break;
                case Stat.Shooting: Shooting = value; break;
                case Stat.Dribbling: Dribbling = value; break;
                case Stat.Defending: Defending = value; break;
                default: Keeping = value; break;
            }
        }
    }
}

public sealed class Card
{
    public string Id = "";
    public string Name = "";
    /// <summary>Index into Cards.Nations.</summary>
    public int Nation;
    public Position Position;
    public Rarity Rarity;
    public int Number;
    public Stats Stats = new();
    /// <summary>Centimetres and kilograms.</summary>
    public int Height, Weight;
    /// <summary>'L' or 'R'.</summary>
    public char Foot = 'R';
    /// <summary>Indices into TeamData.SkinTones / HairColors.</summary>
    public int Skin, Hair, HairStyle;
    /// <summary>When it joined the club (unix ms).</summary>
    public long Got;

    public int Overall => Cards.OverallAt(Stats, Position);
    public string LastName => Name.Contains(' ') ? Name[(Name.LastIndexOf(' ') + 1)..] : Name;
}

public sealed class Nation
{
    public string Code = "";
    /// <summary>Three bands; Vertical: left to right, else top to bottom.</summary>
    public int[] Flag = Array.Empty<int>();
    public bool Vertical;
    public string[] First = Array.Empty<string>(), Last = Array.Empty<string>();
    /// <summary>Indices into TeamData.SkinTones, weighted by repetition.</summary>
    public int[] Skin = Array.Empty<int>();
}

/// <summary>What a card is when it plays in a slot: the engine's player.</summary>
public sealed class SimPlayer
{
    public string Name = "";
    public int Number, Foot;
    public Attributes Attrs = new();
    public Look Look = new();
}

public static class Cards
{
    public static readonly Position[] Positions = (Position[])Enum.GetValues(typeof(Position));
    public static readonly Rarity[] Rarities = (Rarity[])Enum.GetValues(typeof(Rarity));
    public static readonly Stat[] StatKeys = (Stat[])Enum.GetValues(typeof(Stat));

    public static string Label(Rarity r) => r.ToString();

    public static readonly Dictionary<Stat, string> StatLabel = new()
    {
        [Stat.Pace] = "Pace", [Stat.Accel] = "Acceleration", [Stat.Agility] = "Agility", [Stat.Stamina] = "Stamina",
        [Stat.Strength] = "Strength", [Stat.Jumping] = "Jumping", [Stat.Power] = "Shot power", [Stat.Passing] = "Passing",
        [Stat.Shooting] = "Finishing", [Stat.Dribbling] = "Dribbling", [Stat.Defending] = "Defending", [Stat.Keeping] = "Goalkeeping",
    };

    /// <summary>Overall range per rarity.</summary>
    static readonly (int lo, int hi)[] RarityOvr = { (52, 64), (64, 74), (74, 82), (82, 88), (88, 94) };

    public static readonly int[] RarityColor = { 0xc98d55, 0xcfd8e3, 0xffd447, 0xc27bff, 0x7ff6ff };

    // ------------------------------------------------------------------ positions

    /// <summary>A card on the bench, as he'd play in his own position.</summary>
    public static SetupPlayer Sub(Card c)
    {
        var sp = ToSim(c, c.Position);
        return new SetupPlayer { Name = sp.Name, Number = sp.Number, Attrs = sp.Attrs, Look = sp.Look, Foot = sp.Foot, Role = RoleOf(c.Position), Pos = c.Position.ToString() };
    }

    public static Role RoleOf(Position p) => p switch
    {
        Position.GK => Role.GK,
        Position.CB or Position.LB or Position.RB => Role.DEF,
        Position.ST or Position.LW or Position.RW => Role.FWD,
        _ => Role.MID,
    };

    /// <summary>Positions a player can cover nearly as well as his own.</summary>
    static readonly Dictionary<Position, Position[]> Near = new()
    {
        [Position.GK] = Array.Empty<Position>(),
        [Position.CB] = new[] { Position.CDM },
        [Position.LB] = new[] { Position.LM, Position.CB },
        [Position.RB] = new[] { Position.RM, Position.CB },
        [Position.CDM] = new[] { Position.CM, Position.CB },
        [Position.CM] = new[] { Position.CDM, Position.CAM },
        [Position.CAM] = new[] { Position.CM, Position.ST },
        [Position.LM] = new[] { Position.LW, Position.LB, Position.CM },
        [Position.RM] = new[] { Position.RW, Position.RB, Position.CM },
        [Position.LW] = new[] { Position.LM, Position.ST, Position.RW },
        [Position.RW] = new[] { Position.RM, Position.ST, Position.LW },
        [Position.ST] = new[] { Position.CAM, Position.LW, Position.RW },
    };

    /// <summary>1 natural, 0.9 close, 0.85 same line, 0.75 out of position, 0.4 in (or out of) goal.</summary>
    public static double FitFactor(Position card, Position slot)
    {
        if (card == slot) return 1;
        if (card == Position.GK || slot == Position.GK) return 0.4;
        if (Array.IndexOf(Near[card], slot) >= 0) return 0.9;
        if (RoleOf(card) == RoleOf(slot)) return 0.85;
        return 0.75;
    }

    // ------------------------------------------------------------------ overall

    static readonly Dictionary<Position, (Stat, int)[]> W = new()
    {
        [Position.GK] = new[] { (Stat.Keeping, 10), (Stat.Jumping, 1), (Stat.Agility, 1) },
        [Position.CB] = new[] { (Stat.Defending, 5), (Stat.Strength, 3), (Stat.Jumping, 2), (Stat.Pace, 1), (Stat.Passing, 1) },
        [Position.LB] = new[] { (Stat.Defending, 3), (Stat.Pace, 3), (Stat.Stamina, 2), (Stat.Passing, 2), (Stat.Accel, 1) },
        [Position.RB] = new[] { (Stat.Defending, 3), (Stat.Pace, 3), (Stat.Stamina, 2), (Stat.Passing, 2), (Stat.Accel, 1) },
        [Position.CDM] = new[] { (Stat.Defending, 4), (Stat.Passing, 3), (Stat.Stamina, 2), (Stat.Strength, 2) },
        [Position.CM] = new[] { (Stat.Passing, 4), (Stat.Dribbling, 3), (Stat.Stamina, 2), (Stat.Defending, 1), (Stat.Shooting, 1) },
        [Position.CAM] = new[] { (Stat.Passing, 4), (Stat.Dribbling, 4), (Stat.Shooting, 2), (Stat.Agility, 1), (Stat.Accel, 1) },
        [Position.LM] = new[] { (Stat.Pace, 3), (Stat.Passing, 3), (Stat.Dribbling, 3), (Stat.Stamina, 1), (Stat.Accel, 1) },
        [Position.RM] = new[] { (Stat.Pace, 3), (Stat.Passing, 3), (Stat.Dribbling, 3), (Stat.Stamina, 1), (Stat.Accel, 1) },
        [Position.LW] = new[] { (Stat.Pace, 3), (Stat.Dribbling, 4), (Stat.Shooting, 2), (Stat.Accel, 2), (Stat.Agility, 1) },
        [Position.RW] = new[] { (Stat.Pace, 3), (Stat.Dribbling, 4), (Stat.Shooting, 2), (Stat.Accel, 2), (Stat.Agility, 1) },
        [Position.ST] = new[] { (Stat.Shooting, 5), (Stat.Power, 2), (Stat.Pace, 2), (Stat.Dribbling, 2), (Stat.Accel, 1), (Stat.Jumping, 1) },
    };

    public static int OverallAt(Stats s, Position pos)
    {
        double sum = 0, w = 0;
        foreach (var (k, v) in W[pos])
        {
            sum += s[k] * v;
            w += v;
        }
        return JsRound(sum / w);
    }

    /// <summary>Rating in a given slot (out-of-position players play worse).</summary>
    public static int RatingIn(Card c, Position slot)
    {
        double f = FitFactor(c.Position, slot);
        return JsRound(c.Overall * (f == 1 ? 1 : 0.55 + 0.45 * f));
    }

    /// <summary>Math.round as JavaScript does it (halves up).</summary>
    public static int JsRound(double v) => (int)Math.Floor(v + 0.5);

    /// <summary>The six headline numbers on the card face.</summary>
    public static (string, int)[] FaceStats(Card c)
    {
        var s = c.Stats;
        if (c.Position == Position.GK)
            return new[]
            {
                ("DIV", JsRound(s.Keeping * 0.7 + s.Agility * 0.3)),
                ("HAN", s.Keeping),
                ("KIC", JsRound(s.Power * 0.6 + s.Passing * 0.4)),
                ("REF", JsRound(s.Keeping * 0.6 + s.Accel * 0.4)),
                ("SPD", JsRound((s.Pace + s.Accel) / 2.0)),
                ("POS", JsRound(s.Keeping * 0.8 + s.Jumping * 0.2)),
            };
        return new[]
        {
            ("PAC", JsRound(s.Pace * 0.55 + s.Accel * 0.45)),
            ("SHO", JsRound(s.Shooting * 0.7 + s.Power * 0.3)),
            ("PAS", s.Passing),
            ("DRI", JsRound(s.Dribbling * 0.75 + s.Agility * 0.25)),
            ("DEF", s.Defending),
            ("PHY", JsRound(s.Strength * 0.5 + s.Stamina * 0.3 + s.Jumping * 0.2)),
        };
    }

    /// <summary>Short, human description of what makes this player special.</summary>
    public static List<string> Traits(Card c)
    {
        var s = c.Stats;
        var t = new List<string>();
        if (c.Position != Position.GK)
        {
            if (s.Pace >= 85 && s.Accel >= 82) t.Add("Speedster");
            if (c.Height >= 190 && s.Jumping >= 75) t.Add("Aerial threat");
            if (s.Strength >= 85) t.Add("Powerhouse");
            if (s.Power >= 86) t.Add("Rocket shot");
            if (s.Dribbling >= 85 && s.Agility >= 82) t.Add("Magician");
            if (s.Passing >= 86) t.Add("Playmaker");
            if (s.Stamina >= 88) t.Add("Engine");
            if (s.Defending >= 85) t.Add("Wall");
            if (s.Shooting >= 87) t.Add("Clinical");
        }
        else
        {
            if (s.Keeping >= 85) t.Add("Shot stopper");
            if (c.Height >= 194) t.Add("Giant");
            if (s.Accel >= 70) t.Add("Sweeper keeper");
        }
        if (t.Count > 3) t.RemoveRange(3, t.Count - 3);
        return t;
    }

    // ------------------------------------------------------------------ simulation

    /// <summary>1..99 to the simulation's 0..1 scale (60 -> 0.57, 80 -> 0.78, 90 -> 0.89).</summary>
    static double Unit(double s) => Math.Pow(Math.Clamp(s, 1, 99) / 100, 1.1);

    /// <summary>The player's body type, as it shows on the pitch (see sim Body).</summary>
    public static string BodyName(Card c) =>
        Body.Names[(int)Body.TypeOf(c.Height / 100.0, c.Weight, ToSim(c, c.Position).Attrs.Strength)];

    /// <summary>A card playing in a slot: technique suffers out of position, the body doesn't.</summary>
    public static SimPlayer ToSim(Card c, Position slot)
    {
        // Playstyles lift their stats for real.
        var s = new Stats();
        foreach (var k in StatKeys) s[k] = Playstyles.Boosted(c, k);
        double f = FitFactor(c.Position, slot);
        double Tech(double v) => Unit(v) * (f == 1 ? 1 : 0.7 + 0.3 * f);
        double h = c.Height / 100.0;
        double bmi = c.Weight / (h * h);
        return new SimPlayer
        {
            Name = c.Name,
            Number = c.Number,
            Foot = c.Foot == 'L' ? -1 : 1,
            Attrs = new Attributes
            {
                Pace = Unit(s.Pace),
                Accel = Unit(s.Accel),
                Agility = Unit(s.Agility),
                Stamina = Unit(s.Stamina),
                Strength = Unit(s.Strength),
                Jumping = Unit(s.Jumping),
                Power = Unit(s.Power),
                Control = Tech(s.Dribbling),
                Passing = Tech(s.Passing),
                Shooting = Tech(s.Shooting),
                Defending = Tech(s.Defending),
                Keeping = slot == Position.GK ? Unit(s.Keeping) : 0.2,
                Height = h,
                Weight = c.Weight,
            },
            Look = new Look
            {
                Skin = TeamData.SkinTones[Math.Clamp(c.Skin, 0, TeamData.SkinTones.Length - 1)],
                Hair = TeamData.HairColors[Math.Clamp(c.Hair, 0, TeamData.HairColors.Length - 1)],
                HairStyle = c.HairStyle,
                Height = h / 1.8,
                Build = Math.Clamp(0.82 + (bmi - 20) * 0.06, 0.88, 1.16),
            },
        };
    }

    // ------------------------------------------------------------------ generation

    static Nation N(string code, int a, int b, int c, bool vertical, string first, string last, params int[] skin) => new()
    {
        Code = code, Flag = new[] { a, b, c }, Vertical = vertical,
        First = first.Split(','), Last = last.Split(','), Skin = skin,
    };

    public static readonly Nation[] Nations =
    {
        N("POR", 0x046a38, 0xda291c, 0xda291c, true, "João,Rui,Tiago,Diogo,Nuno,Pedro,André,Bruno,Gonçalo,Rafael", "Pais,Mendes,Carvalho,Moreira,Ferraz,Barros,Teixeira,Lopes,Amaral,Coelho", 0, 1, 1, 2, 3),
        N("ESP", 0xaa151b, 0xf1bf00, 0xaa151b, false, "Pablo,Sergio,Iker,Álvaro,Dani,Marcos,Adrián,Hugo,Unai,Jorge", "Ortega,Navarro,Serrano,Molina,Castaño,Iglesias,Romero,Vidal,Herrero,Prieto", 0, 1, 1, 2),
        N("BRA", 0x009c3b, 0xffdf00, 0x009c3b, false, "Thiago,Lucas,Gabriel,Mateus,Caio,Vinícius,Felipe,Davi,Renan,Igor", "Souza,Rocha,Almeida,Nascimento,Ribeiro,Cardoso,Lima,Batista,Farias,Moura", 1, 2, 3, 4, 5),
        N("ARG", 0x74acdf, 0xffffff, 0x74acdf, false, "Facundo,Lautaro,Nicolás,Gonzalo,Franco,Joaquín,Tomás,Santiago,Emiliano,Bautista", "Acosta,Benítez,Quiroga,Ledesma,Funes,Paredes,Villalba,Sosa,Aguirre,Medina", 0, 1, 1, 2),
        N("FRA", 0x0055a4, 0xffffff, 0xef4135, true, "Théo,Hugo,Lucas,Antoine,Kylian,Moussa,Jules,Bastien,Yanis,Rayan", "Lefèvre,Garnier,Rousseau,Diallo,Camara,Bonnet,Fontaine,Mercier,Traoré,Lambert", 0, 1, 3, 4, 5),
        N("GER", 0x000000, 0xdd0000, 0xffce00, false, "Lukas,Jonas,Leon,Felix,Niklas,Tim,Florian,Maximilian,Jannik,Moritz", "Becker,Hoffmann,Wagner,Krämer,Schulz,Neumann,Brandt,Vogel,Hartmann,Keller", 0, 0, 1, 3),
        N("ENG", 0xffffff, 0xce1124, 0xffffff, true, "Harry,Jack,Mason,Declan,Jude,Callum,Reece,Tyrone,Ollie,Ben", "Walker,Fletcher,Henderson,Barnes,Clarke,Wright,Hughes,Palmer,Shaw,Turner", 0, 0, 1, 3, 4),
        N("ITA", 0x009246, 0xffffff, 0xce2b37, true, "Marco,Lorenzo,Federico,Alessandro,Davide,Matteo,Riccardo,Gianluca,Nicolò,Simone", "Bellini,Ricci,Conti,Esposito,Marchetti,Ferrara,Galli,Bianchi,Moretti,Santoro", 0, 1, 1, 2),
        N("NED", 0xae1c28, 0xffffff, 0x21468b, false, "Daan,Sem,Bram,Jesse,Ruben,Stijn,Milan,Thijs,Lars,Joris", "de Vries,Bakker,Visser,Smit,van Dijkman,Mulder,de Boer,Jansen,Kuipers,Brouwer", 0, 0, 1, 4),
        N("NGA", 0x008751, 0xffffff, 0x008751, true, "Chidi,Emeka,Tunde,Kelechi,Samuel,Femi,Ikenna,Obinna,Ademola,Victor", "Okafor,Adeyemi,Nwosu,Balogun,Eze,Okonkwo,Afolabi,Chukwu,Ogunleye,Iwobi", 4, 4, 5, 5),
        N("JPN", 0xffffff, 0xbc002d, 0xffffff, true, "Haruto,Ren,Sota,Yuto,Kaito,Daichi,Takumi,Riku,Kenta,Shoma", "Tanaka,Suzuki,Kobayashi,Nakamura,Ito,Yamamoto,Matsuda,Inoue,Kimura,Hayashi", 0, 1),
        N("SEN", 0x00853f, 0xfdef42, 0xe31b23, true, "Ismaïla,Pape,Cheikh,Moussa,Babacar,Idrissa,Mamadou,Lamine,Ousmane,Abdou", "Ndiaye,Sarr,Diop,Faye,Gueye,Mbaye,Cissé,Fall,Seck,Niang", 4, 5, 5),
        N("URU", 0xffffff, 0x0038a8, 0xffffff, false, "Matías,Federico,Rodrigo,Agustín,Diego,Sebastián,Maximiliano,Facundo,Martín,Bruno", "Pereira,Cáceres,Olivera,Rodríguez,Arrascaeta,Godín,Silva,Torreira,Varela,Núñez", 0, 1, 2),
        N("CRO", 0xff0000, 0xffffff, 0x171796, false, "Luka,Ivan,Mateo,Josip,Marko,Ante,Nikola,Dominik,Filip,Lovro", "Horvat,Kovačić,Babić,Marić,Jurić,Novak,Perić,Vuković,Knežević,Pavlović", 0, 0, 1),
        N("KOR", 0xffffff, 0xcd2e3a, 0xffffff, false, "Min-jae,Heung-min,Jae-sung,Hwang,Seung-ho,In-beom,Kang-in,Woo-young,Ji-sung,Dong-hyun", "Kim,Lee,Park,Choi,Jung,Kang,Cho,Yoon,Jang,Lim", 0, 1),
        N("GHA", 0xce1126, 0xfcd116, 0x006b3f, false, "Kwame,Kofi,Yaw,Kojo,Mohammed,Abdul,Ernest,Jordan,Thomas,Daniel", "Mensah,Asante,Boateng,Owusu,Appiah,Agyemang,Kudus,Partey,Amartey,Badu", 4, 5),
    };

    /// <summary>Stat templates per position (relative strengths around 0).</summary>
    static readonly Dictionary<Position, (Stat, int)[]> Template = new()
    {
        [Position.GK] = new[] { (Stat.Keeping, 20), (Stat.Pace, -25), (Stat.Accel, -18), (Stat.Dribbling, -25), (Stat.Shooting, -40), (Stat.Passing, -15), (Stat.Defending, -30), (Stat.Jumping, 5), (Stat.Agility, -5) },
        [Position.CB] = new[] { (Stat.Defending, 14), (Stat.Strength, 10), (Stat.Jumping, 8), (Stat.Pace, -6), (Stat.Dribbling, -12), (Stat.Shooting, -22), (Stat.Agility, -8), (Stat.Passing, -5) },
        [Position.LB] = new[] { (Stat.Defending, 6), (Stat.Pace, 6), (Stat.Stamina, 10), (Stat.Accel, 4), (Stat.Shooting, -16), (Stat.Jumping, -6) },
        [Position.RB] = new[] { (Stat.Defending, 6), (Stat.Pace, 6), (Stat.Stamina, 10), (Stat.Accel, 4), (Stat.Shooting, -16), (Stat.Jumping, -6) },
        [Position.CDM] = new[] { (Stat.Defending, 10), (Stat.Stamina, 8), (Stat.Strength, 6), (Stat.Passing, 4), (Stat.Shooting, -10), (Stat.Pace, -6) },
        [Position.CM] = new[] { (Stat.Passing, 10), (Stat.Dribbling, 5), (Stat.Stamina, 8), (Stat.Shooting, -4), (Stat.Jumping, -6) },
        [Position.CAM] = new[] { (Stat.Passing, 10), (Stat.Dribbling, 10), (Stat.Agility, 6), (Stat.Defending, -18), (Stat.Strength, -8), (Stat.Shooting, 2) },
        [Position.LM] = new[] { (Stat.Pace, 8), (Stat.Accel, 6), (Stat.Passing, 4), (Stat.Dribbling, 6), (Stat.Stamina, 6), (Stat.Defending, -12), (Stat.Strength, -6) },
        [Position.RM] = new[] { (Stat.Pace, 8), (Stat.Accel, 6), (Stat.Passing, 4), (Stat.Dribbling, 6), (Stat.Stamina, 6), (Stat.Defending, -12), (Stat.Strength, -6) },
        [Position.LW] = new[] { (Stat.Pace, 10), (Stat.Accel, 10), (Stat.Dribbling, 10), (Stat.Agility, 8), (Stat.Defending, -22), (Stat.Strength, -8), (Stat.Jumping, -8) },
        [Position.RW] = new[] { (Stat.Pace, 10), (Stat.Accel, 10), (Stat.Dribbling, 10), (Stat.Agility, 8), (Stat.Defending, -22), (Stat.Strength, -8), (Stat.Jumping, -8) },
        [Position.ST] = new[] { (Stat.Shooting, 14), (Stat.Power, 8), (Stat.Pace, 4), (Stat.Defending, -26), (Stat.Passing, -4), (Stat.Jumping, 2) },
    };

    /// <summary>Typical height (cm) by position.</summary>
    static readonly Dictionary<Position, int> TypicalHeight = new()
    {
        [Position.GK] = 191, [Position.CB] = 188, [Position.LB] = 178, [Position.RB] = 178, [Position.CDM] = 183, [Position.CM] = 179,
        [Position.CAM] = 176, [Position.LM] = 176, [Position.RM] = 176, [Position.LW] = 175, [Position.RW] = 175, [Position.ST] = 183,
    };

    static int _idCounter;

    static string Uid(Rng rng)
    {
        _idCounter = (_idCounter + 1) % 1000000;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x") + ((long)(rng.Next() * 1e9)).ToString("x") + _idCounter.ToString("x");
    }

    static T Pick<T>(Rng rng, T[] a) => a[(int)(rng.Next() * a.Length)];

    public static Position RandomPosition(Rng rng)
    {
        // More midfielders and defenders than keepers, like a real squad.
        Position[] bag =
        {
            Position.GK, Position.GK, Position.CB, Position.CB, Position.CB, Position.LB, Position.RB, Position.CDM, Position.CM,
            Position.CM, Position.CAM, Position.LM, Position.RM, Position.LW, Position.RW, Position.ST, Position.ST, Position.ST,
        };
        return Pick(rng, bag);
    }

    /// <summary>A new player of the given rarity (and position, random if omitted).</summary>
    public static Card Generate(Rng rng, Rarity rarity, Position? position = null, int? targetOvr = null, int? nationOf = null)
    {
        var pos = position ?? RandomPosition(rng);
        var (lo, hi) = RarityOvr[(int)rarity];
        // Skewed toward the bottom of the range: high rolls are the exciting ones.
        int target = targetOvr ?? JsRound(lo + (hi - lo) * Math.Pow(rng.Next(), 1.6));
        int nationIndex = (int)(rng.Next() * Nations.Length);
        if (nationOf is int fixedNation) nationIndex = fixedNation;
        var nation = Nations[nationIndex];

        // Body first: height drives jumping / strength / agility.
        int height = JsRound(Math.Clamp(TypicalHeight[pos] + rng.Gauss() * 6, 164, 203));
        double tall = (height - 180) / 10.0;
        double stocky = rng.Gauss() * 0.8;
        int weight = JsRound(Math.Clamp(22.8 * Math.Pow(height / 100.0, 2) + stocky * 4 + 2, 60, 102));

        var raw = new double[StatKeys.Length];
        var tpl = new Dictionary<Stat, int>();
        foreach (var (k, v) in Template[pos]) tpl[k] = v;
        foreach (var k in StatKeys)
        {
            double v = tpl.GetValueOrDefault(k) + rng.Gauss() * 6;
            if (k == Stat.Jumping) v += tall * 7;
            if (k == Stat.Strength) v += tall * 4 + stocky * 5;
            if (k == Stat.Agility) v -= tall * 5 + stocky * 2;
            if (k == Stat.Accel) v -= tall * 4;
            if (k == Stat.Pace) v -= stocky * 3;
            if (k == Stat.Keeping && pos != Position.GK) v = -60 + rng.Next() * 10;
            raw[(int)k] = v;
        }
        // Archetype spice: some players have one standout trait.
        double spike = rng.Next();
        if (spike < 0.18)
        {
            raw[(int)Stat.Pace] += 8;
            raw[(int)Stat.Accel] += 8;
        }
        else if (spike < 0.3) raw[(int)Stat.Power] += 10;
        else if (spike < 0.4) raw[(int)Stat.Stamina] += 10;
        else if (spike < 0.5) raw[(int)Stat.Strength] += 10;

        // Shift everything so the overall lands on target, then clamp.
        Stats Probe(double shift)
        {
            var s = new Stats();
            foreach (var k in StatKeys)
                s[k] = (int)Math.Clamp(JsRound(raw[(int)k] + shift), k == Stat.Keeping && pos != Position.GK ? 5 : 25, 99);
            return s;
        }
        double sh = target;
        for (int i = 0; i < 12; i++)
        {
            int o = OverallAt(Probe(sh), pos);
            if (o == target) break;
            sh += target - o;
        }

        return new Card
        {
            Id = Uid(rng),
            Name = $"{Pick(rng, nation.First)} {Pick(rng, nation.Last)}",
            Nation = nationIndex,
            Position = pos,
            Rarity = rarity,
            Number = pos == Position.GK ? (rng.Next() < 0.7 ? 1 : 12 + (int)(rng.Next() * 3) * 10) : 2 + (int)(rng.Next() * 30),
            Stats = Probe(sh),
            Height = height,
            Weight = weight,
            Foot = rng.Next() < 0.24 ? 'L' : 'R',
            Skin = Pick(rng, nation.Skin),
            Hair = (int)(rng.Next() * TeamData.HairColors.Length),
            HairStyle = (int)(rng.Next() * 4),
            Got = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    /// <summary>Coins for quick-selling a card.</summary>
    public static int SellValue(Card c)
    {
        int o = c.Overall;
        return JsRound((40 + Math.Pow(Math.Max(0, o - 45), 2.1) * 1.4) / 10) * 10;
    }
}

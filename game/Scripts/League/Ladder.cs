using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GameNight.Club;

namespace GameNight.League;

/// <summary>A home country: where its clubs' towns, names, grounds and managers come from.</summary>
public sealed class Country
{
    public string Code = "", Name = "";
    /// <summary>Flag and people's names.</summary>
    public Nation Nation;
    public string[] TownA = Array.Empty<string>(), TownB = Array.Empty<string>();
    /// <summary>Club name patterns around {T}, the town.</summary>
    public string[] Clubs = Array.Empty<string>();
    /// <summary>Ground name patterns: {T} town, {S} a surname.</summary>
    public string[] Grounds = Array.Empty<string>();
    /// <summary>Southern grounds: the comunale bowl more often than the old terraces.</summary>
    public bool Latin;
}

/// <summary>One of the associations that run football across Europe, from neighbourhood leagues
/// for starting clubs to the Golden League, where every side is full of stars.</summary>
public sealed class LeagueDef
{
    public string Id = "", Name = "", Blurb = "";
    /// <summary>1 local, 2 regional, 3 premier, 4 elite.</summary>
    public int Tier;
    public float Lon, Lat;
    /// <summary>Where its clubs come from.</summary>
    public string[] Countries = Array.Empty<string>();
    /// <summary>League titles needed to be let in.</summary>
    public int Need;

    /// <summary>A lower division: below the local league, only on the map when you zoom in.</summary>
    public bool Lower;

    public double Level => Lower ? 50 : Ladder.Levels[Tier - 1];
    public double PrizeScale => Lower ? 0.6 : Ladder.PrizeScale[Tier - 1];
    public string TierName => Lower ? "GRASSROOTS" : Ladder.TierNames[Tier - 1];
    public int Color => Lower ? Ladder.Grassroots : Ladder.TierColors[Tier - 1];
}

/// <summary>A knockout cup on the map: a national cup (its clubs from one country, as strong as
/// your league) or a special tournament (invited clubs from everywhere, at its own level).</summary>
public sealed class CupDef
{
    public string Id = "", Name = "", Blurb = "";
    public float Lon, Lat;
    /// <summary>Where its clubs come from; empty for a special tournament.</summary>
    public string Country = "";
    /// <summary>League titles needed for an invitation.</summary>
    public int Need;
    /// <summary>Clubs in the draw: 16 or 8.</summary>
    public int Size = 16;
    /// <summary>A special tournament's level; a national cup follows your league.</summary>
    public double Level;
    /// <summary>A special tournament's prize for the winners; a national cup's follows your league.</summary>
    public int Prize;

    public bool Special => Country == "";
    public int Rounds => Size == 8 ? 3 : 4;
    public int Color => Special ? 0xff8fd0 : 0x7fe0a0;
}

/// <summary>Your career across the map: the country you started in and the titles you've won.</summary>
public sealed class CareerSave
{
    public int V = 1;
    public string Country = "";
    public int Trophies;
    public Dictionary<string, int> Titles = new();
    /// <summary>Best finish per league.</summary>
    public Dictionary<string, int> Best = new();
    public int Seasons;
    /// <summary>Cups won, per cup.</summary>
    public Dictionary<string, int> Cups = new();
    /// <summary>Furthest run per cup, in rounds short of the trophy (0 = won, 1 = lost the final).</summary>
    public Dictionary<string, int> CupBest = new();
    /// <summary>The season (Seasons) each cup was last entered: one run per cup each season.</summary>
    public Dictionary<string, int> Entered = new();
}

/// <summary>
/// The ladder: ten local leagues, one per home country, four regional associations, two premier
/// leagues and the Golden League. Each tier has its level (the clubs' ratings), its prize money,
/// and the number of league titles it takes to be let in.
/// </summary>
public static class Ladder
{
    public static readonly double[] Levels = { 56, 64, 72, 81 };
    public static readonly double[] PrizeScale = { 1, 1.8, 3, 5 };
    public static readonly string[] TierNames = { "LOCAL", "REGIONAL", "PREMIER", "ELITE" };
    /// <summary>Bronze, silver, gold, diamond.</summary>
    public static readonly int[] TierColors = { 0xd9915a, 0xc8d4e6, 0xffd447, 0x7ff6ff };
    /// <summary>The lower divisions: earthy.</summary>
    public const int Grassroots = 0x9fb86a;

    static Nation Own(string code, int a, int b, int c, bool vertical, string first, string last) => new()
    {
        Code = code, Flag = new[] { a, b, c }, Vertical = vertical, First = first.Split(','), Last = last.Split(','), Skin = new[] { 0, 1, 1, 2 },
    };

    static Nation Nat(string code) => Cards.Nations.First(n => n.Code == code);

    static string[] S(string s) => s.Split(',');

    public static readonly Country[] Countries =
    {
        new()
        {
            Code = "POR", Name = "Portugal", Nation = Nat("POR"), Latin = true,
            TownA = S("Alva,Bar,Cas,Estre,Fa,Gui,Lou,Mira,Ola,Pe,Sal,Tor,Val,Bra,Ama,Ar,Ovi,Seix"),
            TownB = S("nde,ra,telo,moz,ro,res,ira,dela,nhos,vora,gal,rém,cos,lena,mar"),
            Clubs = S("Sporting de {T},Vitória de {T},União de {T},Académico de {T},{T} FC,Atlético {T},Desportivo de {T},Os {T}"),
            Grounds = S("Estádio do {T},Estádio Municipal de {T},Campo {S},Estádio {S}"),
        },
        new()
        {
            Code = "ESP", Name = "Spain", Nation = Nat("ESP"), Latin = true,
            TownA = S("Alca,Bena,Cala,Cór,Ga,Lor,Mo,Puer,Sego,Tala,Torre,Val,Villa,Ron,Hues,Mur,Al,Can"),
            TownB = S("lá,via,dena,ca,doba,nares,lejo,ra,tos,vera,sa,cia,mero,llo,tano"),
            Clubs = S("Real {T},CD {T},UD {T},Atlético {T},Racing {T},SD {T},{T} CF,Deportivo {T}"),
            Grounds = S("Estadio {T},Campo de {S},Nuevo {T},Estadio {S}"),
        },
        new()
        {
            Code = "FRA", Name = "France", Nation = Nat("FRA"),
            TownA = S("Beau,Belle,Char,Mont,Ro,Ville,Cler,Ange,Vau,Bour,Gre,Dun,Mar,Ver,Tour,Lan,Mor"),
            TownB = S("ville,mont,fort,bourg,lieu,vers,court,ac,nay,ais,oux,ens,mer,gne"),
            Clubs = S("Olympique {T},AS {T},Stade {T},FC {T},Racing {T},US {T},{T} AC,SC {T}"),
            Grounds = S("Stade {S},Parc de {T},Stade Municipal de {T},Stade de la {T}"),
        },
        new()
        {
            Code = "ENG", Name = "England", Nation = Nat("ENG"),
            TownA = S("Ash,Black,Brook,Castle,Cole,Dun,East,Elm,Fair,Glen,Green,Hart,King,Marl,Mill,North,Oak,Red,Stone,Thorn,West,Wolver,Bram,Crow,Iron,Silver"),
            TownB = S("ford,ton,bury,field,wick,mouth,dale,ham,port,stead,bridge,worth,gate,moor,ley,chester,pool"),
            Clubs = S("{T} United,{T} City,{T} Town,{T} Rovers,{T} Athletic,{T} Albion,{T} Wanderers,{T} FC"),
            Grounds = S("{T} Road,{S} Park,The {T} Ground,{S} Lane,Victoria Park,The Old {T} Field"),
        },
        new()
        {
            Code = "GER", Name = "Germany", Nation = Nat("GER"),
            TownA = S("Alten,Bern,Dorn,Eich,Falken,Hohen,Kirch,Lich,Neu,Ober,Rosen,Stein,Wald,Wolfs,Lin,Schön,Elm,Gro"),
            TownB = S("burg,dorf,hausen,bach,heim,stadt,berg,feld,au,ingen,rode,hagen"),
            Clubs = S("SV {T},Borussia {T},Eintracht {T},FC {T},VfB {T},TSV {T},SpVgg {T},Fortuna {T},Union {T}"),
            Grounds = S("{T} Stadion,Sportpark {T},Waldstadion,Stadion an der {S}"),
        },
        new()
        {
            Code = "ITA", Name = "Italy", Nation = Nat("ITA"), Latin = true,
            TownA = S("Ales,Ber,Cam,Cata,Fer,Mon,Pa,Ra,Sas,Tre,Vi,Pog,Cor,Lu,Ma,Sa,Bo"),
            TownB = S("sano,gamo,nia,rara,tova,via,venna,sari,viso,cenza,tona,ella,ano,lerno,rino"),
            Clubs = S("AC {T},{T} Calcio,US {T},Sporting {T},Virtus {T},Pro {T},Real {T},SS {T},Atletico {T}"),
            Grounds = S("Stadio {S},Stadio Comunale {T},Stadio {T},Campo {S}"),
        },
        new()
        {
            Code = "NED", Name = "Netherlands", Nation = Nat("NED"),
            TownA = S("Alk,Del,Ede,Gro,Har,Heer,Hoog,Ka,Lee,Nij,Vol,Zand,Zwol,Wa,Ter,Dor,Ven"),
            TownB = S("maar,veen,ningen,lingen,venen,dam,wijk,huizen,horst,sum,drecht,lo"),
            Clubs = S("{T} Boys,VV {T},Sparta {T},SC {T},FC {T},Excelsior {T},Quick {T},{T} Zwaluwen"),
            Grounds = S("Sportpark {T},Stadion {T},Sportpark {S},De {T}"),
        },
        new()
        {
            Code = "POL", Name = "Poland",
            Nation = Own("POL", 0xffffff, 0xdc143c, 0xdc143c, false,
                "Jakub,Kamil,Piotr,Tomasz,Marcin,Krzysztof,Mateusz,Bartosz,Michal,Pawel,Szymon,Adam",
                "Nowak,Kowalski,Wisniewski,Zielinski,Lewandowski,Kaminski,Wójcik,Szymanski,Dabrowski,Krawczyk,Mazur,Grabowski"),
            TownA = S("Bia,Byd,Glo,Kra,Lu,Pozn,Ra,Sie,Tor,Wro,Zab,Ko,Ole,Gor,Ple,Prze"),
            TownB = S("goszcz,gow,kow,blin,dom,dlce,szalin,sztyn,ce,nica,zow,owo,mysl"),
            Clubs = S("Polonia {T},Lech {T},Stal {T},Górnik {T},Wisla {T},Ruch {T},KS {T},Odra {T},Warta {T}"),
            Grounds = S("Stadion {T},Stadion Miejski {T},Stadion {S}"),
        },
        new()
        {
            Code = "SWE", Name = "Sweden",
            Nation = Own("SWE", 0x006aa7, 0xfecc00, 0x006aa7, false,
                "Erik,Johan,Anders,Lars,Oskar,Viktor,Emil,Gustav,Filip,Albin,Linus,Axel",
                "Andersson,Johansson,Karlsson,Nilsson,Eriksson,Larsson,Olsson,Persson,Svensson,Lindqvist,Berg,Holm"),
            TownA = S("Ble,Fal,Gäv,Hal,Hel,Jön,Kal,Lin,Mal,Nor,Sunds,Upp,Väs,Ör,Ås,Kris,Var"),
            TownB = S("kinge,köping,sborg,stad,mar,vall,berg,by,sund,holm,ham,å"),
            Clubs = S("IFK {T},{T} BK,{T} FF,{T} IF,IK {T},GIF {T},{T} AIS"),
            Grounds = S("{T} IP,Idrottsparken {T},{T} Arena,{S} Vallen"),
        },
        new()
        {
            Code = "CRO", Name = "Croatia", Nation = Nat("CRO"), Latin = true,
            TownA = S("Dar,Ko,Kar,Os,Pu,Ri,Si,Vara,Zad,Velo,Bra,Mak,Po,Kri,Lo,Vin,Sa"),
            TownB = S("ruvar,ijek,ovac,ka,jek,rsko,din,zdin,ce,ar,ina,ivo,mobor"),
            Clubs = S("NK {T},HNK {T},Hajduk {T},Dinamo {T},Slaven {T},Rudar {T},Croatia {T},Uskok {T}"),
            Grounds = S("Gradski stadion {T},Stadion {S},Stadion {T}"),
        },
    };

    static readonly string[] All = Countries.Select(c => c.Code).ToArray();

    public static readonly LeagueDef[] Leagues =
    {
        new() { Id = "por", Tier = 1, Name = "Liga dos Bairros", Lon = -9.14f, Lat = 38.72f, Countries = new[] { "POR" }, Blurb = "Neighbourhood sides from the hills of the capital to the fishing towns of the coast." },
        new() { Id = "esp", Tier = 1, Name = "Liga de Barrio", Lon = -3.7f, Lat = 40.42f, Countries = new[] { "ESP" }, Blurb = "Dusty pitches, loud barrios and plenty of local pride." },
        new() { Id = "fra", Tier = 1, Name = "Ligue des Quartiers", Lon = 2.35f, Lat = 48.86f, Countries = new[] { "FRA" }, Blurb = "The quarters of the big city and the market towns around it." },
        new() { Id = "eng", Tier = 1, Name = "Sunday Combination", Lon = -1.9f, Lat = 52.48f, Countries = new[] { "ENG" }, Blurb = "Muddy Sundays, pub teams with ambition and a tea hut at every ground." },
        new() { Id = "ger", Tier = 1, Name = "Freizeitliga", Lon = 6.96f, Lat = 50.94f, Countries = new[] { "GER" }, Blurb = "Village clubs and works teams, every one with a sausage stand." },
        new() { Id = "ita", Tier = 1, Name = "Lega dei Borghi", Lon = 9.19f, Lat = 45.46f, Countries = new[] { "ITA" }, Blurb = "Old towns, older rivalries, piazzas emptying for the derby." },
        new() { Id = "ned", Tier = 1, Name = "Polder Competitie", Lon = 4.9f, Lat = 52.37f, Countries = new[] { "NED" }, Blurb = "Flat pitches, crosswinds and clubs that love to pass." },
        new() { Id = "pol", Tier = 1, Name = "Liga Podwórkowa", Lon = 21.0f, Lat = 52.23f, Countries = new[] { "POL" }, Blurb = "Courtyard football grown up: hard tackles and big hearts." },
        new() { Id = "swe", Tier = 1, Name = "Korpsligan", Lon = 12.4f, Lat = 57.5f, Countries = new[] { "SWE" }, Blurb = "Long summer evenings on the west coast, plastic pitches by the sea." },
        new() { Id = "cro", Tier = 1, Name = "Kvartovska Liga", Lon = 15.98f, Lat = 45.81f, Countries = new[] { "CRO" }, Blurb = "Neighbourhood clubs with a fierce eye for talent." },

        new() { Id = "atl", Tier = 2, Need = 1, Name = "Atlantic Association", Lon = -7.8f, Lat = 42.3f, Countries = new[] { "POR", "ESP", "FRA" }, Blurb = "The best of the Atlantic coast: quick wingers and windy grounds." },
        new() { Id = "nsa", Tier = 2, Need = 1, Name = "North Sea Alliance", Lon = 4.4f, Lat = 50.85f, Countries = new[] { "ENG", "NED", "GER" }, Blurb = "Rain, floodlights and big northern crowds." },
        new() { Id = "med", Tier = 2, Need = 1, Name = "Mediterranean League", Lon = 12.5f, Lat = 41.9f, Countries = new[] { "ITA", "CRO", "ESP" }, Blurb = "Sunshine football: patient, clever and very hard to beat." },
        new() { Id = "bal", Tier = 2, Need = 1, Name = "Baltic Circle", Lon = 13.4f, Lat = 52.52f, Countries = new[] { "SWE", "POL", "GER" }, Blurb = "Strong, organised sides from around the Baltic." },

        new() { Id = "gwp", Tier = 3, Need = 3, Name = "Grand Western Premier", Lon = 1.6f, Lat = 46.6f, Countries = new[] { "POR", "ESP", "FRA", "ENG", "NED" }, Blurb = "Famous clubs, full stadiums and players worth fortunes." },
        new() { Id = "dan", Tier = 3, Need = 3, Name = "Danube Premier", Lon = 16.37f, Lat = 48.21f, Countries = new[] { "GER", "ITA", "POL", "SWE", "CRO" }, Blurb = "Rich clubs from the heart of the continent, built to win." },

        new() { Id = "gold", Tier = 4, Need = 6, Name = "The Golden League", Lon = 8.54f, Lat = 47.37f, Countries = All, Blurb = "The summit. Sixteen superclubs, every team full of ballers." },

        // The lower divisions: zoom in to find them.
        new() { Id = "por2", Tier = 1, Lower = true, Name = "Liga das Aldeias", Lon = -8.61f, Lat = 41.15f, Countries = new[] { "POR" }, Blurb = "Village sides up north: dirt pitches, family crowds and a grill behind the goal." },
        new() { Id = "esp2", Tier = 1, Lower = true, Name = "Liga de los Pueblos", Lon = -5.98f, Lat = 37.39f, Countries = new[] { "ESP" }, Blurb = "Andalusian villages in the heat. Kick-off waits for the shade." },
        new() { Id = "fra2", Tier = 1, Lower = true, Name = "Ligue des Villages", Lon = 4.84f, Lat = 45.76f, Countries = new[] { "FRA" }, Blurb = "Valley clubs on sloping pitches, a bakery van at every match." },
        new() { Id = "eng2", Tier = 1, Lower = true, Name = "Parks League", Lon = -2.24f, Lat = 53.48f, Countries = new[] { "ENG" }, Blurb = "Council pitches, jumpers in the rain and goalposts carried from the van." },
        new() { Id = "ger2", Tier = 1, Lower = true, Name = "Kreisklasse", Lon = 11.58f, Lat = 48.14f, Countries = new[] { "GER" }, Blurb = "Bavarian villages with a beer tent bigger than the stand." },
        new() { Id = "ita2", Tier = 1, Lower = true, Name = "Lega dei Paesi", Lon = 14.27f, Lat = 40.85f, Countries = new[] { "ITA" }, Blurb = "Southern hill towns: the priest blesses the pitch, the whole town comes." },
        new() { Id = "ned2", Tier = 1, Lower = true, Name = "Dorpenklasse", Lon = 4.48f, Lat = 51.92f, Countries = new[] { "NED" }, Blurb = "Village clubs by the dykes, a canteen with hot chocolate." },
        new() { Id = "pol2", Tier = 1, Lower = true, Name = "Liga Wiejska", Lon = 19.94f, Lat = 50.06f, Countries = new[] { "POL" }, Blurb = "Farm-town clubs from the south, mountains on the horizon." },
        new() { Id = "swe2", Tier = 1, Lower = true, Name = "Bygdeligan", Lon = 18.07f, Lat = 59.33f, Countries = new[] { "SWE" }, Blurb = "Lakeside pitches by red wooden houses, games till ten at night." },
        new() { Id = "cro2", Tier = 1, Lower = true, Name = "Seoska Liga", Lon = 16.44f, Lat = 43.51f, Countries = new[] { "CRO" }, Blurb = "Island and coast villages: a ferry to every away game." },
    };

    /// <summary>The cups: one for every home country, open to all, and a few special tournaments
    /// that invite clubs from everywhere once you've won enough.</summary>
    public static readonly CupDef[] Cups =
    {
        new() { Id = "porcup", Name = "Taça das Vilas", Country = "POR", Lon = -8.43f, Lat = 40.2f, Blurb = "Portugal's people's cup: village clubs dreaming of a giant-killing." },
        new() { Id = "espcup", Name = "Copa de los Barrios", Country = "ESP", Lon = -0.38f, Lat = 39.47f, Blurb = "Sixteen Spanish clubs, one night each round, the final by the sea." },
        new() { Id = "fracup", Name = "Coupe des Quartiers", Country = "FRA", Lon = -0.58f, Lat = 44.84f, Blurb = "France's open cup: anyone can beat anyone over ninety minutes." },
        new() { Id = "engcup", Name = "The Sunday Shield", Country = "ENG", Lon = -0.13f, Lat = 51.5f, Blurb = "The oldest knockout in the country. Muddy ties, magic nights." },
        new() { Id = "gercup", Name = "Dorfpokal", Country = "GER", Lon = 9.99f, Lat = 53.55f, Blurb = "Germany's cup: works teams and village clubs, all the way to the final." },
        new() { Id = "itacup", Name = "Coppa dei Campanili", Country = "ITA", Lon = 11.25f, Lat = 43.77f, Blurb = "The bell-tower cup: town against town, a trophy older than most clubs." },
        new() { Id = "nedcup", Name = "Polderbeker", Country = "NED", Lon = 6.57f, Lat = 53.22f, Blurb = "A Dutch cup in the wind: short passes, long celebrations." },
        new() { Id = "polcup", Name = "Puchar Podwórek", Country = "POL", Lon = 18.65f, Lat = 54.35f, Blurb = "Poland's courtyard cup, decided on the Baltic coast." },
        new() { Id = "swecup", Name = "Folkcupen", Country = "SWE", Lon = 13.0f, Lat = 55.6f, Blurb = "Sweden's people's cup, played through the light summer nights." },
        new() { Id = "crocup", Name = "Kup Kvartova", Country = "CRO", Lon = 14.44f, Lat = 45.33f, Blurb = "Croatia's cup: the harbour towns against the capital's quarters." },

        new() { Id = "algarve", Name = "Algarve Winter Cup", Lon = -7.93f, Lat = 37.02f, Size = 8, Level = 57, Prize = 14000, Blurb = "Winter sun on the south coast: eight invited clubs, a week of football by the beach." },
        new() { Id = "riviera", Name = "Riviera Summer Trophy", Lon = 7.26f, Lat = 43.7f, Size = 8, Need = 1, Level = 65, Prize = 30000, Blurb = "Pre-season on the Riviera: yachts in the harbour, champions on the pitch." },
        new() { Id = "midnight", Name = "Midnight Sun Cup", Lon = 10.75f, Lat = 59.91f, Size = 8, Need = 2, Level = 70, Prize = 45000, Blurb = "Kick-off at midnight in the far north, and the sun never sets." },
        new() { Id = "champions", Name = "Champions Invitational", Lon = 19.04f, Lat = 47.5f, Need = 4, Level = 79, Prize = 120000, Blurb = "Sixteen title winners from across Europe, by invitation only. The night of the year." },
    };

    public static CupDef CupById(string id) => Cups.FirstOrDefault(c => c.Id == id);

    public static LeagueDef ById(string id) => Leagues.FirstOrDefault(l => l.Id == id);

    public static Country CountryOf(string code) => Countries.FirstOrDefault(c => c.Code == code) ?? Countries[3];

    public static LeagueDef EntryOf(string code) => Leagues.FirstOrDefault(l => l.Tier == 1 && !l.Lower && l.Countries[0] == code) ?? Leagues[3];

    public static CareerSave Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var c = JsonSerializer.Deserialize<CareerSave>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true });
            return c != null && c.V == 1 && !string.IsNullOrEmpty(c.Country) ? c : null;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string path, CareerSave c)
    {
        if (c == null) return;
        try
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(c, new JsonSerializerOptions { IncludeFields = true }));
            File.Move(tmp, path, true);
        }
        catch
        {
            /* keep playing in memory */
        }
    }
}

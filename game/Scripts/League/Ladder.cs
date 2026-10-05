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

    public double Level => Ladder.Levels[Tier - 1];
    public double PrizeScale => Ladder.PrizeScale[Tier - 1];
    public string TierName => Ladder.TierNames[Tier - 1];
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
    };

    public static LeagueDef ById(string id) => Leagues.FirstOrDefault(l => l.Id == id);

    public static Country CountryOf(string code) => Countries.FirstOrDefault(c => c.Code == code) ?? Countries[3];

    public static LeagueDef EntryOf(string code) => Leagues.FirstOrDefault(l => l.Tier == 1 && l.Countries[0] == code) ?? Leagues[3];

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

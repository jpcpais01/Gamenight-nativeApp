using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace GameNight.League;

/// <summary>
/// Europe as a little pixel-art relief model, seen from the south at a tilt: hand-drawn coasts,
/// mountain ranges as ridges of height, hillshade from the north-west, snow on the peaks, shallows
/// and deep water. It zooms: whatever the camera looks at is baked fresh at the same pixel density
/// (two UI pixels per art pixel), so zooming in brings out more detail (ragged coasts, woods,
/// smaller hills) instead of bigger pixels. A bake covers the view and a margin around it, so
/// panning just slides it; a new one is made off the main thread when the view moves beyond it or
/// the zoom changes, and until it lands the old one is drawn stretched. No shaders: one texture
/// drawn flat, cheap on any phone.
/// </summary>
public static class EuropeMap
{
    public const int Scale = 2;
    /// <summary>The panel on the right of the map screen, in UI pixels: Europe is fitted to the left of it.</summary>
    public const float PanelW = 272;
    public const float MaxZoom = 5;
    const float Lon0 = -11.5f, Lat0 = 35.8f, Lat1 = 61.5f, LonSpan = 42f;
    const float CosLat = 0.66f, Tilt = 0.8f, Lift = 15f;
    /// <summary>How far the camera may roam, and the latitude of world row 0.</summary>
    const float BLon0 = -12.5f, BLon1 = 31.5f, BLat0 = 34.6f, BLat1 = 62.5f, LatTop = 66f;

    /// <summary>A camera: the point at the middle of the map area and the zoom (1 = all of Europe).</summary>
    public struct View
    {
        public float Lon, Lat, Zoom;

        public View(float lon, float lat, float zoom)
        {
            Lon = lon;
            Lat = lat;
            Zoom = zoom;
        }
    }

    /// <summary>One bake: a rectangle of world pixels at one scale, with the ground's heights.</summary>
    sealed class Tile
    {
        public float S, Z;
        public int X0, Y0, W, H;
        /// <summary>Ground rows J0.. under columns X0..: heights and land, for pins and glints.</summary>
        public int J0, Rows;
        public float[] Hgt;
        public bool[] Land, SeaPx;
        public byte[] Rgb;
        public ImageTexture Tex;

        public ImageTexture Texture() => Tex ??= ImageTexture.CreateFromImage(Image.CreateFromData(W, H, false, Image.Format.Rgb8, Rgb));
    }

    static Vector2I _art;
    static float _fit;
    static Vector2 _centre;
    static Tile _home, _tile;
    static Task<Tile> _pending;
    static View _now;

    // ---------------------------------------------------------------- coasts (longitude, latitude)

    // ---------------------------------------------------------------- coasts (longitude, latitude)

    static readonly float[][] Lands =
    {
        // The mainland: Gibraltar, up the Atlantic, round the Baltic, off the top and the east edge,
        // back along Anatolia, Greece, the Adriatic, Italy and the Mediterranean shore.
        new[]
        {
            -5.6f, 36.0f, -6.3f, 36.5f, -7.0f, 37.2f, -7.9f, 37.0f, -8.9f, 37.0f, -8.8f, 38.0f, -9.2f, 38.4f, -9.5f, 38.75f,
            -9.0f, 39.5f, -8.8f, 40.2f, -8.7f, 41.2f, -8.9f, 42.1f, -9.3f, 42.9f, -8.3f, 43.6f, -7.0f, 43.6f, -5.8f, 43.65f,
            -3.8f, 43.45f, -1.8f, 43.4f, -1.4f, 44.6f, -1.2f, 46.0f, -2.1f, 47.1f, -3.0f, 47.5f, -4.4f, 47.8f, -4.8f, 48.4f,
            -3.5f, 48.8f, -2.0f, 48.65f, -1.6f, 48.8f, -1.6f, 49.65f, -1.2f, 49.4f, 0.2f, 49.45f, 1.5f, 50.1f, 1.6f, 50.9f,
            2.4f, 51.05f, 3.4f, 51.4f, 4.0f, 51.9f, 4.6f, 52.5f, 4.8f, 53.0f, 5.8f, 53.4f, 7.0f, 53.4f, 8.0f, 53.6f,
            8.7f, 53.9f, 8.6f, 54.5f, 8.6f, 55.4f, 8.1f, 56.6f, 8.6f, 57.1f, 10.0f, 57.6f, 10.6f, 57.75f, 10.4f, 57.2f,
            10.2f, 56.6f, 10.9f, 56.4f, 10.2f, 55.9f, 9.6f, 55.5f, 9.9f, 54.85f, 10.9f, 54.4f, 11.2f, 54.0f, 12.2f, 54.2f,
            13.0f, 54.45f, 13.8f, 54.1f, 14.2f, 53.9f, 15.6f, 54.2f, 16.9f, 54.6f, 18.0f, 54.8f, 18.7f, 54.4f, 19.6f, 54.45f,
            21.1f, 55.25f, 21.1f, 56.0f, 21.0f, 56.5f, 21.6f, 57.4f, 22.6f, 57.75f, 23.3f, 57.1f, 24.1f, 57.0f, 24.4f, 57.9f,
            23.6f, 58.4f, 23.5f, 59.0f, 24.7f, 59.45f, 26.5f, 59.55f, 28.0f, 59.5f, 29.9f, 59.95f, 29.0f, 60.2f, 27.0f, 60.5f,
            25.0f, 60.2f, 23.0f, 59.85f, 22.0f, 60.4f, 21.4f, 61.2f, 21.5f, 62.0f, 21.6f, 63.0f, 21.5f, 64.5f, 45f, 64.5f,
            45f, 36.0f, 36.2f, 36.6f, 34.6f, 36.8f, 32.5f, 36.1f, 30.6f, 36.85f, 29.0f, 36.6f, 27.4f, 37.0f, 26.3f, 38.3f,
            26.7f, 39.3f, 26.2f, 39.9f, 26.2f, 40.05f, 26.1f, 40.6f, 25.9f, 40.85f, 24.4f, 40.9f, 23.7f, 40.7f, 22.9f, 40.6f,
            22.6f, 40.0f, 22.9f, 39.35f, 23.1f, 38.9f, 24.05f, 38.1f, 24.0f, 37.65f, 23.6f, 37.95f, 23.0f, 37.95f, 23.2f, 37.5f,
            22.75f, 37.1f, 23.15f, 36.45f, 22.8f, 36.7f, 22.5f, 36.4f, 22.1f, 36.9f, 21.7f, 36.8f, 21.6f, 37.6f, 21.4f, 38.0f,
            21.7f, 38.3f, 21.1f, 38.4f, 20.75f, 38.95f, 20.2f, 39.6f, 19.4f, 40.4f, 19.5f, 41.3f, 19.4f, 41.85f, 18.5f, 42.4f,
            17.5f, 43.0f, 16.4f, 43.5f, 15.2f, 44.1f, 14.5f, 45.0f, 14.4f, 45.35f, 13.9f, 44.8f, 13.6f, 45.5f, 13.75f, 45.65f,
            12.4f, 45.4f, 12.3f, 44.6f, 13.5f, 43.6f, 14.2f, 42.5f, 15.9f, 41.9f, 16.2f, 41.9f, 16.9f, 41.1f, 18.5f, 40.1f,
            18.3f, 39.8f, 17.1f, 40.5f, 16.6f, 39.9f, 17.1f, 39.0f, 16.5f, 38.4f, 15.65f, 38.0f, 15.8f, 38.8f, 15.6f, 40.0f,
            14.8f, 40.6f, 14.0f, 40.8f, 13.0f, 41.25f, 12.2f, 41.75f, 11.1f, 42.4f, 10.5f, 43.0f, 10.3f, 43.55f, 9.8f, 44.05f,
            8.9f, 44.4f, 8.2f, 43.9f, 7.3f, 43.7f, 6.6f, 43.2f, 5.4f, 43.3f, 4.6f, 43.4f, 3.9f, 43.5f, 3.1f, 43.1f,
            3.15f, 42.45f, 3.2f, 41.9f, 2.2f, 41.4f, 0.9f, 41.0f, 0.2f, 40.1f, -0.3f, 39.5f, 0.2f, 38.75f, -0.5f, 38.3f,
            -0.8f, 37.6f, -1.6f, 37.0f, -2.1f, 36.75f, -3.5f, 36.7f, -4.4f, 36.7f, -5.35f, 36.15f,
        },
        // Great Britain.
        new[]
        {
            -5.7f, 50.05f, -4.2f, 50.35f, -3.4f, 50.6f, -1.8f, 50.7f, 0.3f, 50.75f, 1.4f, 51.15f, 1.4f, 51.4f, 0.7f, 51.5f,
            1.7f, 52.5f, 1.75f, 52.6f, 1.3f, 52.95f, 0.3f, 52.9f, 0.1f, 53.5f, -0.1f, 54.1f, -1.2f, 54.6f, -1.6f, 55.6f,
            -2.0f, 55.8f, -2.6f, 56.05f, -2.5f, 56.6f, -2.1f, 57.15f, -1.8f, 57.5f, -3.0f, 57.7f, -4.0f, 57.6f, -3.1f, 58.6f,
            -5.0f, 58.6f, -5.4f, 58.0f, -5.8f, 57.3f, -5.6f, 56.5f, -5.7f, 55.3f, -4.9f, 55.7f, -4.9f, 54.65f, -3.6f, 54.9f,
            -3.4f, 54.6f, -3.0f, 54.0f, -3.0f, 53.4f, -4.6f, 53.3f, -4.1f, 52.9f, -4.4f, 52.2f, -5.3f, 51.8f, -4.0f, 51.6f,
            -3.0f, 51.5f, -3.2f, 51.2f, -4.2f, 51.2f, -5.0f, 50.6f,
        },
        // Ireland.
        new[]
        {
            -6.35f, 52.2f, -6.0f, 53.3f, -6.1f, 54.0f, -5.5f, 54.4f, -5.9f, 55.0f, -7.3f, 55.35f, -8.5f, 55.0f, -8.7f, 54.3f,
            -10.0f, 54.2f, -9.9f, 53.4f, -9.0f, 53.2f, -9.9f, 52.2f, -10.4f, 51.9f, -9.8f, 51.5f, -8.3f, 51.75f, -7.0f, 52.1f,
        },
        // Norway and Sweden, off the top.
        new[]
        {
            10.6f, 59.9f, 10.2f, 59.0f, 9.0f, 58.6f, 8.0f, 58.05f, 7.05f, 57.98f, 5.6f, 58.7f, 5.2f, 59.4f, 5.0f, 60.4f,
            4.9f, 61.0f, 5.0f, 62.0f, 6.0f, 62.6f, 7.5f, 63.2f, 8.5f, 64.5f, 20.3f, 64.5f, 20.3f, 63.8f, 17.4f, 62.4f,
            17.1f, 61.0f, 17.4f, 60.6f, 18.6f, 60.2f, 18.9f, 59.6f, 18.1f, 59.3f, 17.0f, 58.7f, 16.5f, 57.9f, 16.4f, 56.6f,
            15.6f, 56.15f, 14.2f, 55.4f, 12.95f, 55.6f, 12.7f, 56.1f, 12.9f, 56.65f, 11.9f, 57.7f, 11.2f, 58.4f, 11.1f, 59.0f,
            10.8f, 59.2f,
        },
        new[] { 12.1f, 55.95f, 12.6f, 56.05f, 12.6f, 55.65f, 12.2f, 55.2f, 11.8f, 55.0f, 11.2f, 55.2f, 10.95f, 55.7f, 11.1f, 55.9f }, // Zealand
        new[] { 10.0f, 55.5f, 10.6f, 55.6f, 10.8f, 55.3f, 10.6f, 55.0f, 10.0f, 55.1f, 9.8f, 55.4f }, // Funen
        new[] { 18.1f, 57.0f, 18.7f, 57.25f, 19.2f, 57.9f, 18.8f, 57.95f, 18.2f, 57.5f }, // Gotland
        new[] { 9.45f, 43.0f, 9.55f, 42.3f, 9.2f, 41.35f, 8.75f, 41.6f, 8.55f, 42.3f, 9.0f, 42.75f }, // Corsica
        new[] { 9.15f, 41.25f, 9.8f, 40.9f, 9.65f, 39.6f, 9.0f, 39.1f, 8.4f, 39.0f, 8.4f, 39.9f, 8.2f, 40.6f, 8.6f, 40.95f }, // Sardinia
        new[] { 12.4f, 37.8f, 13.3f, 38.2f, 14.2f, 38.0f, 15.6f, 38.25f, 15.1f, 37.5f, 15.1f, 36.7f, 14.4f, 36.9f, 13.0f, 37.5f }, // Sicily
        new[] { 2.3f, 39.55f, 2.9f, 39.9f, 3.45f, 39.7f, 3.2f, 39.3f, 2.7f, 39.45f }, // Mallorca
        new[] { 23.5f, 35.3f, 24.5f, 35.45f, 25.0f, 35.35f, 26.3f, 35.25f, 26.1f, 35.0f, 24.7f, 34.95f, 23.6f, 35.2f }, // Crete
        // North Africa, off the bottom.
        new[]
        {
            -20f, 30f, -20f, 33.5f, -6.8f, 34.0f, -5.9f, 35.8f, -5.3f, 35.9f, -3.0f, 35.3f, -1.0f, 35.3f, 1.0f, 36.4f,
            3.0f, 36.8f, 5.0f, 36.7f, 7.5f, 37.0f, 9.9f, 37.3f, 10.3f, 36.9f, 11.0f, 37.05f, 10.6f, 36.3f, 11.1f, 35.2f,
            10.5f, 34.0f, 12f, 30f,
        },
    };

    /// <summary>Water inside the mainland outline: the Black Sea.</summary>
    static readonly float[][] Seas =
    {
        new[]
        {
            29.0f, 41.2f, 28.0f, 42.0f, 27.5f, 42.5f, 27.9f, 43.2f, 28.6f, 44.2f, 29.7f, 45.0f, 30.7f, 46.5f, 31.8f, 46.6f,
            32.6f, 46.0f, 32.5f, 45.4f, 33.6f, 44.5f, 34.5f, 44.6f, 35.5f, 45.0f, 36.5f, 45.3f, 38.0f, 44.5f, 39.7f, 43.6f,
            41.6f, 41.6f, 40.0f, 41.0f, 38.0f, 40.9f, 35.2f, 42.0f, 33.0f, 41.9f, 31.5f, 41.3f,
        },
    };

    /// <summary>Ranges: a polyline, its height and its half-width in degrees.</summary>
    static readonly (float h, float w, float[] line)[] Ridges =
    {
        (1.0f, 0.75f, new[] { 5.8f, 44.4f, 6.9f, 45.9f, 8.5f, 46.35f, 10.5f, 46.5f, 12.5f, 46.75f, 14.3f, 46.95f, 15.8f, 47.5f }), // Alps
        (0.75f, 0.45f, new[] { -1.6f, 43.0f, 1.0f, 42.7f, 2.7f, 42.45f }), // Pyrenees
        (0.45f, 0.45f, new[] { -7.2f, 42.9f, -4.0f, 43.05f, -2.5f, 43.0f }), // Cantabrians
        (0.35f, 0.7f, new[] { -7.3f, 40.3f, -3.4f, 40.8f }), // Central system
        (0.35f, 0.6f, new[] { -2.9f, 41.6f, -0.8f, 40.4f }), // Iberian system
        (0.6f, 0.45f, new[] { -4.6f, 37.1f, -2.4f, 37.15f }), // Sierra Nevada
        (0.3f, 1.4f, new[] { -5.5f, 39.6f, -2.5f, 39.4f }), // Meseta
        (0.38f, 0.9f, new[] { 2.4f, 45.0f, 3.4f, 45.6f }), // Massif Central
        (0.25f, 0.45f, new[] { 6.0f, 47.6f, 7.0f, 48.3f }), // Vosges and Jura
        (0.25f, 0.45f, new[] { 8.1f, 47.9f, 8.4f, 48.7f }), // Black Forest
        (0.25f, 0.6f, new[] { 10.0f, 50.6f, 12.6f, 50.4f, 15.6f, 50.7f }), // German uplands
        (0.55f, 0.55f, new[] { 8.6f, 44.4f, 11.0f, 44.0f, 13.1f, 42.6f, 15.0f, 41.0f, 16.0f, 39.6f, 16.1f, 38.4f }), // Apennines
        (0.55f, 0.75f, new[] { 14.6f, 45.6f, 16.5f, 44.1f, 18.9f, 42.8f, 20.4f, 41.2f, 21.4f, 39.6f, 22.0f, 38.3f }), // Dinarics, Pindus
        (0.4f, 0.5f, new[] { 22.3f, 43.3f, 25.0f, 42.8f, 27.0f, 42.7f }), // Balkans
        (0.42f, 0.45f, new[] { 23.4f, 41.7f, 25.4f, 41.6f }), // Rhodopes
        (0.55f, 0.65f, new[] { 17.6f, 49.0f, 20.0f, 49.3f, 22.5f, 49.0f, 24.5f, 48.0f, 25.5f, 47.0f, 26.0f, 45.7f, 24.4f, 45.4f, 22.5f, 45.3f }), // Carpathians
        (0.72f, 0.9f, new[] { 6.6f, 59.0f, 7.8f, 60.8f, 9.6f, 62.0f, 12.5f, 63.6f }), // Scandinavian mountains
        (0.4f, 0.6f, new[] { -5.4f, 56.6f, -3.6f, 57.2f }), // Highlands
        (0.22f, 0.35f, new[] { -2.2f, 53.2f, -2.3f, 54.8f }), // Pennines
        (0.28f, 0.45f, new[] { -3.8f, 52.0f, -3.7f, 53.0f }), // Wales
        (0.45f, 0.3f, new[] { 8.9f, 41.7f, 9.1f, 42.6f }), // Corsica
        (0.3f, 0.35f, new[] { 9.1f, 39.4f, 9.3f, 40.6f }), // Sardinia
        (0.5f, 0.3f, new[] { 13.6f, 37.85f, 15.0f, 37.75f }), // Sicily, Etna
        (0.38f, 0.3f, new[] { 23.8f, 35.25f, 25.6f, 35.2f }), // Crete
        (0.55f, 0.7f, new[] { -6.0f, 34.6f, -1.0f, 34.6f, 4.0f, 35.8f, 8.5f, 36.2f }), // Atlas
        (0.45f, 2.0f, new[] { 30.0f, 38.8f, 40.0f, 39.2f }), // Anatolian plateau
        (0.5f, 0.5f, new[] { 29.5f, 37.2f, 34.0f, 37.0f, 36.5f, 37.6f }), // Taurus
        (0.3f, 0.5f, new[] { 24.5f, 64f, 30f, 62.5f }), // Finnish lakeland, gently
    };


    // ---------------------------------------------------------------- the camera

    static void Layout(Vector2 size)
    {
        var art = new Vector2I(Mathf.CeilToInt(size.X / Scale), Mathf.CeilToInt(size.Y / Scale));
        if (art == _art) return;
        _art = art;
        float mapW = art.X - PanelW / Scale - 8;
        _fit = Mathf.Min(mapW / (LonSpan * CosLat), (art.Y + 14) / ((Lat1 - Lat0) * Tilt));
        _centre = new Vector2(4 + mapW / 2, art.Y / 2f);
        _home = _tile = null;
        _now = Home;
    }

    static float Lifted(float z) => Lift * Mathf.Pow(z, 0.8f);
    static float WX(float lon, float s) => (lon - Lon0) * CosLat * s;
    static float WJ(float lat, float s) => (LatTop - lat) * s;

    /// <summary>All of Europe on a screen of this size.</summary>
    public static View HomeFor(Vector2 size)
    {
        Layout(size);
        return Home;
    }

    /// <summary>All of Europe, as the map first opens.</summary>
    public static View Home
    {
        get
        {
            float s = _fit;
            return Clamp(new View(Lon0 + LonSpan / 2, Lat0 + (_art.Y - 3 - _centre.Y) / (s * Tilt), 1));
        }
    }

    public static View Clamp(View v)
    {
        v.Zoom = Mathf.Clamp(v.Zoom, 1, MaxZoom);
        float s = _fit * v.Zoom;
        float hl = _centre.X / (s * CosLat), hb = _centre.Y / (s * Tilt);
        v.Lon = BLon1 - BLon0 <= hl * 2 ? (BLon0 + BLon1) / 2 : Mathf.Clamp(v.Lon, BLon0 + hl, BLon1 - hl);
        v.Lat = BLat1 - BLat0 <= hb * 2 ? (BLat0 + BLat1) / 2 : Mathf.Clamp(v.Lat, BLat0 + hb, BLat1 - hb);
        return v;
    }

    /// <summary>Where world pixel (0, 0) sits on screen, in whole art pixels so bakes stay crisp.</summary>
    static Vector2 Offset(View v)
    {
        float s = _fit * v.Zoom;
        return new Vector2(Mathf.Round(_centre.X - WX(v.Lon, s)), Mathf.Round(_centre.Y - WJ(v.Lat, s) * Tilt));
    }

    /// <summary>The ground under a screen point (sea level), as longitude and latitude.</summary>
    public static Vector2 Unproject(View v, Vector2 ui)
    {
        float s = _fit * v.Zoom;
        var a = ui / Scale - Offset(v);
        return new Vector2(Lon0 + a.X / (CosLat * s), LatTop - a.Y / (Tilt * s));
    }

    /// <summary>Zoom by `factor` keeping the ground under `ui` where it is.</summary>
    public static View ZoomAbout(View v, Vector2 ui, float factor)
    {
        var p = Unproject(v, ui);
        var n = v;
        n.Zoom = Mathf.Clamp(v.Zoom * factor, 1, MaxZoom);
        float s = _fit * n.Zoom;
        n.Lon = p.X - (ui.X / Scale - _centre.X) / (CosLat * s);
        n.Lat = p.Y + (ui.Y / Scale - _centre.Y) / (Tilt * s);
        return Clamp(n);
    }

    /// <summary>Slide the map by a drag of `d` UI pixels.</summary>
    public static View Pan(View v, Vector2 d)
    {
        float s = _fit * v.Zoom;
        v.Lon -= d.X / Scale / (CosLat * s);
        v.Lat += d.Y / Scale / (Tilt * s);
        return Clamp(v);
    }

    /// <summary>Fly to a place: centred, at least this close.</summary>
    public static View Focus(View v, float lon, float lat, float zoom) => Clamp(new View(lon, lat, Mathf.Max(v.Zoom, zoom)));

    static float HeightIn(Tile t, float lon, float lat)
    {
        if (t?.Hgt == null) return 0;
        int x = Mathf.FloorToInt(WX(lon, t.S)) - t.X0, j = Mathf.FloorToInt(WJ(lat, t.S)) - t.J0;
        if (x < 0 || j < 0 || x >= t.W || j >= t.Rows) return 0;
        int i = j * t.W + x;
        return t.Land[i] ? t.Hgt[i] : 0;
    }

    static Vector2 ProjectIn(View v, Tile t, float lon, float lat)
    {
        float s = _fit * v.Zoom;
        var o = Offset(v);
        return new Vector2(o.X + WX(lon, s), o.Y + WJ(lat, s) * Tilt - HeightIn(t, lon, lat) * Lifted(v.Zoom)) * Scale;
    }

    /// <summary>Where a place sits on the map as last drawn, in UI pixels (on the ground, terrain height included).</summary>
    public static Vector2 Project(float lon, float lat) => ProjectIn(_now, _tile, lon, lat);

    /// <summary>Is this UI point over open water? (For the sea's glints.)</summary>
    public static bool Sea(Vector2 ui)
    {
        var t = _tile;
        if (t?.SeaPx == null) return false;
        float k = _fit * _now.Zoom / t.S;
        var a = (ui / Scale - Offset(_now)) / k - new Vector2(t.X0, t.Y0);
        int x = (int)a.X, y = (int)a.Y;
        return x >= 0 && y >= 0 && x < t.W && y < t.H && t.SeaPx[y * t.W + x];
    }

    // ---------------------------------------------------------------- the home screen's window

    static Tile HomeTile(Vector2 size)
    {
        Layout(size);
        if (_home != null) return _home;
        var o = Offset(Home);
        _home = Bake(_fit, 1, (int)-o.X, (int)-o.Y, _art.X, _art.Y);
        return _home;
    }

    /// <summary>All of Europe for a screen of this size, laid out as the map first opens.</summary>
    public static ImageTexture Texture(Vector2 size) => HomeTile(size).Texture();

    /// <summary>Where a place sits on Texture(), in UI pixels.</summary>
    public static Vector2 HomeProject(float lon, float lat) => ProjectIn(Home, _home, lon, lat);

    // ---------------------------------------------------------------- the map screen

    /// <summary>The world rectangle (pixels at scale s) the camera may ever show, the panel's strip included.</summary>
    static Rect2 Bounds(float s, float z)
    {
        float x0 = WX(BLon0, s), x1 = WX(BLon1, s) + PanelW / Scale + 4;
        float y0 = WJ(BLat1, s) * Tilt - 1.4f * Lifted(z), y1 = WJ(BLat0, s) * Tilt + 4;
        return new Rect2(x0, y0, x1 - x0, y1 - y0);
    }

    static Rect2 Visible(View v)
    {
        var o = Offset(v);
        float s = _fit * v.Zoom;
        return new Rect2(-o, _art).Intersection(Bounds(s, v.Zoom));
    }

    static bool Covers(Tile t, View v)
    {
        if (Mathf.Abs(t.S - _fit * v.Zoom) > 1e-4f) return false;
        var vis = Visible(v);
        return vis.Position.X >= t.X0 && vis.Position.Y >= t.Y0 && vis.End.X <= t.X0 + t.W && vis.End.Y <= t.Y0 + t.H;
    }

    /// <summary>The map for this view: a fresh bake if one is ready, else the last one slid and
    /// stretched into place. `settled`: the fingers are off, so a new zoom is worth baking.</summary>
    public static void Draw(CanvasItem ci, Vector2 size, View v, bool settled)
    {
        Layout(size);
        _now = v;
        ci.DrawRect(new Rect2(Vector2.Zero, size), Deep);
        if (_pending is { IsCompleted: true })
        {
            if (_pending.Status == TaskStatus.RanToCompletion) _tile = _pending.Result;
            _pending = null;
        }
        _tile ??= Covers(HomeTile(size), v) ? _home : Bake(Spec(v));
        float s = _fit * v.Zoom;
        bool sameScale = Mathf.Abs(_tile.S - s) < 1e-4f;
        if (_pending == null && !Covers(_tile, v) && (settled || sameScale))
        {
            var spec = Spec(v);
            _pending = Task.Run(() => Bake(spec));
        }
        float k = s / _tile.S;
        var o = Offset(v);
        var at = (o + new Vector2(_tile.X0, _tile.Y0) * k) * Scale;
        ci.DrawTextureRect(_tile.Texture(), new Rect2(sameScale ? at.Round() : at, new Vector2(_tile.W, _tile.H) * k * Scale), false);
    }

    /// <summary>What to bake for a view: what it shows and half a screen around, inside the bounds.</summary>
    static (float s, float z, int x0, int y0, int w, int h) Spec(View v)
    {
        var o = Offset(v);
        float s = _fit * v.Zoom;
        var r = new Rect2(-o - _art / 2, _art * 2).Intersection(Bounds(s, v.Zoom));
        int x0 = Mathf.FloorToInt(r.Position.X), y0 = Mathf.FloorToInt(r.Position.Y);
        return (s, v.Zoom, x0, y0, Mathf.CeilToInt(r.End.X) - x0, Mathf.CeilToInt(r.End.Y) - y0);
    }

    static Tile Bake((float s, float z, int x0, int y0, int w, int h) p) => Bake(p.s, p.z, p.x0, p.y0, p.w, p.h);

    // ---------------------------------------------------------------- baking

    /// <summary>Scanline fill of polygons into a grid whose cell (i, j) is world column gx0 + i,
    /// ground row gj0 + j at scale s.</summary>
    static void Fill(float[][] polys, bool[] mask, bool value, int gw, int gh, int gx0, int gj0, float s)
    {
        var xs = new List<float>();
        for (int j = 0; j < gh; j++)
        {
            float lat = LatTop - (gj0 + j + 0.5f) / s;
            foreach (var p in polys)
            {
                xs.Clear();
                int n = p.Length / 2;
                for (int i = 0, k = n - 1; i < n; k = i++)
                {
                    float ay = p[i * 2 + 1], by = p[k * 2 + 1];
                    if ((ay > lat) == (by > lat)) continue;
                    float lon = p[i * 2] + (lat - ay) / (by - ay) * (p[k * 2] - p[i * 2]);
                    xs.Add(WX(lon, s) - gx0);
                }
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    int a = Math.Max(0, (int)Mathf.Ceil(xs[i] - 0.5f)), b = Math.Min(gw - 1, (int)Mathf.Floor(xs[i + 1] - 0.5f));
                    for (int x = a; x <= b; x++) mask[j * gw + x] = value;
                }
            }
        }
    }

    /// <summary>Two-pass chamfer distance (in cells) to the nearest cell where `from` is true.</summary>
    static float[] Distance(bool[] from, int w, int h)
    {
        var d = new float[w * h];
        for (int i = 0; i < d.Length; i++) d[i] = from[i] ? 0 : 1e6f;
        const float A = 1, B = 1.414f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float v = d[i];
                if (x > 0) v = Math.Min(v, d[i - 1] + A);
                if (y > 0)
                {
                    v = Math.Min(v, d[i - w] + A);
                    if (x > 0) v = Math.Min(v, d[i - w - 1] + B);
                    if (x < w - 1) v = Math.Min(v, d[i - w + 1] + B);
                }
                d[i] = v;
            }
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x;
                float v = d[i];
                if (x < w - 1) v = Math.Min(v, d[i + 1] + A);
                if (y < h - 1)
                {
                    v = Math.Min(v, d[i + w] + A);
                    if (x < w - 1) v = Math.Min(v, d[i + w + 1] + B);
                    if (x > 0) v = Math.Min(v, d[i + w - 1] + B);
                }
                d[i] = v;
            }
        return d;
    }

    /// <summary>Each range's reach, in (u, lat), to skip the far ones fast.</summary>
    static readonly Rect2[] RidgeBox = Array.ConvertAll(Ridges, r =>
    {
        float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
        for (int k = 0; k + 1 < r.line.Length; k += 2)
        {
            x0 = Math.Min(x0, r.line[k] * CosLat);
            x1 = Math.Max(x1, r.line[k] * CosLat);
            y0 = Math.Min(y0, r.line[k + 1]);
            y1 = Math.Max(y1, r.line[k + 1]);
        }
        float m = r.w * 2.6f;
        return new Rect2(x0 - m, y0 - m, x1 - x0 + m * 2, y1 - y0 + m * 2);
    });

    static float Hash(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }

    static float Noise(float x, float y)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float a = Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), fx), b = Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), fx);
        return Mathf.Lerp(a, b, fy);
    }

    static float SegDist(float px, float py, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float t = Mathf.Clamp(((px - ax) * dx + (py - ay) * dy) / Math.Max(1e-6f, dx * dx + dy * dy), 0, 1);
        float ex = ax + dx * t - px, ey = ay + dy * t - py;
        return Mathf.Sqrt(ex * ex + ey * ey);
    }

    static Color C(int hex) => new(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);

    static readonly Color Deep = C(0x173f68), Mid = C(0x22598a), Shallow = C(0x2f74a2), Coastal = C(0x4a97bd), Foam = C(0xb8e2ea);
    static readonly Color Sand = C(0xd8c48c), DrySouth = C(0xa7a05a), Green = C(0x6c9947), North = C(0x4d7d3f), Boreal = C(0x3a6a40);
    static readonly Color Desert = C(0xc9a868), Hills = C(0x8c8150), HillsN = C(0x667846), Rock = C(0x857a70), High = C(0xa59b91), Snow = C(0xf1f4f7);
    static readonly Color Haze = C(0x9cc0d4);


    /// <summary>Bake world pixels [x0, x0 + w) x [y0, y0 + h) at scale s (zoom z). Distances are
    /// worked out over a margin around it, so coasts just off the edge still shade the sea.</summary>
    static Tile Bake(float s, float z, int x0, int y0, int w, int h)
    {
        float lift = Lifted(z);
        int j0 = Mathf.FloorToInt(y0 / Tilt) - 1, j1 = Mathf.CeilToInt((y0 + h + 1.4f * lift) / Tilt) + 1;
        int rows = j1 - j0;
        int m = Mathf.CeilToInt(18 * z) + 2;
        int gw = w + m * 2, gh = rows + m * 2, gx0 = x0 - m, gj0 = j0 - m;
        int n = gw * gh;
        float LonOf(int i) => Lon0 + (gx0 + i) / (s * CosLat);
        float LatOf(int j) => LatTop - (gj0 + j) / s;
        // Detail that only shows up close: ragged coasts, small hills, woods and fields.
        float near = Mathf.Clamp((z - 1) / 2, 0, 1), closer = Mathf.Clamp((z - 2) / 2, 0, 1);

        var land = new bool[n];
        Fill(Lands, land, true, gw, gh, gx0, gj0, s);
        Fill(Seas, land, false, gw, gh, gx0, gj0, s);
        var water = new bool[n];
        for (int i = 0; i < n; i++) water[i] = !land[i];
        var toWater = Distance(water, gw, gh);
        var toLand = Distance(land, gw, gh);
        if (near > 0)
        {
            // Coves and headlands: the coast pushed in and out by noise, within a few base pixels.
            for (int j = 0; j < gh; j++)
            {
                float lat = LatOf(j);
                for (int i = 0; i < gw; i++)
                {
                    int k = j * gw + i;
                    float sd = (toWater[k] - toLand[k]) / z;
                    if (Math.Abs(sd) > 3) continue;
                    float u = LonOf(i) * CosLat;
                    float wob = (Noise(u * 7 + 11, lat * 7) - 0.5f) * 2.2f * near + (Noise(u * 19 + 5, lat * 19) - 0.5f) * 1.2f * closer;
                    land[k] = sd + wob > 0;
                }
            }
            for (int i = 0; i < n; i++) water[i] = !land[i];
            toWater = Distance(water, gw, gh);
            toLand = Distance(land, gw, gh);
        }

        // Height, worked out under the bake (and a ring for the shading): rising from the coast,
        // ranges as ridges, noise for texture.
        var hgt = new float[n];
        for (int j = m - 1; j <= m + rows; j++)
        {
            float lat = LatOf(j);
            for (int i = m - 1; i <= m + w; i++)
            {
                int k = j * gw + i;
                if (!land[k]) continue;
                float u = LonOf(i) * CosLat;
                float tw = toWater[k] / z;
                float hh = 0.04f + 0.1f * Mathf.Clamp(tw / 18f, 0, 1), ranges = 0;
                for (int q = 0; q < Ridges.Length; q++)
                {
                    if (!RidgeBox[q].HasPoint(new Vector2(u, lat))) continue;
                    var (rh, rw, line) = Ridges[q];
                    float best = 1e6f;
                    for (int e = 0; e + 3 < line.Length; e += 2)
                        best = Math.Min(best, SegDist(u, lat, line[e] * CosLat, line[e + 1], line[e + 2] * CosLat, line[e + 3]));
                    if (best > rw * 2.6f) continue;
                    float g = Mathf.Exp(-(best * best) / (rw * rw));
                    // Ranges are ragged: noise breaks the crest into peaks.
                    hh += rh * g * (0.62f + 0.55f * Noise(u * 2.6f + 31, lat * 2.6f));
                    ranges += rh * g;
                }
                // Close up, a range breaks into peaks and valleys instead of one long snowfield.
                if (ranges > 0.02f) hh += ranges * ((Noise(u * 8 + 17, lat * 8) - 0.5f) * 0.9f * near + (Noise(u * 21 + 3, lat * 21) - 0.5f) * 0.3f * closer);
                hh += (Noise(u * 1.3f, lat * 1.3f) - 0.5f) * 0.08f + (Noise(u * 5f + 7, lat * 5f) - 0.5f) * 0.04f;
                hh += (Noise(u * 14 + 3, lat * 14) - 0.5f) * 0.035f * near + (Noise(u * 36 + 9, lat * 36) - 0.5f) * 0.018f * closer;
                hgt[k] = Mathf.Max(0.02f, hh) * Mathf.Clamp(tw / 2.2f, 0.35f, 1);
            }
        }

        // Colour each cell.
        var col = new Color[n];
        for (int j = m; j < m + rows; j++)
        {
            float lat = LatOf(j);
            for (int i = m; i < m + w; i++)
            {
                int k = j * gw + i;
                float u = LonOf(i) * CosLat;
                int cx = gx0 + i, cj = gj0 + j;
                if (!land[k])
                {
                    float d = toLand[k] / z;
                    Color wc = d < 2 ? Coastal : d < 6 ? Shallow : d < 16 ? Mid : Deep;
                    // A dithered seam between depths, so the bands read as pixel art.
                    bool odd = ((cx + cj) & 1) == 0;
                    if (d >= 1.5f && d < 2.5f && odd) wc = Shallow;
                    if (d >= 5.5f && d < 6.5f && odd) wc = Mid;
                    if (d >= 15 && d < 17 && odd) wc = Deep;
                    if (toLand[k] <= 1.01f) wc = Foam.Lerp(Coastal, 0.35f);
                    col[k] = wc;
                    continue;
                }
                float hh = hgt[k];
                float north = Mathf.Clamp((lat - 40f) / 18f, 0, 1);
                float n1 = Noise(u * 3.1f + 100, lat * 3.1f);
                Color low = lat < 37.5f ? Desert.Lerp(DrySouth, Mathf.Clamp((lat - 34.5f) / 3f, 0, 1))
                    : north < 0.45f ? DrySouth.Lerp(Green, north / 0.45f) : north < 0.8f ? Green.Lerp(North, (north - 0.45f) / 0.35f) : North.Lerp(Boreal, (north - 0.8f) / 0.2f);
                // Woods and fields: patches a shade darker or lighter, finer ones close up.
                if (n1 > 0.62f) low = low.Darkened(0.12f);
                else if (n1 < 0.3f) low = low.Lightened(0.06f);
                if (near > 0)
                {
                    float n2 = Noise(u * 13 + 50, lat * 13);
                    if (n2 > 1 - 0.3f * near) low = low.Darkened(0.1f);
                    else if (n2 < 0.18f * near) low = low.Lightened(0.07f);
                }
                float snowline = 0.8f - north * 0.2f;
                Color c = hh < 0.2f ? low : hh < 0.36f ? low.Lerp(north > 0.5f ? HillsN : Hills, (hh - 0.2f) / 0.16f) : hh < 0.55f ? Hills.Lerp(Rock, (hh - 0.36f) / 0.19f) : hh < snowline ? Rock.Lerp(High, (hh - 0.55f) / Math.Max(0.05f, snowline - 0.55f)) : Snow;
                if (toWater[k] / z <= 1.01f && toWater[k] <= Math.Max(1.01f, z * 0.6f) && hh < 0.25f && lat < 56) c = Sand;
                // Light from the north-west, stepped into bands (the slope per cell shrinks as cells do).
                float hl = land[k - gw - 1] ? hgt[k - gw - 1] : hh;
                float hr = land[k + gw + 1] ? hgt[k + gw + 1] : hh;
                float shade = Mathf.Clamp(1 + (hl - hr) * 9f * z, 0.62f, 1.3f);
                shade = Mathf.Round(shade * 8) / 8;
                col[k] = new Color(c.R * shade, c.G * shade, c.B * shade);
            }
        }

        // Voxel columns, nearest row first: each row draws only where it rises above what's in front.
        var t = new Tile { S = s, Z = z, X0 = x0, Y0 = y0, W = w, H = h, J0 = j0, Rows = rows };
        t.SeaPx = new bool[w * h];
        var data = new byte[w * h * 3];
        for (int i = 0; i < w * h; i++)
        {
            data[i * 3] = (byte)(Deep.R * 255);
            data[i * 3 + 1] = (byte)(Deep.G * 255);
            data[i * 3 + 2] = (byte)(Deep.B * 255);
            t.SeaPx[i] = true;
        }
        for (int x = 0; x < w; x++)
        {
            int ymin = h;
            for (int jj = rows - 1; jj >= 0; jj--)
            {
                int k = (jj + m) * gw + x + m;
                float hh = land[k] ? hgt[k] : 0;
                float ground = (j0 + jj) * Tilt;
                int y = Mathf.FloorToInt(ground - hh * lift) - y0;
                if (y >= ymin) continue;
                int top = Math.Max(0, y);
                // Far away the air thickens: a touch of haze toward the north.
                float haze = Mathf.Clamp((LatOf(jj + m) - 36f) / 24f, 0, 1) * 0.22f;
                var c = col[k].Lerp(Haze, haze);
                // A one-pixel step is a soft edge; a cliff goes dark.
                Color side1 = c.Darkened(0.14f), side = c.Darkened(0.32f);
                bool sea = !land[k] && toLand[k] / z > 2;
                for (int yy = top; yy < ymin && yy < h; yy++)
                {
                    var kc = yy == top ? c : yy == top + 1 ? side1 : side;
                    int p = (yy * w + x) * 3;
                    data[p] = (byte)Mathf.Clamp(kc.R * 255, 0, 255);
                    data[p + 1] = (byte)Mathf.Clamp(kc.G * 255, 0, 255);
                    data[p + 2] = (byte)Mathf.Clamp(kc.B * 255, 0, 255);
                    t.SeaPx[yy * w + x] = sea;
                }
                ymin = top;
                if (ymin <= 0) break;
            }
        }
        t.Rgb = data;
        // Keep the ground under the bake for pins.
        t.Hgt = new float[w * rows];
        t.Land = new bool[w * rows];
        for (int jj = 0; jj < rows; jj++)
            for (int x = 0; x < w; x++)
            {
                int k = (jj + m) * gw + x + m;
                t.Hgt[jj * w + x] = hgt[k];
                t.Land[jj * w + x] = land[k];
            }
        return t;
    }
}

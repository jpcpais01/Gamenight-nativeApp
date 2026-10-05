using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.League;

/// <summary>
/// Europe as a little pixel-art relief model, seen from the south at a tilt: hand-drawn coasts,
/// mountain ranges as ridges of height, hillshade from the north-west, snow on the peaks, shallows
/// and deep water. Baked once into an image at half the screen's resolution (two UI pixels per art
/// pixel) by a voxel-column pass, nearest to farthest, so ranges stand up and hide what's behind.
/// No shaders: one texture drawn flat, cheap on any phone.
/// </summary>
public static class EuropeMap
{
    public const int Scale = 2;
    /// <summary>The panel on the right of the map screen, in UI pixels: Europe is fitted to the left of it.</summary>
    public const float PanelW = 272;
    const float Lon0 = -11.5f, Lat0 = 35.8f, Lat1 = 61.5f, LonSpan = 42f;
    const float CosLat = 0.66f, Tilt = 0.8f, Lift = 15f;

    static ImageTexture _tex;
    static Vector2I _size;
    static float _s, _ox, _oy;
    static int _gw, _gh;
    static float[] _h;
    static bool[] _water;

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

    // ---------------------------------------------------------------- the projection

    static void Layout(Vector2 size)
    {
        int aw = Mathf.CeilToInt(size.X / Scale), ah = Mathf.CeilToInt(size.Y / Scale);
        float uSpan = LonSpan * CosLat, vSpan = Lat1 - Lat0;
        float mapW = aw - PanelW / Scale - 8;
        _s = Mathf.Min(mapW / uSpan, (ah + 14) / (vSpan * Tilt));
        _ox = 4 + (mapW - uSpan * _s) / 2;
        _oy = ah - 3 - vSpan * _s * Tilt;
        _gw = aw;
        _gh = Mathf.CeilToInt((ah - _oy) / Tilt) + 4;
        _size = new Vector2I(aw, ah);
    }

    static float LonAt(float x) => Lon0 + (x - _ox) / (_s * CosLat);
    static float LatAt(float j) => Lat1 - j / _s;

    /// <summary>Where a place sits on the baked map, in UI pixels (on the ground, terrain height included).</summary>
    public static Vector2 Project(float lon, float lat)
    {
        float x = _ox + (lon - Lon0) * CosLat * _s;
        float j = (Lat1 - lat) * _s;
        int ix = Mathf.Clamp((int)x, 0, _gw - 1), ij = Mathf.Clamp((int)j, 0, _gh - 1);
        float h = _h != null && !_water[ij * _gw + ix] ? _h[ij * _gw + ix] : 0;
        return new Vector2(x, _oy + j * Tilt - h * Lift) * Scale;
    }

    /// <summary>Is this UI point over open water? (For the sea's glints.)</summary>
    public static bool Sea(Vector2 ui)
    {
        if (_img == null) return false;
        int x = (int)(ui.X / Scale), y = (int)(ui.Y / Scale);
        return x >= 0 && y >= 0 && x < _size.X && y < _size.Y && _seaPx[y * _size.X + x];
    }

    static Image _img;
    static bool[] _seaPx;

    /// <summary>The map for a screen of this size, baked on first use.</summary>
    public static ImageTexture Texture(Vector2 size)
    {
        int aw = Mathf.CeilToInt(size.X / Scale), ah = Mathf.CeilToInt(size.Y / Scale);
        if (_tex != null && _size.X == aw && _size.Y == ah) return _tex;
        Layout(size);
        Bake();
        _tex = ImageTexture.CreateFromImage(_img);
        return _tex;
    }

    // ---------------------------------------------------------------- baking

    static void Fill(float[][] polys, bool[] mask, bool value)
    {
        var xs = new List<float>();
        for (int j = 0; j < _gh; j++)
        {
            float lat = LatAt(j + 0.5f);
            foreach (var p in polys)
            {
                xs.Clear();
                int n = p.Length / 2;
                for (int i = 0, k = n - 1; i < n; k = i++)
                {
                    float ay = p[i * 2 + 1], by = p[k * 2 + 1];
                    if ((ay > lat) == (by > lat)) continue;
                    float lon = p[i * 2] + (lat - ay) / (by - ay) * (p[k * 2] - p[i * 2]);
                    xs.Add(_ox + (lon - Lon0) * CosLat * _s);
                }
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    int a = Math.Max(0, (int)Mathf.Ceil(xs[i] - 0.5f)), b = Math.Min(_gw - 1, (int)Mathf.Floor(xs[i + 1] - 0.5f));
                    for (int x = a; x <= b; x++) mask[j * _gw + x] = value;
                }
            }
        }
    }

    /// <summary>Two-pass chamfer distance (in cells) to the nearest cell where `from` is true.</summary>
    static float[] Distance(bool[] from)
    {
        int w = _gw, h = _gh;
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

    static void Bake()
    {
        int n = _gw * _gh;
        var land = new bool[n];
        Fill(Lands, land, true);
        Fill(Seas, land, false);
        _water = new bool[n];
        for (int i = 0; i < n; i++) _water[i] = !land[i];
        var toWater = Distance(_water);
        var toLand = Distance(land);

        // Height: rising from the coast, ranges as ridges, a little noise for texture.
        _h = new float[n];
        for (int j = 0; j < _gh; j++)
        {
            float lat = LatAt(j);
            for (int x = 0; x < _gw; x++)
            {
                int i = j * _gw + x;
                if (!land[i]) continue;
                float lon = LonAt(x);
                float u = lon * CosLat;
                float h = 0.04f + 0.1f * Mathf.Clamp(toWater[i] / 18f, 0, 1);
                foreach (var (rh, rw, line) in Ridges)
                {
                    float best = 1e6f;
                    for (int k = 0; k + 3 < line.Length; k += 2)
                        best = Math.Min(best, SegDist(u, lat, line[k] * CosLat, line[k + 1], line[k + 2] * CosLat, line[k + 3]));
                    if (best > rw * 2.6f) continue;
                    float g = Mathf.Exp(-(best * best) / (rw * rw));
                    // Ranges are ragged: noise breaks the crest into peaks.
                    h += rh * g * (0.62f + 0.55f * Noise(u * 2.6f + 31, lat * 2.6f));
                }
                h += (Noise(u * 1.3f, lat * 1.3f) - 0.5f) * 0.08f + (Noise(u * 5f + 7, lat * 5f) - 0.5f) * 0.04f;
                _h[i] = Mathf.Max(0.02f, h) * Mathf.Clamp(toWater[i] / 2.2f, 0.35f, 1);
            }
        }

        // Colour each cell.
        var col = new Color[n];
        for (int j = 0; j < _gh; j++)
        {
            float lat = LatAt(j);
            for (int x = 0; x < _gw; x++)
            {
                int i = j * _gw + x;
                float u = LonAt(x) * CosLat;
                if (!land[i])
                {
                    float d = toLand[i];
                    Color wc = d < 2 ? Coastal : d < 6 ? Shallow : d < 16 ? Mid : Deep;
                    // A dithered seam between depths, so the bands read as pixel art.
                    if (d >= 1.5f && d < 2.5f && (x + j) % 2 == 0) wc = Shallow;
                    if (d >= 5.5f && d < 6.5f && (x + j) % 2 == 0) wc = Mid;
                    if (d >= 15 && d < 17 && (x + j) % 2 == 0) wc = Deep;
                    if (d <= 1.01f) wc = Foam.Lerp(Coastal, 0.35f);
                    col[i] = wc;
                    continue;
                }
                float h = _h[i];
                float north = Mathf.Clamp((lat - 40f) / 18f, 0, 1);
                float n1 = Noise(u * 3.1f + 100, lat * 3.1f);
                Color low = lat < 37.5f ? Desert.Lerp(DrySouth, Mathf.Clamp((lat - 34.5f) / 3f, 0, 1))
                    : north < 0.45f ? DrySouth.Lerp(Green, north / 0.45f) : north < 0.8f ? Green.Lerp(North, (north - 0.45f) / 0.35f) : North.Lerp(Boreal, (north - 0.8f) / 0.2f);
                // Woods and fields: patches a shade darker or lighter.
                if (n1 > 0.62f) low = low.Darkened(0.12f);
                else if (n1 < 0.3f) low = low.Lightened(0.06f);
                float snowline = 0.8f - north * 0.2f;
                Color c = h < 0.2f ? low : h < 0.36f ? low.Lerp(north > 0.5f ? HillsN : Hills, (h - 0.2f) / 0.16f) : h < 0.55f ? Hills.Lerp(Rock, (h - 0.36f) / 0.19f) : h < snowline ? Rock.Lerp(High, (h - 0.55f) / Math.Max(0.05f, snowline - 0.55f)) : Snow;
                if (toWater[i] <= 1.01f && h < 0.25f && lat < 56) c = Sand;
                // Light from the north-west, stepped into bands.
                float hl = x > 0 && j > 0 && land[i - _gw - 1] ? _h[i - _gw - 1] : h;
                float hr = x < _gw - 1 && j < _gh - 1 && land[i + _gw + 1] ? _h[i + _gw + 1] : h;
                float shade = Mathf.Clamp(1 + (hl - hr) * 9f, 0.62f, 1.3f);
                shade = Mathf.Round(shade * 8) / 8;
                col[i] = new Color(c.R * shade, c.G * shade, c.B * shade);
            }
        }

        // Voxel columns, nearest row first: each row draws only where it rises above what's in front.
        int aw = _size.X, ah = _size.Y;
        _seaPx = new bool[aw * ah];
        var data = new byte[aw * ah * 3];
        for (int i = 0; i < aw * ah; i++)
        {
            data[i * 3] = (byte)(Deep.R * 255);
            data[i * 3 + 1] = (byte)(Deep.G * 255);
            data[i * 3 + 2] = (byte)(Deep.B * 255);
            _seaPx[i] = true;
        }
        for (int x = 0; x < aw; x++)
        {
            int ymin = ah;
            for (int j = _gh - 1; j >= 0; j--)
            {
                int i = j * _gw + x;
                float h = land[i] ? _h[i] : 0;
                int y = Mathf.FloorToInt(_oy + j * Tilt - h * Lift);
                if (y >= ymin) continue;
                int top = Math.Max(0, y);
                // Far away the air thickens: a touch of haze toward the top.
                float haze = Mathf.Clamp(1 - (_oy + j * Tilt) / ah, 0, 1) * 0.22f;
                var c = col[i].Lerp(Haze, haze);
                var side = c.Darkened(0.32f);
                for (int yy = top; yy < ymin && yy < ah; yy++)
                {
                    var k = yy == top ? c : side;
                    int p = (yy * aw + x) * 3;
                    data[p] = (byte)Mathf.Clamp(k.R * 255, 0, 255);
                    data[p + 1] = (byte)Mathf.Clamp(k.G * 255, 0, 255);
                    data[p + 2] = (byte)Mathf.Clamp(k.B * 255, 0, 255);
                    _seaPx[yy * aw + x] = !land[i] && toLand[i] > 2;
                }
                ymin = top;
                if (ymin <= 0) break;
            }
        }
        _img = Image.CreateFromData(aw, ah, false, Image.Format.Rgb8, data);
    }
}

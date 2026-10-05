using System;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>How a stand's outside meets the ground: its stairs or ramps up the back.</summary>
public enum Stairs { None, Drum, Zigzag, Tower }

/// <summary>A set's outside: the base storey's colour and look, its stairs and their colour.</summary>
public readonly record struct Outside(uint Base, Look BaseLook, float BasePar, Stairs Stairs, uint StairCol);

/// <summary>
/// The outside of every stand, whatever the set: a base storey along the back (in the set's
/// own material, topped with a band in the club's colour that ties mixed sets together), gates
/// every 26 metres with a lit sign over a dark doorway and a canopy, lamps washing the wall at
/// night, and every third bay the set's own way up (ramp drums, steel stairs, stair towers).
/// </summary>
static class Exterior
{
    const float Every = 26, BaseH = 4.6f, Depth = 2.2f;

    /// <summary>Where the stand meets the ground at the back (the end of its outline).</summary>
    static float Foot(Section s) => s.Outline[^1].X;

    public static void Dress(Piece p, BuiltGround g)
    {
        var o = p.Set.Outside;
        var m = p.Mesh(g);

        // The base storey and the band along its top.
        for (int i = 0; i + 1 < p.Path.Count; i++)
        {
            var a = p.Path[i].At(Foot(p.Sec[i]) + Depth / 2, BaseH / 2);
            var b = p.Path[i + 1].At(Foot(p.Sec[i + 1]) + Depth / 2, BaseH / 2);
            var d = b - a;
            d.Y = 0;
            if (d.Length() < 0.05f) continue;
            // Run on a little past each point so the corners close.
            var ext = d.Normalized() * 0.6f;
            m.Hex(o.Base, o.BaseLook, o.BasePar);
            m.Beam(a - ext, b + ext, Depth, BaseH);
            m.Hex(g.Home);
            m.Beam(a - ext + new Vector3(0, BaseH / 2 + 0.25f, 0), b + ext + new Vector3(0, BaseH / 2 + 0.25f, 0), Depth + 0.3f, 0.5f);
        }

        // The gates, and between them now and then the way up.
        int k = 0;
        p.Each(Every, s => new(Foot(s) + Depth, 0), (pos, n, s) =>
        {
            var b = new Basis(Vector3.Up, Mathf.Atan2(n.X, n.Z));
            var side = new Vector3(n.Z, 0, -n.X);
            m.Hex(0x0e0f12);
            m.Box(new Transform3D(b, pos + new Vector3(0, 1.6f, 0) + n * 0.05f), new Vector3(5.2f, 3.2f, 0.3f), 1 | 2 | 4 | 16);
            m.Hex(Kit.Darken(o.Base, 0.7f));
            m.Box(new Transform3D(b, pos + new Vector3(0, 3.4f, 0) + n * 1.1f), new Vector3(7.4f, 0.3f, 2.4f), 63);
            m.Hex(g.Home, Look.Unlit);
            m.Box(new Transform3D(b, pos + new Vector3(0, 4.05f, 0) + n * 0.3f), new Vector3(5, 0.8f, 0.2f), 1 | 2 | 4 | 16);
            // Lamps under the canopy and washing the wall either side.
            m.Hex(0xffffff, Look.Lamp);
            foreach (float f in new[] { -6f, 6 })
                m.Box(new Transform3D(b, pos + side * f + new Vector3(0, 0.3f, 0) + n * 0.6f), new Vector3(0.8f, 0.3f, 0.5f), 4 | 16);
            if (k++ % 3 == 1) Way(m, o, pos + side * (Every / 2), n, b, s);
        });
    }

    /// <summary>The set's way up the back, standing against it at `pos` (on the ground).</summary>
    static void Way(MeshData m, Outside o, Vector3 pos, Vector3 n, Basis b, Section s)
    {
        float h = Mathf.Clamp(s.BackH * 0.72f, 9, 30);
        switch (o.Stairs)
        {
            case Stairs.Drum:
            {
                // A ramp drum: a cylinder banded at each turn of the ramp, a lit slot up it.
                float r = 5;
                var c = pos + n * (r - 0.6f);
                m.Hex(o.StairCol);
                m.Column(c, r, r, h, 12);
                m.Hex(Kit.Darken(o.StairCol, 0.72f));
                for (float y = 3.2f; y < h - 1; y += 3.2f) m.Column(c + new Vector3(0, y, 0), r + 0.35f, r + 0.35f, 0.7f, 12);
                m.Hex(0xffffff, Look.Lamp);
                m.Box(new Transform3D(b, c + n * (r + 0.05f) + new Vector3(0, h / 2, 0)), new Vector3(0.7f, h - 3, 0.2f), 16);
                break;
            }
            case Stairs.Zigzag:
            {
                // Open steel stairs: flights back and forth with landings, posts, a rail.
                var side = new Vector3(n.Z, 0, -n.X);
                var c = pos + n * 1.6f;
                const float Flight = 3.4f, Run = 5;
                int flights = Mathf.Max(2, (int)(h / Flight));
                m.Hex(o.StairCol);
                for (int f = 0; f < flights; f++)
                {
                    float dir = f % 2 == 0 ? 1 : -1;
                    var a = c + side * (-dir * Run) + new Vector3(0, f * Flight, 0);
                    var e = c + side * (dir * Run) + new Vector3(0, (f + 1) * Flight, 0);
                    m.Beam(a, e, 2.2f, 0.4f);
                    m.Beam(a + new Vector3(0, 1, 0) + n * 1.05f, e + new Vector3(0, 1, 0) + n * 1.05f, 0.12f, 0.12f);
                    m.Box(new Transform3D(b, e + side * (dir * 1.3f)), new Vector3(2.8f, 0.4f, 2.4f), 63);
                }
                foreach (float f in new[] { -Run - 1.3f, Run + 1.3f })
                    m.Beam(c + side * f, c + side * f + new Vector3(0, flights * Flight + 1, 0), 0.35f, 0.35f);
                break;
            }
            case Stairs.Tower:
            {
                // A stair tower: a solid core with a slit of lit landings, capped.
                var c = pos + n * 3.4f;
                m.Hex(o.StairCol);
                m.Box(new Transform3D(b, c + new Vector3(0, h / 2, 0)), new Vector3(7, h, 6.4f), 1 | 2 | 4 | 16 | 32);
                m.Hex(Kit.Darken(o.StairCol, 0.75f));
                m.Box(new Transform3D(b, c + new Vector3(0, h + 0.5f, 0)), new Vector3(7.6f, 1, 7), 63);
                m.Hex(0xffffff, Look.Lamp);
                for (float y = 3; y < h - 1; y += 3.4f)
                    m.Box(new Transform3D(b, c + n * 3.25f + new Vector3(0, y, 0)), new Vector3(1.2f, 1.6f, 0.15f), 16);
                break;
            }
        }
    }
}

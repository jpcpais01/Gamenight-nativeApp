using System;
using Godot;

namespace GameNight.Grounds;

/// <summary>What stands round the touchlines at any ground: LED boards, corner flags, the
/// dugouts with the substitutes on the benches, the players' tunnel. Built into the static
/// draw (the corner flags flutter in its vertex shader).</summary>
public static class Pitchside
{
    public const float HL = 52.5f, HW = 34f, GoalHalf = 3.66f;
    /// <summary>Dugout centre: team 0's at -x, team 1's at +x, on the far touchline (the PWA's bench.ts).</summary>
    public static readonly Vector2 Dugout = new(9, -(HW + 2.6f));

    /// <summary>Pitchside LED boards round the pitch (8 designs), with a gap at the halfway line
    /// on the far side where the teams walk out.</summary>
    public static void AdBoards(MeshData m, float side = HW + 3.8f, float end = HL + 4.5f, bool tunnelGap = true)
    {
        const float W = 6f;
        int i = 0;
        void Board(float x, float z, float ry)
        {
            var t = new Transform3D(new Basis(Vector3.Up, ry), new Vector3(x, 0.45f, z));
            m.Hex(0x1e2126);
            m.Box(t, new Vector3(W - 0.08f, 0.9f, 0.12f), 63 & ~16);
            m.Hex(0xffffff, Look.Board, (i * 5 + (i >> 2)) % 8);
            m.Box(t, new Vector3(W - 0.08f, 0.9f, 0.12f), 16);
            i++;
        }
        for (float x = -HL + W / 2; x <= HL - W / 2 + 0.01f; x += W) Board(x, side, Mathf.Pi);
        for (float x = (tunnelGap ? 3 : 0) + W / 2; x <= HL - W / 2 + 1.6f; x += W)
        {
            Board(x, -side, 0);
            Board(-x, -side, 0);
        }
        for (float z = -HW + W / 2; z <= HW - W / 2 + 0.01f; z += W)
        {
            if (Mathf.Abs(z) < GoalHalf + 3.5f) continue;
            Board(-end, z, Mathf.Pi / 2);
            Board(end, z, -Mathf.Pi / 2);
        }
    }

    /// <summary>Corner flags: white poles, yellow flags in the wind.</summary>
    public static void CornerFlags(MeshData m, uint flag = 0xffd447)
    {
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                float x = sx * HL, z = sz * HW;
                m.Hex(0xf2f0e8);
                m.Cylinder(new Vector3(x, 0, z), new Vector3(x, 1.6f, z), 0.025f, 5);
                m.Hex(flag, Look.Cloth, -1);
                // Two-sided cloth, pinned to the pole on its left edge (uv.x = 0 there).
                var a = new Vector3(x, 1.3f, z);
                var d = new Vector3(0.42f, 0, 0) * -sx;
                const int seg = 4;
                for (int s = 0; s < seg; s++)
                {
                    var p0 = a + d * (s / (float)seg);
                    var p1 = a + d * ((s + 1) / (float)seg);
                    m.QuadUV(p0, p1, p1 + new Vector3(0, 0.3f, 0), p0 + new Vector3(0, 0.3f, 0),
                        new(s / (float)seg, 0), new((s + 1) / (float)seg, 0), new((s + 1) / (float)seg, 1), new(s / (float)seg, 1));
                }
            }
    }

    /// <summary>The players' tunnel: a dark mouth in the stand's front wall at z, framed in concrete.</summary>
    public static void Tunnel(MeshData m, float z, uint frame = 0x5c6068)
    {
        m.Hex(0x050507, Look.Unlit);
        m.Quad(new Vector3(-2.2f, 0, z + 0.03f), new Vector3(2.2f, 0, z + 0.03f), new Vector3(2.2f, 2.7f, z + 0.03f), new Vector3(-2.2f, 2.7f, z + 0.03f));
        m.Hex(frame);
        m.Box(new Vector3(-2.7f, 0, z), new Vector3(-2.2f, 3.1f, z + 0.65f));
        m.Box(new Vector3(2.2f, 0, z), new Vector3(2.7f, 3.1f, z + 0.65f));
        m.Box(new Vector3(-2.7f, 2.7f, z), new Vector3(2.7f, 3.2f, z + 0.65f));
    }

    /// <summary>The two dugouts on the far touchline: back wall, glass roof, the bench seat
    /// (its top at 0.45 m, centred 0.55 m behind the dugout centre: BenchView sits on it).</summary>
    public static void Dugouts(MeshData m)
    {
        foreach (int side in new[] { -1, 1 })
        {
            float cx = side * Dugout.X, cz = Dugout.Y;
            m.Hex(0x2b3038);
            m.Box(new Vector3(cx - 3.5f, 0, cz - 0.96f), new Vector3(cx + 3.5f, 2.3f, cz - 0.84f));
            m.Hex(0x9fb4c8, Look.Stipple);
            m.Box(new Vector3(cx - 3.55f, 2.31f, cz - 0.95f), new Vector3(cx + 3.55f, 2.39f, cz + 0.95f));
            foreach (float ex in new[] { -3.5f, 3.5f })
                m.Box(new Vector3(cx + ex - 0.05f, 0, cz - 0.9f), new Vector3(cx + ex + 0.05f, 2.3f, cz + 0.9f));
            m.Hex(0x46505c);
            m.Box(new Vector3(cx - 3.3f, 0, cz - 0.8f), new Vector3(cx + 3.3f, 0.45f, cz - 0.3f));
        }
    }
}

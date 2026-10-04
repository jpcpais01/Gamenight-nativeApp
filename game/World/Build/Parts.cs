using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds.Build;

/// <summary>Small pieces the stand sets share: lamp banks, lattice towers, flags, discs.</summary>
public static class Parts
{
    /// <summary>A basis whose -Z looks along n (toward the pitch when n is a stand's outward normal reversed).</summary>
    public static Basis Facing(Vector3 face) => new(Vector3.Up.Cross(face).Normalized(), Vector3.Up, face);

    /// <summary>A bank of floodlights at c facing `face` (horizontal), tilted down by `tilt`:
    /// rows x cols of lamps on a dark frame. Returns the point the light comes from.</summary>
    public static Vector3 LampBank(MeshData m, Vector3 c, Vector3 face, float w, float h, float tilt = 0.5f, int rows = 2)
    {
        face = new Vector3(face.X, 0, face.Z).Normalized();
        var basis = Facing(face) * new Basis(Vector3.Right, tilt);
        m.Hex(0x23272e);
        m.Box(new Transform3D(basis, c - basis.Z * 0.2f), new Vector3(w + 0.4f, h + 0.4f, 0.35f));
        m.Hex(0xffffff, Look.Lamp);
        for (int r = 0; r < rows; r++)
        {
            var p = c + basis.Y * ((r + 0.5f) / rows - 0.5f) * h + basis.Z * 0.02f;
            var x = basis.X * (w / 2); var y = basis.Y * (h / rows * 0.42f);
            m.QuadUV(p - x - y, p + x - y, p + x + y, p - x + y, new(0, 0), new(1, 0), new(1, 1), new(0, 1));
        }
        return c + face * 1.2f;
    }

    /// <summary>A tapering square lattice tower from the ground to `head`, legs `r0` out at the foot and `r1` at the top.</summary>
    public static void Lattice(MeshData m, Vector3 foot, float head, float r0, float r1, uint col, float bar = 0.22f, float bay = 4)
    {
        Vector3 Leg(int i, float y)
        {
            float r = Mathf.Lerp(r0, r1, y / head);
            return foot + new Vector3((i & 1) != 0 ? r : -r, y, (i & 2) != 0 ? r : -r);
        }
        int[] ring = { 0, 1, 3, 2 };
        m.Hex(col);
        for (float y = 0; y < head - 0.01f; y += bay)
        {
            float y1 = Mathf.Min(head, y + bay);
            for (int c = 0; c < 4; c++)
            {
                int i = ring[c], j = ring[(c + 1) % 4];
                m.Beam(Leg(i, y), Leg(i, y1), bar, bar);
                m.Beam(Leg(i, y1), Leg(j, y1), bar * 0.6f, bar * 0.6f);
                m.Beam(Leg(i, y), Leg(j, y1), bar * 0.45f, bar * 0.45f);
            }
        }
    }

    /// <summary>A flag on a pole: the cloth ties on along the pole and flies along `along`.</summary>
    public static void Flag(MeshData m, Vector3 foot, float pole, Vector3 along, float w, float h, uint col)
    {
        m.Hex(0x3a3d42);
        m.Beam(foot, foot + new Vector3(0, pole, 0), 0.12f, 0.12f);
        m.Hex(col, Look.Cloth, -1);
        var top = foot + new Vector3(0, pole - 0.2f, 0);
        const int seg = 5;
        for (int s = 0; s < seg; s++)
        {
            float u0 = s / (float)seg, u1 = (s + 1) / (float)seg;
            var a = top + along * (w * u0) - new Vector3(0, h, 0);
            var b = top + along * (w * u1) - new Vector3(0, h, 0);
            m.QuadUV(a, b, b + new Vector3(0, h, 0), a + new Vector3(0, h, 0), new(u0, 0), new(u1, 0), new(u1, 1), new(u0, 1));
        }
    }

    /// <summary>A flat disc facing `n`.</summary>
    public static void Disc(MeshData m, Vector3 c, Vector3 n, float r, uint col)
    {
        m.Hex(col);
        var x = Vector3.Up.Cross(n).Normalized();
        var y = n.Cross(x);
        const int N = 16;
        for (int i = 0; i < N; i++)
        {
            float a0 = Mathf.Tau * i / N, a1 = Mathf.Tau * (i + 1) / N;
            m.Tri(c, c + (x * Mathf.Cos(a0) + y * Mathf.Sin(a0)) * r, c + (x * Mathf.Cos(a1) + y * Mathf.Sin(a1)) * r, new(0, 0), new(1, 0), new(0, 1));
        }
    }

    /// <summary>Battlements round a circle: `n` merlons on a wall of radius r at height y.</summary>
    public static void MerlonRing(MeshData m, Vector3 c, float r, float y, int n, uint col)
    {
        m.Hex(col, Look.Brick);
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Tau * i / n;
            var p = c + new Vector3(Mathf.Cos(a) * r, y + 0.65f, Mathf.Sin(a) * r);
            m.Box(new Transform3D(new Basis(Vector3.Up, -a), p), new Vector3(0.9f, 1.3f, Mathf.Tau * r / n * 0.5f));
        }
    }
}

using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds;

/// <summary>A point on a stand's front edge: where it is, its outward normal (away from the
/// pitch) and the section it's in (0 side stands, 1 home end, 2 away end).</summary>
public struct PathPt
{
    public float X, Z, NX, NZ;
    public int Zone;
    public PathPt(float x, float z, float nx, float nz, int zone) { X = x; Z = z; NX = nx; NZ = nz; Zone = zone; }
    /// <summary>The point `o` metres back from the front edge, `h` up.</summary>
    public readonly Vector3 At(float o, float h) => new(X + NX * o, h, Z + NZ * o);
    /// <summary>Yaw that turns -Z (a quad's facing) toward the pitch.</summary>
    public readonly float FacePitch => Mathf.Atan2(NX, NZ);
}

/// <summary>
/// The stands are cross-sections swept along a path round the pitch (the PWA's stadium.ts
/// approach): a rectangle with quadrant corners. Strips, end caps and placements all come
/// from the same path, so every ground can reuse it with its own numbers.
/// </summary>
public static class Bowl
{
    /// <summary>The path from part-way round the near-left corner, behind the home goal (-x),
    /// along the far side and behind the away goal to part-way round the near-right corner.
    /// `sideZone` is the zone of the far straight.</summary>
    public static List<PathPt> Path(float bx, float bz, float r, float startA = 0.7f, float endA = 2.3f)
    {
        var pts = new List<PathPt>();
        float cx = bx - r, cz = bz - r;
        const float P = Mathf.Pi;
        void Arc(float ox, float oz, float a0, float a1, Func<float, int> zone)
        {
            int n = (int)Mathf.Ceil(Mathf.Abs(a1 - a0) / 0.12f);
            for (int i = 0; i <= n; i++)
            {
                float a = a0 + (a1 - a0) * i / n;
                float nx = Mathf.Cos(a), nz = Mathf.Sin(a);
                pts.Add(new PathPt(ox + nx * r, oz + nz * r, nx, nz, zone(a)));
            }
        }
        void Line(float x0, float z0, float x1, float z1, float nx, float nz, int zone)
        {
            int n = (int)Mathf.Ceil(Mathf.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)) / 4);
            for (int i = 1; i < n; i++) pts.Add(new PathPt(x0 + (x1 - x0) * i / n, z0 + (z1 - z0) * i / n, nx, nz, zone));
        }
        Arc(-cx, cz, startA * P, P, _ => 1);
        Line(-bx, cz, -bx, -cz, -1, 0, 1);
        Arc(-cx, -cz, P, 1.5f * P, a => a < 1.22f * P ? 1 : 0);
        Line(-cx, -bz, cx, -bz, 0, -1, 0);
        Arc(cx, -cz, 1.5f * P, 2 * P, a => a > 1.78f * P ? 2 : 0);
        Line(bx, -cz, bx, cz, 1, 0, 2);
        Arc(cx, cz, 2 * P, endA * P, _ => 2);
        return pts;
    }

    /// <summary>The near side, behind the broadcast camera: from where <see cref="Path"/> stops in
    /// the near-right corner, along the near touchline, to where it stops in the near-left.</summary>
    public static List<PathPt> NearPath(float bx, float bz, float r, float a0 = 0.3f, float a1 = 0.7f)
    {
        var pts = new List<PathPt>();
        float cx = bx - r, cz = bz - r;
        const float P = Mathf.Pi;
        void Arc(float ox, float b0, float b1)
        {
            int n = Math.Max(1, (int)Mathf.Ceil(Mathf.Abs(b1 - b0) / 0.12f));
            for (int i = 0; i <= n; i++)
            {
                float a = b0 + (b1 - b0) * i / n;
                pts.Add(new PathPt(ox + Mathf.Cos(a) * r, cz + Mathf.Sin(a) * r, Mathf.Cos(a), Mathf.Sin(a), 0));
            }
        }
        Arc(cx, a0 * P, 0.5f * P);
        int m = (int)Mathf.Ceil(2 * cx / 4);
        for (int i = 1; i < m; i++) pts.Add(new PathPt(cx - 2 * cx * i / m, bz, 0, 1, 0));
        Arc(-cx, 0.5f * P, a1 * P);
        return pts;
    }

    /// <summary>A straight run of path points from (x0,z0) to (x1,z1), facing (nx,nz).</summary>
    public static List<PathPt> Straight(float x0, float z0, float x1, float z1, float nx, float nz, int zone = 0, float step = 4)
    {
        var pts = new List<PathPt>();
        int n = Math.Max(1, (int)Mathf.Ceil(Mathf.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)) / step));
        for (int i = 0; i <= n; i++) pts.Add(new PathPt(x0 + (x1 - x0) * i / n, z0 + (z1 - z0) * i / n, nx, nz, zone));
        return pts;
    }

    /// <summary>Splits the path into before the far straight, the far straight, and after.</summary>
    public static (List<PathPt> left, List<PathPt> main, List<PathPt> right) Split(List<PathPt> path)
    {
        int i0 = path.FindIndex(p => p.NZ < -0.999f);
        int i1 = i0;
        while (i1 + 1 < path.Count && path[i1 + 1].NZ < -0.999f) i1++;
        return (path.GetRange(0, i0 + 1), path.GetRange(i0, i1 - i0 + 1), path.GetRange(i1, path.Count - i1));
    }

    /// <summary>
    /// A surface swept along the path between two points of the cross-section, (offset, height)
    /// each. uv in metres: u along the strip's front edge, v from a toward b. `segs` splits it
    /// across, so vertex lighting can follow a shadow line over a deep tier.
    /// </summary>
    public static void Strip(MeshData m, List<PathPt> path, Vector2 a, Vector2 b, int segs = 1)
    {
        float slope = (b - a).Length();
        float u = 0;
        var prev = path[0].At(a.X, a.Y);
        for (int i = 0; i + 1 < path.Count; i++)
        {
            var p0 = path[i];
            var p1 = path[i + 1];
            var f1 = p1.At(a.X, a.Y);
            float u1 = u + (f1 - prev).Length();
            for (int s = 0; s < segs; s++)
            {
                var c0 = a.Lerp(b, s / (float)segs);
                var c1 = a.Lerp(b, (s + 1) / (float)segs);
                float v0 = slope * s / segs, v1 = slope * (s + 1) / segs;
                m.QuadUV(p0.At(c0.X, c0.Y), p1.At(c0.X, c0.Y), p1.At(c1.X, c1.Y), p0.At(c1.X, c1.Y),
                    new(u, v0), new(u1, v0), new(u1, v1), new(u, v1));
            }
            u = u1;
            prev = f1;
        }
    }

    /// <summary>Length of the path's front edge at offset o (what <see cref="Strip"/> measures as u).</summary>
    public static float Length(List<PathPt> path, float o)
    {
        float len = 0;
        for (int i = 0; i + 1 < path.Count; i++) len += (path[i + 1].At(o, 0) - path[i].At(o, 0)).Length();
        return len;
    }

    /// <summary>u-range (metres along the front at offset o) covered by a zone.</summary>
    public static Vector2 ZoneRange(List<PathPt> path, float o, int zone)
    {
        float u = 0, u0 = -1, u1 = 0;
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0) u += (path[i].At(o, 0) - path[i - 1].At(o, 0)).Length();
            if (path[i].Zone != zone) continue;
            if (u0 < 0) u0 = u;
            u1 = u;
        }
        return new Vector2(u0, u1);
    }

    /// <summary>A flat wall in the section plane at each of `pts`, with the given (offset, height) outline.</summary>
    public static void Caps(MeshData m, IEnumerable<PathPt> pts, Vector2[] outline)
    {
        var tris = Geometry2D.TriangulatePolygon(outline);
        foreach (var p in pts)
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector2 a = outline[tris[i]], b = outline[tris[i + 1]], c = outline[tris[i + 2]];
                m.Tri(p.At(a.X, a.Y), p.At(b.X, b.Y), p.At(c.X, c.Y), a, b, c);
            }
    }

    /// <summary>Walks the path's front edge at offset o in steps of `step` metres, calling
    /// back with (u, position on the edge, outward normal).</summary>
    public static void Walk(List<PathPt> path, float o, float step, Action<float, Vector3, Vector2> at)
    {
        float u = 0, next = step * 0.5f;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            var a = path[i].At(o, 0);
            var b = path[i + 1].At(o, 0);
            float len = (b - a).Length();
            var na = new Vector2(path[i].NX, path[i].NZ);
            var nb = new Vector2(path[i + 1].NX, path[i + 1].NZ);
            while (next <= u + len)
            {
                float t = len > 0 ? (next - u) / len : 0;
                at(next, a.Lerp(b, t), na.Lerp(nb, t).Normalized());
                next += step;
            }
            u += len;
        }
    }
}

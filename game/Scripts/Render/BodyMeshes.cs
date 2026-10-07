using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Render;

/// <summary>
/// The footballer's parts, built exactly as the PWA builds them (players.ts buildGeometries):
/// lathed torso, pelvis, limbs and shorts, a sculpted head (brow, nose, cheekbones, jaw, ears),
/// three haircuts, hands, boots. The
/// geometry helpers follow three.js's own (lathe, sphere, capsule, icosahedron) so the uv
/// layouts the kit shader paints on are the same.
/// </summary>
public static class BodyMeshes
{
    public enum Part { Torso, Pelvis, Neck, Head, HairShort, HairCurly, HairQuiff, UpperArm, Forearm, Hand, ShortsLeg, Thigh, Shin, Boot }
    public const int PartCount = 14;

    /// <summary>A mesh under construction, in three.js's conventions (counter-clockwise front faces).</summary>
    sealed class Geo
    {
        public readonly List<Vector3> P = new(), N = new();
        public readonly List<Vector2> UV = new();
        public readonly List<int> I = new();

        public Geo Scale(float x, float y, float z)
        {
            var s = new Vector3(x, y, z);
            for (int i = 0; i < P.Count; i++)
            {
                P[i] *= s;
                N[i] = (N[i] / s).Normalized();
            }
            return this;
        }

        public Geo Translate(float x, float y, float z)
        {
            var t = new Vector3(x, y, z);
            for (int i = 0; i < P.Count; i++) P[i] += t;
            return this;
        }

        public Geo RotateX(float a)
        {
            float c = MathF.Cos(a), s = MathF.Sin(a);
            Vector3 R(Vector3 v) => new(v.X, v.Y * c - v.Z * s, v.Y * s + v.Z * c);
            for (int i = 0; i < P.Count; i++)
            {
                P[i] = R(P[i]);
                N[i] = R(N[i]);
            }
            return this;
        }

        public Geo Merge(Geo o)
        {
            int b = P.Count;
            P.AddRange(o.P);
            N.AddRange(o.N);
            UV.AddRange(o.UV);
            foreach (int i in o.I) I.Add(i + b);
            return this;
        }

        /// <summary>Smooth normals from the faces (three's computeVertexNormals on indexed geometry).</summary>
        public void SmoothNormals()
        {
            var acc = new Vector3[P.Count];
            for (int t = 0; t < I.Count; t += 3)
            {
                var a = P[I[t]];
                var n = (P[I[t + 1]] - a).Cross(P[I[t + 2]] - a);
                acc[I[t]] += n;
                acc[I[t + 1]] += n;
                acc[I[t + 2]] += n;
            }
            for (int i = 0; i < P.Count; i++) N[i] = acc[i].LengthSquared() > 0 ? acc[i].Normalized() : Vector3.Up;
        }

        public ArrayMesh Commit()
        {
            // Godot's front faces wind clockwise: every triangle turns over.
            var idx = new int[I.Count];
            for (int t = 0; t < I.Count; t += 3)
            {
                idx[t] = I[t];
                idx[t + 1] = I[t + 2];
                idx[t + 2] = I[t + 1];
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = P.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = N.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = UV.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = idx;
            var m = new ArrayMesh();
            m.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return m;
        }
    }

    /// <summary>three's LatheGeometry: the profile (radius, height) swept round the y axis; u round, v along the profile.
    /// Faces always point outward, whichever way the profile is listed.</summary>
    /// <summary>Round-the-body resolution while building (1 = the PWA's): the profiles, and so the
    /// uv layout, stay the same, only the facets around each part get fewer.</summary>
    static float _detail = 1;
    static int D(int n, int min) => Math.Max(min, (int)MathF.Round(n * _detail));

    static Geo Lathe((float r, float y)[] pts, int segments)
    {
        segments = D(segments, 5);
        var g = new Geo();
        int n = pts.Length;
        // Profile normals (as three computes them), turned outward.
        float dir = MathF.Sign(pts[n - 1].y - pts[0].y);
        if (dir == 0) dir = 1;
        var pn = new Vector2[n];
        for (int j = 0; j < n; j++)
        {
            var a = pts[Math.Max(0, j - 1)];
            var b = pts[Math.Min(n - 1, j + 1)];
            var t = new Vector2(b.r - a.r, b.y - a.y);
            pn[j] = (new Vector2(t.Y, -t.X) * dir).Normalized();
        }
        for (int i = 0; i <= segments; i++)
        {
            float phi = i / (float)segments * MathF.Tau;
            float s = MathF.Sin(phi), c = MathF.Cos(phi);
            for (int j = 0; j < n; j++)
            {
                g.P.Add(new Vector3(pts[j].r * s, pts[j].y, pts[j].r * c));
                g.N.Add(new Vector3(pn[j].X * s, pn[j].Y, pn[j].X * c));
                g.UV.Add(new Vector2(i / (float)segments, j / (float)(n - 1)));
            }
        }
        for (int i = 0; i < segments; i++)
            for (int j = 0; j < n - 1; j++)
            {
                int a = j + i * n, b = a + n, c = a + n + 1, d = a + 1;
                if (dir > 0) { g.I.AddRange(new[] { a, b, d, c, d, b }); }
                else { g.I.AddRange(new[] { a, d, b, c, b, d }); }
            }
        return g;
    }

    /// <summary>three's SphereGeometry (uv.y = 1 at the top).</summary>
    static Geo Sphere(float r, int ws, int hs, float thetaLen = MathF.PI)
    {
        ws = D(ws, 6);
        hs = D(hs, 4);
        var g = new Geo();
        var grid = new int[hs + 1, ws + 1];
        for (int iy = 0; iy <= hs; iy++)
        {
            float v = iy / (float)hs;
            float uOff = iy == 0 ? 0.5f / ws : iy == hs && thetaLen >= MathF.PI ? -0.5f / ws : 0;
            for (int ix = 0; ix <= ws; ix++)
            {
                float u = ix / (float)ws;
                float th = v * thetaLen;
                var p = new Vector3(-r * MathF.Cos(u * MathF.Tau) * MathF.Sin(th), r * MathF.Cos(th), r * MathF.Sin(u * MathF.Tau) * MathF.Sin(th));
                grid[iy, ix] = g.P.Count;
                g.P.Add(p);
                g.N.Add(p.LengthSquared() > 0 ? p.Normalized() : Vector3.Up);
                g.UV.Add(new Vector2(u + uOff, 1 - v));
            }
        }
        for (int iy = 0; iy < hs; iy++)
            for (int ix = 0; ix < ws; ix++)
            {
                int a = grid[iy, ix + 1], b = grid[iy, ix], c = grid[iy + 1, ix], d = grid[iy + 1, ix + 1];
                if (iy != 0) g.I.AddRange(new[] { a, b, d });
                if (iy != hs - 1 || thetaLen < MathF.PI) g.I.AddRange(new[] { b, c, d });
            }
        return g;
    }

    /// <summary>A capsule along y: hemispheres of radius r either side of a straight length, as a lathe.</summary>
    static Geo Capsule(float r, float length, int capSeg, int radial, int heightSeg = 1)
    {
        var pts = new List<(float, float)>();
        float h = length / 2;
        for (int k = 0; k <= capSeg; k++)
        {
            float a = -MathF.PI / 2 + k / (float)capSeg * MathF.PI / 2;
            pts.Add((r * MathF.Cos(a), -h + r * MathF.Sin(a)));
        }
        for (int k = 1; k < heightSeg; k++) pts.Add((r, -h + length * k / heightSeg));
        for (int k = 0; k <= capSeg; k++)
        {
            float a = k / (float)capSeg * MathF.PI / 2;
            pts.Add((r * MathF.Cos(a), h + r * MathF.Sin(a)));
        }
        pts[0] = (0, pts[0].Item2);
        pts[^1] = (0, pts[^1].Item2);
        return Lathe(pts.ToArray(), radial);
    }

    /// <summary>three's IcosahedronGeometry(radius, detail): flat-shaded, each face its own vertices.</summary>
    static Geo Icosahedron(float radius, int detail, Func<Vector3, Vector3> shape)
    {
        if (_detail < 0.75f) detail = Math.Max(0, detail - 1);
        float t = (1 + MathF.Sqrt(5)) / 2;
        float[] v = { -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, 0, 0, -1, t, 0, 1, t, 0, -1, -t, 0, 1, -t, t, 0, -1, t, 0, 1, -t, 0, -1, -t, 0, 1 };
        int[] f = { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
        Vector3 V(int i) => new(v[i * 3], v[i * 3 + 1], v[i * 3 + 2]);
        var g = new Geo();
        int cols = detail + 1;
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            a = shape(a.Normalized() * radius);
            b = shape(b.Normalized() * radius);
            c = shape(c.Normalized() * radius);
            var n = (b - a).Cross(c - a);
            // Outward, whatever the subdivision's order.
            if (n.Dot(a + b + c) < 0) { (b, c) = (c, b); n = -n; }
            n = n.Normalized();
            foreach (var p in new[] { a, b, c })
            {
                g.I.Add(g.P.Count);
                g.P.Add(p);
                g.N.Add(n);
                g.UV.Add(Vector2.Zero);
            }
        }
        for (int k = 0; k < f.Length; k += 3)
        {
            Vector3 a = V(f[k]), b = V(f[k + 1]), c = V(f[k + 2]);
            var grid = new Vector3[cols + 1][];
            for (int i = 0; i <= cols; i++)
            {
                var aj = a.Lerp(c, i / (float)cols);
                var bj = b.Lerp(c, i / (float)cols);
                int rows = cols - i;
                grid[i] = new Vector3[rows + 1];
                for (int j = 0; j <= rows; j++) grid[i][j] = j == 0 && i == cols ? aj : aj.Lerp(bj, rows == 0 ? 0 : j / (float)rows);
            }
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < 2 * (cols - i) - 1; j++)
                {
                    int q = j / 2;
                    if (j % 2 == 0) Tri(grid[i][q + 1], grid[i + 1][q], grid[i][q]);
                    else Tri(grid[i][q + 1], grid[i + 1][q + 1], grid[i + 1][q]);
                }
        }
        return g;
    }

    static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>0..1 hump of y between a and b (smooth both ends).</summary>
    static float Bump(float y, float a, float b) => MathF.Sin(MathF.PI * Math.Clamp((y - a) / (b - a), 0, 1));

    /// <summary>A soft round bump centred at (cx, cy), radii (rx, ry).</summary>
    static float Blob(float u, float v, float cx, float cy, float rx, float ry)
    {
        float dx = (u - cx) / rx, dy = (v - cy) / ry;
        float d = dx * dx + dy * dy;
        return d >= 1 ? 0 : (1 - d) * (1 - d);
    }

    static Geo Cap(float r, float theta, float tilt) => Sphere(r, 18, 9, theta).RotateX(-tilt);

    /// <summary>The parts. `detail` below 1 for figures that are only ever a few pixels tall
    /// (the bench): about half the triangles at 0.5, the same shapes.</summary>
    public static ArrayMesh[] Build(float detail = 1)
    {
        _detail = detail;
        try { return BuildParts(); }
        finally { _detail = 1; }
    }

    static ArrayMesh[] BuildParts()
    {
        var m = new ArrayMesh[PartCount];

        // Torso: an athlete's V, narrow at the waist, the chest and shoulder blades full, the
        // shoulders rounding over and the trapezius sloping up to the neck.
        var torso = Lathe(new[] { (0f, -0.01f), (0.138f, 0f), (0.146f, 0.08f), (0.152f, 0.17f), (0.168f, 0.29f), (0.185f, 0.41f), (0.19f, 0.49f), (0.176f, 0.55f), (0.135f, 0.592f), (0.075f, 0.622f), (0f, 0.63f) }, 18)
            .Scale(1.1f, 1, 0.66f);
        for (int i = 0; i < torso.P.Count; i++)
        {
            var p = torso.P[i];
            float chest = Bump(p.Y, 0.3f, 0.52f);
            // Pecs in front, lats and blades behind: the chest is deeper than the waist.
            float z = p.Z * (1 + (p.Z > 0 ? 0.1f : 0.06f) * chest);
            // The shoulders drop away from the neck.
            float wide = Math.Clamp(MathF.Abs(p.X) / 0.2f, 0, 1);
            float y = p.Y - 0.035f * wide * wide * Smooth(0.5f, 0.62f, p.Y);
            torso.P[i] = new Vector3(p.X, y, z);
        }
        torso.SmoothNormals();
        m[(int)Part.Torso] = torso.Commit();
        m[(int)Part.Pelvis] = Lathe(new[] { (0f, 0.08f), (0.148f, 0.07f), (0.158f, 0f), (0.165f, -0.08f), (0.168f, -0.13f), (0f, -0.14f) }, 16)
            .Scale(1.1f, 1, 0.8f).Commit();
        // Neck: flaring into the trapezius at the base.
        m[(int)Part.Neck] = Lathe(new[] { (0f, -0.055f), (0.07f, -0.055f), (0.056f, -0.01f), (0.05f, 0.055f), (0f, 0.055f) }, 10).Scale(1, 1, 0.92f).Translate(0, 0.04f, 0).Commit();

        // Head: sculpted, at a fixed resolution (it fills the frame in the close-ups): skull,
        // brow ridge, eye sockets, nose, cheekbones, a jaw and chin, and the ears. Front = +z.
        float keep = _detail;
        _detail = 1;
        var head = Sphere(0.104f, 20, 16);
        var ear = Sphere(0.026f, 6, 5).Scale(0.45f, 1.25f, 0.85f);
        _detail = keep;
        for (int i = 0; i < head.P.Count; i++)
        {
            var p = head.P[i];
            float x = p.X, y = p.Y, z = p.Z;
            if (y < 0)
            {
                // Jaw: narrower and longer below the cheekbones, a squarer chin.
                float k = -y / 0.104f;
                x *= 1 - 0.2f * k * k + 0.06f * k;
                z *= 1 - 0.1f * k;
                y *= 1.14f;
            }
            if (z > 0)
            {
                z *= 1.04f;
                float u = x / 0.094f, v = y / 0.104f;
                float front = Smooth(0.35f, 0.85f, z / 0.104f);
                // Nose: a ridge down the middle of the face, fullest at the tip.
                float nose = Blob(u, v, 0, -0.12f, 0.17f, 0.3f) * front;
                // Brow ridge over the eyes, the sockets set in under it.
                float brow = Blob(u, v, 0, 0.25f, 0.75f, 0.1f) * front;
                float socket = (Blob(u, v, 0.36f, 0.1f, 0.2f, 0.12f) + Blob(u, v, -0.36f, 0.1f, 0.2f, 0.12f)) * front;
                // Cheekbones and the chin.
                float cheek = (Blob(u, v, 0.55f, -0.05f, 0.22f, 0.18f) + Blob(u, v, -0.55f, -0.05f, 0.22f, 0.18f)) * front;
                float chin = Blob(u, v, 0, -0.98f, 0.32f, 0.16f) * front;
                z += 0.017f * nose + 0.006f * brow - 0.008f * socket + 0.004f * cheek + 0.006f * chin;
            }
            head.P[i] = new Vector3(x * 0.9f, y * 1.06f, z);
        }
        head.SmoothNormals();
        for (int sd = -1; sd <= 1; sd += 2)
        {
            var e = new Geo().Merge(ear);
            e.Translate(sd * 0.091f, 0.002f, -0.006f);
            head.Merge(e);
        }
        m[(int)Part.Head] = head.Translate(0, 0.13f, 0.008f).Commit();

        m[(int)Part.HairShort] = Cap(0.112f, MathF.PI * 0.56f, 0.32f).Scale(0.93f, 1.07f, 1.06f).Translate(0, 0.142f, -0.006f).Commit();
        m[(int)Part.HairCurly] = Icosahedron(0.128f, 2, p =>
        {
            float n = 1 + 0.06f * MathF.Sin(p.X * 90) * MathF.Sin(p.Y * 80) * MathF.Sin(p.Z * 85);
            p *= n;
            return new Vector3(p.X * 0.95f, p.Y * 0.95f, p.Z);
        }).Translate(0, 0.165f, -0.015f).Commit();
        // Swept quiff: a short back and sides, the front brushed up and over.
        m[(int)Part.HairQuiff] = Cap(0.11f, MathF.PI * 0.56f, 0.34f).Scale(0.94f, 1.05f, 1.05f).Translate(0, 0.142f, -0.008f)
            .Merge(Sphere(0.062f, 10, 7).Scale(1.05f, 0.62f, 1.1f).RotateX(-0.35f).Translate(0, 0.228f, 0.035f)).Commit();

        // Arm: sleeve on top (uv.y < ~0.5), skin below; uv.y = 0 at the shoulder.
        // (The deltoid rounds the top; the biceps shows below the sleeve.)
        m[(int)Part.UpperArm] = Lathe(new[] { (0f, 0.035f), (0.062f, 0.022f), (0.07f, -0.035f), (0.067f, -0.11f), (0.068f, -0.155f), (0.053f, -0.17f), (0.055f, -0.205f), (0.046f, -0.285f), (0f, -0.31f) }, 12).Commit();
        // Forearm to the wrist; uv.y > ~0.85 is the wrist (keepers' glove cuffs).
        m[(int)Part.Forearm] = Lathe(new[] { (0f, 0.02f), (0.046f, 0f), (0.05f, -0.055f), (0.038f, -0.19f), (0.031f, -0.24f), (0f, -0.255f) }, 10).Scale(1, 1, 0.85f).Commit();
        // Hand: a relaxed palm, fingers together, a thumb; origin at the wrist, thumb forward.
        var palm = Sphere(0.042f, 8, 6).Scale(0.62f, 1.05f, 1).Translate(0, -0.045f, 0.004f);
        var fingers = Capsule(0.024f, 0.045f, 2, 6).Scale(0.95f, 1, 1.45f).RotateX(0.25f).Translate(0, -0.1f, 0.012f);
        var thumb = Capsule(0.012f, 0.035f, 1, 5).RotateX(0.5f).Translate(0, -0.05f, 0.04f);
        m[(int)Part.Hand] = palm.Merge(fingers).Merge(thumb).Scale(1.1f, 1.08f, 1.1f).Commit();
        // Shorts leg: an open tube (drawn double-sided).
        m[(int)Part.ShortsLeg] = Lathe(new[] { (0.092f, 0.05f), (0.098f, -0.06f), (0.104f, -0.16f), (0.107f, -0.215f) }, 14).Commit();
        m[(int)Part.Thigh] = Lathe(new[] { (0f, 0.02f), (0.074f, 0f), (0.078f, -0.1f), (0.07f, -0.25f), (0.056f, -0.39f), (0.05f, -0.44f), (0f, -0.46f) }, 12).Commit();
        // Shin in a sock: calf bulge; uv.y < ~0.16 is the sock band.
        m[(int)Part.Shin] = Lathe(new[] { (0f, 0.02f), (0.052f, 0f), (0.056f, -0.05f), (0.063f, -0.15f), (0.052f, -0.29f), (0.04f, -0.39f), (0.038f, -0.43f), (0f, -0.45f) }, 12)
            .Scale(1, 1, 1.08f).Commit();
        // Enough rings along the boot for the toe to bend at the ball of the foot.
        m[(int)Part.Boot] = Capsule(0.046f, 0.16f, 3, 10, 8).RotateX(MathF.PI / 2).Scale(0.92f, 0.72f, 1).Translate(0, -0.035f, 0.05f).Commit();
        return m;
    }
}

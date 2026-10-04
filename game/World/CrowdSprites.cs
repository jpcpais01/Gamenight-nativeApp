using System;
using System.Collections.Generic;
using Godot;

namespace GameNight.Grounds;

/// <summary>
/// The fans' sprite sheet, drawn pixel by pixel at start-up: 16 people (slim, average, broad,
/// kids; tees, jackets, hoodies, scarves; short and long hair, curls, caps, beanies, bald
/// heads) in 12 poses (standing about, arms up, the V, clapping, a scarf held up, hands on
/// the head, a card held up for the tifo, a fist pump, pointing). Each texel is a region, not a
/// colour (R: skin, hair, shirt, jacket, legs, shoes, hat, scarf, card, eyes) plus a shade (G),
/// so crowd.gdshader dresses every fan in his own colours from one 192 x 720 texture.
/// </summary>
public static class CrowdSprites
{
    public const int W = 16, H = 45, Poses = 12, Rows = 16;
    public enum R : byte { None, Skin, Hair, Shirt, Jacket, Legs, Shoes, Hat, Scarf, Card, Eyes }
    enum Pose { Idle, IdleB, Up, UpV, ClapOpen, ClapShut, ScarfA, ScarfB, OnHead, Card, Fist, Point }

    // Build: 0 slim, 1 average, 2 broad, 3 kid. Top: 0 tee, 1 jacket, 2 hoodie, 3 scarf worn.
    // Head: 0 short hair, 1 long hair, 2 cap, 3 beanie, 4 bald, 5 curls.
    static readonly (int build, int top, int head)[] People =
    {
        (1, 0, 0), (0, 0, 1), (2, 1, 0), (1, 2, 4), (1, 3, 3), (0, 1, 2), (2, 0, 4), (1, 0, 5),
        (0, 3, 1), (1, 1, 3), (2, 2, 2), (1, 0, 2), (0, 0, 0), (2, 3, 5), (3, 0, 0), (3, 3, 2),
    };

    static ImageTexture _tex;

    public static ImageTexture Texture()
    {
        if (_tex != null) return _tex;
        var img = Image.CreateEmpty(W * Poses, H * Rows, false, Image.Format.Rg8);
        for (int row = 0; row < Rows; row++)
            for (int pose = 0; pose < Poses; pose++)
                new Sheet(img, pose * W, row * H).Person(People[row], (Pose)pose);
        return _tex = ImageTexture.CreateFromImage(img);
    }

    /// <summary>One cell: x 0..15 left to right (the figure's right side on the left: he faces
    /// us), u 0..44 up from his feet.</summary>
    sealed class Sheet
    {
        readonly Image _img;
        readonly int _x0, _y0;

        public Sheet(Image img, int x0, int y0) => (_img, _x0, _y0) = (img, x0, y0);

        void Px(int x, int u, R r, float shade = 1)
        {
            if (x < 0 || x >= W || u < 0 || u >= H) return;
            _img.SetPixel(_x0 + x, _y0 + H - 1 - u, new Color((byte)r / 255f, Mathf.Clamp(shade * 0.5f, 0, 1), 0));
        }

        R At(int x, int u) => x < 0 || x >= W || u < 0 || u >= H ? R.None : (R)(byte)Mathf.RoundToInt(_img.GetPixel(_x0 + x, _y0 + H - 1 - u).R * 255);

        void Block(int x0, int x1, int u0, int u1, R r, float shade = 1)
        {
            for (int x = x0; x <= x1; x++)
                for (int u = u0; u <= u1; u++) Px(x, u, r, shade);
        }

        public void Person((int build, int top, int head) p, Pose pose)
        {
            bool kid = p.build == 3;
            int tw = p.build switch { 0 => 3, 2 => 5, _ => kid ? 3 : 4 };
            int legLen = kid ? 12 : 17, torsoH = kid ? 9 : 12;
            int xl = 8 - tw, xr = 7 + tw;
            int top = legLen + torsoH - 1;          // shoulder row
            int headU = top + 2;                     // bottom row of the head
            const int HeadH = 7;
            bool longSleeves = p.top is 1 or 2;
            R torsoR = R.Shirt;

            // Legs (jeans or dark trousers), the inner columns in shade, shoes.
            int lw = kid ? 2 : p.build == 2 ? 4 : 3;
            for (int s = 0; s < 2; s++)
            {
                int a = s == 0 ? 8 - lw : 8, b = s == 0 ? 7 : 7 + lw;
                Block(a, b, 1, legLen - 1, R.Legs);
                for (int u = 1; u < legLen; u++) Px(s == 0 ? b : a, u, R.Legs, 0.8f);
                Block(a, b, 0, 0, R.Shoes);
            }
            // Torso: round shoulders, lit from the left, shaded on the right.
            for (int u = legLen; u <= top; u++)
                for (int x = xl; x <= xr; x++)
                {
                    if (u == top && (x == xl || x == xr)) continue;
                    float sh = x == xl ? 1.15f : x == xr ? 0.78f : 1f;
                    if (u == legLen) sh *= 0.85f; // the hem
                    R r = torsoR;
                    if (p.top == 1 && (x <= xl + 1 || x >= xr - 1)) r = R.Jacket;      // an open jacket
                    Px(x, u, r, sh);
                }
            if (p.top == 2)
            {
                // Hoodie: the pocket and the hood bunched behind the neck.
                Block(xl + 2, xr - 2, legLen + 2, legLen + 3, R.Shirt, 0.82f);
                Block(6, 9, top, top + 1, R.Jacket, 0.9f);
            }
            // Neck and head: round, a little big (it reads at this size), two eyes.
            Block(7, 8, top + 1, top + 1, R.Skin, 0.85f);
            for (int u = headU; u < headU + HeadH; u++)
                for (int x = 5; x <= 10; x++)
                {
                    bool corner = (u == headU || u == headU + HeadH - 1) && (x == 5 || x == 10);
                    if (!corner) Px(x, u, R.Skin, x == 5 ? 1.08f : x == 10 ? 0.82f : 1f);
                }
            int eyeU = headU + 3;
            Px(6, eyeU, R.Eyes);
            Px(9, eyeU, R.Eyes);
            int crown = headU + HeadH - 1;
            switch (p.head)
            {
                case 0: // short hair
                    Block(6, 9, crown, crown, R.Hair);
                    Block(5, 10, crown - 1, crown - 1, R.Hair);
                    Px(5, crown - 2, R.Hair, 0.9f);
                    Px(10, crown - 2, R.Hair, 0.8f);
                    break;
                case 1: // long hair, down past the shoulders
                    Block(6, 9, crown, crown, R.Hair);
                    Block(5, 10, crown - 1, crown - 1, R.Hair);
                    Block(4, 5, top - 2, crown - 2, R.Hair, 0.9f);
                    Block(10, 11, top - 2, crown - 2, R.Hair, 0.8f);
                    break;
                case 2: // cap, the brim to one side
                    Block(6, 9, crown + 1, crown + 1, R.Hat, 1.1f);
                    Block(5, 10, crown - 1, crown, R.Hat);
                    Block(3, 6, crown - 2, crown - 2, R.Hat, 0.75f);
                    break;
                case 3: // beanie with a bobble
                    Block(5, 10, crown - 2, crown, R.Hat);
                    Block(6, 9, crown + 1, crown + 1, R.Hat, 1.1f);
                    Block(5, 10, crown - 2, crown - 2, R.Hat, 0.8f);
                    Px(7, crown + 2, R.Hat, 1.3f);
                    Px(8, crown + 2, R.Hat, 1.3f);
                    break;
                case 4: // bald: a shine
                    Px(6, crown, R.Skin, 1.25f);
                    break;
                case 5: // curls
                    Block(5, 10, crown - 1, crown + 1, R.Hair);
                    Block(4, 11, crown - 2, crown, R.Hair, 0.9f);
                    Px(4, crown + 1, R.None);
                    Px(11, crown + 1, R.None);
                    Px(7, crown + 1, R.Hair, 1.2f);
                    break;
            }
            if (p.top == 3)
            {
                // A scarf round the neck, one end hanging down the front.
                Block(xl + 1, xr - 1, top, top, R.Scarf);
                Block(9, 10, top - 6, top - 1, R.Scarf);
                Block(9, 10, top - 7, top - 7, R.Scarf, 0.7f); // fringe
            }

            // Arms: the pose's path from each shoulder (left of the sheet first), 2 px thick.
            R sleeve = longSleeves ? (p.top == 1 ? R.Jacket : R.Shirt) : R.Shirt;
            int sleeveLen = longSleeves ? 99 : 4;
            int headMid = headU + 3;
            for (int s = 0; s < 2; s++)
            {
                int sx = s == 0 ? xl - 2 : xr + 1;     // outer column of the arm at the shoulder
                int o = s == 0 ? -1 : 1;              // outward
                var pts = new List<Vector2I> { new(sx, top) };
                Pose ps = pose;
                if (ps == Pose.Fist && s == 1 || ps == Pose.Point && s == 1) ps = Pose.Idle;
                switch (ps)
                {
                    case Pose.Idle: pts.Add(new(sx, top - 11)); break;
                    case Pose.IdleB: pts.Add(new(sx, top - 6)); pts.Add(new(sx - o, top - 10)); break;
                    case Pose.Up: case Pose.Card: pts.Add(new(sx + o * 0, top + 12)); break;
                    case Pose.UpV: case Pose.Fist: pts.Add(new(sx + o * 4, top + 11)); break;
                    case Pose.ClapOpen: pts.Add(new(sx, top - 5)); pts.Add(new(s == 0 ? 5 : 9, top - 3)); break;
                    case Pose.ClapShut: pts.Add(new(sx, top - 5)); pts.Add(new(s == 0 ? 6 : 8, top - 2)); break;
                    case Pose.ScarfA: pts.Add(new(sx + o * 3, top + 11)); break;
                    case Pose.ScarfB: pts.Add(new(sx + o * 2, top + 13)); break;
                    case Pose.OnHead: pts.Add(new(sx + o * 1, top + 5)); pts.Add(new(s == 0 ? 4 : 10, headMid + 2)); break;
                    case Pose.Point: pts.Add(new(sx + o * 5, top + 1)); break;
                }
                Arm(pts, -o, sleeve, sleeveLen);
                if (pose == Pose.Fist && s == 0)
                {
                    var e = pts[^1];
                    Block(Math.Min(e.X, e.X - o), Math.Max(e.X, e.X - o), e.Y, e.Y + 1, R.Skin, 1.1f);
                }
            }
            if (pose is Pose.ScarfA or Pose.ScarfB)
            {
                // The scarf stretched between the hands overhead, its fringes hanging.
                int u = pose == Pose.ScarfA ? top + 11 : top + 13;
                int a = pose == Pose.ScarfA ? xl - 3 : xl - 2, b = 15 - a;
                Block(a, b, u - 1, u, R.Scarf);
                Px(a, u - 2, R.Scarf, 0.7f);
                Px(b, u - 2, R.Scarf, 0.7f);
            }
            if (pose == Pose.Card) Block(3, 12, top + 11, Math.Min(H - 1, top + 17), R.Card);
        }

        /// <summary>An arm along `pts`, 2 px thick (the second pixel toward `inward`): sleeve for
        /// the first `sleeveLen` pixels, then the forearm, the last two the hand.</summary>
        void Arm(List<Vector2I> pts, int inward, R sleeve, int sleeveLen)
        {
            var path = new List<Vector2I>();
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                int n = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
                for (int k = i == 1 ? 0 : 1; k <= n; k++)
                    path.Add(new Vector2I(a.X + (int)MathF.Round((b.X - a.X) * k / (float)n), a.Y + (int)MathF.Round((b.Y - a.Y) * k / (float)n)));
            }
            for (int i = 0; i < path.Count; i++)
            {
                var p = path[i];
                R r = i >= path.Count - 2 ? R.Skin : i < sleeveLen ? sleeve : R.Skin;
                float sh = r == R.Skin && i >= path.Count - 2 ? 1.05f : 0.92f;
                Px(p.X, p.Y, r, sh * 1.06f);
                Px(p.X + inward, p.Y, r, sh * 0.9f);
            }
        }
    }
}

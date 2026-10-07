using System;
using System.IO;
using System.IO.Compression;
using GameNight.Sim;

namespace GameNight.Net;

/// <summary>
/// A match frame (MatchSnapshot) as bytes, for the online friend's screen: positions, the ball
/// and anything that adds up over a match in full floats, the animation's details in half
/// floats, then deflated (about 2 KB a frame). One <see cref="Pass"/> both writes and reads,
/// so the two can't drift apart.
/// </summary>
public sealed class FrameCodec
{
    readonly MemoryStream _raw = new(8192);
    BinaryWriter _w;
    BinaryReader _r;

    /// <summary>The frame as a message: `head` (the message type and flags) then the deflated body.</summary>
    public byte[] Encode(MatchSnapshot s, byte type, byte flags)
    {
        _raw.SetLength(0);
        _w = new BinaryWriter(_raw);
        _r = null;
        Pass(s);
        _w.Flush();
        var o = new MemoryStream(4096);
        o.WriteByte(type);
        o.WriteByte(flags);
        using (var z = new DeflateStream(o, CompressionLevel.Fastest, true)) z.Write(_raw.GetBuffer(), 0, (int)_raw.Length);
        return o.ToArray();
    }

    /// <summary>Reads a frame message (as Encode made it) into `s`; false if it's damaged.</summary>
    public bool Decode(byte[] msg, MatchSnapshot s)
    {
        try
        {
            _raw.SetLength(0);
            using (var z = new DeflateStream(new MemoryStream(msg, 2, msg.Length - 2), CompressionMode.Decompress)) z.CopyTo(_raw);
            _raw.Position = 0;
            _r = new BinaryReader(_raw);
            _w = null;
            Pass(s);
            return true;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            return false;
        }
    }

    void Pass(MatchSnapshot s)
    {
        B(s.Active);
        F(s.X); F(s.Y); F(s.Z); F(s.VX); F(s.VZ); F(s.Facing); H(s.Speed);
        B(s.HasLook); F(s.LookX); F(s.LookZ);
        F(s.StridePhase); H(s.LeanFwd); H(s.LeanSide);
        E(s.Action);
        H(s.ActionT); H(s.ActionDur); H(s.ActionDirX); H(s.ActionDirZ);
        Sb(s.KickLeg);
        E(s.KickType);
        H(s.KickContact); H(s.KickStretch); H(s.KickBallF); H(s.KickBallL);
        B(s.KickLofted); B(s.ThrowIn);
        H(s.SlideStop); H(s.LegX); H(s.LegZ); H(s.CatchY); H(s.CatchF); H(s.CatchL); H(s.DiveFly);
        H(s.DiveRoll); H(s.DiveLift);
        H(s.AccelFwd); F(s.SinceTouch); H(s.TouchH);
        H(s.PullX); H(s.PullZ); F(s.PullT);
        H(s.KickPower); H(s.KickHeight); H(s.KickRel); H(s.SlideV0);
        B(s.Sprinting); H(s.Stamina);
        E(s.Trick); Sb(s.TrickSide);
        Y(s.Cards); Y(s.Team); Y(s.Index); Y(s.Number);
        E(s.Role);
        Sb(s.Foot);
        H(s.Height); H(s.Build);
        Y(s.Skin); Y(s.Hair); Y(s.HairStyle);

        F(ref s.BallX); F(ref s.BallY); F(ref s.BallZ); F(ref s.BallVX); F(ref s.BallVY); F(ref s.BallVZ);
        F(ref s.BallSX); F(ref s.BallSY); F(ref s.BallSZ);
        B(ref s.BallOnGround); B(ref s.BallInGoal);
        D(ref s.Time);
        s.Phase = (Phase)I((int)s.Phase);
        F(ref s.PhaseT);
        I(ref s.Half); I(ref s.Minute);
        if (_w != null) _w.Write(s.ClockLabel ?? "");
        else s.ClockLabel = _r.ReadString();
        I(ref s.Score[0]); I(ref s.Score[1]); I(ref s.Dir[0]); I(ref s.Dir[1]);
        I(ref s.Controlled); I(ref s.Owner); I(ref s.HeldBy); I(ref s.PassTarget); I(ref s.Scorer);
        I(ref s.PossTeam); B(ref s.HumanAttacking);
        B(ref s.Versus); I(ref s.Controlled2); B(ref s.HumanAttacking2);
        B(ref s.CelebrationOpen); B(ref s.HumanScored); B(ref s.KeeperButtons);
        int sp = I(s.SetPiece.HasValue ? (int)s.SetPiece.Value : -1);
        s.SetPiece = sp < 0 ? null : (SetPieceKind)sp;
        I(ref s.SetPieceTeam); I(ref s.SetPieceTaker); F(ref s.SetPieceX); F(ref s.SetPieceZ);
        B(ref s.SetPieceDirect); B(ref s.HasSetPieceTarget); F(ref s.SetPieceTargetX); F(ref s.SetPieceTargetZ);
        I(ref s.DeadBallTaker); F(ref s.DeadBallX); F(ref s.DeadBallZ);
        s.DeadBallKind = (SetPieceKind)I((int)s.DeadBallKind);
        B(ref s.AimingCorner); B(ref s.AimingGoalKick); B(ref s.AimingShot);
        B(ref s.HasAimPoint); F(ref s.AimX); F(ref s.AimY); F(ref s.AimZ);
        B(ref s.HasArc); I(ref s.ArcCount);
        if (s.HasArc)
        {
            F(s.ArcX); F(s.ArcY); F(s.ArcZ);
        }
        F(ref s.ArcRingX); F(ref s.ArcRingZ);
        int cel = I(s.Celebration.HasValue ? (int)s.Celebration.Value : -1);
        s.Celebration = cel < 0 ? null : (CelebrationKind)cel;
        F(ref s.CelebrationAt); F(ref s.CelebrationTurn); F(ref s.Excitement);
        F(ref s.InvaderT); I(ref s.InvaderSeed);
        I(ref s.ShotTeam); I(ref s.AttackingTeam); I(ref s.LastTouchTeam); B(ref s.PenaltyPending);

        // This frame's events (everything since the last frame sent, folded together).
        I(ref s.KickCount); F(ref s.KickMax);
        I(ref s.Whistle); I(ref s.Goal); I(ref s.Foul); I(ref s.Card); I(ref s.Offside); I(ref s.Sub);
        F(ref s.Post); F(ref s.Net); F(ref s.Bounce); F(ref s.Save); F(ref s.Tackle); F(ref s.Skill);
    }

    void F(float[] a)
    {
        for (int i = 0; i < a.Length; i++)
            if (_w != null) _w.Write(a[i]);
            else a[i] = _r.ReadSingle();
    }

    void H(float[] a)
    {
        for (int i = 0; i < a.Length; i++)
            if (_w != null) _w.Write((Half)a[i]);
            else a[i] = (float)_r.ReadHalf();
    }

    void B(bool[] a)
    {
        for (int i = 0; i < a.Length; i++)
            if (_w != null) _w.Write(a[i]);
            else a[i] = _r.ReadBoolean();
    }

    void Y(byte[] a)
    {
        if (_w != null) _w.Write(a);
        else _r.Read(a, 0, a.Length);
    }

    void Sb(sbyte[] a)
    {
        for (int i = 0; i < a.Length; i++)
            if (_w != null) _w.Write(a[i]);
            else a[i] = _r.ReadSByte();
    }

    void E<T>(T[] a) where T : struct, Enum
    {
        for (int i = 0; i < a.Length; i++)
            if (_w != null) _w.Write((byte)Convert.ToInt32(a[i]));
            else a[i] = (T)Enum.ToObject(typeof(T), _r.ReadByte());
    }

    void F(ref float v)
    {
        if (_w != null) _w.Write(v);
        else v = _r.ReadSingle();
    }

    void D(ref double v)
    {
        if (_w != null) _w.Write(v);
        else v = _r.ReadDouble();
    }

    void B(ref bool v)
    {
        if (_w != null) _w.Write(v);
        else v = _r.ReadBoolean();
    }

    void I(ref int v)
    {
        if (_w != null) _w.Write(v);
        else v = _r.ReadInt32();
    }

    int I(int v)
    {
        I(ref v);
        return v;
    }
}

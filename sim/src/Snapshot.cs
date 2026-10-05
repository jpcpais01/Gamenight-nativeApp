using System;

namespace GameNight.Sim;

/// <summary>
/// The match at one instant as flat arrays: everything a renderer, HUD or sound needs, and
/// nothing it could mutate. Write one after each step (Match.Write), keep the previous one and
/// interpolate. Indexed by player id (0..10 home, 11..21 away); `Active` says who's taking
/// part (training drills field fewer). Floats for drawing; the sim itself stays in doubles.
/// </summary>
public sealed class MatchSnapshot
{
    public const int N = 22;

    // ---- players
    public readonly bool[] Active = new bool[N];
    public readonly float[] X = new float[N], Y = new float[N], Z = new float[N];
    public readonly float[] VX = new float[N], VZ = new float[N];
    /// <summary>Body direction, radians (atan2 of the facing vector), and ground speed (m/s).</summary>
    public readonly float[] Facing = new float[N], Speed = new float[N];
    /// <summary>Where the head looks, when it looks somewhere (HasLook).</summary>
    public readonly bool[] HasLook = new bool[N];
    public readonly float[] LookX = new float[N], LookZ = new float[N];
    /// <summary>Animation: run cycle phase and body lean (set by Player.Move).</summary>
    public readonly float[] StridePhase = new float[N], LeanFwd = new float[N], LeanSide = new float[N];
    public readonly ActionKind[] Action = new ActionKind[N];
    public readonly float[] ActionT = new float[N], ActionDur = new float[N], ActionDirX = new float[N], ActionDirZ = new float[N];
    /// <summary>The strike being animated: leg (+1 right, -1 left), type, contact time, reach and where the ball sits (body frame).</summary>
    public readonly sbyte[] KickLeg = new sbyte[N];
    public readonly KickType[] KickType = new KickType[N];
    public readonly float[] KickContact = new float[N], KickStretch = new float[N], KickBallF = new float[N], KickBallL = new float[N];
    public readonly bool[] KickLofted = new bool[N], ThrowIn = new bool[N];
    /// <summary>Slide: when the grass stops it; tackle leg direction; keeper: catch height and dive pose.</summary>
    public readonly float[] SlideStop = new float[N], LegX = new float[N], LegZ = new float[N], CatchY = new float[N], CatchF = new float[N], CatchL = new float[N], DiveFly = new float[N];
    public readonly float[] DiveRoll = new float[N], DiveLift = new float[N];
    /// <summary>For the skeleton: forward acceleration, time since and height of the last touch,
    /// the last shirt pull (direction and match time, -1 = none), the strike's power, height and
    /// release point, and the slide's starting speed.</summary>
    public readonly float[] AccelFwd = new float[N], SinceTouch = new float[N], TouchH = new float[N];
    public readonly float[] PullX = new float[N], PullZ = new float[N], PullT = new float[N];
    public readonly float[] KickPower = new float[N], KickHeight = new float[N], KickRel = new float[N], SlideV0 = new float[N];
    public readonly bool[] Sprinting = new bool[N];
    public readonly float[] Stamina = new float[N];
    public readonly byte[] Cards = new byte[N];
    // Who they are (fixed for the match, copied every time for simplicity).
    public readonly byte[] Team = new byte[N], Index = new byte[N], Number = new byte[N];
    public readonly Role[] Role = new Role[N];
    public readonly sbyte[] Foot = new sbyte[N];
    /// <summary>Look.Height and Look.Build: body scale factors (about 0.95-1.1), not metres.</summary>
    public readonly float[] Height = new float[N], Build = new float[N];
    /// <summary>Skin and hair: indices into TeamData.SkinTones / HairColors (0 if a line-up brings its own colour).</summary>
    public readonly byte[] Skin = new byte[N], Hair = new byte[N], HairStyle = new byte[N];

    // ---- ball
    public float BallX, BallY, BallZ, BallVX, BallVY, BallVZ, BallSX, BallSY, BallSZ;
    public bool BallOnGround, BallInGoal;

    // ---- match
    public double Time;
    public Phase Phase;
    public float PhaseT;
    public int Half;
    /// <summary>Game minute as the TV clock shows it, and its label (23', 45+2').</summary>
    public int Minute;
    public string ClockLabel = "";
    public readonly int[] Score = new int[2];
    /// <summary>+1 attacks toward +x, -1 toward -x.</summary>
    public readonly int[] Dir = new int[2];
    /// <summary>Player ids, -1 for none.</summary>
    public int Controlled = -1, Owner = -1, HeldBy = -1, PassTarget = -1, Scorer = -1;
    public int PossTeam;
    public bool HumanAttacking;
    /// <summary>For the buttons: the celebration is the human's to pick (Match.CelebrationOpen), the human
    /// just scored (and isn't on autoplay), the human's keeper is in goal without the ball (training in goal).</summary>
    public bool CelebrationOpen, HumanScored, KeeperButtons;
    /// <summary>Dead ball: its kind (null when none), team, spot and taker.</summary>
    public SetPieceKind? SetPiece;
    public int SetPieceTeam, SetPieceTaker = -1;
    public float SetPieceX, SetPieceZ;
    public bool SetPieceDirect;
    /// <summary>The delivery ring of an aimed corner or goal kick (SetPiece.Target).</summary>
    public bool HasSetPieceTarget;
    public float SetPieceTargetX, SetPieceTargetZ;
    /// <summary>The dead-ball camera (Match.DeadBallView): taker id (-1 when none), the point it frames, the kind.</summary>
    public int DeadBallTaker = -1;
    public float DeadBallX, DeadBallZ;
    public SetPieceKind DeadBallKind;
    /// <summary>The human is aiming a corner / goal kick / dead-ball shot (Match.AimingCorner, ...).</summary>
    public bool AimingCorner, AimingGoalKick, AimingShot;
    /// <summary>The dead-ball shot's aim point on the goal mouth (Match.AimPoint()).</summary>
    public bool HasAimPoint;
    public float AimX, AimY, AimZ;
    /// <summary>The aimed delivery's dotted flight (DeliveryPreview): ArcCount points from the foot to where
    /// it comes down, to draw faint to strong, and the ring where it lands.</summary>
    public bool HasArc;
    public int ArcCount;
    public readonly float[] ArcX = new float[DeliveryPreview.Dots], ArcY = new float[DeliveryPreview.Dots], ArcZ = new float[DeliveryPreview.Dots];
    public float ArcRingX, ArcRingZ;
    public CelebrationKind? Celebration;
    public float CelebrationAt, CelebrationTurn;
    public float Excitement;
    /// <summary>A pitch invader is on: seconds since the whistle for him (-1: none), and his seed.</summary>
    public float InvaderT = -1;
    public int InvaderSeed;
    /// <summary>For the crowd and sound: who shot (-1 = no shot on), who attacks, who touched last, a penalty given or being taken.</summary>
    public int ShotTeam = -1, AttackingTeam = -1, LastTouchTeam = -1;
    public bool PenaltyPending;

    // ---- this step's events (sound, camera, HUD)
    public int KickCount;
    /// <summary>Strongest kick this step, 0..1.</summary>
    public float KickMax;
    public int Whistle, Goal = -1, Foul, Card, Offside;
    public float Post, Net, Bounce, Save, Tackle;

    public void CopyFrom(MatchSnapshot o)
    {
        Array.Copy(o.Active, Active, N);
        Array.Copy(o.X, X, N); Array.Copy(o.Y, Y, N); Array.Copy(o.Z, Z, N);
        Array.Copy(o.VX, VX, N); Array.Copy(o.VZ, VZ, N);
        Array.Copy(o.Facing, Facing, N); Array.Copy(o.Speed, Speed, N);
        Array.Copy(o.HasLook, HasLook, N); Array.Copy(o.LookX, LookX, N); Array.Copy(o.LookZ, LookZ, N);
        Array.Copy(o.StridePhase, StridePhase, N); Array.Copy(o.LeanFwd, LeanFwd, N); Array.Copy(o.LeanSide, LeanSide, N);
        Array.Copy(o.Action, Action, N); Array.Copy(o.ActionT, ActionT, N); Array.Copy(o.ActionDur, ActionDur, N);
        Array.Copy(o.ActionDirX, ActionDirX, N); Array.Copy(o.ActionDirZ, ActionDirZ, N);
        Array.Copy(o.KickLeg, KickLeg, N); Array.Copy(o.KickType, KickType, N); Array.Copy(o.KickContact, KickContact, N);
        Array.Copy(o.KickStretch, KickStretch, N); Array.Copy(o.KickBallF, KickBallF, N); Array.Copy(o.KickBallL, KickBallL, N);
        Array.Copy(o.KickLofted, KickLofted, N); Array.Copy(o.ThrowIn, ThrowIn, N);
        Array.Copy(o.SlideStop, SlideStop, N); Array.Copy(o.LegX, LegX, N); Array.Copy(o.LegZ, LegZ, N); Array.Copy(o.CatchY, CatchY, N); Array.Copy(o.CatchF, CatchF, N); Array.Copy(o.CatchL, CatchL, N); Array.Copy(o.DiveFly, DiveFly, N);
        Array.Copy(o.DiveRoll, DiveRoll, N); Array.Copy(o.DiveLift, DiveLift, N);
        Array.Copy(o.AccelFwd, AccelFwd, N); Array.Copy(o.SinceTouch, SinceTouch, N); Array.Copy(o.TouchH, TouchH, N);
        Array.Copy(o.PullX, PullX, N); Array.Copy(o.PullZ, PullZ, N); Array.Copy(o.PullT, PullT, N);
        Array.Copy(o.KickPower, KickPower, N); Array.Copy(o.KickHeight, KickHeight, N); Array.Copy(o.KickRel, KickRel, N); Array.Copy(o.SlideV0, SlideV0, N);
        Array.Copy(o.Sprinting, Sprinting, N); Array.Copy(o.Stamina, Stamina, N); Array.Copy(o.Cards, Cards, N);
        Array.Copy(o.Team, Team, N); Array.Copy(o.Index, Index, N); Array.Copy(o.Number, Number, N);
        Array.Copy(o.Role, Role, N); Array.Copy(o.Foot, Foot, N);
        Array.Copy(o.Height, Height, N); Array.Copy(o.Build, Build, N);
        Array.Copy(o.Skin, Skin, N); Array.Copy(o.Hair, Hair, N); Array.Copy(o.HairStyle, HairStyle, N);
        BallX = o.BallX; BallY = o.BallY; BallZ = o.BallZ;
        BallVX = o.BallVX; BallVY = o.BallVY; BallVZ = o.BallVZ;
        BallSX = o.BallSX; BallSY = o.BallSY; BallSZ = o.BallSZ;
        BallOnGround = o.BallOnGround; BallInGoal = o.BallInGoal;
        Time = o.Time; Phase = o.Phase; PhaseT = o.PhaseT; Half = o.Half; Minute = o.Minute; ClockLabel = o.ClockLabel;
        Score[0] = o.Score[0]; Score[1] = o.Score[1];
        Dir[0] = o.Dir[0]; Dir[1] = o.Dir[1];
        Controlled = o.Controlled; Owner = o.Owner; HeldBy = o.HeldBy; PassTarget = o.PassTarget; Scorer = o.Scorer;
        PossTeam = o.PossTeam; HumanAttacking = o.HumanAttacking;
        CelebrationOpen = o.CelebrationOpen; HumanScored = o.HumanScored; KeeperButtons = o.KeeperButtons;
        SetPiece = o.SetPiece; SetPieceTeam = o.SetPieceTeam; SetPieceTaker = o.SetPieceTaker; SetPieceX = o.SetPieceX; SetPieceZ = o.SetPieceZ;
        SetPieceDirect = o.SetPieceDirect; HasSetPieceTarget = o.HasSetPieceTarget; SetPieceTargetX = o.SetPieceTargetX; SetPieceTargetZ = o.SetPieceTargetZ;
        DeadBallTaker = o.DeadBallTaker; DeadBallX = o.DeadBallX; DeadBallZ = o.DeadBallZ; DeadBallKind = o.DeadBallKind;
        AimingCorner = o.AimingCorner; AimingGoalKick = o.AimingGoalKick; AimingShot = o.AimingShot;
        HasAimPoint = o.HasAimPoint; AimX = o.AimX; AimY = o.AimY; AimZ = o.AimZ;
        HasArc = o.HasArc; ArcCount = o.ArcCount; ArcRingX = o.ArcRingX; ArcRingZ = o.ArcRingZ;
        Array.Copy(o.ArcX, ArcX, ArcX.Length); Array.Copy(o.ArcY, ArcY, ArcY.Length); Array.Copy(o.ArcZ, ArcZ, ArcZ.Length);
        Celebration = o.Celebration; CelebrationAt = o.CelebrationAt; CelebrationTurn = o.CelebrationTurn; Excitement = o.Excitement;
        ShotTeam = o.ShotTeam; AttackingTeam = o.AttackingTeam; LastTouchTeam = o.LastTouchTeam; PenaltyPending = o.PenaltyPending;
        KickCount = o.KickCount; KickMax = o.KickMax;
        InvaderT = o.InvaderT; InvaderSeed = o.InvaderSeed;
        Whistle = o.Whistle; Goal = o.Goal; Foul = o.Foul; Card = o.Card; Offside = o.Offside;
        Post = o.Post; Net = o.Net; Bounce = o.Bounce; Save = o.Save; Tackle = o.Tackle;
    }

    /// <summary>Fold another step's events into these (a frame that spans several steps keeps them all).</summary>
    public void AddEvents(MatchEvents e)
    {
        KickCount += e.Kicks.Count;
        foreach (double k in e.Kicks) KickMax = Math.Max(KickMax, (float)k);
        Whistle = Math.Max(Whistle, e.Whistle);
        if (e.Goal >= 0) Goal = e.Goal;
        Foul = Math.Max(Foul, e.Foul);
        Card = Math.Max(Card, e.Card);
        Offside = Math.Max(Offside, e.Offside);
        Post = Math.Max(Post, (float)e.Post);
        Net = Math.Max(Net, (float)e.Net);
        Bounce = Math.Max(Bounce, (float)e.Bounce);
        Save = Math.Max(Save, (float)e.Save);
        Tackle = Math.Max(Tackle, (float)e.Tackle);
    }

    public void ClearEvents()
    {
        KickCount = 0;
        KickMax = 0;
        Whistle = Foul = Card = Offside = 0;
        Goal = -1;
        Post = Net = Bounce = Save = Tackle = 0;
    }
}

public sealed partial class Match
{
    /// <summary>Writes the current state into `s` (events are left alone: see MatchSnapshot.AddEvents).</summary>
    public void Write(MatchSnapshot s)
    {
        Array.Clear(s.Active);
        foreach (var p in Players) s.Active[p.Id] = true;
        foreach (var p in All)
        {
            int i = p.Id;
            s.X[i] = (float)p.Pos.X;
            s.Y[i] = (float)p.Pos.Y;
            s.Z[i] = (float)p.Pos.Z;
            s.VX[i] = (float)p.Vel.X;
            s.VZ[i] = (float)p.Vel.Z;
            s.Facing[i] = (float)p.Facing;
            s.Speed[i] = (float)p.Speed;
            s.HasLook[i] = p.LookAt != null;
            s.LookX[i] = p.LookAt != null ? (float)p.LookAt.X : 0;
            s.LookZ[i] = p.LookAt != null ? (float)p.LookAt.Z : 0;
            s.StridePhase[i] = (float)p.StridePhase;
            s.LeanFwd[i] = (float)p.LeanFwd;
            s.LeanSide[i] = (float)p.LeanSide;
            s.Action[i] = p.Action;
            s.ActionT[i] = (float)p.ActionT;
            s.ActionDur[i] = (float)p.ActionDur;
            s.ActionDirX[i] = (float)p.ActionDirX;
            s.ActionDirZ[i] = (float)p.ActionDirZ;
            s.KickLeg[i] = (sbyte)p.KickLeg;
            s.KickType[i] = p.KickType;
            s.KickContact[i] = (float)p.KickContact;
            s.KickStretch[i] = (float)p.KickStretch;
            s.KickBallF[i] = (float)p.KickBallF;
            s.KickBallL[i] = (float)p.KickBallL;
            s.KickLofted[i] = p.KickLofted;
            s.ThrowIn[i] = p.ThrowIn;
            s.AccelFwd[i] = (float)p.AccelFwd;
            s.SinceTouch[i] = (float)p.SinceTouch;
            s.TouchH[i] = (float)p.TouchH;
            s.PullX[i] = (float)p.PullX;
            s.PullZ[i] = (float)p.PullZ;
            s.PullT[i] = (float)p.PullT;
            s.KickPower[i] = (float)p.KickPower;
            s.KickHeight[i] = (float)p.KickHeight;
            s.KickRel[i] = (float)p.KickRel;
            s.SlideV0[i] = (float)p.SlideV0;
            s.SlideStop[i] = (float)p.SlideStop;
            s.LegX[i] = (float)p.LegX;
            s.LegZ[i] = (float)p.LegZ;
            s.CatchY[i] = (float)p.CatchY;
            s.CatchF[i] = (float)p.CatchF;
            s.CatchL[i] = (float)p.CatchL;
            s.DiveFly[i] = (float)p.DiveFly;
            s.DiveRoll[i] = (float)AI.DiveRoll[i];
            s.DiveLift[i] = (float)AI.DiveLift[i];
            s.Sprinting[i] = p.Sprinting;
            s.Stamina[i] = (float)p.Stamina;
            s.Cards[i] = (byte)Cards[i];
            s.Team[i] = (byte)p.Team;
            s.Index[i] = (byte)p.Index;
            s.Number[i] = (byte)p.Number;
            s.Role[i] = p.Role;
            s.Foot[i] = (sbyte)p.Foot;
            s.Height[i] = (float)p.Look.Height;
            s.Build[i] = (float)p.Look.Build;
            s.Skin[i] = (byte)Math.Max(0, Array.IndexOf(TeamData.SkinTones, p.Look.Skin));
            s.Hair[i] = (byte)Math.Max(0, Array.IndexOf(TeamData.HairColors, p.Look.Hair));
            s.HairStyle[i] = (byte)p.Look.HairStyle;
        }
        var b = Ball;
        s.BallX = (float)b.Pos.X;
        s.BallY = (float)b.Pos.Y;
        s.BallZ = (float)b.Pos.Z;
        s.BallVX = (float)b.Vel.X;
        s.BallVY = (float)b.Vel.Y;
        s.BallVZ = (float)b.Vel.Z;
        s.BallSX = (float)b.Spin.X;
        s.BallSY = (float)b.Spin.Y;
        s.BallSZ = (float)b.Spin.Z;
        s.BallOnGround = b.OnGround;
        s.BallInGoal = b.InGoal;
        s.Time = Time;
        s.Phase = Phase;
        s.PhaseT = (float)PhaseT;
        int minute = DisplayMinute;
        if (minute != s.Minute || Half != s.Half || s.ClockLabel.Length == 0) s.ClockLabel = ClockLabel;
        s.Half = Half;
        s.Minute = minute;
        s.Score[0] = Teams[0].Score;
        s.Score[1] = Teams[1].Score;
        s.Dir[0] = (int)Teams[0].Dir;
        s.Dir[1] = (int)Teams[1].Dir;
        s.Controlled = Controlled.Id;
        s.Owner = Owner?.Id ?? -1;
        s.HeldBy = HeldBy?.Id ?? -1;
        s.PassTarget = PassTarget?.Id ?? -1;
        s.Scorer = Scorer?.Id ?? -1;
        s.PossTeam = PossTeam;
        s.HumanAttacking = HumanAttacking();
        s.CelebrationOpen = CelebrationOpen;
        s.HumanScored = Phase == Phase.Goal && !AutoPlay && Scorer != null && Scorer.Team == HumanTeam;
        s.KeeperButtons = KeeperHuman && Controlled.Role == Role.GK && HeldBy != Controlled;
        s.SetPiece = SetPiece?.Kind;
        s.SetPieceTeam = SetPiece?.Team ?? -1;
        s.SetPieceTaker = SetPiece?.Taker.Id ?? -1;
        s.SetPieceX = (float)(SetPiece?.X ?? 0);
        s.SetPieceZ = (float)(SetPiece?.Z ?? 0);
        s.SetPieceDirect = SetPiece?.Direct ?? false;
        var target = SetPiece?.Target;
        s.HasSetPieceTarget = target != null;
        s.SetPieceTargetX = (float)(target?.X ?? 0);
        s.SetPieceTargetZ = (float)(target?.Z ?? 0);
        var view = DeadBallView;
        s.DeadBallTaker = view?.taker.Id ?? -1;
        s.DeadBallX = (float)(view?.x ?? 0);
        s.DeadBallZ = (float)(view?.z ?? 0);
        s.DeadBallKind = view?.kind ?? SetPieceKind.Kickoff;
        s.AimingCorner = AimingCorner;
        s.AimingGoalKick = AimingGoalKick;
        s.AimingShot = AimingShot;
        var aim = AimPoint();
        s.HasAimPoint = aim != null;
        s.AimX = (float)(aim?.x ?? 0);
        s.AimY = (float)(aim?.y ?? 0);
        s.AimZ = (float)(aim?.z ?? 0);
        s.Celebration = Celebration?.Kind;
        s.CelebrationAt = (float)(Celebration?.At ?? 0);
        s.CelebrationTurn = (float)(Celebration?.Turn ?? 0);
        s.Excitement = (float)Excitement;
        s.ShotTeam = ShotTeam();
        s.AttackingTeam = AttackingTeam();
        s.LastTouchTeam = LastTouch?.Team ?? -1;
        s.PenaltyPending = PenaltyPending;
        s.InvaderT = (float)InvaderT;
        s.InvaderSeed = InvaderSeed;
    }
}

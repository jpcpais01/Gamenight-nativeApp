using Godot;
using GameNight.Menus;

namespace GameNight.Account;

/// <summary>
/// The account box (the key by the settings cog): sign in or create an account with a username
/// and a password; signed in, who you are, when the save last reached the cloud, SYNC NOW and
/// SIGN OUT.
/// </summary>
public sealed partial class AccountModal : Modal
{
    readonly LineEdit _name, _pass;
    bool _create, _working;
    string _error;

    static Account A => Account.Instance;

    public AccountModal(Menus.Menus ui) : base(ui)
    {
        _name = Field("username", 16, false);
        _pass = Field("password", 64, true);
        _name.TextSubmitted += _ => _pass.GrabFocus();
        _pass.TextSubmitted += _ => Submit();
    }

    protected override Vector2 BoxSize => new(540, A?.SignedIn == true ? 300 : 380);

    LineEdit Field(string hint, int max, bool secret)
    {
        var e = new LineEdit
        {
            MaxLength = max, Secret = secret, PlaceholderText = hint, CaretBlink = true, ContextMenuEnabled = false, Visible = false,
            VirtualKeyboardType = secret ? LineEdit.VirtualKeyboardTypeEnum.Password : LineEdit.VirtualKeyboardTypeEnum.Default,
        };
        var box = new StyleBoxFlat
        {
            BgColor = new Color(8 / 255f, 6 / 255f, 26 / 255f, 0.9f),
            BorderColor = Px.Line2,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 2, ContentMarginBottom = 2,
        };
        box.SetBorderWidthAll(3);
        var focus = (StyleBoxFlat)box.Duplicate();
        focus.BorderColor = Px.Gold;
        e.AddThemeStyleboxOverride("normal", box);
        e.AddThemeStyleboxOverride("focus", focus);
        e.AddThemeFontOverride("font", Px.Big);
        e.AddThemeFontSizeOverride("font_size", 26);
        e.AddThemeColorOverride("font_color", Px.Ink);
        e.AddThemeColorOverride("font_placeholder_color", Px.InkDim);
        e.AddThemeColorOverride("caret_color", Px.Gold);
        AddChild(e);
        return e;
    }

    static void Place(LineEdit e, bool show, Rect2 r = default)
    {
        if (e.Visible != show) e.Visible = show;
        if (!show) return;
        e.Position = r.Position;
        e.Size = r.Size;
    }

    public override void Dismiss()
    {
        DisplayServer.VirtualKeyboardHide();
        base.Dismiss();
    }

    protected override void PaintBox(Rect2 b)
    {
        float x = b.Position.X + 24, w = b.Size.X - 48;
        Kicker(b.Position + new Vector2(24, 30), "GAMENIGHT ACCOUNT");
        if (A == null || !FirebaseConfig.Ready)
        {
            Place(_name, false);
            Place(_pass, false);
            Heading(b.Position + new Vector2(24, 64), "Accounts");
            Lines(x, b.Position.Y + 100, w, "Accounts aren't switched on in this version yet. Your club is saved on this device as always.", Px.InkDim);
            return;
        }
        if (A.SignedIn) SignedIn(b, x, w);
        else SignIn(b, x, w);
    }

    void SignedIn(Rect2 b, float x, float w)
    {
        Place(_name, false);
        Place(_pass, false);
        Heading(b.Position + new Vector2(24, 66), A.User);
        var (status, col) = A.Now switch
        {
            Account.State.Working => ("SAVING TO YOUR ACCOUNT" + new string('.', 1 + (int)(T * 3) % 3), Px.Cyan),
            Account.State.Offline => ("OFFLINE · SAVED ON THIS DEVICE, WILL SEND IT LATER", Px.Gold),
            _ => ("SAVED TO YOUR ACCOUNT · " + A.SyncedAgo, Px.Win),
        };
        float y = b.Position.Y + 96;
        DrawRect(new Rect2(x, y - 7, 8, 8), col);
        Px.Text(this, Px.Small, new Vector2(x + 16, y), Px.Fit(Px.Small, status, 8, w - 16), 8, col);
        Lines(x, y + 30, w, "Your club, your league and your career follow you: sign in with this username on any phone or PC and pick up where you left off.", Px.InkDim);
        float bw = (w - 16) / 2, by = b.End.Y - 66;
        GhostButton("sync", new Rect2(x, by, bw, 44), "SYNC NOW", 22, A.SyncNow);
        GhostButton("out", new Rect2(x + bw + 16, by, bw, 44), "SIGN OUT", 22, () => Ui.Open(new ConfirmModal(Ui, "Sign out?",
            "Your club stays on this device. Sign in again any time to keep it in step with your account.", "SIGN OUT", () =>
            {
                A.SignOut();
                Ui.Toast("Signed out");
            })), new Color(Px.Loss, 0.7f), Px.Loss);
    }

    void SignIn(Rect2 b, float x, float w)
    {
        Heading(b.Position + new Vector2(24, 66), _create ? "Create account" : "Sign in");
        // The two ways in.
        float tw = (w - 10) / 2, ty = b.Position.Y + 82;
        Tab("tin", new Rect2(x, ty, tw, 30), "SIGN IN", !_create, () => _create = false);
        Tab("tnew", new Rect2(x + tw + 10, ty, tw, 30), "CREATE ACCOUNT", _create, () => _create = true);

        float y = ty + 50;
        Px.Text(this, Px.Small, new Vector2(x, y), "USERNAME", 8, Px.Gold);
        Place(_name, true, new Rect2(x, y + 6, w, 40));
        y += 62;
        Px.Text(this, Px.Small, new Vector2(x, y), "PASSWORD", 8, Px.Gold);
        Place(_pass, true, new Rect2(x, y + 6, w, 40));
        y += 66;

        string note = _error ?? (_create
            ? "Your club on this device becomes your account's club. Passwords need 6+ characters."
            : "Your account's club comes to this device (the one here is backed up first).");
        Lines(x, y, w, note, _error != null ? Px.Loss : Px.InkDim);

        string label = _working ? "ONE MOMENT" + new string('.', 1 + (int)(T * 3) % 3) : _create ? "CREATE ACCOUNT" : "SIGN IN";
        GoldButton("go", new Rect2(b.GetCenter().X - 130, b.End.Y - 62, 260, 46), label, 26, Submit, !_working);
    }

    void Tab(string key, Rect2 r, string label, bool on, System.Action tap)
    {
        bool held = Held(key);
        var rr = held ? r.Translated(Vector2.One * 2) : r;
        Px.Frame(this, rr, on ? Px.Gold : new Color(16 / 255f, 14 / 255f, 44 / 255f, 0.85f), on ? Px.Hex(0xb37400) : Px.Line2, held ? null : Px.ShadowSoft, 2, 3);
        Px.TextC(this, Px.Small, rr.GetCenter().X, rr.GetCenter().Y + 4, label, 8, on ? Px.Dark : Px.Ink);
        Tap(key, r, () =>
        {
            tap();
            _error = null;
        });
    }

    void Lines(float x, float y, float w, string s, Color c)
    {
        foreach (var l in Px.Wrap(Px.Small, s, 8, w))
        {
            Px.Text(this, Px.Small, new Vector2(x, y), l, 8, c);
            y += 15;
        }
    }

    async void Submit()
    {
        if (_working || A == null) return;
        _working = true;
        _error = null;
        DisplayServer.VirtualKeyboardHide();
        bool create = _create;
        string err = await A.Enter(_name.Text, _pass.Text, create);
        if (!IsInstanceValid(this)) return;
        _working = false;
        _error = err;
        if (err != null) return;
        _pass.Text = "";
        Ui.Toast(create ? $"Welcome, {A.User}! Your club is saved to your account" : $"Signed in as {A.User}");
        Ui.Close(this);
    }
}

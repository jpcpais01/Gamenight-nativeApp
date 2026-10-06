using Godot;
using GameNight.Menus;

namespace GameNight.Link;

/// <summary>On the PC: how to make a phone the controller. A QR code for the web controller
/// (any phone's camera, an iPhone included), its address to type, and the app's PLAY ON PC.</summary>
public sealed partial class PhoneModal : Modal
{
    string _url;
    bool[,] _qr;

    public PhoneModal(Menus.Menus ui) : base(ui) { }

    protected override Vector2 BoxSize => new(620, 330);

    protected override void PaintBox(Rect2 box)
    {
        var host = Host.Instance;
        string url = host?.WebAddress ?? "";
        if (url != _url)
        {
            _url = url;
            _qr = url != "" ? Qr.Encode(url) : null;
        }
        float x = box.Position.X + 24, y = box.Position.Y + 28;
        Kicker(new Vector2(x, y), "Phone as controller");
        Heading(new Vector2(x, y + 34), "Play with your phone");

        // The code, on white with its quiet zone.
        float cell = 5, q = (Qr.Size + 8) * cell;
        var qr = new Rect2(x, y + 52, q, q);
        if (_qr != null)
        {
            DrawRect(qr, Colors.White);
            for (int r = 0; r < Qr.Size; r++)
                for (int c = 0; c < Qr.Size; c++)
                    if (_qr[r, c]) DrawRect(new Rect2(qr.Position.X + (c + 4) * cell, qr.Position.Y + (r + 4) * cell, cell, cell), Px.Hex(0x14121c));
        }
        else
        {
            Px.Frame(this, qr, Px.Glass, Px.Line2, null, 2, 0);
            Px.TextC(this, Px.Small, qr.GetCenter().X, qr.GetCenter().Y, "NO NETWORK", 8, Px.InkDim);
        }

        float tx = qr.End.X + 22, ty = qr.Position.Y + 10, tw = box.End.X - 24 - tx;
        void Line(string s, Color c, int size = 8)
        {
            foreach (var l in Px.Wrap(Px.Small, s, size, tw))
            {
                Px.Text(this, Px.Small, new Vector2(tx, ty), l, size, c);
                ty += size + 7;
            }
            ty += 5;
        }
        Line("IPHONE OR ANY PHONE, NO APP NEEDED", Px.Cyan, 9);
        Line("1. PUT IT ON THE SAME WI-FI AS THIS PC.", Px.Ink);
        Line("2. POINT ITS CAMERA AT THE CODE AND OPEN THE LINK, OR TYPE THIS IN SAFARI:", Px.Ink);
        if (url != "") Px.Text(this, Px.Big, new Vector2(tx, ty + 12), url.Replace("http://", ""), 24, Px.Gold);
        ty += 26;
        Line("3. TURN THE PHONE SIDEWAYS. SHARE > ADD TO HOME SCREEN MAKES IT FULL SCREEN.", Px.Ink);
        ty += 4;
        Line("ANDROID WITH THE GAMENIGHT APP: TAP PLAY ON PC ON ITS HOME SCREEN.", Px.InkDim);
        bool on = host?.PhoneConnected == true;
        Px.Text(this, Px.Big, new Vector2(tx, box.End.Y - 22), on ? "PHONE CONNECTED" : "WAITING FOR A PHONE...", 22, on ? Px.Win : Px.InkDim);
    }
}

using System;
using System.Text;

namespace GameNight.Link;

/// <summary>
/// A QR code for a short text (the controller page's address): version 3 (29×29), error
/// correction L, byte mode, mask 0. Up to 53 bytes, plenty for "http://192.168.100.200:47821".
/// </summary>
public static class Qr
{
    public const int Size = 29;
    const int DataWords = 55, EccWords = 15;

    /// <summary>The modules, true = dark, [y, x]; null if the text is too long.</summary>
    public static bool[,] Encode(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > 53) return null;

        // Data codewords: mode 0100, an 8-bit count, the bytes, terminator, then pad bytes.
        var bits = new System.Collections.Generic.List<bool>();
        void Put(int v, int n)
        {
            for (int i = n - 1; i >= 0; i--) bits.Add(((v >> i) & 1) != 0);
        }
        Put(4, 4);
        Put(bytes.Length, 8);
        foreach (var b in bytes) Put(b, 8);
        Put(0, Math.Min(4, DataWords * 8 - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);
        var data = new byte[DataWords + EccWords];
        int n = bits.Count / 8;
        for (int i = 0; i < n; i++)
            for (int k = 0; k < 8; k++)
                if (bits[i * 8 + k]) data[i] |= (byte)(0x80 >> k);
        for (int i = n; i < DataWords; i++) data[i] = (byte)((i - n) % 2 == 0 ? 0xEC : 0x11);
        Ecc(data);

        var m = new bool[Size, Size];
        var fixedCell = new bool[Size, Size];
        void Set(int x, int y, bool dark)
        {
            m[y, x] = dark;
            fixedCell[y, x] = true;
        }
        // Finder patterns with their separators.
        foreach (var (fx, fy) in new[] { (0, 0), (Size - 7, 0), (0, Size - 7) })
            for (int dy = -1; dy <= 7; dy++)
                for (int dx = -1; dx <= 7; dx++)
                {
                    int x = fx + dx, y = fy + dy;
                    if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                    bool ring = dx is 0 or 6 || dy is 0 or 6, core = dx is >= 2 and <= 4 && dy is >= 2 and <= 4;
                    bool inside = dx is >= 0 and <= 6 && dy is >= 0 and <= 6;
                    Set(x, y, inside && (ring || core));
                }
        // Alignment pattern (version 3: centred at 22, 22).
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                Set(22 + dx, 22 + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        // Timing patterns.
        for (int i = 8; i < Size - 8; i++)
        {
            Set(i, 6, i % 2 == 0);
            Set(6, i, i % 2 == 0);
        }
        // The dark module, and the format information: level L, mask 0.
        Set(8, Size - 8, true);
        const string format = "111011111000100"; // most significant bit first
        for (int i = 0; i < 15; i++)
        {
            bool f = format[14 - i] == '1';
            // Around the top-left finder.
            if (i < 6) Set(8, i, f);
            else if (i < 8) Set(8, i + 1, f);
            else if (i == 8) Set(7, 8, f);
            else Set(14 - i, 8, f);
            // Split between the other two.
            if (i < 8) Set(Size - 1 - i, 8, f);
            else Set(8, Size - 15 + i, f);
        }
        // Data, in the zigzag from the bottom right, two columns at a time, skipping column 6.
        int bit = 0, total = data.Length * 8;
        bool up = true;
        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < Size; vert++)
            {
                int y = up ? Size - 1 - vert : vert;
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    if (fixedCell[y, x]) continue;
                    bool dark = bit < total && ((data[bit >> 3] >> (7 - (bit & 7))) & 1) != 0;
                    bit++;
                    // Mask 0: (x + y) even flips.
                    m[y, x] = dark ^ ((x + y) % 2 == 0);
                }
            }
            up = !up;
        }
        return m;
    }

    /// <summary>Reed-Solomon: the last 15 bytes become the error correction for the first 55.</summary>
    static void Ecc(byte[] all)
    {
        var exp = new byte[512];
        var log = new byte[256];
        int v = 1;
        for (int i = 0; i < 255; i++)
        {
            exp[i] = (byte)v;
            log[v] = (byte)i;
            v <<= 1;
            if (v >= 256) v ^= 0x11D;
        }
        for (int i = 255; i < 512; i++) exp[i] = exp[i - 255];
        byte Mul(byte a, byte b) => a == 0 || b == 0 ? (byte)0 : exp[log[a] + log[b]];

        // Generator: product of (x - α^i), i = 0..14.
        var gen = new byte[EccWords + 1];
        gen[0] = 1;
        for (int i = 0; i < EccWords; i++)
        {
            for (int j = i + 1; j > 0; j--) gen[j] = (byte)(gen[j - 1] ^ Mul(gen[j], exp[i]));
            gen[0] = Mul(gen[0], exp[i]);
        }
        // gen is stored lowest power first; divide.
        var rem = new byte[EccWords];
        for (int i = 0; i < DataWords; i++)
        {
            byte f = (byte)(all[i] ^ rem[0]);
            Array.Copy(rem, 1, rem, 0, EccWords - 1);
            rem[EccWords - 1] = 0;
            for (int j = 0; j < EccWords; j++) rem[j] ^= Mul(gen[EccWords - 1 - j], f);
        }
        Array.Copy(rem, 0, all, DataWords, EccWords);
    }
}

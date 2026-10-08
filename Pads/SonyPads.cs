using HidSharp;

namespace PsToXbox.Pads;

/// <summary>Base para os controles Sony oficiais (DS3, DS4, DualSense) via USB.</summary>
public abstract class SonyPad : IPad
{
    protected readonly HidStream Stream;
    readonly byte[] _buf;
    public PadInfo Info { get; }
    public virtual bool SupportsRumble => true;

    protected SonyPad(PadInfo info, HidStream stream)
    {
        Info = info; Stream = stream;
        Stream.ReadTimeout = 250;
        int len = info.Device.GetMaxInputReportLength();
        _buf = new byte[len > 0 ? len : 64];
    }

    protected bool IsBluetooth;

    public bool TryRead(out PadState state)
    {
        state = PadState.Neutral;
        int n;
        try { n = Stream.Read(_buf); }
        catch (TimeoutException) { return false; }
        catch (Exception) { throw new IOException("Controle desconectado"); }
        if (n < 12) return false;
        
        int offset;
        if (_buf[0] == 0x01) { offset = 1; IsBluetooth = false; }
        else if (_buf[0] == 0x11) { offset = 3; IsBluetooth = true; } // DS4 BT
        else if (_buf[0] == 0x31) { offset = 2; IsBluetooth = true; } // DualSense BT
        else return false;
        
        return Parse(_buf, n, offset, ref state);
    }

    protected abstract bool Parse(byte[] b, int n, int o, ref PadState s);
    public abstract void SetRumble(byte large, byte small);

    /// <summary>No Windows o relatório precisa ter exatamente o tamanho máximo de saída do dispositivo.</summary>
    protected void Write(byte[] report)
    {
        try
        {
            int len = Info.Device.GetMaxOutputReportLength();
            if (len > 0 && report.Length != len) Array.Resize(ref report, len);
            Stream.Write(report);
        }
        catch { /* ignora falhas de escrita */ }
    }

    /// <summary>Idem para feature reports: tamanho exato do maior feature report.</summary>
    protected bool SetFeature(byte[] report)
    {
        try
        {
            int len = Info.Device.GetMaxFeatureReportLength();
            if (len > 0 && report.Length != len) Array.Resize(ref report, len);
            Stream.SetFeature(report);
            return true;
        }
        catch { return false; }
    }

    // Hat: 0=N,1=NE,2=E,3=SE,4=S,5=SW,6=W,7=NW,8+=solto
    protected static void SetDpad(ref PadState s, int hat)
    {
        s.Up = hat is 7 or 0 or 1;
        s.Right = hat is 1 or 2 or 3;
        s.Down = hat is 3 or 4 or 5;
        s.Left = hat is 5 or 6 or 7;
    }

    public void Dispose() { try { Stream.Dispose(); } catch { } }
}

/// <summary>PS4 DualShock 4.</summary>
public sealed class DualShock4Pad : SonyPad
{
    public DualShock4Pad(PadInfo info, HidStream stream) : base(info, stream) { }

    protected override bool Parse(byte[] b, int n, int o, ref PadState s)
    {
        s.LX = b[o+0]; s.LY = b[o+1]; s.RX = b[o+2]; s.RY = b[o+3];
        byte b5 = b[o+4], b6 = b[o+5], b7 = b[o+6];
        SetDpad(ref s, b5 & 0x0F);
        s.Square = (b5 & 0x10) != 0; s.Cross = (b5 & 0x20) != 0;
        s.Circle = (b5 & 0x40) != 0; s.Triangle = (b5 & 0x80) != 0;
        s.L1 = (b6 & 0x01) != 0; s.R1 = (b6 & 0x02) != 0;
        s.Share = (b6 & 0x10) != 0; s.Options = (b6 & 0x20) != 0;
        s.L3 = (b6 & 0x40) != 0; s.R3 = (b6 & 0x80) != 0;
        s.PS = (b7 & 0x01) != 0;
        s.L2 = b[o+7]; s.R2 = b[o+8];
        return true;
    }

    public override void SetRumble(byte large, byte small)
    {
        if (IsBluetooth) return; // Rumble BT DS4 requer CRC32
        var r = new byte[32];
        r[0] = 0x05; r[1] = 0x01; r[4] = small; r[5] = large;
        Write(r);
    }
}

/// <summary>PS5 DualSense (e DualSense Edge).</summary>
public sealed class DualSensePad : SonyPad
{
    public DualSensePad(PadInfo info, HidStream stream) : base(info, stream) { }

    protected override bool Parse(byte[] b, int n, int o, ref PadState s)
    {
        s.LX = b[o+0]; s.LY = b[o+1]; s.RX = b[o+2]; s.RY = b[o+3];
        s.L2 = b[o+4]; s.R2 = b[o+5];
        byte b8 = b[o+7], b9 = b[o+8], b10 = b[o+9];
        SetDpad(ref s, b8 & 0x0F);
        s.Square = (b8 & 0x10) != 0; s.Cross = (b8 & 0x20) != 0;
        s.Circle = (b8 & 0x40) != 0; s.Triangle = (b8 & 0x80) != 0;
        s.L1 = (b9 & 0x01) != 0; s.R1 = (b9 & 0x02) != 0;
        s.Share = (b9 & 0x10) != 0; s.Options = (b9 & 0x20) != 0;
        s.L3 = (b9 & 0x40) != 0; s.R3 = (b9 & 0x80) != 0;
        s.PS = (b10 & 0x01) != 0;
        return true;
    }

    public override void SetRumble(byte large, byte small)
    {
        if (IsBluetooth) return; // Rumble BT DualSense requer CRC32
        var r = new byte[63];
        r[0] = 0x02; r[1] = 0x03; r[3] = small; r[4] = large;
        r[39] = 0x04;   // valid_flag2: COMPATIBLE_VIBRATION2 (firmwares recentes)
        Write(r);
    }
}

/// <summary>
/// PS3 DualShock 3 / Sixaxis. No driver HID padrão do Windows, o controle só envia dados
/// depois do comando 0xF4. Para melhor resultado (vibração, Bluetooth) use o driver DsHidMini.
/// </summary>
public sealed class DualShock3Pad : SonyPad
{
    public DualShock3Pad(PadInfo info, HidStream stream) : base(info, stream)
    {
        // Sem este comando o DS3 não envia dados no driver HID padrão do Windows.
        // Se falhar, o controle pode já estar ativo (ex.: driver DsHidMini).
        SetFeature(new byte[] { 0xF4, 0x42, 0x03, 0x00, 0x00 });
    }

    protected override bool Parse(byte[] b, int n, int o, ref PadState s)
    {
        if (n < o + 19) return false;
        byte b2 = b[o+1], b3 = b[o+2], b4 = b[o+3];
        s.Share = (b2 & 0x01) != 0; s.L3 = (b2 & 0x02) != 0;
        s.R3 = (b2 & 0x04) != 0; s.Options = (b2 & 0x08) != 0;
        s.Up = (b2 & 0x10) != 0; s.Right = (b2 & 0x20) != 0;
        s.Down = (b2 & 0x40) != 0; s.Left = (b2 & 0x80) != 0;
        s.L1 = (b3 & 0x04) != 0; s.R1 = (b3 & 0x08) != 0;
        s.Triangle = (b3 & 0x10) != 0; s.Circle = (b3 & 0x20) != 0;
        s.Cross = (b3 & 0x40) != 0; s.Square = (b3 & 0x80) != 0;
        s.PS = (b4 & 0x01) != 0;
        s.LX = b[o+5]; s.LY = b[o+6]; s.RX = b[o+7]; s.RY = b[o+8];
        s.L2 = b[o+17]; s.R2 = b[o+18];
        if (s.L2 == 0 && (b3 & 0x01) != 0) s.L2 = 255;
        if (s.R2 == 0 && (b3 & 0x02) != 0) s.R2 = 255;
        return true;
    }

    public override void SetRumble(byte large, byte small)
    {
        var r = new byte[49];
        r[0] = 0x01;
        r[1] = 0xFE; r[2] = (byte)(small > 0 ? 1 : 0);   // motor direito: liga/desliga
        r[3] = 0xFE; r[4] = large;                        // motor esquerdo: 0..255
        r[9] = 0x02;                                      // LED do jogador 1
        Write(r);
    }
}

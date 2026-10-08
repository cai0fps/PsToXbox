using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;

namespace PsToXbox.Pads;

/// <summary>
/// Gamepad HID genérico: cobre adaptadores USB de PS1/PS2 (e qualquer controle similar).
/// Lê o descritor HID, guarda os valores brutos e traduz para <see cref="PadState"/> com um perfil.
/// </summary>
public sealed class GenericHidPad : IPad
{
    readonly HidStream _stream;
    readonly HidDeviceInputReceiver _receiver;
    readonly DeviceItemInputParser _parser;
    readonly byte[] _buf;
    readonly object _lock = new();
    readonly Dictionary<string, double> _raw = new();

    public PadInfo Info { get; }
    public bool SupportsRumble => false;
    public MappingProfile Profile { get; set; }

    GenericHidPad(PadInfo info, HidStream stream, HidDeviceInputReceiver rx, DeviceItemInputParser parser, MappingProfile prof)
    {
        Info = info; _stream = stream; _receiver = rx; _parser = parser; Profile = prof;
        _buf = new byte[Math.Max(info.Device.GetMaxInputReportLength(), 16)];
        foreach (var a in new[] { "A10030", "A10031", "A10032", "A10033", "A10034", "A10035" }) _raw[a] = 0.5;
        _raw["H"] = -1;
    }

    public static GenericHidPad? Open(PadInfo info)
    {
        try
        {
            var rd = info.Device.GetReportDescriptor();
            var item = rd.DeviceItems.FirstOrDefault(IsGamepadItem) ?? rd.DeviceItems.First();
            if (!info.Device.TryOpen(out var stream)) return null;
            var rx = rd.CreateHidDeviceInputReceiver();
            var parser = item.CreateDeviceItemInputParser();
            rx.Start(stream);
            return new GenericHidPad(info, stream, rx, parser, MappingProfile.Load(info.ProfileKey));
        }
        catch { return null; }
    }

    public static bool IsGamepadItem(DeviceItem di)
    {
        // Generic Desktop (0x01): Joystick = 0x04, Game Pad = 0x05
        return di.Usages.GetAllValues().Any(u => u == 0x10004 || u == 0x10005);
    }

    /// <summary>Cópia dos valores brutos atuais (botões "B#", eixos "A#####" 0..1, hat "H").</summary>
    public Dictionary<string, double> SnapshotRaw()
    {
        lock (_lock) return new Dictionary<string, double>(_raw);
    }

    public bool TryRead(out PadState state)
    {
        state = PadState.Neutral;
        if (!_receiver.IsRunning) throw new IOException("Controle desconectado");

        bool got = false;
        if (!_receiver.WaitHandle.WaitOne(250)) return Translate(out state);
        while (_receiver.TryRead(_buf, 0, out Report report))
        {
            if (!_parser.TryParseReport(_buf, 0, report)) continue;
            got = true;
            lock (_lock)
            {
                while (_parser.HasChanged)
                {
                    int idx = _parser.GetNextChangedIndex();
                    var dv = _parser.GetValue(idx);
                    Store(dv);
                }
            }
        }
        _ = got;
        return Translate(out state);
    }

    void Store(DataValue dv)
    {
        uint usage = dv.Usages.FirstOrDefault();
        uint page = usage >> 16, id = usage & 0xFFFF;
        int v = dv.GetLogicalValue();
        var di = dv.DataItem;

        if (page == 0x09)                       // botões
        {
            _raw["B" + id] = v != 0 ? 1 : 0;
        }
        else if (page == 0x01 && id == 0x39)    // hat switch
        {
            int idx = v - di.LogicalMinimum;
            _raw["H"] = (idx >= 0 && idx <= 7) ? idx : -1;
        }
        else if (page == 0x01 && id >= 0x30 && id <= 0x35)  // eixos X Y Z Rx Ry Rz
        {
            double span = Math.Max(1, di.LogicalMaximum - di.LogicalMinimum);
            _raw["A" + usage.ToString("X")] = Math.Clamp((v - di.LogicalMinimum) / span, 0, 1);
        }
    }

    bool Translate(out PadState s)
    {
        Dictionary<string, double> raw;
        lock (_lock) raw = new Dictionary<string, double>(_raw);
        s = PadState.Neutral;
        var m = Profile.Map;

        bool Btn(string t)
        {
            if (!m.TryGetValue(t, out var src)) return false;
            if (src == "H") return false;
            return Value(raw, src) > 0.5;
        }
        byte Trig(string t)
        {
            if (!m.TryGetValue(t, out var src)) return 0;
            return (byte)Math.Round(Math.Clamp(Value(raw, src), 0, 1) * 255);
        }
        byte Axis(string t)
        {
            if (!m.TryGetValue(t, out var src)) return 128;
            return (byte)Math.Round(Math.Clamp(Value(raw, src), 0, 1) * 255);
        }

        s.Cross = Btn("Cross"); s.Circle = Btn("Circle"); s.Square = Btn("Square"); s.Triangle = Btn("Triangle");
        s.L1 = Btn("L1"); s.R1 = Btn("R1"); s.L3 = Btn("L3"); s.R3 = Btn("R3");
        s.Share = Btn("Share"); s.Options = Btn("Options"); s.PS = Btn("PS");
        s.L2 = Trig("L2"); s.R2 = Trig("R2");
        s.LX = Axis("LX"); s.LY = Axis("LY"); s.RX = Axis("RX"); s.RY = Axis("RY");

        int hat = (int)raw.GetValueOrDefault("H", -1);
        bool hu = hat is 7 or 0 or 1, hr = hat is 1 or 2 or 3, hd = hat is 3 or 4 or 5, hl = hat is 5 or 6 or 7;
        s.Up = m.GetValueOrDefault("Up") == "H" ? hu : Btn("Up");
        s.Down = m.GetValueOrDefault("Down") == "H" ? hd : Btn("Down");
        s.Left = m.GetValueOrDefault("Left") == "H" ? hl : Btn("Left");
        s.Right = m.GetValueOrDefault("Right") == "H" ? hr : Btn("Right");
        return true;
    }

    /// <summary>Valor 0..1 de uma origem ("B3", "A10030", "A10030-").</summary>
    static double Value(Dictionary<string, double> raw, string src)
    {
        bool inv = src.EndsWith('-');
        if (inv) src = src[..^1];
        double v = raw.GetValueOrDefault(src, src.StartsWith('A') ? 0.5 : 0);
        return inv ? 1 - v : v;
    }

    public void SetRumble(byte large, byte small) { }

    public void Dispose() { try { _stream.Dispose(); } catch { } }
}

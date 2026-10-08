using HidSharp;

namespace PsToXbox.Pads;

public static class PadFinder
{
    public const int SonyVid = 0x054C;
    const int MicrosoftVid = 0x045E;     // controles Xbox/ViGEm: não precisam de conversão

    static readonly int[] Ds3Pids = { 0x0268 };
    static readonly int[] Ds4Pids = { 0x05C4, 0x09CC, 0x0BA0 };   // v1, v2, dongle
    static readonly int[] DualSensePids = { 0x0CE6, 0x0DF2 };      // DualSense, Edge

    static readonly Dictionary<string, bool> _isGamepadCache = new();

    public static List<PadInfo> Find()
    {
        var list = new List<PadInfo>();
        foreach (var d in DeviceList.Local.GetHidDevices())
        {
            try
            {
                if (d.VendorID == SonyVid)
                {
                    if (Ds3Pids.Contains(d.ProductID)) { list.Add(Make(d, PadKind.DualShock3)); continue; }
                    if (Ds4Pids.Contains(d.ProductID)) { list.Add(Make(d, PadKind.DualShock4)); continue; }
                    if (DualSensePids.Contains(d.ProductID)) { list.Add(Make(d, PadKind.DualSense)); continue; }
                }
                if (d.VendorID == MicrosoftVid) continue;
                if (IsGamepad(d)) list.Add(Make(d, PadKind.GenericAdapter));
            }
            catch { /* dispositivo inacessível: ignora */ }
        }
        return list;
    }

    static PadInfo Make(HidDevice d, PadKind kind)
    {
        string name;
        try { name = d.GetProductName(); } catch { name = ""; }
        if (string.IsNullOrWhiteSpace(name)) name = $"Controle {d.VendorID:X4}:{d.ProductID:X4}";
        return new PadInfo(d, kind, name);
    }

    static bool IsGamepad(HidDevice d)
    {
        if (_isGamepadCache.TryGetValue(d.DevicePath, out var c)) return c;
        bool ok = false;
        try
        {
            var rd = d.GetReportDescriptor();
            ok = rd.DeviceItems.Any(GenericHidPad.IsGamepadItem) && d.GetMaxInputReportLength() >= 3;
        }
        catch { }
        _isGamepadCache[d.DevicePath] = ok;
        return ok;
    }

    public static IPad? Open(PadInfo info)
    {
        if (info.Kind == PadKind.GenericAdapter) return GenericHidPad.Open(info);
        if (!info.Device.TryOpen(out var stream)) return null;
        return info.Kind switch
        {
            PadKind.DualShock3 => new DualShock3Pad(info, stream),
            PadKind.DualShock4 => new DualShock4Pad(info, stream),
            PadKind.DualSense => new DualSensePad(info, stream),
            _ => null
        };
    }
}

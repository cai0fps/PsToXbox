using HidSharp;

namespace PsToXbox.Pads;

/// <summary>Estado normalizado de qualquer controle PS (PS1 a PS5).</summary>
public struct PadState
{
    public byte LX, LY, RX, RY;       // 0..255, 128 = centro, Y: 0 = cima
    public byte L2, R2;               // 0..255
    public bool Up, Down, Left, Right;
    public bool Square, Cross, Circle, Triangle;
    public bool L1, R1, L3, R3;
    public bool Share, Options, PS;

    public static PadState Neutral => new() { LX = 128, LY = 128, RX = 128, RY = 128 };
}

public enum PadKind
{
    /// <summary>PS1/PS2 através de adaptador USB (e qualquer gamepad HID genérico).</summary>
    GenericAdapter,
    DualShock3,
    DualShock4,
    DualSense,
}

public static class PadKindExt
{
    public static string Label(this PadKind k) => k switch
    {
        PadKind.GenericAdapter => "PS1 / PS2 (adaptador USB)",
        PadKind.DualShock3 => "PS3 DualShock 3",
        PadKind.DualShock4 => "PS4 DualShock 4",
        PadKind.DualSense => "PS5 DualSense",
        _ => "Controle"
    };
}

/// <summary>Descrição de um controle encontrado (ainda não aberto).</summary>
public sealed record PadInfo(HidDevice Device, PadKind Kind, string Name)
{
    public string Path => Device.DevicePath;
    public int Vid => Device.VendorID;
    public int Pid => Device.ProductID;
    public string ProfileKey => $"{Vid:X4}_{Pid:X4}";
    public override string ToString() => $"{Name} ({Kind.Label()})";
}

/// <summary>Controle aberto, pronto para leitura.</summary>
public interface IPad : IDisposable
{
    PadInfo Info { get; }

    /// <summary>
    /// Lê o próximo estado, esperando no máximo ~250 ms.
    /// Retorna false se não chegou nada novo. Lança IOException se o controle foi desconectado.
    /// </summary>
    bool TryRead(out PadState state);

    /// <summary>Vibração 0..255 (motor forte, motor fraco). Ignora falhas.</summary>
    void SetRumble(byte large, byte small);

    bool SupportsRumble { get; }
}

using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using PsToXbox.Pads;

namespace PsToXbox.Services;

/// <summary>Converte <see cref="PadState"/> (PS) em relatório de Xbox 360 virtual.</summary>
public static class Mapper
{
    /// <summary>Zona morta radial; entrada e saída em -1..1.</summary>
    static (double x, double y) Deadzone(double x, double y, double dz)
    {
        double mag = Math.Sqrt(x * x + y * y);
        if (mag <= dz || mag == 0) return (0, 0);
        double scale = Math.Min(1, (mag - dz) / (1 - dz)) / mag;
        return (x * scale, y * scale);
    }

    static short ToShort(double v) => (short)Math.Clamp(Math.Round(v * 32767), short.MinValue, short.MaxValue);
    // 0..255 -> -1..1 com 128 no centro; /127 garante que 255 chegue a +1.0
    static double Norm(byte b) => Math.Clamp((b - 128) / 127.0, -1.0, 1.0);

    public static void Apply(IXbox360Controller x, PadState s, Settings cfg)
    {
        var (lx, ly) = Deadzone(Norm(s.LX), Norm(s.LY), cfg.DeadzoneLeft);
        var (rx, ry) = Deadzone(Norm(s.RX), Norm(s.RY), cfg.DeadzoneRight);

        x.SetAxisValue(Xbox360Axis.LeftThumbX, ToShort(lx));
        x.SetAxisValue(Xbox360Axis.LeftThumbY, ToShort(-ly));   // Xbox: cima = positivo
        x.SetAxisValue(Xbox360Axis.RightThumbX, ToShort(rx));
        x.SetAxisValue(Xbox360Axis.RightThumbY, ToShort(-ry));
        x.SetSliderValue(Xbox360Slider.LeftTrigger, s.L2);
        x.SetSliderValue(Xbox360Slider.RightTrigger, s.R2);

        x.SetButtonState(Xbox360Button.A, s.Cross);
        x.SetButtonState(Xbox360Button.B, s.Circle);
        x.SetButtonState(Xbox360Button.X, s.Square);
        x.SetButtonState(Xbox360Button.Y, s.Triangle);
        x.SetButtonState(Xbox360Button.LeftShoulder, s.L1);
        x.SetButtonState(Xbox360Button.RightShoulder, s.R1);
        x.SetButtonState(Xbox360Button.LeftThumb, s.L3);
        x.SetButtonState(Xbox360Button.RightThumb, s.R3);
        x.SetButtonState(Xbox360Button.Back, s.Share);
        x.SetButtonState(Xbox360Button.Start, s.Options);
        x.SetButtonState(Xbox360Button.Guide, s.PS);
        x.SetButtonState(Xbox360Button.Up, s.Up);
        x.SetButtonState(Xbox360Button.Down, s.Down);
        x.SetButtonState(Xbox360Button.Left, s.Left);
        x.SetButtonState(Xbox360Button.Right, s.Right);
        x.SubmitReport();
    }
}

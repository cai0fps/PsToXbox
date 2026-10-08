using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PsToXbox.Pads;

namespace PsToXbox.Views;

/// <summary>Desenho do controle na tela: acende o que está sendo pressionado (modo teste).</summary>
public sealed class ControllerView : Canvas
{
    static readonly Brush Off = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42));
    static readonly Brush On = new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53)); // Verde neon
    static readonly Brush TextOff = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
    static readonly Brush TextOn = Brushes.White;

    readonly Dictionary<string, Border> _btn = new();
    readonly Border _l2Fill, _r2Fill;
    readonly Ellipse2 _lDot, _rDot;
    const double TriW = 80;

    sealed class Ellipse2
    {
        public Border Dot = null!;
        public double Cx, Cy;
    }

    public ControllerView()
    {
        Width = 460; Height = 300;

        // Corpo do controle
        Children.Add(new Border
        {
            Width = 420, Height = 220, CornerRadius = new CornerRadius(80),
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)), BorderThickness = new Thickness(2),
        });
        SetLeft(Children[0], 20); SetTop(Children[0], 50);

        // gatilhos analógicos L2/R2
        _l2Fill = Trigger(60, 6, "L2 / LT");
        _r2Fill = Trigger(320, 6, "R2 / RT");

        Add("L1", 60, 40, 100, 24, 8, "L1 / LB");
        Add("R1", 300, 40, 100, 24, 8, "R1 / RB");

        // D-Pad
        Add("Up", 102, 100, 26, 28, 4, "▲");
        Add("Down", 102, 156, 26, 28, 4, "▼");
        Add("Left", 74, 128, 28, 28, 4, "◀");
        Add("Right", 130, 128, 28, 28, 4, "▶");

        // Action buttons
        Add("Triangle", 327, 98, 38, 38, 19, "△ Y");
        Add("Cross", 327, 156, 38, 38, 19, "✕ A");
        Add("Square", 298, 127, 38, 38, 19, "□ X");
        Add("Circle", 356, 127, 38, 38, 19, "○ B");

        // Menu buttons
        Add("Share", 184, 92, 44, 22, 11, "Sel");
        Add("Options", 234, 92, 44, 22, 11, "Start");
        Add("PS", 214, 126, 34, 34, 17, "PS");

        _lDot = Stick("L3", 180, 215);
        _rDot = Stick("R3", 280, 215);
        Update(PadState.Neutral);
    }

    Border Trigger(double x, double y, string label)
    {
        var back = new Border
        {
            Width = TriW + 20, Height = 28, CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), BorderThickness = new Thickness(1),
        };
        SetLeft(back, x); SetTop(back, y);
        Children.Add(back);
        var fill = new Border { Width = 0, Height = 28, CornerRadius = new CornerRadius(8), Background = On };
        SetLeft(fill, x); SetTop(fill, y);
        Children.Add(fill);
        var tb = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = TextOff, Width = TriW + 20, TextAlignment = TextAlignment.Center };
        SetLeft(tb, x); SetTop(tb, y + 5);
        Children.Add(tb);
        return fill;
    }

    void Add(string name, double x, double y, double w, double h, double radius, string text)
    {
        var b = new Border
        {
            Width = w, Height = h, CornerRadius = new CornerRadius(radius), Background = Off,
            Child = new TextBlock
            {
                Text = text, FontSize = w < 40 ? 12 : 11, FontWeight = FontWeights.SemiBold, Foreground = TextOff,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI Symbol"),
            },
        };
        SetLeft(b, x); SetTop(b, y);
        Children.Add(b);
        _btn[name] = b;
    }

    Ellipse2 Stick(string clickName, double cx, double cy)
    {
        var baseRing = new Border
        {
            Width = 70, Height = 70, CornerRadius = new CornerRadius(35),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x22)), BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22)),
        };
        SetLeft(baseRing, cx - 35); SetTop(baseRing, cy - 35);
        Children.Add(baseRing);

        var dot = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = Off,
            Child = new TextBlock
            {
                Text = clickName, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = TextOff,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Children.Add(dot);
        _btn[clickName] = dot;
        return new Ellipse2 { Dot = dot, Cx = cx, Cy = cy };
    }

    void Set(string name, bool on)
    {
        var b = _btn[name];
        b.Background = on ? On : Off;
        if (b.Child is TextBlock t) t.Foreground = on ? TextOn : TextOff;
    }

    public void Update(PadState s)
    {
        Set("L1", s.L1); Set("R1", s.R1);
        Set("Up", s.Up); Set("Down", s.Down); Set("Left", s.Left); Set("Right", s.Right);
        Set("Triangle", s.Triangle); Set("Cross", s.Cross); Set("Square", s.Square); Set("Circle", s.Circle);
        Set("Share", s.Share); Set("Options", s.Options); Set("PS", s.PS);
        Set("L3", s.L3); Set("R3", s.R3);

        _l2Fill.Width = (TriW + 20) * s.L2 / 255.0;
        _r2Fill.Width = (TriW + 20) * s.R2 / 255.0;

        MoveStick(_lDot, s.LX, s.LY);
        MoveStick(_rDot, s.RX, s.RY);
    }

    static void MoveStick(Ellipse2 st, byte x, byte y)
    {
        double dx = (x - 128) / 128.0, dy = (y - 128) / 128.0;
        SetLeft(st.Dot, st.Cx - 16 + dx * 19);
        SetTop(st.Dot, st.Cy - 16 + dy * 19);
    }
}

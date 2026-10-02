using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Keyvert.Core;
using Keyvert.Services;

namespace Keyvert.Views;

/// <summary>Live drawing of the virtual controller. Pressed parts fill with the accent color; the highlighted action gets an outline.</summary>
public partial class ControllerView : UserControl
{
    private const double StickTravel = 13;
    private const double TriggerWidth = 70;

    private const string IdleFill = "ControlFillColorSecondaryBrush";
    private const string PressedFill = "AccentFillColorDefaultBrush";
    private const string IdleStroke = "ControlStrongStrokeColorDefaultBrush";
    private const string HighlightStroke = "TextFillColorPrimaryBrush";
    private const string IdleLabel = "TextFillColorSecondaryBrush";
    private const string PressedLabel = "TextOnAccentFillColorPrimaryBrush";

    private static readonly FaceStyle AStyle = new(Color.FromRgb(0x4C, 0xB0, 0x50));
    private static readonly FaceStyle BStyle = new(Color.FromRgb(0xE5, 0x53, 0x4B));
    private static readonly FaceStyle XStyle = new(Color.FromRgb(0x3D, 0x8B, 0xF2));
    private static readonly FaceStyle YStyle = new(Color.FromRgb(0xE3, 0xA9, 0x2B));

    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot), typeof(ControllerSnapshot), typeof(ControllerView),
        new PropertyMetadata(default(ControllerSnapshot), (d, _) => ((ControllerView)d).Refresh()));

    public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register(
        nameof(Highlight), typeof(ControllerAction?), typeof(ControllerView),
        new PropertyMetadata(null, (d, _) => ((ControllerView)d).Refresh()));

    public ControllerView()
    {
        InitializeComponent();
        Refresh();
    }

    public ControllerSnapshot Snapshot
    {
        get => (ControllerSnapshot)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public ControllerAction? Highlight
    {
        get => (ControllerAction?)GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    private void Refresh()
    {
        var state = Snapshot.State;
        var buttons = state.Buttons;
        bool Pressed(PadButtons button) => (buttons & button) != 0;
        bool Lit(params ControllerAction[] actions) => Highlight is { } h && actions.Contains(h);

        SetPart(LbShape, LbLabel, Pressed(PadButtons.LB), Lit(ControllerAction.LB));
        SetPart(RbShape, RbLabel, Pressed(PadButtons.RB), Lit(ControllerAction.RB));
        SetPart(DpadUp, null, Pressed(PadButtons.DpadUp), Lit(ControllerAction.DpadUp));
        SetPart(DpadDown, null, Pressed(PadButtons.DpadDown), Lit(ControllerAction.DpadDown));
        SetPart(DpadLeft, null, Pressed(PadButtons.DpadLeft), Lit(ControllerAction.DpadLeft));
        SetPart(DpadRight, null, Pressed(PadButtons.DpadRight), Lit(ControllerAction.DpadRight));
        SetPart(BackShape, null, Pressed(PadButtons.Back), Lit(ControllerAction.Back));
        SetPart(StartShape, null, Pressed(PadButtons.Start), Lit(ControllerAction.Start));
        SetPart(GuideShape, null, Pressed(PadButtons.Guide), Lit(ControllerAction.Guide));

        SetTrigger(LtBack, LtFill, LtLabel, state.LeftTrigger, Lit(ControllerAction.LT));
        SetTrigger(RtBack, RtFill, RtLabel, state.RightTrigger, Lit(ControllerAction.RT));

        SetStick(LeftRing, LeftThumb, LeftThumbOffset, state.LeftX, state.LeftY, Pressed(PadButtons.LS),
            Lit(ControllerAction.LeftStickUp, ControllerAction.LeftStickDown, ControllerAction.LeftStickLeft,
                ControllerAction.LeftStickRight, ControllerAction.LS, ControllerAction.Walk));
        SetStick(RightRing, RightThumb, RightThumbOffset, state.RightX, state.RightY, Pressed(PadButtons.RS),
            Lit(ControllerAction.RightStickUp, ControllerAction.RightStickDown, ControllerAction.RightStickLeft,
                ControllerAction.RightStickRight, ControllerAction.RS));

        SetFace(AShape, ALabel, AStyle, Pressed(PadButtons.A), Lit(ControllerAction.A));
        SetFace(BShape, BLabel, BStyle, Pressed(PadButtons.B), Lit(ControllerAction.B));
        SetFace(XShape, XLabel, XStyle, Pressed(PadButtons.X), Lit(ControllerAction.X));
        SetFace(YShape, YLabel, YStyle, Pressed(PadButtons.Y), Lit(ControllerAction.Y));

        LeftXText.Text = state.LeftX.ToString();
        LeftYText.Text = state.LeftY.ToString();
        RightXText.Text = state.RightX.ToString();
        RightYText.Text = state.RightY.ToString();
        LtText.Text = $"{Math.Round(state.LeftTrigger * 100 / 255.0)}%";
        RtText.Text = $"{Math.Round(state.RightTrigger * 100 / 255.0)}%";
    }

    private static void SetPart(Shape shape, TextBlock? label, bool pressed, bool highlighted)
    {
        shape.SetResourceReference(Shape.FillProperty, pressed ? PressedFill : IdleFill);
        SetOutline(shape, highlighted);
        label?.SetResourceReference(TextBlock.ForegroundProperty, pressed ? PressedLabel : IdleLabel);
    }

    private static void SetOutline(Shape shape, bool highlighted)
    {
        shape.SetResourceReference(Shape.StrokeProperty, highlighted ? HighlightStroke : IdleStroke);
        shape.StrokeThickness = highlighted ? 2.5 : 1;
    }

    private static void SetTrigger(Shape back, FrameworkElement fill, TextBlock label, byte value, bool highlighted)
    {
        SetOutline(back, highlighted);
        fill.Width = value / 255.0 * TriggerWidth;
        label.SetResourceReference(TextBlock.ForegroundProperty, value > 127 ? PressedLabel : IdleLabel);
    }

    private static void SetStick(Shape ring, Shape thumb, TranslateTransform offset, short x, short y, bool clicked, bool highlighted)
    {
        offset.X = x / (double)short.MaxValue * StickTravel;
        offset.Y = -y / (double)short.MaxValue * StickTravel;

        bool moved = x != 0 || y != 0;
        thumb.SetResourceReference(Shape.FillProperty, moved || clicked ? PressedFill : IdleFill);
        ring.SetResourceReference(Shape.FillProperty, clicked ? PressedFill : IdleFill);
        ring.Opacity = clicked ? 0.45 : 1;
        SetOutline(ring, highlighted);
    }

    private static void SetFace(Shape shape, TextBlock label, FaceStyle style, bool pressed, bool highlighted)
    {
        shape.Fill = pressed ? style.Solid : style.Tint;
        if (highlighted)
        {
            shape.SetResourceReference(Shape.StrokeProperty, HighlightStroke);
            shape.StrokeThickness = 2.5;
        }
        else
        {
            shape.Stroke = style.Edge;
            shape.StrokeThickness = 1.2;
        }
        label.Foreground = pressed ? Brushes.White : style.Solid;
    }

    private sealed class FaceStyle
    {
        public FaceStyle(Color color)
        {
            Solid = Freeze(new SolidColorBrush(color));
            Tint = Freeze(new SolidColorBrush(Color.FromArgb(0x38, color.R, color.G, color.B)));
            Edge = Freeze(new SolidColorBrush(Color.FromArgb(0xA0, color.R, color.G, color.B)));
        }

        public Brush Solid { get; }
        public Brush Tint { get; }
        public Brush Edge { get; }

        private static Brush Freeze(Brush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}

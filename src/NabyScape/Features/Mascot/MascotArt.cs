using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal enum MascotState { Idle, Work, Error, Music }

internal sealed class MascotArt(BitmapSource monitor)
{
    private readonly record struct Pose(
        Vector BodyOffset, double BodyAngle, Vector Gaze,
        Point LeftElbow, Point RightElbow, Point LeftHand, Point RightHand,
        Point LeftKnee, Point RightKnee, Point LeftFoot, Point RightFoot,
        double LeftBootAngle = 0, double RightBootAngle = 0,
        Point? ShovelHandle = null, Point? ShovelTip = null, bool OpenRightHand = false);

    private static readonly Geometry Screen = Geometry.Parse(
        "M -79,-54 Q -78,-62 -67,-65 C -34,-69 21,-69 52,-63 Q 59,-61 61,-51 " +
        "C 64,-29 63,4 60,21 Q 58,28 48,30 C 14,34 -44,34 -68,28 " +
        "Q -77,26 -78,18 C -82,-7 -81,-34 -79,-54 Z");

    private static readonly Pose Resting = new(
        new(0, 0), -4, new(5, -3),
        new(-109, 42), new(112, 45), new(-113, 78), new(113, 83),
        new(-40, 106), new(41, 106), new(-49, 135), new(52, 135), -4, 3);

    private static readonly Pose Digging = new(
        new(4, 6), 13, new(6, 3),
        new(-111, 38), new(111, 79), new(-89, 44), new(45, 103),
        new(-62, 110), new(54, 109), new(-67, 136), new(71, 135), -8, 4,
        new(-116, 32), new(125, 138));

    private static readonly Pose Alarmed = new(
        new(-3, -2), -9, new(-7, -4),
        new(-109, 44), new(118, 32), new(-112, 8), new(137, -8),
        new(-53, 105), new(46, 109), new(-63, 136), new(59, 135), -9, 7,
        new(-123, 52), new(-85, -102), true);

    private static readonly Pose Dancing = new(
        new(3, -9), -10, new(3, -3),
        new(-113, 41), new(108, 38), new(-69, 75), new(67, 75),
        new(-43, 98), new(55, 102), new(-49, 136), new(85, 113), -8, -23,
        new(-131, 75), new(136, 75));

    private static readonly Dictionary<string, Geometry> Shapes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, SolidColorBrush> Colors = new(StringComparer.Ordinal);
    private static readonly Brush BootLeather = Linear("#666965", "#252A2A", new(.20, 0), new(.66, 1));
    private static readonly Brush Glove = Linear("#F5F0E2", "#AAA397", new(.13, .07), new(.77, .88));
    private static readonly Brush Tube = Linear("#838785", "#343D40", new(0, 0), new(0, 1));
    private static readonly Brush Steel = Linear("#9DABA9", "#414D53", new(.12, 0), new(.86, 1));
    private static readonly Brush Shaft = Linear("#C19660", "#654626", new(0, 0), new(0, 1));
    private static readonly Brush Handle = Linear("#857159", "#4A3E30", new(0, 0), new(0, 1));
    private static readonly Brush Socket = Linear("#B4BDB9", "#68777A", new(0, 0), new(.2, 1));
    private static readonly Brush Joint = Linear("#6D7473", "#383F42", new(.16, .08), new(.77, .9));
    private static readonly Brush FloorShadow = Radial("#A6081011", "#00081011", new(.50, .50), .50, .50);

    internal void DrawFull(DrawingContext dc, MascotFrame frame, Point origin, double scale)
    {
        var state = frame.State;
        var pose = frame.State switch
        {
            MascotState.Work => WorkPose(frame),
            MascotState.Error => ErrorPose(frame),
            MascotState.Music => DancePose(frame),
            _ => Resting with { Gaze = frame.Gaze, BodyAngle = -4 + frame.Gaze.X * .23 }
        };
        dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawEllipse(FloorShadow, null, new Point(0, 151), 113, 15);

        Point OnBody(double x, double y) => new RotateTransform(pose.BodyAngle).Transform(new Point(x, y)) + pose.BodyOffset;
        DrawLimb(dc, OnBody(-38, 72), pose.LeftKnee, pose.LeftFoot, 19);
        DrawLimb(dc, OnBody(33, 76), pose.RightKnee, pose.RightFoot, 19);
        DrawBoot(dc, pose.LeftFoot, pose.LeftBootAngle, true);
        DrawBoot(dc, pose.RightFoot, pose.RightBootAngle, false);

        var left = Grip(pose, state switch { MascotState.Work => .11, MascotState.Error => .29, _ => .23 });
        var right = state is MascotState.Music or MascotState.Work
            ? Grip(pose, state == MascotState.Work ? .67 : .74) : pose.RightHand;
        DrawLimb(dc, OnBody(-91, 6), pose.LeftElbow, left, 17);
        DrawLimb(dc, OnBody(87, 8), pose.RightElbow, right, 17);
        DrawMonitor(dc, new Point(pose.BodyOffset.X, pose.BodyOffset.Y), 1, pose.BodyAngle, pose.Gaze);

        if (pose.ShovelHandle is Point start && pose.ShovelTip is Point tip)
            DrawShovel(dc, start, tip, 1);

        DrawGlove(dc, left, state == MascotState.Idle ? 32 : -12, true, false);
        DrawGlove(dc, right, state == MascotState.Music ? -18 : 14, false, pose.OpenRightHand);

        if (state == MascotState.Error)
        {
            Mark(dc, new(125, -43), new(131, -55), "#E3AA76");
            Mark(dc, new(140, -34), new(153, -42), "#E3AA76");
        }
        if (state == MascotState.Music)
        {
            Mark(dc, new(102, 115), new(110, 107), "#9EBB81");
            Mark(dc, new(104, 124), new(116, 118), "#9EBB81");
        }
        dc.Pop();
        dc.Pop();
    }

    internal void DrawCompact(DrawingContext dc, MascotFrame frame, Point origin, double magnification)
    {
        dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
        dc.PushTransform(new ScaleTransform(magnification, magnification));
        switch (frame.State)
        {
            case MascotState.Work:
                double thrust = DigStroke(frame);
                DrawMonitor(dc, new Point(-8, -1), .143, 2 + 3 * thrust, new(3, 1));
                DrawShovel(dc, new Point(10 - 3 * thrust, -14 + 4 * thrust),
                    new Point(14 + 8 * thrust, 13), .13);
                break;
            case MascotState.Error:
                double shake = ErrorShake(frame);
                DrawMonitor(dc, new Point(-8, 1), .143, -6 + shake * 4, new(-5, -3));
                DrawShovel(dc, new Point(10, 12), new Point(20 + shake * 6, -13 + Math.Abs(shake) * 2), .13);
                Mark(dc, new(26, -10), new(29, -13), "#E3AA76", .8);
                break;
            case MascotState.Music:
                DrawMonitor(dc, new Point(0, -5.5), .143, -2 + frame.Sway * 2.5, new(4, -4));
                DrawShovel(dc, new Point(-14 + frame.Sway * 6, 12), new Point(15 + frame.Sway * 6, 12), .13);
                break;
            default:
                DrawMonitor(dc, new Point(0, 0), .150, -3 + frame.Gaze.X * .19, frame.Gaze);
                break;
        }
        dc.Pop();
        dc.Pop();
    }

    private void DrawMonitor(DrawingContext dc, Point center, double scale, double tilt, Vector gaze)
    {
        dc.PushTransform(new TranslateTransform(center.X, center.Y));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.PushTransform(new RotateTransform(tilt));
        dc.DrawImage(monitor, new Rect(-100, -100, 200, 200));
        if (gaze.LengthSquared > .01)
        {
            dc.PushClip(Screen);
            const double zoom = 1.13;
            var screenCenter = new Point(-9, -18);
            dc.DrawImage(monitor, new Rect(
                (-100 - screenCenter.X) * zoom + screenCenter.X + gaze.X,
                (-100 - screenCenter.Y) * zoom + screenCenter.Y + gaze.Y,
                200 * zoom, 200 * zoom));
            dc.Pop();
        }
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    internal static double DigStroke(MascotFrame frame)
    {
        if (frame.Still) return 1;
        double cycle = frame.Seconds % 1.35 / 1.35;
        const double liftEnd = .62;
        double lift = cycle < liftEnd ? cycle / liftEnd : (1 - cycle) / (1 - liftEnd);
        return 1 - lift * lift * (3 - 2 * lift);
    }

    private static Pose WorkPose(MascotFrame frame)
    {
        double lift = 1 - DigStroke(frame);
        return Digging with
        {
            BodyOffset = Digging.BodyOffset + new Vector(-8 * lift, -10 * lift),
            BodyAngle = 13 - 18 * lift,
            LeftElbow = Digging.LeftElbow + new Vector(-2 * lift, -29 * lift),
            RightElbow = Digging.RightElbow + new Vector(-9 * lift, -37 * lift),
            ShovelHandle = Digging.ShovelHandle!.Value + new Vector(-1 * lift, -12 * lift),
            ShovelTip = Digging.ShovelTip!.Value + new Vector(-24 * lift, -117 * lift)
        };
    }

    private static double ErrorShake(MascotFrame frame) => frame.Still ? 0 :
        Math.Sin(frame.Seconds * Math.Tau * 3.1) * Math.Clamp(1 - frame.Seconds / LopataMascotMotion.ErrorSeconds, 0, 1);

    private static Pose ErrorPose(MascotFrame frame)
    {
        double wave = ErrorShake(frame);
        return Alarmed with
        {
            BodyAngle = -9 + 5 * wave,
            RightHand = Alarmed.RightHand + new Vector(10 * wave, -14 * wave),
            ShovelTip = Alarmed.ShovelTip!.Value + new Vector(25 * wave, 13 * Math.Abs(wave))
        };
    }

    private static Pose DancePose(MascotFrame frame)
    {
        if (frame.Still) return Dancing;
        double sway = frame.Sway, lift = frame.Accent;
        return Dancing with
        {
            BodyOffset = new Vector(5 * sway, -11 * lift),
            BodyAngle = -8 * sway,
            LeftKnee = new Point(-43 - 9 * sway, 107 - 11 * Math.Max(0, -sway) * lift),
            RightKnee = new Point(47 + 8 * sway, 107 - 11 * Math.Max(0, sway) * lift),
            LeftFoot = new Point(-55, 136 - 23 * Math.Max(0, -sway) * lift),
            RightFoot = new Point(66, 136 - 23 * Math.Max(0, sway) * lift),
            LeftBootAngle = -7 + 13 * Math.Min(0, sway) * lift,
            RightBootAngle = 5 - 24 * Math.Max(0, sway) * lift,
            ShovelHandle = Dancing.ShovelHandle!.Value + new Vector(10 * sway, -10 * lift),
            ShovelTip = Dancing.ShovelTip!.Value + new Vector(10 * sway, -10 * lift)
        };
    }

    private static Point Grip(Pose pose, double fraction) => pose.ShovelHandle is Point start && pose.ShovelTip is Point tip
        ? start + (tip - start) * fraction : pose.LeftHand;

    private static void DrawLimb(DrawingContext dc, Point shoulder, Point elbow, Point wrist, double width)
    {
        DrawTube(dc, shoulder, elbow, width);
        DrawTube(dc, elbow, wrist, width * .88);
        dc.DrawEllipse(Joint,
            new Pen(ColorBrush("#343C3F"), .8), elbow, width * .44, width * .44);
    }

    private static void DrawTube(DrawingContext dc, Point from, Point to, double width)
    {
        var direction = to - from;
        dc.PushTransform(new TranslateTransform(from.X, from.Y));
        dc.PushTransform(new RotateTransform(Math.Atan2(direction.Y, direction.X) * 180 / Math.PI));

        var bounds = new Rect(-3, -width / 2, direction.Length + 6, width);
        dc.DrawRoundedRectangle(Tube, new Pen(ColorBrush("#252C2E"), 1.5), bounds, width / 2, width / 2);
        dc.PushClip(new RectangleGeometry(bounds, width / 2, width / 2));
        for (double x = 7; x < direction.Length - 4; x += 8.5)
        {
            Mark(dc, new(x, -width * .40), new(x - 1, width * .39), "#444A4A", 1.3);
            Mark(dc, new(x + 1.3, -width * .33), new(x + .5, width * .21), "#6F7774", .75);
        }
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private static void DrawBoot(DrawingContext dc, Point ankle, double rotation, bool left)
    {
        dc.PushTransform(new TranslateTransform(ankle.X, ankle.Y));
        dc.PushTransform(new RotateTransform(rotation));
        if (left) dc.PushTransform(new ScaleTransform(-1, 1));
        Shape(dc, "M -15,8 Q -16,-3 -11,-12 Q -6,-17 6,-15 Q 13,-14 16,-7 " +
            "Q 33,-7 37,7 Q 40,15 29,17 L -9,18 Q -17,17 -15,8 Z", BootLeather, "#202728", 1.5);
        Shape(dc, "M -15,13 Q -5,16 9,14 L 37,12 L 35,18 Q 28,21 -8,21 Q -16,20 -15,13 Z",
            ColorBrush("#303735"), "#1D2425", 1.1);
        Shape(dc, "M 15,-2 Q 28,-7 33,4 Q 30,1 20,3 Z", ColorBrush("#757771"));
        Shape(dc, "M -10,-8 Q 1,-8 11,-11 L 14,-6 Q 5,-2 -9,-3 Z", ColorBrush("#797B74"), "#404742", .8);
        if (left) dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private static void DrawGlove(DrawingContext dc, Point wrist, double rotation, bool left, bool open)
    {
        dc.PushTransform(new TranslateTransform(wrist.X, wrist.Y));
        dc.PushTransform(new RotateTransform(rotation));
        if (left) dc.PushTransform(new ScaleTransform(-1, 1));
        if (open)
        {
            Shape(dc, "M -11,10 Q -22,8 -21,2 Q -18,-2 -11,2 L -9,-10 Q -8,-17 -5,-17 " +
                "Q -1,-17 -1,-10 L -2,-6 L 2,-19 Q 4,-23 7,-21 Q 11,-19 8,-12 L 7,-6 " +
                "L 14,-16 Q 17,-19 20,-16 Q 22,-14 17,-7 L 14,-2 L 21,-7 Q 25,-8 26,-5 " +
                "Q 27,-2 20,4 Q 14,10 13,15 Q 8,22 0,20 Q -7,18 -11,10 Z",
                Glove, "#645F57", 1.3);
            Shape(dc, "M -9,4 Q 0,3 3,10 Q 6,4 12,2", null, "#928B7D", 1.25);
        }
        else
        {
            Shape(dc, "M -10,-11 Q -4,-16 4,-13 Q 10,-13 12,-8 L 16,-4 Q 22,0 16,5 " +
                "Q 19,10 12,14 Q 8,18 -1,17 Q -12,17 -15,8 Q -19,-1 -10,-11 Z",
                Glove, "#655F55", 1.3);
            Shape(dc, "M 1,-10 Q 4,-6 4,-2 M 10,-8 Q 12,-4 10,0 M 15,1 Q 10,0 5,2 " +
                "Q 2,4 1,7 Q 5,7 9,6 M -9,4 Q -9,11 -5,13", null, "#928A7D", 1.25);
        }
        Shape(dc, open ? "M -9,12 Q 1,15 10,13 L 9,20 Q 0,23 -8,20 Z"
            : "M -11,-12 Q -4,-16 7,-12 L 8,-8 Q -2,-9 -12,-6 Z", ColorBrush("#B9B1A1"), "#7F7669", .8);
        if (left) dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private static void DrawShovel(DrawingContext dc, Point handle, Point tip, double detailScale)
    {
        var axis = tip - handle;
        dc.PushTransform(new TranslateTransform(handle.X, handle.Y));
        dc.PushTransform(new RotateTransform(Math.Atan2(axis.Y, axis.X) * 180 / Math.PI));
        dc.PushTransform(new ScaleTransform(detailScale, detailScale));
        double length = axis.Length / detailScale;
        var grip = new Rect(1, -5, length - 39, 10);
        dc.DrawRoundedRectangle(Shaft,
            new Pen(ColorBrush("#49341F"), 1), grip, 4.7, 4.7);
        Mark(dc, new(8, -2.2), new(length - 43, -2.2), "#D0A46B", 1.35);
        dc.DrawRoundedRectangle(Handle,
            new Pen(ColorBrush("#302B27"), .9), new Rect(-3, -7, 23, 14), 6, 6);
        dc.PushTransform(new TranslateTransform(length, 0));
        Shape(dc, "M -57,-6 L -43,-8 Q -49,-15 -44,-22 C -27,-24 -10,-13 0,0 " +
            "C -10,13 -27,24 -44,22 Q -49,15 -43,8 L -57,6 Z", Steel, "#344145", 1.45);
        Shape(dc, "M -42,-19 Q -26,-21 -6,-4 Q -17,-10 -41,-12 Z", ColorBrush("#B8C1BA"));
        Shape(dc, "M -54,-5 Q -34,-6 -17,0 Q -34,6 -54,5 Z",
            Socket, "#57676B", .9);
        Shape(dc, "M -42,18 Q -22,17 -5,5 Q -16,19 -41,21 Z", ColorBrush("#788587"));
        dc.Pop();
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private static void Shape(DrawingContext dc, string path, Brush? brush, string? stroke = null, double thickness = 1)
    {
        if (!Shapes.TryGetValue(path, out var shape))
        {
            shape = Geometry.Parse(path);
            shape.Freeze();
            Shapes.Add(path, shape);
        }
        dc.DrawGeometry(brush, stroke is null ? null : new Pen(ColorBrush(stroke), thickness), shape);
    }

    private static void Mark(DrawingContext dc, Point start, Point end, string color, double width = 2.4) =>
        dc.DrawLine(new Pen(ColorBrush(color), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, start, end);

    private static SolidColorBrush ColorBrush(string value)
    {
        if (Colors.TryGetValue(value, out var brush)) return brush;
        brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        Colors.Add(value, brush);
        return brush;
    }

    private static LinearGradientBrush Linear(string first, string last, Point start, Point end)
    {
        var brush = new LinearGradientBrush(ColorBrush(first).Color, ColorBrush(last).Color, start, end);
        brush.Freeze();
        return brush;
    }

    private static RadialGradientBrush Radial(string center, string edge, Point origin, double radiusX, double radiusY)
    {
        var brush = new RadialGradientBrush(ColorBrush(center).Color, ColorBrush(edge).Color)
        { Center = origin, GradientOrigin = origin, RadiusX = radiusX, RadiusY = radiusY };
        brush.Freeze();
        return brush;
    }
}

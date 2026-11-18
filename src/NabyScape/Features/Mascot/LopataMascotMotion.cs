using System;
using System.Windows;

namespace NabyScape;

internal readonly record struct MascotFrame(MascotState State, bool Still, Vector Gaze = default,
    double Seconds = 0, double Sway = 0, double Accent = 0);

internal sealed class LopataMascotMotion
{
    internal const double ErrorSeconds = 1.55;
    private double elapsed, baseline, lastPeak, sinceBeat, beatStrength, sway, accent;
    private int direction = -1;
    private Vector gaze, targetGaze;

    internal MascotState State { get; private set; }
    internal bool NeedsFrame => State != MascotState.Idle || gaze != targetGaze;

    internal bool SetActivity(bool working, bool playing, bool error)
    {
        var next = error ? MascotState.Error : working ? MascotState.Work : playing ? MascotState.Music : MascotState.Idle;
        if (next == State) return false;
        State = next;
        elapsed = 0;
        baseline = lastPeak = beatStrength = sway = accent = 0;
        sinceBeat = 1;
        direction = -1;
        return true;
    }

    internal void RestartError() => elapsed = 0;

    internal void LookToward(Vector offset)
    {
        targetGaze = new Vector(Math.Clamp(offset.X, -8, 8), Math.Clamp(offset.Y, -5, 5));
    }

    internal void Advance(double seconds, double peak, bool fresh)
    {
        elapsed += seconds;
        if (State == MascotState.Idle)
        {
            gaze += (targetGaze - gaze) * Smooth(seconds, .13);
            if ((gaze - targetGaze).LengthSquared < .01) gaze = targetGaze;
            return;
        }
        if (State != MascotState.Music) return;

        sinceBeat += seconds;
        if (fresh)
        {
            peak = Math.Clamp(peak, 0, 1);
            // A rising accent must stand above recent energy. The floor and cooldown
            // keep silence and a steady note from being mistaken for a rhythm.
            if (sinceBeat >= .16 && peak > Math.Max(.022, baseline * 1.30 + .008) && peak > lastPeak + .009)
            {
                direction = -direction;
                sinceBeat = 0;
                beatStrength = .62 + Math.Min(.38, peak);
            }
            baseline = baseline * .94 + peak * .06;
            lastPeak = peak;
        }
        double kick = sinceBeat < .16 ? beatStrength : beatStrength * Math.Exp(-(sinceBeat - .16) / .14);
        accent += (kick - accent) * Smooth(seconds, .07);
        sway += (direction * kick - sway) * Smooth(seconds, .085);
        if (Math.Abs(sway) < .003) sway = 0;
        if (accent < .003) accent = 0;
    }

    internal MascotFrame Frame(bool animate) => !animate ? new(State, true) : State switch
    {
        MascotState.Idle => new(State, false, gaze),
        MascotState.Music => new(State, false, Sway: sway, Accent: accent),
        _ => new(State, false, Seconds: elapsed)
    };

    private static double Smooth(double seconds, double duration) => 1 - Math.Exp(-seconds / duration);
}

using System;
using System.Windows;

namespace NabyScape;

internal readonly record struct MascotFrame(MascotState State, bool Still, Vector Gaze = default,
    double Seconds = 0, double Sway = 0, double Accent = 0);

internal sealed class LopataMascotMotion
{
    internal const double ErrorSeconds = 1.55;
    private const double QuietLevel = .008;
    private const double GuessedBeatInterval = .48;
    private double elapsed, baseline, lastPeak, sinceBeat, interval, phase, phaseCorrection;
    private double amplitude, accent, sway;
    private int beatDirection;
    private Vector gaze, targetGaze;

    internal MascotState State { get; private set; }
    internal bool NeedsFrame => State != MascotState.Idle || gaze != targetGaze;

    internal bool SetActivity(bool working, bool playing, bool error)
    {
        var next = error ? MascotState.Error : working ? MascotState.Work : playing ? MascotState.Music : MascotState.Idle;
        if (next == State) return false;
        State = next;
        elapsed = 0;
        baseline = lastPeak = amplitude = phase = phaseCorrection = sway = accent = 0;
        sinceBeat = double.PositiveInfinity;
        interval = GuessedBeatInterval;
        beatDirection = 0;
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

        AdvanceMusic(seconds, Math.Clamp(peak, 0, 1), fresh);
    }

    private void AdvanceMusic(double seconds, double peak, bool fresh)
    {
        sinceBeat += seconds;
        if (fresh)
        {
            // Peaks correct the direction and pace. The oscillator keeps moving
            // through audible sections without a clear new attack.
            if (sinceBeat >= .21 && peak > Math.Max(.022, baseline * 1.30 + .008) && peak > lastPeak + .009)
                MarkBeat();
            baseline += (peak - baseline) * .055;
            lastPeak = peak;
        }

        double target = peak > QuietLevel ? .33 + .62 * Math.Sqrt(peak) : 0;
        amplitude += (target - amplitude) * Smooth(seconds, target > amplitude ? .10 : .23);
        if (amplitude < .006 && target == 0)
        {
            amplitude = sway = accent = phase = phaseCorrection = 0;
            return;
        }

        double correction = phaseCorrection * Smooth(seconds, .14);
        phaseCorrection -= correction;
        phase = (phase + Math.PI * seconds / interval + correction) % Math.Tau;
        double swing = Math.Sin(phase);
        sway = swing * amplitude;
        accent = amplitude * (.18 + .82 * Math.Abs(swing));
    }

    private void MarkBeat()
    {
        if (sinceBeat is >= .26 and <= 1.05)
            interval += (sinceBeat - interval) * .42;
        if (sinceBeat > 1.6 && amplitude < .025)
        {
            phase = phaseCorrection = 0;
            beatDirection = 0;
        }
        else
        {
            // Catch up gradually so a late accent never teleports the shovel.
            beatDirection = 1 - beatDirection;
            phaseCorrection = Math.Clamp(Math.IEEERemainder(beatDirection * Math.PI - phase, Math.Tau),
                -Math.PI * .5, Math.PI * .5);
        }
        sinceBeat = 0;
    }

    internal MascotFrame Frame(bool animate) => !animate ? new(State, true) : State switch
    {
        MascotState.Idle => new(State, false, gaze),
        MascotState.Music => new(State, false, Sway: sway, Accent: accent),
        _ => new(State, false, Seconds: elapsed)
    };

    private static double Smooth(double seconds, double duration) => 1 - Math.Exp(-seconds / duration);
}

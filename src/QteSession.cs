using System;

namespace MimesisEscape;

public enum QteState { Idle, Active, Succeeded, Failed, Cancelled }

// Contains no Unity, loader, input-device or network code. The caller supplies a monotonic clock.
public sealed class QteSession
{
    private long lastCapture = long.MinValue;
    private bool armed;
    public QteState State { get; private set; }
    public int Progress { get; private set; }
    public int Required { get; private set; }
    public double Deadline { get; private set; }

    public bool Begin(long capture, double now, int required, double duration, bool keyAlreadyDown)
    {
        if (capture == lastCapture || State == QteState.Active) return false;
        if (!Finite(now) || !Finite(duration) || duration <= 0 || required < 1)
            throw new ArgumentOutOfRangeException(nameof(duration));
        lastCapture = capture;
        Progress = 0;
        Required = required;
        Deadline = now + duration;
        armed = !keyAlreadyDown;
        State = QteState.Active;
        return true;
    }

    // Returns true only on the transition to success. A press requires an observed release.
    public bool Tick(double now, bool down, bool pressedThisFrame, bool inputAllowed)
    {
        if (State != QteState.Active) return false;
        if (!Finite(now) || now >= Deadline)
        {
            State = QteState.Failed;
            return false;
        }
        if (!inputAllowed) { armed = false; return false; }
        if (!down) { armed = true; return false; }
        if (!armed) return false;
        armed = false;
        if (!pressedThisFrame) return false;
        Progress++;
        if (Progress < Required) return false;
        State = QteState.Succeeded;
        return true;
    }

    public void Cancel()
    {
        if (State == QteState.Active) State = QteState.Cancelled;
    }

    public double Remaining(double now) => Math.Max(0, Deadline - now);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

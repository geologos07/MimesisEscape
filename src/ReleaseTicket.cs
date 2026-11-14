using System;
using System.Security.Cryptography;

namespace MimesisEscape;

// The host binds a one-use capability to one authenticated victim and one native attach boundary.
internal sealed class ReleaseTicket
{
    internal readonly Guid Token = CreateToken();
    internal readonly int Victim, Caster;
    internal readonly long NativeStart, Deadline;
    private bool consumed;

    private static Guid CreateToken()
    {
        var bytes = new byte[16];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        return new Guid(bytes);
    }

    internal ReleaseTicket(int victim, int caster, long nativeStart, long deadline)
    {
        if (deadline <= nativeStart) throw new ArgumentOutOfRangeException(nameof(deadline));
        Victim = victim; Caster = caster; NativeStart = nativeStart; Deadline = deadline;
    }

    internal long Remaining(long now) => Math.Max(0, Deadline - now);

    internal bool TryConsume(Guid token, int authenticatedVictim, int currentCaster, long currentStart,
        long now, bool nativeCaptureValid)
    {
        if (consumed || !nativeCaptureValid || token != Token || authenticatedVictim != Victim ||
            currentCaster != Caster || currentStart != NativeStart || now < NativeStart || now >= Deadline)
            return false;
        consumed = true; // Latch before calling the game, including when that call rejects the request.
        return true;
    }

    // Subtract the entire measured request/response time: conservative, no shared host/client clock needed.
    internal static double ClientWindow(double remainingSeconds, double requestSentAt, double receivedAt)
    {
        if (double.IsNaN(remainingSeconds) || double.IsInfinity(remainingSeconds) ||
            double.IsNaN(requestSentAt) || double.IsInfinity(requestSentAt) ||
            double.IsNaN(receivedAt) || double.IsInfinity(receivedAt) || receivedAt < requestSentAt)
            return 0;
        return Math.Max(0, remainingSeconds - (receivedAt - requestSentAt));
    }
}

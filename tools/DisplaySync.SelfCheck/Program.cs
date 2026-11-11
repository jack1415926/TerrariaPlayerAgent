using System;
using TerrariaPlayerAgent.Common;

namespace TerrariaPlayerAgent.SelfCheck;

// Deterministic replay checks for Common/DisplaySync.cs. No game, no network, no packages.
// Velocities below are Terraria world units per simulation tick (60 ticks per second).
internal static class Program
{
    private static int failures, checks;

    private static int Main()
    {
        NormalMovement();
        InterpolationDelayIsHonoured();
        StopHoldsAuthority();
        DirectionReversal();
        IrregularIntervals();
        ExtrapolationUsesTicksPerSecond();
        ExtrapolationIsCappedAndContinuous();
        MovingThenStoppingDoesNotOvershoot();
        OutOfOrderAndDuplicate();
        TeleportResetsPlayback();
        ResetClearsDisplayAndCounters();
        CorrectionHistoryIsBounded();
        StaleRecoveryBehindPrediction();
        StaleRecoveryAheadOfPrediction();
        StaleRecoveryWithReversedVelocity();
        StaleRecoveryThenStop();
        RepeatedOutagesStayBounded();
        BuffersStayBounded();
        NormalPlaybackIsNotRateLimited();

        Console.WriteLine(failures == 0
            ? $"SELF-CHECK OK ({checks} checks)"
            : $"SELF-CHECK FAILED ({failures}/{checks})");
        return failures == 0 ? 0 : 1;
    }

    private static SyncSnapshot Snap(uint sequence, double receivedAt, float x, float y = 0f,
        float vx = 2f, float vy = 0f, long tick = 0) =>
        new()
        {
            Sequence = sequence, ServerTick = tick, X = x, Y = y,
            VelocityX = vx, VelocityY = vy, ReceivedAt = receivedAt
        };

    private static void NormalMovement()
    {
        var sync = new DisplaySync();
        for (uint i = 0; i < 6; i++)
            Check($"accept seq {i}", sync.Add(Snap(i + 1, i * 0.1, i * 12f)) == SnapshotResult.Accepted);

        Check("sample exists", sync.TrySample(0.35, out DisplaySample sample));
        Near("interpolates between snapshots", 27.6f, sample.X, 0.01f); // 24 + 12 * 0.3
        Check("not extrapolated", !sample.Extrapolated);
        Check("not stale", !sample.Stale);

        sync.TrySample(0.1, out DisplaySample early);
        Near("starts at oldest snapshot", 0f, early.X, 0.01f);
    }

    private static void InterpolationDelayIsHonoured()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 0f, vx: 1f));
        sync.Add(Snap(2, 0.1, 6f, vx: 1f));
        Check("delayed samples", sync.TrySample(0.15, out DisplaySample sample));
        // now=0.15 minus the 0.12 delay renders t=0.03: 6 * 0.3. Using now would show 6.
        Near("uses the delayed render time", 1.8f, sample.X, 0.01f);
        Check("delay sample is interpolated", !sample.Extrapolated);
    }

    private static void StopHoldsAuthority()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 50f, vx: 0f));
        sync.Add(Snap(2, 0.1, 50f, vx: 0f));
        sync.Add(Snap(3, 0.2, 50f, vx: 0f));

        Check("stop samples", sync.TrySample(0.6, out DisplaySample sample));
        Near("stopped actor stays at authority", 50f, sample.X, 0.001f);
        Check("stop is not extrapolated", !sample.Extrapolated);
        Check("stop is not stale before the stale window", !sample.Stale);

        sync.TrySample(0.2 + sync.StaleAfter + 0.01, out DisplaySample late);
        Check("stale after silence", late.Stale);
        Near("stale holds the last authority", 50f, late.X, 0.001f);
    }

    private static void DirectionReversal()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 0f));
        sync.Add(Snap(2, 0.1, 12f));
        sync.Add(Snap(3, 0.2, 6f, vx: -1f));
        Check("reversal samples", sync.TrySample(0.25, out DisplaySample sample));
        Near("reversal interpolates backwards", 10.2f, sample.X, 0.01f); // 12 + (6-12) * 0.3
    }

    private static void IrregularIntervals()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.00, 0f));
        sync.Add(Snap(2, 0.06, 6f));
        sync.Add(Snap(3, 0.20, 20f));
        sync.Add(Snap(4, 0.27, 27f));
        Check("irregular samples", sync.TrySample(0.20, out DisplaySample sample));
        Near("uses receive times", 8f, sample.X, 0.05f);
        Check("stays inside the segment", sample.X >= 6f && sample.X <= 20f);
    }

    private static void ExtrapolationUsesTicksPerSecond()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 0f, vx: 3f));
        sync.Add(Snap(2, 0.1, 10f, vx: 3f)); // 3 world units per tick

        Check("extrapolates during a gap", sync.TrySample(0.35, out DisplaySample sample));
        Check("gap sample is extrapolated", sample.Extrapolated);
        // render time 0.23, latest 0.10: 0.13 s ahead = 7.8 ticks, 3 * 7.8 = 23.4 px.
        Near("prediction converts ticks to pixels", 33.4f, sample.X, 0.01f);
        Near("predicted displacement", 23.4f, sample.X - 10f, 0.01f);
    }

    private static void ExtrapolationIsCappedAndContinuous()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 0f, vx: 3f));
        sync.Add(Snap(2, 0.1, 10f, vx: 3f));

        // 0.15 s at 60 ticks/s = 9 ticks; 3 * 9 = 27 px, so the display freezes at 37.
        Check("capped sample", sync.TrySample(0.5, out DisplaySample capped));
        Near("stops at the 150 ms endpoint", 37f, capped.X, 0.01f);

        Check("still capped", sync.TrySample(0.55, out DisplaySample stillCapped));
        Near("cap holds before staleness", 37f, stillCapped.X, 0.01f);

        sync.TrySample(0.59, out DisplaySample beforeStale);
        sync.TrySample(0.62, out DisplaySample afterStale);
        Check("stale flag crosses the threshold", !beforeStale.Stale && afterStale.Stale);
        Near("no backward jump when going stale", 0f, afterStale.X - beforeStale.X, 0.01f);
        Near("stale keeps the predicted endpoint", 37f, afterStale.X, 0.01f);
    }

    private static void MovingThenStoppingDoesNotOvershoot()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 0f, vx: 3f));
        sync.Add(Snap(2, 0.1, 18f, vx: 3f));
        sync.Add(Snap(3, 0.2, 36f, vx: 0f));
        sync.Add(Snap(4, 0.3, 36f, vx: 0f));

        sync.TrySample(0.25, out DisplaySample moving);
        Near("still moves before the stop", 23.4f, moving.X, 0.01f);

        for (int step = 0; step <= 10; step++)
        {
            double now = 0.4 + step * 0.05;
            sync.TrySample(now, out DisplaySample sample);
            if (sample.X > 36.001f || sample.X < 35.999f)
            {
                Check($"stopped display stays at 36 (now={now:F2}, x={sample.X:F3})", false);
                return;
            }
        }
        Check("stopped display never overshoots", true);
    }

    private static void OutOfOrderAndDuplicate()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(5, 0.1, 50f));
        Check("duplicate rejected", sync.Add(Snap(5, 0.1, 50f)) == SnapshotResult.Duplicate);
        Check("old snapshot rejected", sync.Add(Snap(3, 0.0, 0f)) == SnapshotResult.OutOfOrder);
        Check("counters", sync.DuplicateCount == 1 && sync.OutOfOrderCount == 1);
        sync.TrySample(0.2, out DisplaySample sample);
        Near("old snapshot never overwrites", 50f, sample.X, 0.001f);
    }

    private static void TeleportResetsPlayback()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 0f));
        sync.Add(Snap(2, 0.1, 500f));
        Check("teleport counted", sync.TeleportCount >= 1);
        Check("teleport keeps only the new snapshot", sync.HasSnapshots);
        sync.TrySample(0.12, out DisplaySample sample);
        Near("teleport shows the new position at once", 500f, sample.X, 0.001f);
    }

    private static void ResetClearsDisplayAndCounters()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 10f));
        sync.TrySample(0.1, out _);
        sync.RecordCorrection(2.5f, 0.1);
        sync.Reset();

        Check("reset drops snapshots", !sync.HasSnapshots);
        Check("reset stops sampling", !sync.TrySample(0.2, out _));
        Check("reset clears per-instance counters",
            sync.ReceivedCount == 0 && sync.CorrectionCount == 0 &&
            sync.ExtrapolatedFrames == 0 && sync.LastCorrection == 0f && sync.MaxCorrection == 0f);
        Check("reset allows a new sequence", sync.Add(Snap(1, 0.3, 99f)) == SnapshotResult.Accepted);
    }

    private static void CorrectionHistoryIsBounded()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 0f));
        for (int i = 0; i < DisplaySync.CorrectionHistory + 10; i++)
            sync.RecordCorrection(i, 0.1 + i * 0.001);
        Check("history is bounded", sync.RecentCorrections.Count == DisplaySync.CorrectionHistory);
        Check("correction counters", sync.CorrectionCount == DisplaySync.CorrectionHistory + 10);
        Check("max correction tracked", sync.MaxCorrection == DisplaySync.CorrectionHistory + 9);
    }

    private static void StaleRecoveryBehindPrediction()
    {
        var (sync, previous, maxStep, corrected) = FreezePrediction();
        RunFrames(sync, 0.7, 0.8, ref previous, ref maxStep, ref corrected);

        // The actor actually stopped during the outage, so truth is behind the frozen prediction.
        sync.Add(Snap(3, 0.8, 10f, vx: 0f));
        RunFrames(sync, 0.8, 2.3, ref previous, ref maxStep, ref corrected);

        Check("recovery behind is rate limited", corrected);
        Check("recovery behind never jumps more than one step", maxStep <= 10.01f);
        Near("recovery behind converges to authority", 10f, previous, 0.5f);
    }

    private static void StaleRecoveryAheadOfPrediction()
    {
        var (sync, previous, maxStep, corrected) = FreezePrediction();
        RunFrames(sync, 0.7, 0.8, ref previous, ref maxStep, ref corrected);
        sync.Add(Snap(3, 0.8, 60f, vx: 3f)); // truth is ahead of the frozen endpoint
        RunFrames(sync, 0.8, 2.8, ref previous, ref maxStep, ref corrected);

        Check("recovery ahead is rate limited", corrected);
        Check("recovery ahead never jumps more than one step", maxStep <= 10.01f);
        Near("recovery ahead converges to the new authority", 87f, previous, 0.5f);
    }

    private static void StaleRecoveryWithReversedVelocity()
    {
        var (sync, previous, maxStep, corrected) = FreezePrediction();
        RunFrames(sync, 0.7, 0.8, ref previous, ref maxStep, ref corrected);
        sync.Add(Snap(3, 0.8, 5f, vx: -3f)); // the actor reversed during the outage
        RunFrames(sync, 0.8, 2.8, ref previous, ref maxStep, ref corrected);

        Check("reversed recovery is rate limited", corrected);
        Check("reversed recovery never jumps more than one step", maxStep <= 10.01f);
        Check("reversed recovery moves backwards smoothly", previous < 0f);
        Near("reversed recovery converges", -22f, previous, 0.5f);
    }

    private static void StaleRecoveryThenStop()
    {
        var (sync, previous, maxStep, corrected) = FreezePrediction();
        RunFrames(sync, 0.7, 0.8, ref previous, ref maxStep, ref corrected);
        sync.Add(Snap(3, 0.8, 10f, vx: 0f));
        sync.Add(Snap(4, 0.9, 10f, vx: 0f)); // stops immediately after recovery
        RunFrames(sync, 0.8, 2.3, ref previous, ref maxStep, ref corrected);

        Near("recovery then stop settles", 10f, previous, 0.5f);
        float settled = previous;
        float settleStep = 0f;
        bool unused = false;
        RunFrames(sync, 2.3, 2.8, ref previous, ref settleStep, ref unused);
        Check("recovery then stop does not drift", settleStep <= 0.01f);
        Near("recovery then stop holds position", settled, previous, 0.01f);
    }

    private static void RepeatedOutagesStayBounded()
    {
        var sync = new DisplaySync();
        uint sequence = 1;
        double time = 0;
        float x = 0f;
        float previous = 0f;
        float maxStep = 0f;
        bool corrected = false;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            sync.Add(Snap(sequence++, time, x, vx: 3f));
            time += 0.1; x += 18f;
            sync.Add(Snap(sequence++, time, x, vx: 3f));

            double staleAt = time + 0.8;
            Check($"cycle {cycle} goes stale", sync.TrySample(staleAt, out DisplaySample stale) && stale.Stale);
            previous = stale.X;
            RunFrames(sync, staleAt, staleAt + 0.2, ref previous, ref maxStep, ref corrected);

            time = staleAt + 0.2; x += 10f;
            sync.Add(Snap(sequence++, time, x, vx: 0f));
            RunFrames(sync, time, time + 0.7, ref previous, ref maxStep, ref corrected);
            Near($"cycle {cycle} converges after recovery", x, previous, 0.5f);
        }
        Check("repeated outages stay rate limited", maxStep <= 10.01f);
        Check("buffer stays bounded", sync.BufferedSnapshots <= DisplaySync.Capacity);
        Check("corrections stay bounded", sync.RecentCorrections.Count <= DisplaySync.CorrectionHistory);
    }

    private static void BuffersStayBounded()
    {
        var sync = new DisplaySync();
        for (uint i = 1; i <= 40; i++) sync.Add(Snap(i, i * 0.05, i * 2f));
        Check("snapshot buffer capped", sync.BufferedSnapshots == DisplaySync.Capacity);
        Check("all snapshots counted", sync.ReceivedCount == 40);
        for (int i = 0; i < DisplaySync.CorrectionHistory + 20; i++) sync.RecordCorrection(i, i * 0.001);
        Check("correction history capped", sync.RecentCorrections.Count == DisplaySync.CorrectionHistory);
    }

    private static void NormalPlaybackIsNotRateLimited()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 0f, vx: 3f));
        sync.Add(Snap(2, 0.1, 18f, vx: 3f));
        sync.TrySample(0.12, out _);

        // A large target with no outage gap must be assigned directly, not eased for half a second.
        sync.Add(Snap(3, 0.15, 90f, vx: 3f));
        Check("normal playback samples", sync.TrySample(0.3, out DisplaySample sample));
        Check("normal playback is not rate limited", !sample.Correcting);
        Near("normal playback reaches the target", 95.4f, sample.X, 0.01f);
    }

    // Two moving snapshots frozen by a long outage; the display ends at the 9-tick prediction.
    private static (DisplaySync sync, float previous, float maxStep, bool corrected) FreezePrediction()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 0f, vx: 3f));
        sync.Add(Snap(2, 0.1, 10f, vx: 3f));
        sync.TrySample(0.7, out DisplaySample frozen);
        Check("freeze is stale", frozen.Stale);
        Near("freeze sits on the prediction endpoint", 37f, frozen.X, 0.01f);
        return (sync, frozen.X, 0f, false);
    }

    private static void RunFrames(DisplaySync sync, double from, double to,
        ref float previous, ref float maxStep, ref bool corrected)
    {
        int frames = (int)Math.Round((to - from) * 60);
        for (int i = 1; i <= frames; i++)
            Frame(sync, from + i / 60.0, ref previous, ref maxStep, ref corrected);
    }

    private static void Frame(DisplaySync sync, double now, ref float previous, ref float maxStep, ref bool corrected)
    {
        sync.TrySample(now, out DisplaySample sample);
        maxStep = MathF.Max(maxStep, MathF.Abs(sample.X - previous));
        previous = sample.X;
        if (sample.Correcting) corrected = true;
    }
    private static void Near(string name, float expected, float actual, float tolerance)
    {
        bool ok = MathF.Abs(expected - actual) <= tolerance;
        Check($"{name} (expected {expected}, got {actual})", ok);
    }

    private static void Check(string name, bool ok)
    {
        checks++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
        if (!ok) failures++;
    }
}

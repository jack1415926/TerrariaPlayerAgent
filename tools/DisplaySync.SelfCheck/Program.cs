using System;
using TerrariaPlayerAgent.Common;

namespace TerrariaPlayerAgent.SelfCheck;

// Deterministic replay checks for Common/DisplaySync.cs. No game, no network, no packages.
internal static class Program
{
    private static int failures;

    private static int Main()
    {
        NormalMovement();
        StopHoldsAuthority();
        DirectionReversal();
        IrregularIntervals();
        DelayAndLoss();
        OutOfOrderAndDuplicate();
        TeleportResetsPlayback();
        ResetClearsDisplay();
        CorrectionHistoryIsBounded();

        Console.WriteLine(failures == 0 ? "SELF-CHECK OK" : $"SELF-CHECK FAILED ({failures})");
        return failures == 0 ? 0 : 1;
    }

    private static SyncSnapshot Snap(uint sequence, double receivedAt, float x, float y = 0f,
        float vx = 100f, float vy = 0f, long tick = 0) =>
        new()
        {
            Sequence = sequence, ServerTick = tick, X = x, Y = y,
            VelocityX = vx, VelocityY = vy, ReceivedAt = receivedAt
        };

    private static void NormalMovement()
    {
        var sync = new DisplaySync();
        for (uint i = 0; i < 6; i++)
            Check($"accept seq {i}", sync.Add(Snap(i + 1, i * 0.1, i * 10f)) == SnapshotResult.Accepted);

        Check("sample exists", sync.TrySample(0.35, out DisplaySample sample));
        Near("interpolates between snapshots", 23f, sample.X, 0.01f);
        Check("not extrapolated", !sample.Extrapolated);
        Check("not stale", !sample.Stale);

        sync.TrySample(0.1, out DisplaySample early);
        Near("starts at oldest snapshot", 0f, early.X, 0.01f);
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
        Near("stale holds last authority", 50f, late.X, 0.001f);
    }

    private static void DirectionReversal()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 0f));
        sync.Add(Snap(2, 0.1, 10f));
        sync.Add(Snap(3, 0.2, 5f, vx: -50f));
        Check("reversal samples", sync.TrySample(0.25, out DisplaySample sample));
        Near("reversal interpolates backwards", 8.5f, sample.X, 0.01f);
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

    private static void DelayAndLoss()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 0f));
        sync.Add(Snap(2, 0.1, 10f));
        Check("extrapolates during a gap", sync.TrySample(0.35, out DisplaySample sample));
        Check("gap sample is extrapolated", sample.Extrapolated);
        Near("extrapolation is capped", 25f, sample.X, 0.01f); // 10 + 100 * 0.15

        sync.TrySample(0.35 + sync.StaleAfter, out DisplaySample stale);
        Check("long gap becomes stale", stale.Stale);
        Near("stale returns to authority", 10f, stale.X, 0.001f);
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

    private static void ResetClearsDisplay()
    {
        var sync = new DisplaySync();
        sync.Add(Snap(1, 0.0, 10f));
        sync.TrySample(0.1, out _);
        sync.Reset();
        Check("reset drops snapshots", !sync.HasSnapshots);
        Check("reset stops sampling", !sync.TrySample(0.2, out _));
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

    private static void Near(string name, float expected, float actual, float tolerance)
    {
        bool ok = MathF.Abs(expected - actual) <= tolerance;
        Check($"{name} (expected {expected}, got {actual})", ok);
    }

    private static void Check(string name, bool ok)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
        if (!ok) failures++;
    }
}

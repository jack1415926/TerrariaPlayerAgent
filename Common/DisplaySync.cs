using System;
using System.Collections.Generic;

namespace TerrariaPlayerAgent.Common;

// Client-side display smoothing for the multiplayer probe.
// This file deliberately has no Terraria or XNA dependency so it can be replayed offline.
public struct SyncSnapshot
{
    public uint Sequence;
    public long ServerTick;
    public float X, Y;
    public float VelocityX, VelocityY;
    public double ReceivedAt;
}

public struct DisplaySample
{
    public float X, Y;
    public bool Extrapolated;
    public bool Stale;
    public bool Correcting;
}

public enum SnapshotResult { Accepted, Duplicate, OutOfOrder }

public readonly struct CorrectionSample
{
    public readonly uint Sequence;
    public readonly long ServerTick;
    public readonly float Offset;
    public readonly double Time;

    public CorrectionSample(uint sequence, long serverTick, float offset, double time)
    {
        Sequence = sequence; ServerTick = serverTick; Offset = offset; Time = time;
    }
}

public sealed class DisplaySync
{
    // Bounded playback: two snapshot intervals of delay, a short extrapolation, then hold.
    public const int Capacity = 12;
    // Terraria simulates at 60 ticks per second; Player.velocity is world units per tick.
    public const double TicksPerSecond = 60.0;
    public double InterpolationDelay = 0.12;
    public double MaxExtrapolation = 0.15;
    public double StaleAfter = 0.5;
    public float TeleportDistance = 96f;
    public float StopSpeed = 0.05f;
    // Recovered snapshots can land far from the frozen prediction; move at this speed instead of jumping.
    public float MaxCorrectionSpeed = 600f;
    public const int CorrectionHistory = 64;

    private readonly List<SyncSnapshot> snapshots = new();
    private readonly List<CorrectionSample> corrections = new();
    private uint lastSequence;
    private bool hasSequence;
    private float lastSequenceX, lastSequenceY;
    private bool hasDisplay;
    private float displayX, displayY;
    private bool hasSampleTime;
    private double lastSampleTime;
    private bool resyncing;
    private double lastReceivedAt;

    public bool HasSnapshots => snapshots.Count > 0;
    public int BufferedSnapshots => snapshots.Count;
    public uint LastSequence => lastSequence;

    public long ReceivedCount { get; private set; }
    public long DuplicateCount { get; private set; }
    public long OutOfOrderCount { get; private set; }
    public long ExtrapolatedFrames { get; private set; }
    public long StaleFrames { get; private set; }
    public long TeleportCount { get; private set; }
    public long CorrectionCount { get; private set; }
    public float LastCorrection { get; private set; }
    public float MaxCorrection { get; private set; }

    public SnapshotResult Add(in SyncSnapshot snapshot)
    {
        if (hasSequence && snapshot.Sequence == lastSequence) { DuplicateCount++; return SnapshotResult.Duplicate; }
        if (hasSequence && snapshot.Sequence < lastSequence) { OutOfOrderCount++; return SnapshotResult.OutOfOrder; }

        if (hasSequence && Distance(lastSequenceX, lastSequenceY, snapshot.X, snapshot.Y) > TeleportDistance)
        {
            // A jump this large is a respawn/teleport, not movement: never interpolate across it.
            snapshots.Clear();
            TeleportCount++;
            resyncing = false;
        }
        else if (hasSequence && snapshot.ReceivedAt - lastReceivedAt > StaleAfter)
        {
            // Playback resumes after an outage: correct the frozen prediction smoothly, not by jumping.
            resyncing = true;
        }

        lastSequence = snapshot.Sequence;
        lastSequenceX = snapshot.X;
        lastSequenceY = snapshot.Y;
        lastReceivedAt = snapshot.ReceivedAt;
        hasSequence = true;
        snapshots.Add(snapshot);
        if (snapshots.Count > Capacity) snapshots.RemoveAt(0);
        ReceivedCount++;
        return SnapshotResult.Accepted;
    }

    // now and all ReceivedAt values use the same monotonic clock, in seconds.
    public bool TrySample(double now, out DisplaySample sample)
    {
        sample = default;
        if (snapshots.Count == 0) return false;

        SyncSnapshot latest = snapshots[^1];
        double renderTime = now - InterpolationDelay;
        float x, y;
        bool extrapolated = false, stale = false;

        if (renderTime <= snapshots[0].ReceivedAt)
        {
            x = snapshots[0].X; y = snapshots[0].Y;
        }
        else if (renderTime >= latest.ReceivedAt)
        {
            double silence = now - latest.ReceivedAt;
            double ahead = renderTime - latest.ReceivedAt;
            stale = silence > StaleAfter;
            float speed = MathF.Sqrt(latest.VelocityX * latest.VelocityX + latest.VelocityY * latest.VelocityY);
            if (ahead <= 0 || speed < StopSpeed)
            {
                // A stopped actor settles exactly on authority; a non-positive horizon never predicts.
                x = latest.X; y = latest.Y;
            }
            else
            {
                // velocity is world units per simulation tick, so convert the horizon into ticks.
                // Beyond MaxExtrapolation the position stays at the same endpoint: going stale must
                // not snap the display back to the last packet.
                double seconds = Math.Min(ahead, MaxExtrapolation);
                float ticks = (float)(seconds * TicksPerSecond);
                x = latest.X + latest.VelocityX * ticks;
                y = latest.Y + latest.VelocityY * ticks;
                extrapolated = true;
            }
        }
        else
        {
            int index = 1;
            while (index < snapshots.Count && snapshots[index].ReceivedAt <= renderTime) index++;
            SyncSnapshot from = snapshots[index - 1], to = snapshots[index];
            double span = to.ReceivedAt - from.ReceivedAt;
            float t = span <= 0 ? 1f : (float)((renderTime - from.ReceivedAt) / span);
            x = from.X + (to.X - from.X) * t;
            y = from.Y + (to.Y - from.Y) * t;
        }

        // Normal playback assigns the target directly. Only a post-outage resync eases the display
        // toward the new authority, so a frozen prediction never snaps backwards in one frame.
        float targetX = x, targetY = y;
        bool correcting = false;
        double elapsed = hasSampleTime ? now - lastSampleTime : 0;
        if (hasDisplay && resyncing && elapsed > 0)
        {
            float distance = Distance(displayX, displayY, targetX, targetY);
            float step = MaxCorrectionSpeed * (float)Math.Min(elapsed, 0.25);
            if (distance > step)
            {
                float scale = step / distance;
                displayX += (targetX - displayX) * scale;
                displayY += (targetY - displayY) * scale;
                correcting = true;
            }
            else
            {
                displayX = targetX; displayY = targetY;
                resyncing = false;
            }
        }
        else
        {
            displayX = targetX; displayY = targetY;
        }
        hasDisplay = true;
        hasSampleTime = true;
        lastSampleTime = now;
        if (extrapolated) ExtrapolatedFrames++;
        if (stale) StaleFrames++;

        sample = new DisplaySample
        {
            X = displayX, Y = displayY,
            Extrapolated = extrapolated, Stale = stale, Correcting = correcting
        };
        return true;
    }

    // Called by the client with the distance it had to move the mirror this frame.
    public void RecordCorrection(float offset, double now)
    {
        CorrectionCount++;
        LastCorrection = offset;
        if (offset > MaxCorrection) MaxCorrection = offset;
        CorrectionsAdd(offset, now);
    }

    private void CorrectionsAdd(float offset, double now)
    {
        uint sequence = snapshots.Count > 0 ? snapshots[^1].Sequence : 0;
        long tick = snapshots.Count > 0 ? snapshots[^1].ServerTick : 0;
        corrections.Add(new CorrectionSample(sequence, tick, offset, now));
        if (corrections.Count > CorrectionHistory) corrections.RemoveAt(0);
    }

    public IReadOnlyList<CorrectionSample> RecentCorrections => corrections;

    public bool TryGetLatest(out SyncSnapshot latest)
    {
        if (snapshots.Count == 0) { latest = default; return false; }
        latest = snapshots[^1];
        return true;
    }

    public void Reset()
    {
        snapshots.Clear();
        corrections.Clear();
        hasSequence = false;
        hasDisplay = false;
        lastSequence = 0;
        lastSequenceX = lastSequenceY = 0;
        displayX = displayY = 0;
        hasSampleTime = false;
        lastSampleTime = 0;
        resyncing = false;
        lastReceivedAt = 0;
        // Counters describe the current mirror instance; a new AI starts from zero.
        ReceivedCount = DuplicateCount = OutOfOrderCount = 0;
        ExtrapolatedFrames = StaleFrames = TeleportCount = CorrectionCount = 0;
        LastCorrection = MaxCorrection = 0f;
    }

    private static float Distance(float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

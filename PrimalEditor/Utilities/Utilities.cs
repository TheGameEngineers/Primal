// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
global using IdType = System.Int32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Windows.Threading;

namespace PrimalEditor.Utilities;

static class ID
{
    public static IdType INVALID_ID => -1;
    public static bool IsValid(IdType id) => id != INVALID_ID;
}

static class MathUtil
{
    public enum VectorAxis
    {
        X = 0,
        Y = 1,
        Z = 2
    }

    public static float Epsilon => 1e-5f;
    public static float Pi => (float)Math.PI;
    public static float HalfPi => Pi * 0.5f;

    public static float WrapAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0) angle += 360f;
        return angle;
    }

    public static bool IsTheSameAs(this float value, float other)
    {
        return Math.Abs(value - other) < Epsilon;
    }

    public static bool IsTheSameAs(this float? value, float? other)
    {
        if (!value.HasValue || !other.HasValue) return false;
        return Math.Abs(value.Value - other.Value) < Epsilon;
    }

    public static bool IsTheSameAs(this double value, double other)
    {
        return Math.Abs(value - other) < Epsilon;
    }

    // Align by rounding up. Will result in a multiple of 'alignment' that is greater than or equal to 'size'.
    public static long AlignSizeUp(long size, long alignment)
    {
        Debug.Assert(alignment > 0, "Alignment must be non-zero.");
        long mask = alignment - 1;
        Debug.Assert((alignment & mask) == 0, "Alignment should be a power of 2.");
        return (size + mask) & ~mask;
    }

    // Align by rounding down. Will result in a multiple of 'alignment' that is less than or equal to 'size'.
    public static long AlignSizeDown(long size, long alignment)
    {
        Debug.Assert(alignment > 0, "Alignment must be non-zero.");
        long mask = alignment - 1;
        Debug.Assert((alignment & mask) == 0, "Alignment should be a power of 2.");
        return size & ~mask;
    }

    public static bool IsPow2(int x)
    {
        return (x != 0) && (x & (x - 1)) == 0;
    }

    /// <summary>
    /// Sets one component of a unit vector to <paramref name="newValue"/> and
    /// rescales the other two so the result remains on the unit sphere.
    /// Direction of the unconstrained pair is preserved.
    /// </summary>
    public static Vector3 AdjustComponent(Vector3 v, VectorAxis axis, float newValue)
    {
        const float Eps = 1e-12f;

        newValue = float.Clamp(newValue, -1f, 1f);
        float rNew = float.Sqrt(float.Max(0f, 1f - newValue * newValue));

        switch (axis)
        {
            case VectorAxis.X:
                {
                    float rOldSq = v.Y * v.Y + v.Z * v.Z;
                    if (rOldSq < Eps)
                        return new Vector3(newValue, rNew, 0f);
                    float s = rNew / float.Sqrt(rOldSq);
                    return new Vector3(newValue, v.Y * s, v.Z * s);
                }
            case VectorAxis.Y:
                {
                    float rOldSq = v.Z * v.Z + v.X * v.X;
                    if (rOldSq < Eps)
                        return new Vector3(0f, newValue, rNew);
                    float s = rNew / float.Sqrt(rOldSq);
                    return new Vector3(v.X * s, newValue, v.Z * s);
                }
            case VectorAxis.Z:
                {
                    float rOldSq = v.X * v.X + v.Y * v.Y;
                    if (rOldSq < Eps)
                        return new Vector3(rNew, 0f, newValue);
                    float s = rNew / float.Sqrt(rOldSq);
                    return new Vector3(v.X * s, v.Y * s, newValue);
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(axis));
        }
    }
}

class DelayEventTimerArgs(IEnumerable<object> data) : EventArgs
{
    public bool RepeatEvent { get; set; }
    public IEnumerable<object> Data { get; set; } = data;
}

class DelayEventTimer
{
    private readonly DispatcherTimer _timer;
    private readonly TimeSpan _delay;
    private readonly List<object> _data = [];
    private DateTime _lastEventTime = DateTime.Now;

    public event EventHandler<DelayEventTimerArgs> Triggered;

    public void Trigger(object data = null)
    {
        if (data != null)
        {
            _data.Add(data);
        }

        _lastEventTime = DateTime.Now;
        _timer.IsEnabled = true;
    }

    public void Disable()
    {
        _timer.IsEnabled = false;
    }

    private void OnTimerTick(object sender, EventArgs e)
    {
        if ((DateTime.Now - _lastEventTime) < _delay) return;
        var eventArgs = new DelayEventTimerArgs(_data);
        Triggered?.Invoke(this, eventArgs);
        if (!eventArgs.RepeatEvent)
        {
            _data.Clear();
        }
        _timer.IsEnabled = eventArgs.RepeatEvent;
    }

    public DelayEventTimer(TimeSpan delay, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        _delay = delay;
        _timer = new DispatcherTimer(priority)
        {
            Interval = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 0.5)
        };
        _timer.Tick += OnTimerTick;
    }
}
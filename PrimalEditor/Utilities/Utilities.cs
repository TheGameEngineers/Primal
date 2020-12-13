// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Threading;

namespace PrimalEditor.Utilities
{
    public static class ID
    {
        public static int INVALID_ID => -1;
        public static bool IsValid(int id) => id != INVALID_ID;
    }

    public static class MathUtil
    {
        public static float Epsilon => 0.00001f;

        public static bool IsTheSameAs(this float value, float other)
        {
            return Math.Abs(value - other) < Epsilon;
        }

        public static bool IsTheSameAs(this float? value, float? other)
        {
            if (!value.HasValue || !other.HasValue) return false;
            return Math.Abs(value.Value - other.Value) < Epsilon;
        }
    }

    class DelayEventTimerArgs : EventArgs
    {
        public bool RepeatEvent { get; set; }
        public object Data { get; set; }

        public DelayEventTimerArgs(object data)
        {
            Data = data;
        }
    }

    class DelayEventTimer
    {
        private readonly DispatcherTimer _timer;
        private readonly TimeSpan _delay;
        private DateTime _lastEventTime = DateTime.Now;
        private object _data;

        public event EventHandler<DelayEventTimerArgs> Triggered;

        public void Trigger(object data = null)
        {
            _data = data;
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
}

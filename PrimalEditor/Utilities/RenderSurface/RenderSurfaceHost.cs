// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.DllWrappers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using System.Windows.Interop;

namespace PrimalEditor.Utilities
{
    class RenderSurfaceHost : HwndHost
    {
        private readonly int _width = 800;
        private readonly int _height = 600;
        private IntPtr _renderWindowHandle = IntPtr.Zero;
        private DelayEventTimer _resizeTimer;

        public int SurfaceId { get; private set; } = ID.INVALID_ID;

        public void Resize()
        {
            _resizeTimer.Trigger();
        }

        private void Resize(object sender, DelayEventTimerArgs e)
        {
            e.RepeatEvent = Mouse.LeftButton == MouseButtonState.Pressed;
            if (!e.RepeatEvent)
            {
                EngineAPI.ResizeRenderSurface(SurfaceId);
            }
        }

        public RenderSurfaceHost(double width, double height)
        {
            _width = (int)width;
            _height = (int)height;
            _resizeTimer = new DelayEventTimer(TimeSpan.FromMilliseconds(250.0));
            _resizeTimer.Triggered += Resize;
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            SurfaceId = EngineAPI.CreateRenderSurface(hwndParent.Handle, _width, _height);
            Debug.Assert(ID.IsValid(SurfaceId));
            _renderWindowHandle = EngineAPI.GetWindowHandle(SurfaceId);
            Debug.Assert(_renderWindowHandle != IntPtr.Zero);

            return new HandleRef(this, _renderWindowHandle);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            EngineAPI.RemoveRenderSurface(SurfaceId);
            SurfaceId = ID.INVALID_ID;
            _renderWindowHandle = IntPtr.Zero;
        }
    }
}

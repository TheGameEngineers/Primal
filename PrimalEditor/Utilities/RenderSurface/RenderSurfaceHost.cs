// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.DllWrappers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace PrimalEditor.Utilities;

class FrameInfo
{
    public int SurfaceId { get; init; }
    public ulong LightSetKey { get; init; }
    public IdType CameraId { get; init; } = ID.INVALID_ID;
}

delegate FrameInfo RenderFrameCallback(int frame);

partial class RenderSurfaceHost : HwndHost
{
    private static class RenderThread
    {
        private static readonly Dictionary<IntPtr, RenderFrameCallback> _callbackMap = [];
        private static readonly List<RenderFrameCallback> _callbacks = [];
        private static readonly List<int> _frameCounts = [];
        private static readonly Lock _lock = new();
        private static Thread _renderThread;

        public static bool IsRunning { get; private set; }

        private static void Render(object obj)
        {
            while (IsRunning)
            {
                lock (_lock)
                {
                    for (int i = 0; i < _callbacks.Count; i++)
                    {
                        var info = _callbacks[i](_frameCounts[i]++);
                        EngineAPI.RenderFrame(info.SurfaceId, info.CameraId, info.LightSetKey);
#if DEBUG
                        // Editor's UI becomes very sluggish when it's running in VS debugger while
                        // rendering at full frame rate. Here we slow down the renderer which seems
                        // to solve the issue.
                        if (Debugger.IsAttached)
                        {
                            Thread.Sleep(8);
                        }
#endif
                    }
                }
            }
        }

        public static void RegisterCallback(IntPtr hwnd, RenderFrameCallback callback)
        {
            lock (_lock)
            {
                if (!_callbackMap.ContainsKey(hwnd))
                {
                    _callbackMap[hwnd] = callback;
                    _callbacks.Add(callback);
                    _frameCounts.Add(0);

                    if (_callbackMap.Count == 1 && _renderThread == null)
                    {
                        Start();
                    }
                }
            }
        }

        public static void UnregisterCallback(IntPtr hwnd)
        {
            lock (_lock)
            {
                if (_callbackMap.TryGetValue(hwnd, out var callback))
                {
                    var index = _callbacks.IndexOf(callback);
                    _frameCounts.RemoveAt(index);
                    _callbacks.RemoveAt(index);
                    _callbackMap.Remove(hwnd);

                    if (_callbackMap.Count == 0 && _renderThread != null)
                    {
                        // Call Stop on another thread so that it doesn't block this method and cause a deadlock.
                        _ = Task.Run(Stop);
                    }
                }
            }
        }

        private static void Start()
        {
            Debug.Assert(_renderThread == null);

            _renderThread = new(Render)
            {
                Name = "RenderThread",
                IsBackground = true
            };

            IsRunning = true;
            _renderThread.Start();
        }

        private static void Stop()
        {
            if (_renderThread != null)
            {
                IsRunning = false;
                _renderThread.Join();
                _renderThread = null;
                Debug.WriteLine("Render thread stopped.");
            }
        }
    } // RenderThread

    private readonly int _width = 800;
    private readonly int _height = 600;
    private readonly DelayEventTimer _resizeTimer;
    private readonly RenderFrameCallback _renderFrameCallback;
    private readonly AutoResetEvent _resetEvent = new(false);
    private IntPtr _windowHandle = IntPtr.Zero;

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hwnd);

    public int SurfaceId { get; private set; } = ID.INVALID_ID;

    public bool HostReady { get; private set; }

    private void Resize(object sender, DelayEventTimerArgs e)
    {
        e.RepeatEvent = KeyboardHelper.GetAsyncKeyState(KeyboardHelper.VKey.MouseLeft) < 0;
        if (!e.RepeatEvent)
        {
            EngineAPI.ResizeRenderSurface(SurfaceId);
        }
    }

    public RenderSurfaceHost(double width, double height, RenderFrameCallback renderFrameCallback)
    {
        _width = (int)width;
        _height = (int)height;
        _resizeTimer = new DelayEventTimer(TimeSpan.FromMilliseconds(50.0));
        _resizeTimer.Triggered += Resize;
        _renderFrameCallback = renderFrameCallback;
        SizeChanged += (s, e) => _resizeTimer.Trigger();
    }

    private bool _disposed;

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                RenderThread.UnregisterCallback(_windowHandle);
                EngineAPI.RemoveRenderSurface(SurfaceId);
                _windowHandle = IntPtr.Zero;
                Debug.WriteLine($"Render host for surface {SurfaceId} disposed.");
                SurfaceId = ID.INVALID_ID;
            }

            _disposed = true;
        }
        base.Dispose(disposing);
    }

    public async Task WaitReady() => await Task.Run(_resetEvent.WaitOne);

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        SurfaceId = EngineAPI.CreateRenderSurface(hwndParent.Handle, _width, _height);
        Debug.Assert(ID.IsValid(SurfaceId));
        _windowHandle = EngineAPI.GetWindowHandle(SurfaceId);
        Debug.Assert(_windowHandle != IntPtr.Zero);
        RenderThread.RegisterCallback(_windowHandle, _renderFrameCallback);
        HostReady = RenderThread.IsRunning;
        _resetEvent.Set();
        return new HandleRef(this, _windowHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        handled = false;
        return IntPtr.Zero;
    }
}

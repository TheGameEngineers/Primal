// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.DllWrappers;
using PrimalEditor.GameProject;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using static PrimalEditor.KeyboardHelper;
using static PrimalEditor.MouseHelper;

namespace PrimalEditor.Utilities;

class RenderSurfaceFrameStatsArgs(float averageFrameTime, int fps) : EventArgs
{
    public float AverageFrameTime { get; } = averageFrameTime;
    public int FPS { get; } = fps;
}

class RenderSurfaceControl : ContentControl, IDisposable
{
    private class FrameTimer
    {
        private long _timerStart = Stopwatch.GetTimestamp();
        private int _timerCount = 1;
        private float _avgFrameTimeMicroseconds = 0;

        public float AverageFrameTime { get; private set; } = 0.016667f;
        public int FPS { get; private set; }
        public float LastFrameTime { get; private set; } = 0.016667f;

        public bool MeasureFrameTime()
        {
            var timerEnd = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(_timerStart, timerEnd);
            LastFrameTime = (float)(elapsed.TotalMicroseconds * 1e-6);
            _timerStart = timerEnd;
            _avgFrameTimeMicroseconds += (float)(elapsed.TotalMicroseconds - _avgFrameTimeMicroseconds) / _timerCount++;

            if (AverageFrameTime * _timerCount > 1)
            {
                AverageFrameTime = _avgFrameTimeMicroseconds * 1e-6f;
                FPS = _timerCount;
                _timerCount = 1;
                _avgFrameTimeMicroseconds = 0;
                return true;
            }

            return false;
        }
    }

    private enum Win32Msg
    {
        WM_SIZING = 0x0214,
        WM_ENTERSIZEMOVE = 0x0231,
        WM_EXITSIZEMOVE = 0x0232,
        WM_SIZE = 0x0005,

        WM_LBUTTONDOWN = 0x0201,
        WM_LBUTTONUP = 0x0202,

        WM_MBUTTONDOWN = 0x0207,
        WM_MBUTTONUP = 0x0208,

        WM_RBUTTONDOWN = 0x0204,
        WM_RBUTTONUP = 0x0205,

        WM_MOUSEHOVER = 0x02A1,
        WM_MOUSELEAVE = 0x02A3,
        WM_MOUSEMOVE = 0x0200,

        WM_MOUSEWHEEL = 0x020A,

        WM_KEYDOWN = 0x0100,
        WM_KEYUP = 0x0101,

        WM_SYSCOMMAND = 0x0112,
    }

    private readonly FrameTimer _frameTimer = new();
    private readonly EditorCamera _camera = new();
    private RenderSurfaceHost _host = null;
    private Point _clickPosition = new(0, 0);
    private bool _capturedLeft;
    private bool _capturedRight;
    private bool _isMouseOver;
    private bool _isXZLocked = false;
    private ulong _lightSetKey;

    public event EventHandler<RenderSurfaceFrameStatsArgs> FrameStatsUpdated;

    public bool IsXZLocked
    {
        get { return (bool)GetValue(IsXZLockedProperty); }
        set { SetValue(IsXZLockedProperty, value); }
    }
    public static readonly DependencyProperty IsXZLockedProperty =
        DependencyProperty.Register(nameof(IsXZLocked), typeof(bool), typeof(RenderSurfaceControl),
            new PropertyMetadata(false, new PropertyChangedCallback(IsXZLockedChanged)));

    public int CameraSpeed
    {
        get { return (int)GetValue(CameraSpeedProperty); }
        set { SetValue(CameraSpeedProperty, value); }
    }
    public static readonly DependencyProperty CameraSpeedProperty =
        DependencyProperty.Register(nameof(CameraSpeed), typeof(int), typeof(RenderSurfaceControl),
            new PropertyMetadata(5, new PropertyChangedCallback(CameraSpeedChanged)));

    public float CameraFov
    {
        get { return (float)GetValue(CameraFovProperty); }
        set { SetValue(CameraFovProperty, value); }
    }
    public static readonly DependencyProperty CameraFovProperty =
        DependencyProperty.Register(nameof(CameraFov), typeof(float), typeof(RenderSurfaceControl),
            new PropertyMetadata(45f, new PropertyChangedCallback(CameraFovChanged)));

    public float CameraNearZ
    {
        get { return (float)GetValue(CameraNearZProperty); }
        set { SetValue(CameraNearZProperty, value); }
    }
    public static readonly DependencyProperty CameraNearZProperty =
        DependencyProperty.Register(nameof(CameraNearZ), typeof(float), typeof(RenderSurfaceControl),
            new PropertyMetadata(0.1f, new PropertyChangedCallback(CameraNearZChanged)));

    public float CameraFarZ
    {
        get { return (float)GetValue(CameraFarZProperty); }
        set { SetValue(CameraFarZProperty, value); }
    }
    public static readonly DependencyProperty CameraFarZProperty =
        DependencyProperty.Register(nameof(CameraFarZ), typeof(float), typeof(RenderSurfaceControl),
            new PropertyMetadata(100f, new PropertyChangedCallback(CameraFarZChanged)));

    private static void IsXZLockedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = d as RenderSurfaceControl;
        control._isXZLocked = (bool)e.NewValue;
    }

    private static void CameraSpeedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = d as RenderSurfaceControl;
        // NOTE: although _camera.Speed is float, we want to change it in integer steps in the UI.
        control._camera.Speed = (int)e.NewValue;
    }

    private static void CameraFovChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = d as RenderSurfaceControl;
        control._camera.FoV = (float)e.NewValue;
    }

    private static void CameraNearZChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = d as RenderSurfaceControl;
        control._camera.NearZ = (float)e.NewValue;
    }
    private static void CameraFarZChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = d as RenderSurfaceControl;
        control._camera.FarZ = (float)e.NewValue;
    }

    private async void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue != null && _host == null)
        {
            _host = new RenderSurfaceHost(ActualWidth, ActualHeight, new RenderFrameCallback(OnRenderFrame));
            _host.MessageHook += new HwndSourceHook(HostMsgFilter);
            Content = _host;
            await _host.WaitReady();
            Project.Current.UpdateScene();
            _camera.SetSurfaceId(_host.SurfaceId);
            _disposedValue = false;
        }
        else if (e.NewValue == null && _host != null)
        {
            SetComponentIds([]);
            Content = null;
            Dispose();
        }
    }

    private void OnRenderSurfaceControl_KeyDown(object sender, KeyEventArgs e)
    {
        // Prevent main menu from gaining focus when Alt-key is pressed in renderer.
        e.Handled = e.Key == Key.System && e.OriginalSource is RenderSurfaceControl;
    }

    private FrameInfo OnRenderFrame(int frame)
    {
        if (_frameTimer.MeasureFrameTime())
        {
            FrameStatsUpdated?.Invoke(this, new RenderSurfaceFrameStatsArgs(_frameTimer.AverageFrameTime, _frameTimer.FPS));
        }

        if (_capturedLeft && !_isXZLocked && GetAsyncKeyState(VKey.Alt) == 0)
        {
            var moveDir = GetMoveDirection();
            if (moveDir.LengthSquared() > MathUtil.Epsilon)
            {
                _ = Application.Current.Dispatcher.BeginInvoke(() => _camera.ChangePosition(moveDir, _frameTimer.AverageFrameTime));
            }
        }

        _camera.Update(_frameTimer.AverageFrameTime);

        return new()
        {
            SurfaceId = _host.SurfaceId,
            LightSetKey = _lightSetKey,
        };
    }

    private static Vector3 GetMoveDirection()
    {
        static float GetKey(VKey key) => GetAsyncKeyState(key) < 0 ? 1.0f : 0.0f;

        var moveDir = Vector3.Zero;

        moveDir.X += GetKey(VKey.A);
        moveDir.X -= GetKey(VKey.D);
        moveDir.Z += GetKey(VKey.W);
        moveDir.Z -= GetKey(VKey.S);
        moveDir.Y += GetKey(VKey.E);
        moveDir.Y -= GetKey(VKey.Q);

        return moveDir;
    }

    private void OnRenderHost_Mouse_LBD(int x, int y)
    {
        Focus();
        _clickPosition = new(x, y);
        _capturedLeft = true;
        SetCapture(_host.Handle);
    }

    private void OnRenderHost_Mouse_LBU()
    {
        _capturedLeft = false;
        ReleaseCapture();
    }

    private void OnRenderHost_Mouse_RBD(int x, int y)
    {
        _clickPosition = new(x, y);
        _capturedRight = true;
        SetCapture(_host.Handle);
    }

    private void OnRenderHost_Mouse_RBU()
    {
        _capturedRight = false;
        ReleaseCapture();
    }

    private void OnRenderHost_MouseMove(int x, int y)
    {
        var currentPosition = new Point(x, y);
        var d = currentPosition - _clickPosition;
        _clickPosition = currentPosition;

        if (_capturedLeft)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || _isXZLocked)
            {
                _camera.Orbit(d.X, d.Y, 0);
            }
            else
            {
                _camera.ChangeDirection(d.X, d.Y);
            }
        }
        else if (_capturedRight && (_isXZLocked || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
        {
            var yOffset = (float)d.Y * 0.0005f * _camera.OrbitRadius;
            var target = _camera.Target;
            target.Y += yOffset;
            _camera.Goto(target);
        }
    }

    private void OnRenderHost_MouseWheel(int sign)
    {
        if (_isXZLocked || (IsFocused && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
        {
            _camera.Orbit(0.0, 0.0, sign);
        }
    }

    private static (short, short) SplitParam(IntPtr param)
    {
        var p = param.ToInt64();
        var low = (short)(p & 0xffff);
        var high = (short)((p >> 16) & 0xffff);
        return (low, high);
    }

    private IntPtr HostMsgFilter(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch ((Win32Msg)msg)
        {
            case Win32Msg.WM_SIZING:
                break;
            case Win32Msg.WM_ENTERSIZEMOVE:
                break;
            case Win32Msg.WM_EXITSIZEMOVE:
                break;
            case Win32Msg.WM_SIZE:
                break;
            case Win32Msg.WM_LBUTTONDOWN:
                {
                    var (x, y) = SplitParam(lParam);
                    OnRenderHost_Mouse_LBD(x, y);
                }
                break;
            case Win32Msg.WM_LBUTTONUP:
                OnRenderHost_Mouse_LBU();
                break;
            case Win32Msg.WM_MBUTTONDOWN:
                break;
            case Win32Msg.WM_MBUTTONUP:
                break;
            case Win32Msg.WM_RBUTTONDOWN:
                if (_isXZLocked || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    var (x, y) = SplitParam(lParam);
                    OnRenderHost_Mouse_RBD(x, y);
                }
                break;
            case Win32Msg.WM_RBUTTONUP:
                if (_isXZLocked || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    OnRenderHost_Mouse_RBU();
                }
                break;
            case Win32Msg.WM_MOUSEHOVER:
                _isMouseOver = true;
                break;
            case Win32Msg.WM_MOUSELEAVE:
                _isMouseOver = false;
                break;
            case Win32Msg.WM_MOUSEMOVE:
                {
                    var (x, y) = SplitParam(lParam);
                    OnRenderHost_MouseMove(x, y);
                }
                break;
            case Win32Msg.WM_MOUSEWHEEL:
                {
                    var (_, high) = SplitParam(wParam);
                    OnRenderHost_MouseWheel(Math.Sign(high));
                }
                break;
            case Win32Msg.WM_KEYDOWN:
                break;
            case Win32Msg.WM_KEYUP:
                break;
            default:
                break;
        }

        return IntPtr.Zero;
    }

    public void UseLightSet(ulong lightSetKey)
    {
        _lightSetKey = lightSetKey;
    }

    public void SetComponentIds(List<IdType> componentIds)
    {
        Debug.Assert(componentIds != null);
        if (_host?.HostReady == true)
        {
            EngineAPI.SetGeometryIds(_host.SurfaceId, [.. componentIds], componentIds.Count);
        }
    }

    public void FocusPosition(Vector3 position)
    {
        if(_isMouseOver || IsFocused)
        {
            _camera.Goto(position);
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        DataContextChanged += OnDataContextChanged;
        KeyDown += OnRenderSurfaceControl_KeyDown;

        OnDataContextChanged(this, new(DataContextProperty, null, DataContext));
    }

    static RenderSurfaceControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RenderSurfaceControl),
            new FrameworkPropertyMetadata(typeof(RenderSurfaceControl)));
    }

    #region IDisposable Support
    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                Content = null;
                _host.Dispose();
                _host = null;
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion
}

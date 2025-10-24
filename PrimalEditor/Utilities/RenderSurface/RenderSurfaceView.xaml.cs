// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Windows;
using System.Windows.Controls;

namespace PrimalEditor.Utilities;

/// <summary>
/// Interaction logic for RenderSurfaceView.xaml
/// </summary>
public partial class RenderSurfaceView : UserControl
{
    internal RenderSurfaceControl RenderSurfaceControl => renderSurface;
    public RenderSurfaceView()
    {
        InitializeComponent();
        Loaded += OnRenderSurfaceViewLoaded;
    }

    private void OnRenderSurfaceViewLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnRenderSurfaceViewLoaded;

        renderSurface.FrameStatsUpdated += (_, e) =>
        {
            _ = Application.Current.Dispatcher.BeginInvoke(() =>
            {
                frameTime.Text = $"DT: {e.AverageFrameTime * 1000f:F1} ms";
                frameRate.Text = $"FPS: {e.FPS} Hz";
            });
        };
    }
}

// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using PrimalEditor.Utilities;
using System.ComponentModel;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for GeometryEditorView.xaml
/// </summary>
partial class GeometryEditorView : UserControl
{
    private Window _window;
    public GeometryEditorView()
    {
        InitializeComponent();

        Loaded += (s, e) =>
        {
            if (DataContext is GeometryEditor editor)
            {
                editor.GeometryChanged += (_, id) =>
                {
                    sv.RenderSurfaceControl.UseLightSet(editor.LightSetKey);
                    sv.RenderSurfaceControl.SetComponentIds(ID.IsValid(id) ? [id] : []);

                    var p = editor.MeshRenderer?.CameraTarget ?? new();
                    var pos = new Vector3((float)p.X, (float)p.Y, (float)p.Z);
                    sv.RenderSurfaceControl.Focus();
                    sv.RenderSurfaceControl.FocusPosition(pos);
                };
            }

            sv.RenderSurfaceControl.IsXZLocked = true;

            _window = Window.GetWindow(this);
            _window.Closing += OnWindowClosing;
        };

        Unloaded += (s, e) =>
        {
            _window?.Closing -= OnWindowClosing;
            _window = null;
        };
    }

    private void OnWindowClosing(object sender, CancelEventArgs e)
    {
        (DataContext as GeometryEditor)?.Unload();
        DataContext = null;
    }

    private void OnRenderSurfaceView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F)
        {
            var pos = (DataContext as GeometryEditor)?.MeshRenderer?.CameraTarget ?? new();
            sv.RenderSurfaceControl.FocusPosition(new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z));
        }
    }

    private void OnAmbientLight_ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: AssetInfo info } && DataContext is GeometryEditor editor)
        {
            editor.AmbientLight.SetEnvMap(info);
        }
    }
}

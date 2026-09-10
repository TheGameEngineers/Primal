// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for GeometryDetailsView.xaml
/// </summary>
partial class GeometryDetailsView : UserControl
{
    public GeometryDetailsView()
    {
        InitializeComponent();
    }

    private void ToggleHighlighted(MeshRendererVertexData mesh)
    {
        var wasHighlighted = mesh.IsHighlighted;
        var vm = DataContext as GeometryEditor;
        foreach (var m in vm.MeshRenderer.Meshes)
        {
            m.IsHighlighted = false;
        }
        mesh.IsHighlighted = !wasHighlighted;
    }

    private void ToggleIsolated(MeshRendererVertexData mesh)
    {
        var wasIsolated = mesh.IsIsolated;
        var vm = DataContext as GeometryEditor;
        foreach (var m in vm.MeshRenderer.Meshes)
        {
            m.IsIsolated = false;
        }
        if (Tag is GeometryView geometryView)
        {
            mesh.IsIsolated = !wasIsolated;
            geometryView.SetGeometry(mesh.IsIsolated ? vm.MeshRenderer.Meshes.IndexOf(mesh) : -1);
        }
    }

    private void OnHighlight_CheckBox_Click(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var mesh = checkBox.DataContext as MeshRendererVertexData;
        ToggleHighlighted(mesh);
    }

    private void OnIsolate_CheckBox_Click(object sender, RoutedEventArgs e)
    {
        var checkBox = sender as CheckBox;
        var mesh = checkBox.DataContext as MeshRendererVertexData;
        ToggleIsolated(mesh);
    }

    private void OnImage_Mouse_LBD(object sender, MouseButtonEventArgs e)
    {
        if ((sender as Visual).FindVisualParent<DockPanel>()?.DataContext is MeshRendererVertexData mesh)
        {
            ToggleHighlighted(mesh);
        }
    }

    private void OnImage_Mouse_RBD(object sender, MouseButtonEventArgs e)
    {
        if ((sender as Visual).FindVisualParent<DockPanel>()?.DataContext is MeshRendererVertexData mesh)
        {
            ToggleIsolated(mesh);
        }
    }
}

// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.Content;
using PrimalEditor.GameDev;
using PrimalEditor.GameProject;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for WorldEditorView.xaml
/// </summary>
partial class WorldEditorView : UserControl
{
    public WorldEditorView()
    {
        InitializeComponent();
        Project.SceneUpdated += OnSceneUpdated;
        DataContextChanged += OnWorldEditorDataContextChanged;
    }

    private void OnWorldEditorDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {        
        if (DataContext is Project vm)
        {
            var index = LightSet.LightSets.IndexOf(vm.ActiveScene.LightSetKey);
            if (index < 0)
            {
                LightSet.AddLightSet(vm.ActiveScene.LightSetKey, true);
                index = LightSet.LightSets.IndexOf(vm.ActiveScene.LightSetKey);
                Debug.Assert(index >= 0);
            }

            lightSetComboBox.SelectedIndex = index;
        }

        Focus();
    }

    private void OnSceneUpdated(object sender, EventArgs e)
    {
        Debug.Assert((sender as Scene)?.Project == Project.Current);

        if (sender is Scene scene)
        {
            var ids = scene.IsActive ? scene.GetGeometryComponentIds() : [];
            sv1.RenderSurfaceControl.SetComponentIds(ids);
            sv2.RenderSurfaceControl.SetComponentIds(ids);
            sv3.RenderSurfaceControl.SetComponentIds(ids);
            sv4.RenderSurfaceControl.SetComponentIds(ids);
        }
    }


    private void OnNewScript_Button_Click(object sender, RoutedEventArgs e)
    {
        new NewScriptDialog().ShowDialog();
    }

    private void OnCreatePrimitiveMesh_Button_Click(object sender, RoutedEventArgs e)
    {
        new PrimitiveMeshDialog().ShowDialog();
    }

    private static void UnloadAndCloseAllWindows()
    {
        var mainWindow = Application.Current.MainWindow;

        foreach (Window win in Application.Current.Windows)
        {
            if (win != mainWindow)
            {
                win.Close();
            }
        }
        // NOTE: the order of these lines matter!
        mainWindow.DataContext = null;
        GameEntityView.Instance.DataContext = null;
        Project.Current?.Unload();
        mainWindow.Close();
    }

    private void OnNewProject(object sender, ExecutedRoutedEventArgs e)
    {
        ProjectBrowserDialog.GotoNewProjectTab = true;
        UnloadAndCloseAllWindows();
    }

    private void OnOpenProject(object sender, ExecutedRoutedEventArgs e) => UnloadAndCloseAllWindows();

    private void OnEditorClose(object sender, ExecutedRoutedEventArgs e)
    {
        Application.Current.MainWindow.Close();
    }

    private void OnContentBrowser_Loaded(object sender, RoutedEventArgs e)
        => OnContentBrowser_IsVisibleChanged(sender, default);

    private void OnContentBrowser_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((sender as FrameworkElement).DataContext is ContentBrowser contentBrowser &&
            string.IsNullOrEmpty(contentBrowser.SelectedFolder?.Trim()))
        {
            contentBrowser.SelectedFolder = contentBrowser.ContentFolder;
        }
    }

    private void OnProjectLayoutView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.OriginalSource is not TextBox && MSEntity.CurrentSelection?.SelectedEntities.Count > 0)
        {
            var avgPos = Vector3.Zero;
            foreach (var entity in MSEntity.CurrentSelection.SelectedEntities)
            {
                avgPos += entity.GetComponent<Transform>().Position;
            }

            avgPos /= MSEntity.CurrentSelection.SelectedEntities.Count;

            sv1.RenderSurfaceControl.FocusPosition(avgPos);
            sv2.RenderSurfaceControl.FocusPosition(avgPos);
            sv3.RenderSurfaceControl.FocusPosition(avgPos);
            sv4.RenderSurfaceControl.FocusPosition(avgPos);
        }
    }

    private void OnLightSet_ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not Project vm) return;

        var lightSetKey = (sender as ComboBox).SelectedItem as string;

        var key = LightSet.GetKey(lightSetKey);
        if(key == LightSet.InvalidKey)
        {
            key = LightSet.AddLightSet(lightSetKey, true);
        }
        Debug.Assert(key != LightSet.InvalidKey);
        vm.ActiveScene.LightSetKey = lightSetKey;
        sv1.RenderSurfaceControl.UseLightSet(key);
        sv2.RenderSurfaceControl.UseLightSet(key);
        sv3.RenderSurfaceControl.UseLightSet(key);
        sv4.RenderSurfaceControl.UseLightSet(key);
    }
}

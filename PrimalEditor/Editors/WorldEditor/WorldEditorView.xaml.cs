// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.Content;
using PrimalEditor.DllWrappers;
using PrimalEditor.GameDev;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for WorldEditorView.xaml
/// </summary>
partial class WorldEditorView : UserControl
{
    /// <summary>
    /// Gets or sets a value indicating whether the render surface is playing.
    /// When set to true, Game scripts are updated, the input will be captured and the active scene camera will be used.
    /// When set to false, the input will be released and the editor camera will be used.
    /// </summary>
    private volatile bool _isPlaying = false;
    /// <summary>
    /// Gets or sets a value indicating whether the game scripts are updated.
    /// </summary>
    private volatile bool _isRunning;

    private volatile IdType _activeCameraId = ID.INVALID_ID;

    public WorldEditorView()
    {
        InitializeComponent();
        Application.Current.Activated += OnRequestStop;
        Application.Current.Deactivated += OnRequestStop;
        Application.Current.MainWindow.StateChanged += OnRequestStop;

        Project.SceneUpdated += OnSceneUpdated;
        DataContextChanged += OnWorldEditorDataContextChanged;
        sv1.RenderSurfaceControl.IsHostReadyChanged += OnRenderSurfaceView_IsHostReadyChanged;
        sv1.RenderSurfaceControl.RenderFrame += OnRenderFrame;
        sv1.RenderSurfaceControl.RequestStop += OnRequestStop;
    }

    private void OnRequestStop(object sender, EventArgs e)
    {
        if (DataContext is not null)
        {
            Focus();
            playToggleButton.IsChecked = false;
            OnPlay_ToggleButton_Click(playToggleButton, null);
        }
    }

    // NOTE: OnRenderFrame is run on RenderSurfaceHost's render thread.
    //       Don't access any UI owned data in here.
    private void OnRenderFrame(object sender, RenderFrameEventArgs e)
    {
        // We want to run the scripts and update data once per frame.
        if (sender != sv1.RenderSurfaceControl) return;

        if (_isPlaying || _isRunning)
        {
            var scriptUpdateLock = Project.LockScriptUpdates;
            if (scriptUpdateLock.TryEnter())
            {
                try
                {
                    if (_isPlaying || _isRunning)
                    {
                        e.CameraId = _activeCameraId;
                        EngineAPI.UpdateScripts(e.DeltaTime);
                    }
                }
                finally { scriptUpdateLock.Exit(); }
            }
        }

        EngineAPI.UpdateEngineData();
    }

    private void OnRenderSurfaceView_IsHostReadyChanged(object sender, RoutedPropertyChangedEventArgs<bool> e)
    {
        if (e.NewValue && DataContext is Project vm)
        {
            vm.UpdateScene();
        }
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
            //sv2.RenderSurfaceControl.SetComponentIds(ids);
            //sv3.RenderSurfaceControl.SetComponentIds(ids);
            //sv4.RenderSurfaceControl.SetComponentIds(ids);
        }
    }

    private void OnPlay_ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        lock (Project.LockScriptUpdates)
        {
            _isPlaying = (sender as ToggleButton).IsChecked == true;
            if (_isPlaying)
            {
                if (!_isRunning) EngineAPI.BeginPlayAll();
                if (DataContext is Project { ActiveScene: Scene scene })
                {
                    // TODO: properly assign a camera as the active camera of the scene.
                    _activeCameraId = scene.GameEntities.OfType<PerspectiveCamera>().FirstOrDefault()?.CameraId ?? ID.INVALID_ID;
                    if(!ID.IsValid(_activeCameraId))
                    {
                        Logger.Log(MessageType.Warning, "The scene doesn't contain a perspective camera. Using editor camera instead.");
                    }
                }
                KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
                sv1.RenderSurfaceControl.FocusHost(true);
            }
            else
            {
                _activeCameraId = ID.INVALID_ID;
                sv1.RenderSurfaceControl.FocusHost(false);
                KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Continue);

                if (!_isRunning) MSEntity.RestoreEntityStates((DataContext as Project).ActiveScene.GameEntities);
            }
        }
    }

    private void OnRun_ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        lock (Project.LockScriptUpdates)
        {
            _isRunning = (sender as ToggleButton).IsChecked == true;
            if (_isRunning)
            {
                EngineAPI.BeginPlayAll();
            }
            else
            {
                MSEntity.RestoreEntityStates((DataContext as Project).ActiveScene.GameEntities);
            }
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
            //sv2.RenderSurfaceControl.FocusPosition(avgPos);
            //sv3.RenderSurfaceControl.FocusPosition(avgPos);
            //sv4.RenderSurfaceControl.FocusPosition(avgPos);
        }
    }

    private void OnLightSet_ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not Project vm) return;

        var lightSetKey = (sender as ComboBox).SelectedItem as string;

        var key = LightSet.GetKey(lightSetKey);
        if (key == LightSet.InvalidKey)
        {
            key = LightSet.AddLightSet(lightSetKey, true);
        }
        Debug.Assert(key != LightSet.InvalidKey);
        vm.ActiveScene.LightSetKey = lightSetKey;
        sv1.RenderSurfaceControl.UseLightSet(key);
        //sv2.RenderSurfaceControl.UseLightSet(key);
        //sv3.RenderSurfaceControl.UseLightSet(key);
        //sv4.RenderSurfaceControl.UseLightSet(key);
    }
}

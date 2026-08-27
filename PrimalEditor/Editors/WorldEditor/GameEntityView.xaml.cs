// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.Content;
using PrimalEditor.DllWrappers;
using PrimalEditor.GameDev;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for GameEntityView.xaml
/// </summary>
partial class GameEntityView : UserControl
{
    public static GameEntityView Instance { get; private set; }

    private string _propertyName = string.Empty;
    private bool _disableUndoRedo;

    private readonly List<(GameEntity Entity, bool IsEnabled)> _isEnabled = [];
    private readonly List<(GameEntity Entity, string Name)> _names = [];

    public GameEntityView()
    {
        InitializeComponent();
        DataContext = null;
        Instance = this;
        DataContextChanged += OnGameEntityView_DataContextChanged;
    }

    private void OnGameEntityView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is MSEntity vm)
        {
            GetValues(vm);

            vm.PropertyChanged += (_, e) =>
            {
                _propertyName = e.PropertyName;

                if (!_disableUndoRedo)
                {
                    SetUndoRedo();
                }
            };
        }
    }

    private void GetValues(MSEntity vm)
    {
        _isEnabled.Clear();
        _names.Clear();

        _isEnabled.AddRange(vm.SelectedEntities.Select(x => (x, x.IsEnabled)));
        _names.AddRange(vm.SelectedEntities.Select(x => (x, x.Name)));
    }

    private void SetUndoRedo()
    {
        if (string.IsNullOrEmpty(_propertyName)) return;

        switch (_propertyName)
        {
            case nameof(MSEntity.IsEnabled):
                {
                    var vm = DataContext as MSEntity;
                    var undoAction = GetIsEnabledAction();
                    var isEnabled = vm.IsEnabled.Value;
                    SetIsEnabled([.. vm.SelectedEntities.Select(x => (x, isEnabled))]);
                    var redoAction = GetIsEnabledAction();
                    var entityMsg = vm.SelectedEntities.Count == 1 ? "entity" : "entities";
                    var enableMsg = isEnabled ? "Enable" : "Disable";
                    Project.UndoRedo.Add(new UndoRedoAction(undoAction, redoAction, $"{enableMsg} game {entityMsg}"));
                }
                break;
            case nameof(MSEntity.Name):
                {
                    var vm = DataContext as MSEntity;
                    var undoAction = GetRenameAction();
                    GetValues(vm);
                    var redoAction = GetRenameAction();
                    var entityMsg = vm.SelectedEntities.Count == 1 ? "entity" : "entities";
                    Project.UndoRedo.Add(new UndoRedoAction(undoAction, redoAction, $"Rename game {entityMsg}"));
                }
                break;
        }

        _propertyName = string.Empty;
    }

    private Action GetRenameAction()
    {
        var oldValues = _names.ToList();
        return new(() =>
        {
            _disableUndoRedo = true;
            oldValues.ForEach(x => x.Entity.Name = x.Name);
            MSEntity.Refresh();
            GetValues(MSEntity.CurrentSelection);
            _disableUndoRedo = false;
            _propertyName = string.Empty;
        });
    }
    

    // NOTE: when the selection is a mix of game entities and lights, or any other heterogeneous selection, then the multi-selection type
    //       will be an MSEntity and therefore cannot handle IsEnabled property for selected lights. This is why we handle it here instead
    //       of in the UpdateGameEntities method of the ViewModel. This maybe a design flaw, worth revisiting in the future.
    private void SetIsEnabled(List<(GameEntity Entity, bool IsEnabled)> selection)
    {
        List<IdType> ids = [];
        List<ulong> lightSetKeys = [];
        List<int> isEnabled = [];

        selection.ForEach(item =>
        {
            item.Entity.IsEnabled = item.IsEnabled;

            if (item.Entity is Light light)
            {
                ids.Add(light.LightId);
                lightSetKeys.Add(LightSet.GetKey(light.LightSetKey));
                isEnabled.Add(light.IsEnabled ? 1 : 0);
            }
        });

        if (ids.Count > 0)
        {
            EngineAPI.SetLightIsEnabled([.. ids], [.. lightSetKeys], [.. isEnabled], ids.Count);
        }

        Project.Current.UpdateScene();
        MSEntity.Refresh();
        GetValues(MSEntity.CurrentSelection);
    }

    private Action GetIsEnabledAction()
    {
        var oldValues = _isEnabled.ToList();
        return new(() =>
        {
            _disableUndoRedo = true;
            SetIsEnabled(oldValues);
            _disableUndoRedo = false;
            _propertyName = string.Empty;
        });
    }
    
    private void OnAddComponent_Button_PreviewMouse_LBD(object sender, MouseButtonEventArgs e)
    {
        var menu = FindResource("addComponentMenu") as ContextMenu;
        var btn = sender as ToggleButton;
        btn.IsChecked = true;
        menu.Placement = PlacementMode.Bottom;
        menu.PlacementTarget = btn;
        menu.MinWidth = btn.ActualWidth + 5.0;
        menu.IsOpen = true;
    }

    private static void ResetComponent(List<(GameEntity Entity, Component Component)> changedEntities, Action<GameEntity, Component> action)
    {
        var scene = changedEntities[0].Entity.ParentScene;
        var enableList = scene.DisableAndUpdate([.. changedEntities.Select(x => x.Entity)]);
        changedEntities.ForEach(x => action(x.Entity, x.Component));
        scene.EnableAndUpdate(enableList);
        MSEntity.Refresh();
    }

    private void AddComponent(ComponentType componentType, object data)
    {
        var creationFunction = ComponentFactory.GetCreationFunction(componentType);
        var changedEntities = new List<(GameEntity Entity, Component Component)>();
        var vm = DataContext as MSEntity;
        var enableList = vm.ParentScene.DisableAndUpdate(vm.SelectedEntities);

        foreach (var entity in vm.SelectedEntities)
        {
            var component = creationFunction(entity, data);
            if (entity.AddComponent(component))
            {
                changedEntities.Add((entity, component));
            }
        }

        vm.ParentScene.EnableAndUpdate(enableList);

        if (changedEntities.Count > 0)
        {
            MSEntity.Refresh();

            Project.UndoRedo.Add(new UndoRedoAction(
            () => ResetComponent(changedEntities, (entity, component) => entity.RemoveComponent(component)),
            () => ResetComponent(changedEntities, (entity, component) => entity.AddComponent(component)),
            $"Add {componentType} component"));
        }
    }

    private void OnAddScriptComponent(object sender, RoutedEventArgs e)
    {
        AddComponent(ComponentType.Script, (sender as MenuItem).Header.ToString());
    }

    private void OnAddGeometryComponent(object sender, RoutedEventArgs e)
    {
        AddComponent(ComponentType.Geometry, DefaultAssets.DefaultGeometry);
    }

    private void OnCreateNewScript_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        new NewScriptDialog().ShowDialog();
    }
}

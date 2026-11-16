// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.Content;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for ProjectLayoutView.xaml
/// </summary>
partial class ProjectLayoutView : UserControl
{
    private List<int> _previousSelectedIndices = [];
    private void OnAddGameEntity_Button_Click(object sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        var scene = btn.DataContext as Scene;
        scene.AddGameEntities([new(scene) { Name = "Empty Game Entity" }]);
    }

    private void OnGameEntities_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var listBox = sender as ListBox;
        if (listBox.DataContext is not Scene vm) return;

        var newSelection = listBox.SelectedItems.Cast<GameEntity>().ToList();
        var newSelectedIndices = newSelection.Select(vm.GameEntities.IndexOf).ToList();
        var previousSelectedIndices = _previousSelectedIndices.ToList();
        _previousSelectedIndices = [.. newSelectedIndices];

        Project.UndoRedo.Add(new UndoRedoAction(
            () => // undo action
            {
                listBox.UnselectAll();
                previousSelectedIndices.ForEach(x =>
                {
                    if (listBox.ItemContainerGenerator.ContainerFromIndex(x) is ListBoxItem item)
                        item.IsSelected = true;
                });
            },
            () => //redo action
            {
                listBox.UnselectAll();
                newSelectedIndices.ForEach(x =>
                {
                    if (listBox.ItemContainerGenerator.ContainerFromIndex(x) is ListBoxItem item)
                        item.IsSelected = true;
                });
            },
            "Selection changed"
            ));

        MSEntity msEntities = null;
        MSEntity.Reset();

        if (newSelection.Count != 0)
        {
            var lights = newSelection.Where(x => x is Light).Cast<Light>().ToList();
            var cameras = newSelection.Where(x => x is Camera).Cast<Camera>().ToList();

            if (lights.Count == newSelection.Count)
            {
                if (lights.All(x => x.Type == LightType.Directional))
                {
                    msEntities = new MSDirectionalLight(newSelection);
                }
                else if (lights.All(x => x.Type == LightType.Point))
                {
                    msEntities = new MSPointLight(newSelection);
                }
                else if (lights.All(x => x.Type == LightType.Spot))
                {
                    msEntities = new MSSpotlight(newSelection);
                }
                else if (lights.All(x => x.Type == LightType.Ambient))
                {
                    msEntities = new MSAmbientLight(newSelection);
                }
                else
                {
                    msEntities = new MSLight(newSelection);
                }
            }
            else if (cameras.Count == newSelection.Count)
            {
                if (cameras.All(x => x.Type == CameraType.Perspective))
                {
                    msEntities = new MSPerspectiveCamera(newSelection);
                }
                else if (cameras.All(x => x.Type == CameraType.Orthographic))
                {
                    msEntities = new MSOrthographicCamera(newSelection);
                }
                else
                {
                    msEntities = new MSCamera(newSelection);
                }
            }
            else
            {
                msEntities = new MSGameEntity(newSelection);
            }
        }
        GameEntityView.Instance.DataContext = msEntities;
    }

    private async void OnGameEntities_ListBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && sender is FrameworkElement { DataContext: Scene scene } && scene.IsActive)
        {
            List<GameEntity> entities = [];

            await Task.Run(() =>
            {
                var fileList = files
                    .Where(x => Path.GetExtension(x).ToLower() == Asset.AssetFileExtension && Asset.TryGetAssetInfo(x)?.Type == AssetType.Mesh);

                foreach (var file in fileList)
                {
                    Debug.Assert(!string.IsNullOrEmpty(file?.Trim()));
                    if (Asset.TryGetAssetInfo(file) is AssetInfo assetInfo)
                    {
                        // NOTE: adding an entity to an active scene will automatically set its IsActive to true.
                        //       However, setting it to true here will create and upload entity resources without blocking the UI thread.
                        //       Also we can't add components to inactive entities, since they don't exist in the engine.
                        var entity = new GameEntity(scene) { Name = assetInfo.FileName.Trim(), IsActive = true };
                        entity.AddComponent(new Components.Geometry(entity, assetInfo));
                        entities.Add(entity);
                    }
                }

                // TODO: If the scene hasn't an ambient light and there are textures in dropped files, then we can try to create one.
                // TODO: add asset flags to AssetInfo, so that we can have more info about textures without reading them.
            });

            if (entities.Count > 0)
            {
                scene.AddGameEntities(entities);
            }
        }
    }

    private void OnGameEntities_ListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            var listBox = sender as ListBox;
            var entities = new List<GameEntity>();
            foreach (GameEntity entity in listBox.SelectedItems)
            {
                entities.Add(entity);
            }

            listBox.UnselectAll();

            if (entities.Count > 0)
            {
                RemoveGameEntities(entities);
            }
        }
    }

    private void RemoveGameEntities(List<GameEntity> entities)
    {
        if (DataContext is Project { ActiveScene: Scene scene })
        {
            scene.RemoveGameEntities(entities);
        }
    }

    private void OnRemoveGameEntity_Button_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GameEntity entity })
        {
            RemoveGameEntities([entity]);
        }
    }

    private void OnGameEntities_ListBox_Loaded(object sender, RoutedEventArgs e)
    {
        var gameEntityListBox = sender as ListBox;
        if (gameEntityListBox.IsEnabled)
        {
            if (gameEntityListBox.Items.Count > 0)
            {
                gameEntityListBox.SelectedIndex = 0;
                var item = gameEntityListBox.ItemContainerGenerator
                    .ContainerFromIndex(gameEntityListBox.SelectedIndex) as ListBoxItem;
                item?.Focus();
            }
            else
            {
                GameEntityView.Instance.DataContext = null;
            }
        }
    }

    private void OnAddGameEntity_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        var tag = (string)menuItem.Tag;
        var scene = menuItem.DataContext as Scene;

        if (tag == "5" && scene.GameEntities.Any(x => x is AmbientLight))
        {
            Logger.Log(MessageType.Warning, "Scene already has an ambient light.");
            return;
        }

        GameEntity entity = tag switch
        {
            "0" or "1" => new GameEntity(scene),
            "2" => new DirectionalLight(scene, scene.LightSetKey),
            "3" => new PointLight(scene, scene.LightSetKey),
            "4" => new Spotlight(scene, scene.LightSetKey),
            "5" => new AmbientLight(scene, scene.LightSetKey),
            "6" => new PerspectiveCamera(scene),
            "7" => new OrthographicCamera(scene),
            _ => null
        };

        if (entity == null) return;

        entity.Name = menuItem.Header.ToString();

        if (tag == "1")
        {
            entity.IsActive = true;
            var c = new Components.Geometry(entity, DefaultAssets.DefaultGeometry);
            entity.AddComponent(c);
        }

        scene.AddGameEntities([entity]);
        activeSceneListBox.SelectedIndex = scene.GameEntities.Count - 1;
        activeSceneListBox.Focus();
    }

    private void OnRenameScene_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        var textBox = (TextBox)(sender as MenuItem).Tag;
        textBox.Visibility = Visibility.Visible;
        textBox.Focus();
    }

    public ProjectLayoutView()
    {
        InitializeComponent();
    }
}

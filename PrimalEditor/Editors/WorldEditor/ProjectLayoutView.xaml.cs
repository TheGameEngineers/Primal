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
public partial class ProjectLayoutView : UserControl
{
    private List<int> _previousSelectedIndices = [];
    private void OnRenameScene_Button_Click(object sender, RoutedEventArgs e)
    {
        var textBox = (TextBox)(sender as Button).Tag;
        textBox.Visibility = Visibility.Visible;
        textBox.Focus();
    }

    private void OnAddGameEntity_Button_Click(object sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        var scene = btn.DataContext as Scene;
        scene.AddGameEntityCommand.Execute(new GameEntity(scene) { Name = "Empty Game Entity" });
    }

    private void OnGameEntities_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var listBox = sender as ListBox;
        var vm = listBox.DataContext as Scene;

        var newSelection = listBox.SelectedItems.Cast<GameEntity>().ToList();
        var newSelectedIndices = newSelection.Select(item => vm.GameEntities.IndexOf(item)).ToList();
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

        MSGameEntity msEntities = null;
        if (newSelection.Count != 0)
        {
            msEntities = new MSGameEntity(newSelection);
        }
        GameEntityView.Instance.DataContext = msEntities;
    }

    private async void OnGameEntities_ListBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && sender is FrameworkElement { DataContext: Scene scene } && scene.IsActive)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

            var fileList = files?
                .Where(x => Path.GetExtension(x).ToLower() == Asset.AssetFileExtension && Asset.TryGetAssetInfo(x)?.Type == AssetType.Mesh)
                .ToList();
            List<GameEntity> entities = [];

            await Task.Run(() =>
            {
                foreach (var file in fileList)
                {
                    Debug.Assert(!string.IsNullOrEmpty(file.Trim()));
                    var assetInfo = Asset.TryGetAssetInfo(file);
                    if (assetInfo != null)
                    {
                        var entity = new GameEntity(scene) { Name = assetInfo.FileName.Trim() };
                        // NOTE: adding an entity to an active scene will automatically set its IsActive to true.
                        //       However, setting it to true here will create and upload entity resources without blocking the UI thread.
                        //       Also we can't add components to inactive entities, since they don't exist in the engine.
                        entity.IsActive = true;
                        entity.AddComponent(new Components.Geometry(entity, assetInfo));
                        entities.Add(entity);
                    }
                }
            });

            entities.ForEach(entity => scene.AddGameEntityCommand.Execute(entity));
        }
    }

    private void OnGameEntities_ListBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // TODO: remove entities.
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

    public ProjectLayoutView()
    {
        InitializeComponent();
    }
}

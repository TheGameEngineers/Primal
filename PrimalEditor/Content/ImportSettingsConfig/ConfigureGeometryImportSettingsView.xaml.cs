// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System.Windows;
using System.Windows.Controls;

namespace PrimalEditor.Content
{
    /// <summary>
    /// Interaction logic for ConfigureGeometryImportSettingsView.xaml
    /// </summary>
    public partial class ConfigureGeometryImportSettingsView : UserControl
    {
        private void OnRemove_Button_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ConfigureImportSettings;
            vm.GeometryImportSettingsConfigurator.RemoveFile((sender as FrameworkElement).DataContext as GeometryProxy);
        }


        private void OnImport_Button_Click(object sender, RoutedEventArgs e)
        {
            ((sender as FrameworkElement).DataContext as GeometryImportSettingsConfigurator).Import();
        }

        private void OnApplyToSelection_Button_Click(object sender, RoutedEventArgs e)
        {
            var settings = ((sender as FrameworkElement).DataContext as GeometryProxy).ImportSettings;
            var selection = geometryListBox.SelectedItems;
            foreach (GeometryProxy proxy in selection)
            {
                proxy.CopySettings(settings);
            }
        }

        private void OnApplyToAll_Button_Click(object sender, RoutedEventArgs e)
        {
            var settings = ((sender as FrameworkElement).DataContext as GeometryProxy).ImportSettings;
            var vm = DataContext as ConfigureImportSettings;
            foreach (var proxy in vm.GeometryImportSettingsConfigurator.GeometryProxies)
            {
                proxy.CopySettings(settings);
            }
        }

        private void OnListBox_Drop(object sender, DragEventArgs e)
        {
            ConfigureImportSettingsWindow.AddDroppedFiles(DataContext as ConfigureImportSettings, sender as ListBox, e);
        }

        private void OnClearImportingItems_Button_Click(object sender, RoutedEventArgs e)
        {
            ImportingItemCollection.Clear(AssetType.Animation);
            ImportingItemCollection.Clear(AssetType.Material);
            ImportingItemCollection.Clear(AssetType.Mesh);
            ImportingItemCollection.Clear(AssetType.Skeleton);
        }

        public ConfigureGeometryImportSettingsView()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                var item = geometryListBox.ItemContainerGenerator
                .ContainerFromIndex(geometryListBox.SelectedIndex) as ListBoxItem;
                item?.Focus();
            };
        }
    }
}

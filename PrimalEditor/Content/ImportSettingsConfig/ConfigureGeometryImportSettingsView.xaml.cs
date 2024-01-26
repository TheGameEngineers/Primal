// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PrimalEditor.Content
{
    /// <summary>
    /// Interaction logic for ConfigureGeometryImportSettingsView.xaml
    /// </summary>
    public partial class ConfigureGeometryImportSettingsView : UserControl
    {
        private void RefreshListBoxItems()
        {
            // TODO: this is quite hacky and feels icky. Can we refresh items numbering in some other way?
            var vm = (DataContext as ConfigureImportSettings).GeometryImportSettingsConfigurator;
            vm.OnPropertyChanged(nameof(vm.GeometryProxies));
        }

        private void OnRemove_Button_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ConfigureImportSettings;
            vm.GeometryImportSettingsConfigurator.RemoveFile((sender as FrameworkElement).DataContext as GeometryProxy);
            RefreshListBoxItems();
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
            RefreshListBoxItems();
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

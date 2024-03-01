// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using PrimalEditor.GameDev;
using PrimalEditor.GameProject;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PrimalEditor.Editors
{
    /// <summary>
    /// Interaction logic for WorldEditorView.xaml
    /// </summary>
    public partial class WorldEditorView : UserControl
    {
        public WorldEditorView()
        {
            InitializeComponent();
            Loaded += OnWorldEditorViewLoaded;
        }

        private void OnWorldEditorViewLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnWorldEditorViewLoaded;
            Focus();
        }

        private void OnNewScript_Button_Click(object sender, RoutedEventArgs e)
        {
            new NewScriptDialog().ShowDialog();
        }

        private void OnCreatePrimitiveMesh_Button_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new PrimitiveMeshDialog();
            dlg.ShowDialog();
        }

        private void UnloadAndCloseAllWindows()
        {
            Project.Current?.Unload();

            var mainWindow = Application.Current.MainWindow;

            foreach (Window win in Application.Current.Windows)
            {
                if (win != mainWindow)
                {
                    win.DataContext = null;
                    win.Close();
                }
            }

            mainWindow.DataContext = null;
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
    }
}

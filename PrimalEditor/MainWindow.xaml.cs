// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.GameProject;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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

namespace PrimalEditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static string PrimalPath { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnMainWindowLoaded;
            Closing += OnMainWindowClosing;
        }
        
        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnMainWindowLoaded;
            GetEnginePath();
            OpenProjectBrowserDialog();
        }

        private void GetEnginePath()
        {
            var primalPath = Environment.GetEnvironmentVariable("PRIMAL_ENGINE", EnvironmentVariableTarget.User);
            if(primalPath == null || !Directory.Exists(Path.Combine(primalPath, @"Engine\EngineAPI")))
            {
                var dlg = new EnginePathDialog();
                if (dlg.ShowDialog() == true)
                {
                    PrimalPath = dlg.PrimalPath;
                    Environment.SetEnvironmentVariable("PRIMAL_ENGINE", PrimalPath.ToUpper(), EnvironmentVariableTarget.User);
                }
                else
                {
                    Application.Current.Shutdown();
                }
            }
            else
            {
                PrimalPath = primalPath;
            }
        }

        private void OnMainWindowClosing(object sender, CancelEventArgs e)
        {
            Closing -= OnMainWindowClosing;
            Project.Current?.Unload();
        }

        private void OpenProjectBrowserDialog()
        {
            var projectBrowser = new ProjectBrowserDialog();
            if(projectBrowser.ShowDialog() == false || projectBrowser.DataContext == null)
            {
                Application.Current.Shutdown();
            }
            else
            {
                Project.Current?.Unload();
                DataContext = projectBrowser.DataContext;
            }
        }
    }
}

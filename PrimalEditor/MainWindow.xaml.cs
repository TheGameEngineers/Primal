// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using PrimalEditor.DllWrappers;
using PrimalEditor.GameProject;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace PrimalEditor;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
partial class MainWindow : Window
{
    private static readonly Version _editorVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
    public static string PrimalPath { get; private set; }

    private static bool VersionsAreEqual(int[] engineVersion, Version editorVersion) =>
        engineVersion[0] == editorVersion.Major &&
        engineVersion[1] == editorVersion.Minor &&
        engineVersion[2] == editorVersion.Build;

    private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnMainWindowLoaded;
        DefaultAssets.GenerateDefaultAssets();
        GetEnginePath();
        var initResult = EngineAPI.InitializeEngine();
        if (initResult == EngineAPIStructs.EngineInitError.Succeeded)
        {
            EngineAPI.GetEngineVersion(out var major, out var minor, out var revision);
            Debug.Assert(VersionsAreEqual([major, minor, revision], _editorVersion));
            OpenProjectBrowserDialog();
        }
        else
        {
            MessageBox.Show($"{initResult.GetDescription()}", "Engine initialization failed", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    private void GetEnginePath()
    {
        var primalPath = Environment.GetEnvironmentVariable("PRIMAL_ENGINE", EnvironmentVariableTarget.User);
        if (primalPath == null || !Directory.Exists(Path.Combine(primalPath, @"Engine\EngineAPI")))
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

    private void Shutdown()
    {
        Closing -= OnMainWindowClosing;

        foreach (Window win in Application.Current.Windows)
        {
            if (win != this)
            {
                win.Close();
            }
        }

        DataContext = null;
        Project.Current?.Unload();
        ContentToolsAPI.ShutDownContentTools();
        EngineAPI.ShutdownEngine();
    }

    private void OnMainWindowClosing(object sender, CancelEventArgs e)
    {
        if (DataContext == null)
        {
            e.Cancel = true;
            Application.Current.MainWindow.Hide();
            OpenProjectBrowserDialog();
            if (DataContext != null)
            {
                Application.Current.MainWindow.Show();
            }
        }
        else
        {
            Shutdown();
        }
    }

    private void OpenProjectBrowserDialog()
    {
        Project.Current?.Unload();
        var projectBrowser = new ProjectBrowserDialog();
        if (projectBrowser.ShowDialog() == false || projectBrowser.DataContext == null)
        {
            Shutdown();
            Application.Current.Shutdown();
        }
        else
        {
            var project = projectBrowser.DataContext as Project;
            Debug.Assert(project != null);
            DataContext = project;
            Title = $"Primal Editor v{_editorVersion.ToString(3)} [{project.Name}]";
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnMainWindowLoaded;
        Closing += OnMainWindowClosing;
    }
}

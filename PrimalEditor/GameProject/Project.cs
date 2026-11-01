// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using EnvDTE;
using PrimalEditor.Components;
using PrimalEditor.Content;
using PrimalEditor.DllWrappers;
using PrimalEditor.GameDev;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace PrimalEditor.GameProject;

[DataContract(Name = "Game")]
class Project : ViewModelBase
{
    public static event EventHandler SceneUpdated;

    private bool _isBuilding;
    private bool _isDebugging;
    private string _lastLoadedDllPath = string.Empty;

    public static string EngineApi => @"Include\EngineAPI";
    public static string EngineLib => "engine.lib";
    public static string PrimalEnv => "PRIMAL_ENGINE";
    public static string EditorPath { get; } = AppDomain.CurrentDomain.BaseDirectory;
    public static string Extension => ".primal";
    public static Lock LockScriptUpdates { get; private set; } = new();
    public static bool IsRunningInEditor { get; set; }

    [DataMember]
    public string Name { get; private set; } = "New Project";
    /// <summary>
    /// Gets the root folder that contains the current project.
    /// </summary>
    public string Path { get; private set; }
    /// <summary>
    /// Gets the full path of the current Primal project file, including its file name and extension.
    /// </summary>
    public string FullPath => $@"{Path}{Name}{Extension}";
    public string Solution => $@"{Path}{Name}.slnx";
    public string ContentPath => $@"{Path}Content\";
    public string TempFolder => $@"{Path}.Primal\Temp\";

    [DataMember]
    public int BuildConfig
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(BuildConfig));
            }
        }
    }

    public BuildConfiguration StandAloneBuildConfig => BuildConfig == 0 ? BuildConfiguration.Debug : BuildConfiguration.Release;
    public BuildConfiguration DLLBuildConfig => BuildConfig == 0 ? BuildConfiguration.DebugEditor : BuildConfiguration.ReleaseEditor;

    public string[] AvailableScripts
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(AvailableScripts));
            }
        }
    }


    [DataMember(Name = nameof(Scenes))]
    private readonly ObservableCollection<Scene> _scenes = [];
    public ReadOnlyObservableCollection<Scene> Scenes
    { get; private set; }

    public Scene ActiveScene
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(ActiveScene));
            }
        }
    }

    public static Project Current { get; private set; }

    public static UndoRedo UndoRedo { get; } = new UndoRedo();

    public ICommand UndoCommand { get; private set; }
    public ICommand RedoCommand { get; private set; }

    public ICommand AddSceneCommand { get; private set; }
    public ICommand RemoveSceneCommand { get; private set; }
    public ICommand SaveCommand { get; private set; }
    public ICommand DebugStartCommand { get; private set; }
    public ICommand DebugStartWithoutDebuggingCommand { get; private set; }
    public ICommand DebugStopCommand { get; private set; }
    public ICommand BuildCommand { get; private set; }

    private static void RefreshCommandState()
        => Application.Current?.Dispatcher.InvokeAsync(CommandManager.InvalidateRequerySuggested);

    public void UpdateScene()
    {
        if (Current != null) SceneUpdated?.Invoke(ActiveScene, new());
    }

    private void SetCommands()
    {
        AddSceneCommand = new RelayCommand<object>(x =>
        {
            AddScene($"New Scene {_scenes.Count}");
            var newScene = _scenes.Last();
            var sceneIndex = _scenes.Count - 1;

            UndoRedo.Add(new UndoRedoAction(
                () => RemoveScene(newScene),
                () => _scenes.Insert(sceneIndex, newScene),
                $"Add {newScene.Name}"));
        });

        RemoveSceneCommand = new RelayCommand<Scene>(x =>
        {
            var sceneIndex = _scenes.IndexOf(x);
            RemoveScene(x);

            UndoRedo.Add(new UndoRedoAction(
                () => _scenes.Insert(sceneIndex, x),
                () => RemoveScene(x),
                $"Remove {x.Name}"));
        }, x => !x.IsActive);

        UndoCommand = new RelayCommand<object>(x => UndoRedo.Undo(), x => UndoRedo.UndoList.Any());
        RedoCommand = new RelayCommand<object>(x => UndoRedo.Redo(), x => UndoRedo.RedoList.Any());
        SaveCommand = new RelayCommand<object>(x => Save(this));

        DebugStartCommand = new RelayCommand<object>(
            async _ => await RunGame(true),
            _ => !_isBuilding && !_isDebugging && VisualStudio.BuildSucceeded);

        DebugStartWithoutDebuggingCommand = new RelayCommand<object>(
            async _ => await RunGame(false),
            _ => !_isBuilding && !_isDebugging && VisualStudio.BuildSucceeded);

        DebugStopCommand = new RelayCommand<object>(
            async _ => await StopGame(),
            _ => _isDebugging);

        BuildCommand = new RelayCommand<bool>(
            async showWindow => await BuildGameCodeDLL(showWindow),
            _ => !_isBuilding && !_isDebugging);

        OnPropertyChanged(nameof(AddSceneCommand));
        OnPropertyChanged(nameof(RemoveSceneCommand));
        OnPropertyChanged(nameof(UndoCommand));
        OnPropertyChanged(nameof(RedoCommand));
        OnPropertyChanged(nameof(SaveCommand));
        OnPropertyChanged(nameof(DebugStartCommand));
        OnPropertyChanged(nameof(DebugStartWithoutDebuggingCommand));
        OnPropertyChanged(nameof(DebugStopCommand));
        OnPropertyChanged(nameof(BuildCommand));
    }

    private void AddScene(string sceneName)
    {
        Debug.Assert(!string.IsNullOrEmpty(sceneName.Trim()));
        _scenes.Add(new Scene(this, sceneName));
    }

    private void RemoveScene(Scene scene)
    {
        Debug.Assert(_scenes.Contains(scene));
        _scenes.Remove(scene);
    }

    public static Project Load(string file)
    {
        Debug.Assert(File.Exists(file));
        var path = System.IO.Path.GetDirectoryName(file);
        if (!path.EndsWith(System.IO.Path.DirectorySeparatorChar)) path += System.IO.Path.DirectorySeparatorChar;

        ContentWatcher.Reset($@"{path}Content\", path);
        LightSet.AddLightSet(LightSet.DefaultKey, true);

        try
        {
            var project = Serializer.FromFile<Project>(file);
            Debug.Assert(project != null);
            project.Path = path;
            Current = project;
            project.UpdateScene();
            return project;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Logger.Log(MessageType.Error, $"Failed to load project from {file}");
            if (Current != null)
            {
                ContentWatcher.Reset(Current.ContentPath, Current.Path);
            }
            return Current;
        }

    }

    public void Unload()
    {
        ActiveScene.GameEntities.ToList().ForEach(entity => entity.IsEnabled = false);
        UpdateScene();
        ActiveScene.IsActive = false;
        MSEntity.Reset();
        LightSet.Reset();
        UnloadGameCodeDLL();

        try
        {
            // Wait up to 5 seconds for VS to close
            VisualStudio.CloseVisualStudioAsync().Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        AssetRegistry.Save();
        UndoRedo.Reset();
        Logger.Clear();
        DeleteTempFolder();
        Current = null;
    }

    private void DeleteTempFolder()
    {
        if (Directory.Exists(TempFolder))
        {
            // Set attributes to normal to delete read-only files.
            _ = new DirectoryInfo(TempFolder) { Attributes = FileAttributes.Normal };
            Directory.Delete(TempFolder, true);
        }
    }

    private static void Save(Project project)
    {
        Serializer.ToFile(project, project.FullPath);
        Logger.Log(MessageType.Info, $"Project saved to {project.FullPath}");
    }

    private async Task RunGame(bool debug)
    {
        if (_isBuilding || _isDebugging) return;
        _isBuilding = true;
        RefreshCommandState();

        try
        {
            // Set ConfigureAwait to false to prevent forcing the UI thread to resume
            var succeeded = await VisualStudio.BuildSolutionAsync(this, StandAloneBuildConfig, debug).ConfigureAwait(false);

            if (!succeeded || !VisualStudio.BuildSucceeded)
            {
                Logger.Log(MessageType.Error, "Standalone build failed; not starting the game.");
                return;
            }

            Save(this);
            Chef.SaveForDryRun();
            Logger.Log(MessageType.Info, $"Starting game {(debug ? "with" : "without")} debugging");

            _isDebugging = true;
            RefreshCommandState();

            await VisualStudio.RunAsync(debug).ConfigureAwait(false);

            // Monitor the background debug state to unlock the UI when the game exits
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000); // Give VS time to transition into debug/run mode

                // Wait while the game is still running
                while (await VisualStudio.IsDebuggingAsync().ConfigureAwait(false))
                {
                    await Task.Delay(1000);
                }

                // Once finished, release the lock so the user can hit Start again
                _isDebugging = false;
                RefreshCommandState();
            });
        }
        catch (Exception ex)
        {
            Logger.Log(MessageType.Error, $"Failed to run game: {ex.Message}");
            _isDebugging = false;
        }
        finally
        {
            _isBuilding = false;
            RefreshCommandState();
        }
    }

    private async Task StopGame()
    {
        if (!_isDebugging) return;

        try
        {
            await VisualStudio.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Log(MessageType.Error, $"Failed to stop debugging: {ex.Message}");
        }
        finally
        {
            _isDebugging = false;
            RefreshCommandState();
        }
    }

    private async Task BuildGameCodeDLL(bool showWindow = true)
    {
        if (_isBuilding) return;

        _isBuilding = true;
        RefreshCommandState();

        var scriptNames = UnloadGameCodeDLL();
        try
        {
            var succeeded = await VisualStudio.BuildSolutionAsync(this, DLLBuildConfig, showWindow)
                .ConfigureAwait(false);

            if (!succeeded)
                Logger.Log(MessageType.Error, "Game-code DLL build failed.");
        }
        catch (Exception ex)
        {
            Logger.Log(MessageType.Error, $"Game-code DLL build threw: {ex.Message}");
        }
        finally
        {
            _isBuilding = false;
            LoadGameCodeDLL(scriptNames);
            RefreshCommandState();
        }
    }

    private List<(GameEntity Entity, string ScriptName)> RemoveScriptComponents() =>
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (!ActiveScene.IsActive) return [];

            var scriptNames = new List<(GameEntity Entity, string ScriptName)>();

            lock (LockScriptUpdates)
            {
                foreach (var entity in ActiveScene.GameEntities)
                {
                    if (ID.IsValid(entity.EntityId) && entity.GetComponent<Script>() is Script script)
                    {
                        Debug.Assert(entity.IsActive && ID.IsValid(entity.EntityId));
                        scriptNames.Add((entity, script.Name));
                        entity.RemoveComponent(script);
                    }
                }
            }
            MSEntity.Refresh();
            return scriptNames;
        });

    private void AddScriptComponents(List<(GameEntity Entity, string ScriptName)> scriptNames)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            lock (LockScriptUpdates)
            {
                foreach (var (entity, scriptName) in scriptNames)
                {
                    if (!ID.IsValid(entity.EntityId) || !AvailableScripts.Contains(scriptName)) continue;
                    Debug.Assert(entity.GetComponent<Script>() == null && !string.IsNullOrEmpty(scriptName));
                    var script = ComponentFactory.GetCreationFunction(ComponentType.Script)(entity, scriptName);
                    entity.AddComponent(script);
                }

                if (IsRunningInEditor) EngineAPI.BeginPlayAll();
            }
            MSEntity.Refresh();
        });
    }

    private void LoadGameCodeDLL(List<(GameEntity Entity, string ScriptName)> scriptNames)
    {
        var configName = VisualStudio.GetConfigurationName(DLLBuildConfig);
        var dll = $@"{Path}x64\{configName}\{Name}.dll";
        if (!File.Exists(dll) && !string.IsNullOrEmpty(_lastLoadedDllPath))
        {
            Logger.Log(MessageType.Error, $@"Failed to load \{configName}\{Name}.dll.");
            Logger.Log(MessageType.Info, $"Loading {System.IO.Path.GetRelativePath(Path, _lastLoadedDllPath)}");
            dll = _lastLoadedDllPath;
            if (!File.Exists(dll))
            {
                Logger.Log(MessageType.Error, "Failed to load previous version.");
                return;
            }
        }

        AvailableScripts = null;
        if (File.Exists(dll) && EngineAPI.LoadGameCodeDll(dll) != 0)
        {
            var scripts = EngineAPI.GetScriptNames();
            Application.Current.Dispatcher.Invoke(() => AvailableScripts = scripts);
            Logger.Log(MessageType.Info, "Game code DLL loaded successfully.");
            AddScriptComponents(scriptNames);
            _lastLoadedDllPath = dll;
        }
        else
        {
            Logger.Log(MessageType.Warning, "Failed to load game code DLL file. Try to build the project first!");
        }
    }

    private List<(GameEntity Entity, string ScriptName)> UnloadGameCodeDLL()
    {
        var scriptNames = RemoveScriptComponents();

        if (EngineAPI.UnloadGameCodeDll() != 0)
        {
            Logger.Log(MessageType.Info, "Game code DLL unloaded.");
            AvailableScripts = null;
        }

        return scriptNames;
    }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        LockScriptUpdates ??= new();

        if (_scenes != null)
        {
            Scenes = new ReadOnlyObservableCollection<Scene>(_scenes);
            OnPropertyChanged(nameof(Scenes));
        }

        ActiveScene = _scenes.FirstOrDefault(x => x.IsActive);
        Debug.Assert(ActiveScene != null);
        SetCommands();

        Task.Run(() => BuildGameCodeDLL(false));
    }

    public Project(string name, string path)
    {
        Name = name;
        Path = path;
        Debug.Assert(File.Exists((Path + Name + Extension).ToLower()));
        OnDeserialized(new StreamingContext());
    }
}

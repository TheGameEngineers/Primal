// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace PrimalEditor.GameProject;

[DataContract(Name = "Game")]
class Project : ViewModelBase
{
    public static event EventHandler SceneUpdated;

    public static string Extension => ".primal";
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
    public string Solution => $@"{Path}{Name}.sln";
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
        DebugStartCommand = new RelayCommand<object>(async x => await RunGame(true), x => !VisualStudio.IsDebugging() && VisualStudio.BuildDone);
        DebugStartWithoutDebuggingCommand = new RelayCommand<object>(async x => await RunGame(false), x => VisualStudio.BuildDone);
        DebugStopCommand = new RelayCommand<object>(async x => await StopGame(), x => VisualStudio.IsDebugging());
        BuildCommand = new RelayCommand<bool>(async x => await BuildGameCodeDLL(x), x => !VisualStudio.IsDebugging() && VisualStudio.BuildDone);

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
        VisualStudio.CloseVisualStudio();
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

    private void SaveToBinary()
    {
        var configName = VisualStudio.GetConfigurationName(StandAloneBuildConfig);
        var bin = $@"{Path}x64\{configName}\game.bin";

        using (var bw = new BinaryWriter(File.Open(bin, FileMode.Create, FileAccess.Write)))
        {
            bw.Write(ActiveScene.GameEntities.Count);
            foreach (var entity in ActiveScene.GameEntities)
            {
                bw.Write(0); // entity type (reserved for later)
                bw.Write(entity.Components.Count);
                foreach (var component in entity.Components)
                {
                    bw.Write((int)component.ToEnumType());
                    component.WriteToBinary(bw);
                }
            }
        }
    }

    private async Task RunGame(bool debug)
    {
        await Task.Run(() => VisualStudio.BuildSolution(this, StandAloneBuildConfig, debug));
        if (VisualStudio.BuildSucceeded)
        {
            SaveToBinary();
            await Task.Run(() => VisualStudio.Run(this, StandAloneBuildConfig, debug));
        }
    }

    private async Task StopGame() => await Task.Run(VisualStudio.Stop);

    private async Task BuildGameCodeDLL(bool showWindow = true)
    {
        try
        {
            var scriptNames = UnloadGameCodeDLL();
            await Task.Run(() => VisualStudio.BuildSolution(this, DLLBuildConfig, showWindow));
            if (VisualStudio.BuildSucceeded)
            {
                LoadGameCodeDLL(scriptNames);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            throw;
        }
    }

    private List<(GameEntity Entity, string ScriptName)> RemoveScriptComponents()
    {
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!ActiveScene.IsActive) return [];

            var scriptNames = new List<(GameEntity Entity, string ScriptName)>();

            foreach (var entity in ActiveScene.GameEntities)
            {
                if (ID.IsValid(entity.EntityId) && entity.GetComponent<Script>() is Script script)
                {
                    Debug.Assert(entity.IsActive && ID.IsValid(entity.EntityId));
                    scriptNames.Add((entity, script.Name));
                    entity.RemoveComponent(script);
                }
            }

            MSEntity.Refresh();
            return scriptNames;
        });

        return [];
    }

    private void AddScriptComponents(List<(GameEntity Entity, string ScriptName)> scriptNames)
    {
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (var (entity, scriptName) in scriptNames)
            {
                if (!ID.IsValid(entity.EntityId)) continue;
                Debug.Assert(entity.GetComponent<Script>() == null && !string.IsNullOrEmpty(scriptName));
                var script = ComponentFactory.GetCreationFunction(ComponentType.Script)(entity, scriptName);
                entity.AddComponent(script);
            }

            MSEntity.Refresh();
        });
    }

    private void LoadGameCodeDLL(List<(GameEntity Entity, string ScriptName)> scriptNames)
    {
        var configName = VisualStudio.GetConfigurationName(DLLBuildConfig);
        var dll = $@"{Path}x64\{configName}\{Name}.dll";
        AvailableScripts = null;
        if (File.Exists(dll) && EngineAPI.LoadGameCodeDll(dll) != 0)
        {
            AvailableScripts = EngineAPI.GetScriptNames();
            Logger.Log(MessageType.Info, "Game code DLL loaded successfully.");
            AddScriptComponents(scriptNames);
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
    private async void OnDeserialized(StreamingContext context)
    {
        if (_scenes != null)
        {
            Scenes = new ReadOnlyObservableCollection<Scene>(_scenes);
            OnPropertyChanged(nameof(Scenes));
        }

        ActiveScene = _scenes.FirstOrDefault(x => x.IsActive);
        Debug.Assert(ActiveScene != null);
        SetCommands();

        await BuildGameCodeDLL(false);
    }

    public Project(string name, string path)
    {
        Name = name;
        Path = path;
        Debug.Assert(File.Exists((Path + Name + Extension).ToLower()));
        OnDeserialized(new StreamingContext());
    }
}

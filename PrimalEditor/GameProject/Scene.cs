// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.Utilities;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Input;

namespace PrimalEditor.GameProject;

[DataContract]
class Scene : ViewModelBase
{
    public ICommand RenameCommand { get; private set; }

    [DataMember]
    public string Name
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    [DataMember]
    public Project Project { get; init; }

    [DataMember]
    public bool IsActive
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                SetActiveGameEntities(field);
                OnPropertyChanged(nameof(IsActive));
            }
        }
    }

    [DataMember(Name = nameof(GameEntities))]
    private readonly ObservableCollection<GameEntity> _gameEntities = [];
    public ReadOnlyObservableCollection<GameEntity> GameEntities { get; private set; }

    private void SetActiveGameEntities(bool isActive)
    {
        foreach (var entity in _gameEntities)
        {
            entity.IsActive = isActive;
        }
    }

    private void AddGameEntities_Internal(List<(GameEntity, bool, int)> entities)
    {
        if (entities.Count == 0) return;

        foreach (var (entity, isEnabled, index) in entities)
        {
            Debug.Assert(!_gameEntities.Contains(entity));

            entity.IsEnabled = isEnabled;
            entity.IsActive = IsActive;

            if (index == -1 || index >= _gameEntities.Count)
            {
                _gameEntities.Add(entity);
            }
            else
            {
                _gameEntities.Insert(index, entity);
            }
        }

        Project.UpdateScene();
    }

    public void AddGameEntities(List<GameEntity> entities, int index = -1)
    {
        if (entities.Count == 0) return;

        var enableList = entities.Select(entity => (entity, entity.IsEnabled, index)).ToList();
        AddGameEntities_Internal(enableList);
        index = _gameEntities.Count - 1;

        Project.UndoRedo.Add(new UndoRedoAction(
            () => RemoveGameEntities_Internal(entities),
            () => AddGameEntities_Internal(enableList),
            entities.Count == 1 ?
                $"Add {entities[0].Name} to {Name}" :
                $"Add multiple entities to {Name}"));
    }

    private List<(GameEntity, bool, int)> RemoveGameEntities_Internal(List<GameEntity> entities)
    {
        if (entities.Count == 0) return [];

        var enableList = DisableAndUpdate(entities);
        var indices = enableList.Select(x => (x.Entity, x.IsEnabled, _gameEntities.IndexOf(x.Entity))).ToList();

        foreach (var entity in entities)
        {
            Debug.Assert(_gameEntities.Contains(entity));
            entity.IsActive = false;
            _gameEntities.Remove(entity);
        }

        return indices;
    }

    public void RemoveGameEntities(List<GameEntity> entities)
    {
        if (entities.Count == 0) return;

        var enableList = RemoveGameEntities_Internal(entities);

        Project.UndoRedo.Add(new UndoRedoAction(
            () => AddGameEntities_Internal(enableList),
            () => RemoveGameEntities_Internal(entities),
            entities.Count == 1 ?
                $"Remove {entities[0].Name} from {Name}" :
                $"Remove multiple entities from {Name}"));
    }

    public List<IdType> GetGeometryComponentIds()
    {
        var ids = GameEntities
            .Where(entity => entity.IsEnabled && entity.IsActive)
            .Select(entity => entity.GetComponent<Geometry>())
            .Where(c => c != null)
            .Select(c => c.GetComponentId())
            .ToList();

        Debug.Assert(ids.All(ID.IsValid));
        return ids;
    }

    public List<(GameEntity Entity, bool IsEnabled)> DisableAndUpdate(List<GameEntity> entities, bool update = true)
    {
        var enableList = entities.Select(x => (x, x.IsEnabled)).ToList();
        entities.ForEach(x => x.IsEnabled = false);

        if (update) Project.UpdateScene();

        return enableList;
    }

    public void EnableAndUpdate(List<(GameEntity Entity, bool IsEnabled)> enableList, bool update = true)
    {
        enableList.ForEach(x => x.Entity.IsEnabled = x.IsEnabled);
        if (update) Project.UpdateScene();
    }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        if (_gameEntities != null)
        {
            GameEntities = new ReadOnlyObservableCollection<GameEntity>(_gameEntities);
            OnPropertyChanged(nameof(GameEntities));
        }

        RenameCommand = new RelayCommand<string>(x =>
        {
            var oldName = Name;
            Name = x;

            Project.UndoRedo.Add(new UndoRedoAction(nameof(Name), this,
                oldName, x, $"Rename scene '{oldName}' to '{x}'"));
        }, x => x != Name);
    }

    public Scene(Project project, string name)
    {
        Debug.Assert(project != null);
        Project = project;
        Name = name;
        OnDeserialized(new StreamingContext());
    }
}

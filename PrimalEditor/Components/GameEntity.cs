// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.DllWrappers;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;

namespace PrimalEditor.Components;

[DataContract]
[KnownType(typeof(Transform))]
[KnownType(typeof(Script))]
[KnownType(typeof(Geometry))]
class GameEntity : ViewModelBase
{
    public IdType EntityId { get; private set; } = ID.INVALID_ID;

    public bool IsActive
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                if (field)
                {
                    _components.ToList().ForEach(x => x.Load());
                    EntityId = EngineAPI.EntityAPI.CreateGameEntity(this);
                    Debug.Assert(ID.IsValid(EntityId));
                }
                else if (ID.IsValid(EntityId))
                {
                    EngineAPI.EntityAPI.RemoveGameEntity(EntityId);
                    _components.ToList().ForEach(x => x.Unload());
                    EntityId = ID.INVALID_ID;
                }

                OnPropertyChanged(nameof(IsActive));
            }
        }
    }

    [DataMember]
    public bool IsEnabled
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
    } = true;

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
    public Scene ParentScene { get; init; }

    [DataMember(Name = nameof(Components))]
    private readonly ObservableCollection<Component> _components = [];
    public ReadOnlyObservableCollection<Component> Components { get; private set; }

    public Component GetComponent(Type type) => Components.FirstOrDefault(c => c.GetType() == type);
    public T GetComponent<T>() where T : Component => GetComponent(typeof(T)) as T;

    public bool AddComponent(Component component)
    {
        Debug.Assert(component != null);
        if (!Components.Any(x => x.GetType() == component.GetType()))
        {
            component.Load();
            _components.Add(component);
            EngineAPI.EntityAPI.UpdateComponent(this, component.ToEnumType());
            return true;
        }
        Logger.Log(MessageType.Warning, $"Entity {Name} already has a {component.GetType().Name} component.");
        return false;
    }

    public void RemoveComponent(Component component)
    {
        Debug.Assert(component != null);
        if (component is Transform || !_components.Contains(component)) return; // Transform component can't be removed

        _components.Remove(component);
        EngineAPI.EntityAPI.UpdateComponent(this, component.ToEnumType());
        component.Unload();
    }

    [OnDeserialized]
    void OnDeserialized(StreamingContext context)
    {
        if (_components != null)
        {
            Components = new ReadOnlyObservableCollection<Component>(_components);
            OnPropertyChanged(nameof(Components));
        }
    }

    public GameEntity(Scene scene)
    {
        Debug.Assert(scene != null);
        ParentScene = scene;
        _components.Add(new Transform(this));
        OnDeserialized(new StreamingContext());
    }
}

abstract class MSEntity : ViewModelBase
{
    // Enables updates to selected entities
    private bool _enableUpdates = true;

    public bool? IsEnabled
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(IsEnabled));
            }
        }
    }

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

    private readonly ObservableCollection<IMSComponent> _components = [];
    public ReadOnlyObservableCollection<IMSComponent> Components { get; }

    public List<GameEntity> SelectedEntities { get; }

    public Scene ParentScene => SelectedEntities[0].ParentScene;

    public static MSEntity CurrentSelection { get; private set; }

    public T GetMSComponent<T>() where T : IMSComponent
    {
        return (T)Components.FirstOrDefault(x => x.GetType() == typeof(T));
    }


    private void MakeComponentList()
    {
        _components.Clear();
        var firstEntity = SelectedEntities.FirstOrDefault();
        if (firstEntity == null) return;

        foreach (var component in firstEntity.Components)
        {
            var type = component.GetType();
            if (!SelectedEntities.Skip(1).Any(entity => entity.GetComponent(type) == null))
            {
                Debug.Assert(Components.FirstOrDefault(x => x.GetType() == type) == null);
                _components.Add(component.GetMultiselectionComponent(this));
            }
        }
    }

    public static int? GetMixedValue<T>(List<T> objects, Func<T, int> getProperty)
    {
        var value = getProperty(objects.First());
        return objects.Skip(1).Any(x => value != getProperty(x)) ? null : value;
    }

    public static float? GetMixedValue<T>(List<T> objects, Func<T, float> getProperty)
    {
        var value = getProperty(objects.First());
        return objects.Skip(1).Any(x => !getProperty(x).IsTheSameAs(value)) ? null : value;
    }

    public static bool? GetMixedValue<T>(List<T> objects, Func<T, bool> getProperty)
    {
        var value = getProperty(objects.First());
        return objects.Skip(1).Any(x => value != getProperty(x)) ? null : value;
    }

    public static string GetMixedValue<T>(List<T> objects, Func<T, string> getProperty)
    {
        var value = getProperty(objects.First());
        return objects.Skip(1).Any(x => value != getProperty(x)) ? null : value;
    }

    protected virtual bool UpdateGameEntities(string propertyName)
    {
        switch (propertyName)
        {
            case nameof(IsEnabled): SelectedEntities.ForEach(x => x.IsEnabled = IsEnabled.Value); return true;
            case nameof(Name): SelectedEntities.ForEach(x => x.Name = Name); return true;
        }
        return false;
    }

    protected virtual bool UpdateMSGameEntity()
    {
        IsEnabled = GetMixedValue(SelectedEntities, new Func<GameEntity, bool>(x => x.IsEnabled));
        Name = GetMixedValue(SelectedEntities, new Func<GameEntity, string>(x => x.Name));

        return true;
    }

    public static void Reset()
    {
        CurrentSelection = null;
    }

    public void Refresh()
    {
        _enableUpdates = false;
        UpdateMSGameEntity();
        MakeComponentList();
        _enableUpdates = true;
    }

    public MSEntity(List<GameEntity> entities)
    {
        Debug.Assert(entities?.Count > 0);
        CurrentSelection = this;
        Components = new ReadOnlyObservableCollection<IMSComponent>(_components);
        SelectedEntities = entities;
        PropertyChanged += (s, e) => { if (_enableUpdates) UpdateGameEntities(e.PropertyName); };
    }
}

class MSGameEntity : MSEntity
{
    public MSGameEntity(List<GameEntity> entities) : base(entities)
    {
        Refresh();
    }
}

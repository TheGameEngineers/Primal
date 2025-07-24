// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using System;
using System.Diagnostics;

namespace PrimalEditor.Components;

enum ComponentType
{
    Transform,
    Script,
    Geometry,
}

static class ComponentFactory
{
    private static readonly Func<GameEntity, object, Component>[] _functions =
        [
            (entity, data) => new Transform(entity),
            (entity, data) => new Script(entity){ Name = (string)data},
            (entity, data) => new Geometry(entity, (AssetInfo)data),
        ];

    public static Func<GameEntity, object, Component> GetCreationFunction(ComponentType componentType)
    {
        Debug.Assert((int)componentType < _functions.Length);
        return _functions[(int)componentType];
    }

    public static ComponentType ToEnumType(this Component component)
    {
        return component switch
        {
            Transform _ => ComponentType.Transform,
            Script _ => ComponentType.Script,
            Geometry _ => ComponentType.Geometry,
            _ => throw new ArgumentException("Unknown component type"),
        };
    }
}

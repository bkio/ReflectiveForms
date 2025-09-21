// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using AngleSharp.Html.Dom;
using CrossCloudKit.Interfaces.Classes;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Operation;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class DisplayCondition(string displayCondition) : Attribute
{
    public readonly string Condition = displayCondition;
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
public abstract class Field : Attribute
{
    internal Field() {}

    public string? Label;

    public string? Instructions;

    public FieldType Type;

    private object? _calculatedDynamicDefaultValueNullable;
    public object? CalculatedDynamicDefaultValueNullable
    {
        get => _calculatedDynamicDefaultValueNullable;
        set
        {
            _calculatedDynamicDefaultValueNullable = value;
            OverrideDefaultValue(value);
        }
    }

    public abstract Task<OperationResult<bool>> SanityCheckAsync(
        int entityId,
        JObject haystack,
        string jNeedleFieldName,
        string jsObjectPathIncludingThis,
        EntityOperationState operationState,
        CancellationToken cancellationToken);

    public abstract Task GenerateAdminEditHtmlElementAsync(
        string entityName,
        CreateElement createElement,
        IHtmlDivElement elementWrapper,
        JObject parentObjectOfCurrentValueJToken,
        JToken nullableCurrentValueJToken,
        string jsObjectPathIncludingThis,
        string jFieldName,
        int depth,
        EntityOperationState operationState,
        bool isForReserveParentElement,
        CancellationToken cancellationToken);

    public abstract Task GenerateViewHtmlElementAsync(
        string entityName,
        CreateElement createElement,
        IHtmlDivElement elementWrapper,
        JToken currentValueJToken,
        string jFieldName,
        int depth,
        EntityOperationState operationState,
        CancellationToken cancellationToken);

    public abstract void SetDefaultValue(EntityOperationState operationState, Action<object> setValue);

    protected abstract void OverrideDefaultValue(object? value);
}

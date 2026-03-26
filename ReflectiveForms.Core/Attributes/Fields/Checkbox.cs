// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json.Linq;
using AngleSharp.Html.Dom;
using CrossCloudKit.Interfaces.Classes;
using ReflectiveForms.Core.Utilities;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Operation;

namespace ReflectiveForms.Core.Attributes.Fields;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class Checkbox : Field
{
    private bool _defaultValue;
    protected override void OverrideDefaultValue(object? value)
    {
        _defaultValue = (bool)(value ?? false);
    }

    public Checkbox(
        string label, string instructions,
        bool defaultValue)
    {
        Type = FieldType.Checkbox;

        Label = label;
        Instructions = instructions;

        _defaultValue = defaultValue;
    }

    public override Task<OperationResult<bool>> SanityCheckAsync(
        int entityId,
        JObject haystack,
        string jNeedleFieldName,
        string jsObjectPathIncludingThis,
        EntityOperationState operationState,
        CancellationToken cancellationToken)
    {
        if (haystack.ContainsKey(jNeedleFieldName)
            && haystack[jNeedleFieldName] is { Type: JTokenType.Boolean })
            return Task.FromResult(OperationResult<bool>.Success(true));
        if (haystack.ContainsKey(jNeedleFieldName))
            haystack[jNeedleFieldName] = _defaultValue;
        else
            haystack.Add(jNeedleFieldName, _defaultValue);

        return Task.FromResult(OperationResult<bool>.Success(true));
    }

    public override Task GenerateAdminEditHtmlElementAsync(
        string entityName,
        CreateElement createElement,
        IHtmlDivElement elementWrapper,
        JObject parentObjectOfCurrentValueJToken,
        JToken? nullableCurrentValueJToken,
        string jsObjectPathIncludingThis,
        string jFieldName,
        int depth,
        EntityOperationState operationState,
        bool isForReserveParentElement,
        CancellationToken cancellationToken)
    {
        var element = createElement.Invoke<IHtmlInputElement>();
        elementWrapper.AppendChild(element);
        element.Type = "checkbox";

        if (nullableCurrentValueJToken is { Type: JTokenType.Boolean })
        {
            if ((bool)nullableCurrentValueJToken)
            {
                element.SetAttribute("checked", "");
            }
        }
        else
        {
            if (_defaultValue)
            {
                element.SetAttribute("checked", "");
            }

            if (!parentObjectOfCurrentValueJToken.ContainsKey(jFieldName))
                parentObjectOfCurrentValueJToken.Add(jFieldName, _defaultValue);
            else
                parentObjectOfCurrentValueJToken[jFieldName] = _defaultValue;
        }

        element.SetAttribute("onchange", $"RF.FormState.setCheckboxValue('{jsObjectPathIncludingThis}', this);");
        return Task.CompletedTask;
    }

    public override void SetDefaultValue(EntityOperationState operationState, Action<object> setValue)
    {
        setValue(_defaultValue);
    }

    public override Task GenerateViewHtmlElementAsync(
        string entityName,
        CreateElement createElement,
        IHtmlDivElement elementWrapper,
        JToken? currentValueJToken,
        string jFieldName,
        int depth,
        EntityOperationState operationState,
        CancellationToken cancellationToken)
    {
        var element = createElement.Invoke<IHtmlSpanElement>();
        elementWrapper.AppendChild(element);

        if (currentValueJToken is { Type: JTokenType.Boolean })
        {
            element.InnerHtml = (bool)currentValueJToken ?
                "<i class='fa-regular fa-square-check'></i>"
                : "<i class='fa-regular fa-square'></i>";
        }
        else
        {
            element.InnerHtml = "Not specified";
        }
        return Task.CompletedTask;
    }
}

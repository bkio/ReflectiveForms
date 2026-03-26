// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using Newtonsoft.Json.Linq;
using AngleSharp.Html.Dom;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using ReflectiveForms.Core.Utilities;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Operation;

namespace ReflectiveForms.Core.Attributes.Fields;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class WysiwygEditor : Field
{
    private readonly bool _mandatory;

    public WysiwygEditor(
        string label, string instructions, bool mandatory)
    {
        Type = FieldType.WysiwygEditor;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;
    }

    public override Task<OperationResult<bool>> SanityCheckAsync(
        int entityId,
        JObject haystack,
        string jNeedleFieldName,
        string jsObjectPathIncludingThis,
        EntityOperationState operationState,
        CancellationToken cancellationToken)
    {
        if (!haystack.TryGetValue(jNeedleFieldName, out var value))
        {
            return Task.FromResult(!_mandatory
                ? OperationResult<bool>.Success(true)
                : OperationResult<bool>.Failure($"Field {jNeedleFieldName} is mandatory and missing.", HttpStatusCode.BadRequest));
        }

        if (!_mandatory && value.Type == JTokenType.Null)
            return Task.FromResult(OperationResult<bool>.Success(true));

        if (haystack[jNeedleFieldName] is not { Type: JTokenType.String })
        {
            return Task.FromResult(OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Type is incorrect.", HttpStatusCode.BadRequest));
        }

        var casted = (haystack[jNeedleFieldName]?.Value<string>()).NotNull();
        if (_mandatory && casted.Length == 0)
        {
            return Task.FromResult(OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Should have at least one character.", HttpStatusCode.BadRequest));
        }

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
        var element = createElement.Invoke<IHtmlTextAreaElement>();
        elementWrapper.AppendChild(element);

        element.SetAttribute("rows", "10");
        if (this._mandatory) element.SetAttribute("required", "");

        element.DefaultValue = nullableCurrentValueJToken is { Type: JTokenType.String }
            ? nullableCurrentValueJToken.Value<string>().NotNull()
            : "";

        if (!parentObjectOfCurrentValueJToken.ContainsKey(jFieldName))
            parentObjectOfCurrentValueJToken.Add(jFieldName, element.DefaultValue);
        else
            parentObjectOfCurrentValueJToken[jFieldName] = element.DefaultValue;

        element.SetAttribute("onchange", $"RF.FormState.setFieldValue('{jsObjectPathIncludingThis}', this.value);");
        element.SetAttribute("oninput", "window.global_oninput(this);");
        return Task.CompletedTask;
    }

    public override void SetDefaultValue(EntityOperationState operationState, Action<object> setValue)
    {
        setValue("");
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
        var element = createElement.Invoke<IHtmlDivElement>();
        elementWrapper.AppendChild(element);

        if (currentValueJToken is not { Type: JTokenType.String }) return Task.CompletedTask;
        var asString = currentValueJToken.Value<string>().NotNull();
        element.InnerHtml = asString.Replace(Environment.NewLine, "<br>");

        return Task.CompletedTask;
    }

    protected override void OverrideDefaultValue(object? value)
    {
        //Irrelevant
    }
}

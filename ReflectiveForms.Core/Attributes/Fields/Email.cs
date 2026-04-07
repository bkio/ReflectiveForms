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

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class Email : Field
{
    private readonly bool _mandatory;

    private string? _defaultValueNullable;
    protected override void OverrideDefaultValue(object? value)
    {
        _defaultValueNullable = (string?)value;
    }

    private readonly string _placeholderText;

    public Email(
        string label, string instructions, bool mandatory,
        string placeholderText)
    {
        Type = FieldType.Email;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _placeholderText = placeholderText;
    }
    public Email(
        string label, string instructions, bool mandatory,
        string defaultValue, string placeholderText)
    {
        Type = FieldType.Email;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _defaultValueNullable = defaultValue;
        _placeholderText = placeholderText;
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
                : OperationResult<bool>.Failure($"{Label} is mandatory and missing.", HttpStatusCode.BadRequest));
        }

        if (!_mandatory && value.Type == JTokenType.Null)
            return Task.FromResult(OperationResult<bool>.Success(true));

        if (haystack[jNeedleFieldName] is not { Type: JTokenType.String })
        {
            return Task.FromResult(OperationResult<bool>.Failure($"{Label}: Type is incorrect.", HttpStatusCode.BadRequest));
        }

        var casted = (haystack[jNeedleFieldName]?.Value<string>()).NotNull();
        if (casted.Length > 0 && !NetworkUtilities.IsValidEmail(casted))
        {
            return Task.FromResult(OperationResult<bool>.Failure($"{Label}: Should represent a valid email address.", HttpStatusCode.BadRequest));
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
        var element = createElement.Invoke<IHtmlInputElement>();
        elementWrapper.AppendChild(element);

        element.Type = "email";
        if (_mandatory) element.SetAttribute("required", "");
        element.SetAttribute("placeholder", this._placeholderText);

        if (nullableCurrentValueJToken is { Type: JTokenType.String })
        {
            element.DefaultValue = nullableCurrentValueJToken.Value<string>().NotNull();
        }
        else if (_defaultValueNullable != null)
        {
            element.DefaultValue = _defaultValueNullable;
        }
        else
        {
            element.DefaultValue = "";
        }

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
        setValue(_defaultValueNullable ?? "");
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

        if (currentValueJToken is not { Type: JTokenType.String }) return Task.CompletedTask;

        var asString = currentValueJToken.Value<string>();
        element.InnerHtml = $"<a href='mailto:{asString}'>{asString}</a>";

        return Task.CompletedTask;
    }
}

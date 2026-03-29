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
public sealed class Select : Field
{
    private string? _defaultValue;
    protected override void OverrideDefaultValue(object? value)
    {
        _defaultValue = (string?)value;
    }

    private string[]? _choices;
    public string[]? Choices
    {
        get => _choices;
        set
        {
            _choices = value;
            if (_choices == null) return;

            _choicesDb.Clear(); // optional: clear previous entries
            foreach (var choicePair in _choices)
            {
                var split = choicePair.Split(" : ");
                _choicesDb.Add(split[0]);
            }
        }
    }

    public string RuntimeChoiceJsFunction
    {
        set => _internalRuntimeChoiceJsFunction = value;
    }
    private string? _internalRuntimeChoiceJsFunction;

    private readonly HashSet<string> _choicesDb = [];

    public Select(
        string label, string instructions,
        string defaultValue, string[]? choices)
    {
        Type = FieldType.Select;

        Label = label;
        Instructions = instructions;

        _defaultValue = defaultValue;
        Choices = choices;
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
            return Task.FromResult(OperationResult<bool>.Success(true));
        }

        if (value.Type != JTokenType.String)
        {
            return Task.FromResult(OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Type is incorrect.", HttpStatusCode.BadRequest));
        }

        var casted = (haystack[jNeedleFieldName]?.Value<string>()).NotNull();
        if (!_choicesDb.Contains(casted))
        {
            // Skip validation for DynamicChoicesRuntimeAsync fields whose choices
            // are resolved at runtime via JavaScript — __choicesDb is empty for these.
            if (_internalRuntimeChoiceJsFunction != null)
                return Task.FromResult(OperationResult<bool>.Success(true));

            return Task.FromResult(OperationResult<bool>.Failure(casted.Length == 0
                ? $"Field {jNeedleFieldName}: Mandatory to choose an option."
                : $"Field {jNeedleFieldName}: Unexpected choice {casted}.", HttpStatusCode.BadRequest));
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
        var element = createElement.Invoke<IHtmlSelectElement>();
        elementWrapper.AppendChild(element);
        element.StyleElement("max-width: 100%;");

        string? defaultSelection;
        if (nullableCurrentValueJToken is { Type: JTokenType.String }
            && nullableCurrentValueJToken.Value<string>().NotNull().Length > 0)
        {
            defaultSelection = nullableCurrentValueJToken.Value<string>().NotNull();
        }
        else
        {
            defaultSelection = _defaultValue;

            if (!parentObjectOfCurrentValueJToken.ContainsKey(jFieldName))
                parentObjectOfCurrentValueJToken.Add(jFieldName, _defaultValue);
            else
                parentObjectOfCurrentValueJToken[jFieldName] = _defaultValue;
        }

        if (Choices is { Length: > 0 })
        {
            foreach (var choicePair in Choices)
            {
                var split = choicePair.Split(" : ");

                var optionElement = createElement.Invoke<IHtmlOptionElement>();
                element.AppendChild(optionElement);

                optionElement.Value = split[0];
                optionElement.Text = split[1];

                if (defaultSelection == optionElement.Value)
                    optionElement.SetAttribute("selected", "");
            }
        }

        if (_internalRuntimeChoiceJsFunction != null)
        {
            element.SetAttribute("dynamic-options-function", _internalRuntimeChoiceJsFunction);
            element.SetAttribute("dynamic-options-input-path", jsObjectPathIncludingThis.TrimEnd($"{jFieldName}").Trim('.'));
        }

        element.SetAttribute("onchange", $"RF.FormState.setFieldValue('{jsObjectPathIncludingThis}', this.value);");
        //No need for oninput, it is a select element

        return Task.CompletedTask;
    }

    public override void SetDefaultValue(EntityOperationState operationState, Action<object> setValue)
    {
        if (_defaultValue != null) setValue(_defaultValue);
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

        if (currentValueJToken is { Type: JTokenType.String })
        {
            if (Choices is { Length: > 0 })
            {
                foreach (var choicePair in Choices)
                {
                    var split = choicePair.Split(" : ");

                    if (split[0] != currentValueJToken.Value<string>()) continue;
                    element.InnerHtml = split[1];
                    return Task.CompletedTask;
                }
            }

            element.InnerHtml = currentValueJToken.Value<string>().NotNull();
        }
        else
        {
            element.InnerHtml = "Not specified";
        }
        return Task.CompletedTask;
    }
}

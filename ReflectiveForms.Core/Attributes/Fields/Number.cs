// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Net;
using Newtonsoft.Json.Linq;
using AngleSharp.Html.Dom;
using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using ReflectiveForms.Core.Utilities;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Operation;

namespace ReflectiveForms.Core.Attributes.Fields;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class Number : Field
{
    private readonly bool _mandatory;

    private double? _defaultValue;
    protected override void OverrideDefaultValue(object? value)
    {
        _defaultValue = value switch
        {
            double d => d,
            float f => f,
            long l => l,
            int i => i,
            _ => _defaultValue
        };
    }

    private readonly double? _minimumValue;

    private readonly double? _maximumValue;

    private readonly double? _stepSize;

    private readonly string _placeholderText = "";

    public Number() => Type = FieldType.Number;
    public Number(
        string label, string instructions, bool mandatory,
        string placeholderText)
    {
        Type = FieldType.Number;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _placeholderText = placeholderText;
    }
    public Number(
        string label, string instructions, bool mandatory,
        string placeholderText, double defaultValue)
    {
        Type = FieldType.Number;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _placeholderText = placeholderText;
        _defaultValue = defaultValue;
    }
    public Number(
        string label, string instructions, bool mandatory,
        string placeholderText, double[] minimumMaximumValues)
    {
        Type = FieldType.Number;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _placeholderText = placeholderText;

        _minimumValue = minimumMaximumValues[0];
        _maximumValue = minimumMaximumValues[1];
    }
    public Number(
        string label, string instructions, bool mandatory,
        string placeholderText, double defaultValue, double[] minimumMaximumValues)
    {
        Type = FieldType.Number;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _placeholderText = placeholderText;

        _defaultValue = defaultValue;
        _minimumValue = minimumMaximumValues[0];
        _maximumValue = minimumMaximumValues[1];
    }
    public Number(
        string label, string instructions, bool mandatory,
        string placeholderText, double[] minimumMaximumValues, double stepSize)
    {
        Type = FieldType.Number;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _placeholderText = placeholderText;

        _minimumValue = minimumMaximumValues[0];
        _maximumValue = minimumMaximumValues[1];
        _stepSize = stepSize;
    }
    public Number(
        string label, string instructions, bool mandatory,
        string placeholderText, double defaultValue, double[] minimumMaximumValues, double stepSize)
    {
        Type = FieldType.Number;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _placeholderText = placeholderText;
        _defaultValue = defaultValue;

        _minimumValue = minimumMaximumValues[0];
        _maximumValue = minimumMaximumValues[1];
        _stepSize = stepSize;
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

        if (haystack[jNeedleFieldName] is not { Type: JTokenType.Float }
            && haystack[jNeedleFieldName] is not { Type: JTokenType.Integer })
        {
            return Task.FromResult(OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Type is incorrect.", HttpStatusCode.BadRequest));
        }

        var casted = (double)haystack[jNeedleFieldName].NotNull();

        return casted < _minimumValue
            ? Task.FromResult(OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Value given is {casted}. Must be >= {_minimumValue}", HttpStatusCode.BadRequest))
            : Task.FromResult(casted > _maximumValue
                ? OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Value given is {casted}. Must be <= {_maximumValue}", HttpStatusCode.BadRequest)
                : OperationResult<bool>.Success(true));
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

        element.Type = "number";
        if (_mandatory) element.SetAttribute("required", "");
        element.SetAttribute("placeholder", _placeholderText);
        if (_minimumValue.HasValue)
        {
            element.Minimum = _minimumValue.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (_maximumValue.HasValue)
        {
            element.Maximum = _maximumValue.Value.ToString(CultureInfo.InvariantCulture);
        }
        if (_stepSize.HasValue)
        {
            element.Step = _stepSize.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (nullableCurrentValueJToken is { Type: JTokenType.Integer or JTokenType.Float })
        {
            element.DefaultValue = nullableCurrentValueJToken.Type == JTokenType.Integer ? ((int)nullableCurrentValueJToken).ToString() : ((double)nullableCurrentValueJToken).ToString(CultureInfo.InvariantCulture);
        }
        else if (_defaultValue.HasValue)
        {
            element.DefaultValue = _defaultValue.Value.ToString(CultureInfo.InvariantCulture);

            if (!parentObjectOfCurrentValueJToken.ContainsKey(jFieldName))
                parentObjectOfCurrentValueJToken.Add(jFieldName, _defaultValue.Value);
            else
                parentObjectOfCurrentValueJToken[jFieldName] = _defaultValue.Value;
        }
        else
        {
            element.DefaultValue = "";
        }

        element.SetAttribute("onchange", $"RF.FormState.setNumberValue('{jsObjectPathIncludingThis}', this);");
        element.SetAttribute("oninput", "window.global_oninput(this);");
        return Task.CompletedTask;
    }

    public override void SetDefaultValue(EntityOperationState operationState, Action<object> setValue)
    {
        if (!_defaultValue.HasValue) return;
        try
        {
            setValue(_defaultValue.Value);
        }
        catch
        {
            try
            {
                setValue((float)_defaultValue.Value);
            }
            catch
            {
                try
                {
                    setValue((long)_defaultValue.Value);
                }
                catch
                {
                    setValue((int)_defaultValue.Value);
                }
            }
        }
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

        if (currentValueJToken == null) return Task.CompletedTask;

        element.InnerHtml = currentValueJToken.Type switch
        {
            JTokenType.Integer => ((int)currentValueJToken).ToString(),
            JTokenType.Float => ((double)currentValueJToken).ToString(CultureInfo.InvariantCulture),
            _ => element.InnerHtml
        };

        return Task.CompletedTask;
    }
}

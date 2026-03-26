// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Globalization;
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
public sealed class Range : Field
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

    private readonly double _minimumValue;

    private readonly double _maximumValue;

    private readonly double _stepSize;

    public Range(
        string label, string instructions, bool mandatory,
        double minimumValue, double maximumValue, double stepSize = 1.0f)
    {
        Type = FieldType.Range;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _minimumValue = minimumValue;
        _maximumValue = maximumValue;
        _stepSize = stepSize;
    }
    public Range(
        string label, string instructions, bool mandatory,
        double defaultValue, double minimumValue, double maximumValue, double stepSize = 1.0f)
    {
        Type = FieldType.Range;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _defaultValue = defaultValue;
        _minimumValue = minimumValue;
        _maximumValue = maximumValue;
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
                ? OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Value given is {casted}. Must be <= {_minimumValue}", HttpStatusCode.BadRequest)
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
        var decrementButtonElement = elementWrapper.CreateButtonOnElement(createElement, "", "fa-solid fa-minus");
        decrementButtonElement.SetAttribute("onclick", $"RF.FormState.incrementRange(this, {this._stepSize * -1}, {this._minimumValue}, {this._maximumValue});");

        var incrementButtonElement = elementWrapper.CreateButtonOnElement(createElement, "", "fa-solid fa-plus");
        incrementButtonElement.SetAttribute("onclick", $"RF.FormState.incrementRange(this, {this._stepSize}, {this._minimumValue}, {this._maximumValue});");

        var element = createElement.Invoke<IHtmlInputElement>();
        elementWrapper.AppendChild(element);

        element.Type = "range";
        if (_mandatory) element.SetAttribute("required", "");
        element.Minimum = _minimumValue.ToString(CultureInfo.InvariantCulture);
        element.Maximum = _maximumValue.ToString(CultureInfo.InvariantCulture);
        element.Step = _stepSize.ToString(CultureInfo.InvariantCulture);

        var numberShowElement = createElement.Invoke<IHtmlSpanElement>();
        elementWrapper.AppendChild(numberShowElement);

        if (nullableCurrentValueJToken is { Type: JTokenType.Integer or JTokenType.Float })
        {
            element.DefaultValue = nullableCurrentValueJToken.Type == JTokenType.Integer ? ((int)nullableCurrentValueJToken).ToString() : ((double)nullableCurrentValueJToken).ToString(CultureInfo.InvariantCulture);

            numberShowElement.InnerHtml = element.DefaultValue;
        }
        else if (_defaultValue.HasValue)
        {
            element.DefaultValue = _defaultValue.Value.ToString(CultureInfo.InvariantCulture);
            numberShowElement.InnerHtml = element.DefaultValue;

            if (!parentObjectOfCurrentValueJToken.ContainsKey(jFieldName))
                parentObjectOfCurrentValueJToken.Add(jFieldName, _defaultValue.Value);
            else
                parentObjectOfCurrentValueJToken[jFieldName] = _defaultValue.Value;
        }
        else
        {
            element.DefaultValue = "";
            numberShowElement.InnerHtml = element.DefaultValue;
        }

        element.SetAttribute("onchange", $"RF.FormState.setRangeValue('{jsObjectPathIncludingThis}', this);");
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

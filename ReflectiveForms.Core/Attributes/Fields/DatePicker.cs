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

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class DatePicker : Field
{
    private readonly bool _mandatory;

    private string? _defaultValueNullable;
    protected override void OverrideDefaultValue(object? value)
    {
        _defaultValueNullable = (string?)value ?? throw new ArgumentNullException(nameof(value));
    }

    private readonly string _dateFormat;

    public DatePicker(
        string label, string instructions, bool mandatory,
        string dateFormat = "yyyyMMdd")
    {
        Type = FieldType.DatePicker;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _dateFormat = dateFormat;
    }
    public DatePicker(
        string label, string instructions, bool mandatory,
        string defaultValue, string dateFormat = "yyyyMMdd")
    {
        Type = FieldType.DatePicker;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _defaultValueNullable = defaultValue;
        _dateFormat = dateFormat;
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

        var casted = haystack[jNeedleFieldName]?.Value<string>();
        return Task.FromResult(!DateTime.TryParseExact(casted, _dateFormat, null, DateTimeStyles.None, out _)
            ? OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Should represent a time with format {_dateFormat}.", HttpStatusCode.BadRequest) :
            OperationResult<bool>.Success(true));
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
        var element = createElement.Invoke<IHtmlDivElement>();
        elementWrapper.AppendChild(element);

        var elementDay = createElement.Invoke<IHtmlInputElement>();
        var elementMonth = createElement.Invoke<IHtmlSelectElement>();
        var elementYear = createElement.Invoke<IHtmlInputElement>();
        element.AppendChild(elementDay);
        element.AppendChild(elementMonth);
        element.AppendChild(elementYear);

        elementDay.Type = "number";
        elementYear.Type = "number";
        if (_mandatory)
        {
            elementDay.SetAttribute("required", "");
            elementMonth.SetAttribute("required", "");
            elementYear.SetAttribute("required", "");
        }

        elementDay.StyleElement("width: 3em; height: 2em;");
        elementMonth.StyleElement("width: 9em; height: 2em;");
        elementYear.StyleElement("width: 4em; height: 2em;");

        elementDay.Minimum = "1";
        elementDay.Maximum = "31";

        elementYear.Minimum = "2023";
        elementYear.Maximum = "2033";

        elementDay.Step = "1";
        elementYear.Step = "1";

        var selectedMonth = -1;
        if (nullableCurrentValueJToken is { Type: JTokenType.String }
            && DateTime.TryParseExact(nullableCurrentValueJToken.Value<string>(), _dateFormat, null, DateTimeStyles.None, out DateTime parsed))
        {
            var selectedYear = parsed.Year;
            selectedMonth = parsed.Month;
            var selectedDay = parsed.Day;
            elementYear.DefaultValue = selectedYear.ToString();
            elementDay.DefaultValue = selectedDay.ToString();
        }
        else if (_defaultValueNullable != null
            && DateTime.TryParseExact(_defaultValueNullable, _dateFormat, null, DateTimeStyles.None, out parsed))
        {
            var selectedYear = parsed.Year;
            selectedMonth = parsed.Month;
            var selectedDay = parsed.Day;
            elementYear.DefaultValue = selectedYear.ToString();
            elementDay.DefaultValue = selectedDay.ToString();

            if (!parentObjectOfCurrentValueJToken.ContainsKey(jFieldName))
                parentObjectOfCurrentValueJToken.Add(jFieldName, new DateTime(selectedYear, selectedMonth, selectedDay).ToString(_dateFormat, CultureInfo.InvariantCulture));
            else
                parentObjectOfCurrentValueJToken[jFieldName] = new DateTime(selectedYear, selectedMonth, selectedDay).ToString(_dateFormat, CultureInfo.InvariantCulture);
        }

        for (var i = 1; i <= 12; i++)
        {
            var optionElement = createElement.Invoke<IHtmlOptionElement>();
            elementMonth.AppendChild(optionElement);
            optionElement.Value = i.ToString();
            optionElement.Text = DateUtilities.MonthNames[i];
            if (selectedMonth == i)
                optionElement.SetAttribute("selected", "");
        }

        var onChangeScript = $"RF.FormState.setDateValue('{jsObjectPathIncludingThis}', this.parentElement, '{_dateFormat.ToUpper()}');";
        elementDay.SetAttribute("onchange", onChangeScript);
        elementMonth.SetAttribute("onchange", onChangeScript);
        elementYear.SetAttribute("onchange", onChangeScript);

        elementDay.SetAttribute("oninput", "window.global_oninput(this);");
        //No need for Element_Month oninput. It is a select element.
        elementYear.SetAttribute("oninput", "window.global_oninput(this);");
        return Task.CompletedTask;
    }

    public override void SetDefaultValue(EntityOperationState operationState, Action<object> setValue)
    {
        if (_defaultValueNullable != null
            && DateTime.TryParseExact(_defaultValueNullable, _dateFormat, null, DateTimeStyles.None, out var parsed))
        {
            setValue(parsed.ToString(_dateFormat));
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

        DateTime dateInstance;
        switch (currentValueJToken.Type)
        {
            case JTokenType.Date:
                dateInstance = (DateTime)currentValueJToken;
                break;
            case JTokenType.String
                when DateTime.TryParseExact(currentValueJToken.Value<string>(), _dateFormat, null, DateTimeStyles.None, out var tmp):
                dateInstance = tmp;
                break;
            default:
                return Task.CompletedTask;
        }

        element.InnerHtml = dateInstance.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);
        return Task.CompletedTask;
    }
}

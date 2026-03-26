// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using Newtonsoft.Json.Linq;
using AngleSharp.Html.Dom;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using ReflectiveForms.Core.Utilities;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Operation;

namespace ReflectiveForms.Core.Attributes.Fields;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class Relation : Field
{
    private readonly bool _mandatory;

    private readonly string _relationEntityName;

    private readonly bool _isRelationEntityNotExistsOk;

    private int? _defaultValueId;
    protected override void OverrideDefaultValue(object? value)
    {
        _defaultValueId = (int)(value ?? 0);
    }

    public Relation(
        string label, string instructions, bool mandatory,
        string relationEntityName, bool isRelationEntityNotExistsOk)
    {
        Type = FieldType.Relation;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _relationEntityName = relationEntityName;
        _isRelationEntityNotExistsOk = isRelationEntityNotExistsOk;
    }
    public Relation(
        string label, string instructions, bool mandatory,
        string relationEntityName, bool isRelationEntityNotExistsOk, int defaultValueId)
    {
        Type = FieldType.Relation;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;

        _relationEntityName = relationEntityName;
        _isRelationEntityNotExistsOk = isRelationEntityNotExistsOk;
        _defaultValueId = defaultValueId;
    }

    public override async Task<OperationResult<bool>> SanityCheckAsync(
        int entityId,
        JObject haystack,
        string jNeedleFieldName,
        string jsObjectPathIncludingThis,
        EntityOperationState operationState,
        CancellationToken cancellationToken)
    {
        if (!haystack.TryGetValue(jNeedleFieldName, out var value))
        {
            return !_mandatory
                ? OperationResult<bool>.Success(true)
                : OperationResult<bool>.Failure($"Field -{jNeedleFieldName}- is mandatory, but missing.", HttpStatusCode.BadRequest);
        }

        if (!_mandatory && value.Type == JTokenType.Null)
            return OperationResult<bool>.Success(true);

        if (haystack[jNeedleFieldName] is not { Type: JTokenType.Integer })
        {
            if (haystack[jNeedleFieldName] is { Type: JTokenType.Float })
            {
                haystack[jNeedleFieldName] = (int)Math.Round((float)haystack[jNeedleFieldName].NotNull());
            }
            else
            {
                return OperationResult<bool>.Failure($"Field -{jNeedleFieldName}-: Type is incorrect: {haystack[jNeedleFieldName].NotNull().Type}", HttpStatusCode.BadRequest);
            }
        }

        var casted = (int)haystack[jNeedleFieldName].NotNull();
        if (casted is -1 or 0)
        {
            return !_mandatory
                ? OperationResult<bool>.Success(true)
                : OperationResult<bool>.Failure($"Field -{jNeedleFieldName}- is mandatory, but missing.", HttpStatusCode.BadRequest);
        }

        if (_isRelationEntityNotExistsOk)
        {
            return OperationResult<bool>.Success(true);
        }

        var getResult = await operationState.GetEntityInOperationAsync(
            _relationEntityName,
            casted,
            cancellationToken);
        if (getResult.IsSuccessful) return OperationResult<bool>.Success(true);

        return getResult.StatusCode == HttpStatusCode.NotFound
            ? OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Entity type {_relationEntityName} with an {EntityModelAttributes.Id} of {casted} does not exist.", getResult.StatusCode)
            : OperationResult<bool>.Failure(getResult.StatusCode == HttpStatusCode.BadRequest
                ? $"Field {jNeedleFieldName}: Error occured during checking existence of post type {_relationEntityName} with an {EntityModelAttributes.Id} of {casted}. Failure code: 400"
                : $"Field {jNeedleFieldName}: Error occured during checking existence of post type {_relationEntityName} with an {EntityModelAttributes.Id} of {casted}. Failure: {getResult.ErrorMessage}", getResult.StatusCode);
    }

    public override async Task GenerateAdminEditHtmlElementAsync(
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

        if (_mandatory) element.SetAttribute("required", "");

        var refreshButtonElement = elementWrapper.CreateButtonOnElement(createElement, "Refresh", "fa-solid fa-arrows-rotate");
        refreshButtonElement.SetAttribute("onclick", $"window.refresh_relation_list(this.parentNode.querySelector('select'), '{_relationEntityName}');");

        JArray? entities = null;

        var getEntitiesResult = await operationState.GetAllEntitiesInOperationAsync(
            _relationEntityName,
            cancellationToken);
        if (!getEntitiesResult.IsSuccessful)
        {
            entities = [];
        }

        string? defaultSelection = null;
        string? tmp = null;
        if (nullableCurrentValueJToken is { Type: JTokenType.Integer })
        {
            defaultSelection = ((int)nullableCurrentValueJToken).ToString();

            if (isForReserveParentElement
                && defaultSelection is "-1" or "0")
            {
                tmp = defaultSelection;
                defaultSelection = null;
            }
        }

        if (defaultSelection == null)
        {
            if (tmp != null) //Order is important logic-wise. Tmp being here is essential.
            {
                defaultSelection = tmp;
            }
            else if (_defaultValueId.HasValue)
            {
                defaultSelection = _defaultValueId.Value.ToString();
            }
        }

        var optionElement = createElement.Invoke<IHtmlOptionElement>();
        element.AppendChild(optionElement);
        optionElement.Value = "-1";
        optionElement.Text = "";
        if (defaultSelection is null or "-1" or "0")
        {
            optionElement.SetAttribute("selected", "");
        }
        else
        {
            if (!parentObjectOfCurrentValueJToken.ContainsKey(jFieldName))
                parentObjectOfCurrentValueJToken.Add(jFieldName, int.Parse(defaultSelection));
            else
                parentObjectOfCurrentValueJToken[jFieldName] = int.Parse(defaultSelection);
        }

        var sortedDic = new SortedDictionary<string, List<int>>();
        foreach (var choiceJToken in entities.NotNull())
        {
            var choiceJObject = (JObject)choiceJToken;

            if (!choiceJObject.TryGetTypedValue(EntityModelAttributes.Id, out int choiceId)) continue;

            if (!choiceJObject.TryGetTypedValue(EntityModelAttributes.Title, out JObject? titleJObject)
                || !titleJObject.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out string? titleRendered))
                continue;

            if (titleRendered != null && sortedDic.TryGetValue(titleRendered, out var existing))
            {
                existing.Add(choiceId);
            }
            else
            {
                if (titleRendered != null) sortedDic.Add(titleRendered, [choiceId]);
            }
        }

        foreach (var (choiceNameOrTitle, choiceIds) in sortedDic)
        {
            foreach (var choiceId in choiceIds)
            {
                optionElement = createElement.Invoke<IHtmlOptionElement>();
                element.AppendChild(optionElement);

                optionElement.Value = choiceId.ToString();
                optionElement.Text = choiceNameOrTitle + (choiceIds.Count > 1 ? $" (Id: {choiceId})" : "");

                if (defaultSelection == optionElement.Value)
                    optionElement.SetAttribute("selected", "");
            }
        }

        element.SetAttribute("onchange", $"RF.FormState.setNumberValue('{jsObjectPathIncludingThis}', this);");
        //No need for oninput, it is a select element
    }

    public override void SetDefaultValue(EntityOperationState operationState, Action<object> setValue)
    {
        if (_defaultValueId.HasValue)
        {
            setValue(_defaultValueId.Value);
        }
    }

    public override async Task GenerateViewHtmlElementAsync(
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

        if (currentValueJToken is { Type: JTokenType.Integer })
        {
            var entityId = (int)currentValueJToken;
            if (entityId >= 1)
            {
                var getResult =
                    await operationState.GetEntityInOperationAsync(_relationEntityName, entityId, cancellationToken);
                if (getResult.IsSuccessful)
                {
                    var entityObject = getResult.Data;

                    if (!entityObject.NotNull().TryGetTypedValue(EntityModelAttributes.Title, out JObject? titleJObject)
                        || !titleJObject.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out string? titleRendered))
                    {
                        titleRendered = $"{_relationEntityName}: {entityId}";
                    }

                    element.InnerHtml =
                        $"<a href='{RfConfiguration.EndpointConfiguration.GetEntityUrl(_relationEntityName, entityId)}'>{titleRendered}</a>";
                    return;
                }
            }
        }

        element.InnerHtml = "<i class='fa-solid fa-link-slash'></i>";
    }
}

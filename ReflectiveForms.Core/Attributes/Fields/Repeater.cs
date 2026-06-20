// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using AngleSharp.Html.Dom;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Operation;
using ReflectiveForms.Core.Utilities;
// ReSharper disable ClassNeverInstantiated.Global

namespace ReflectiveForms.Core.Attributes.Fields;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class Repeater : Field
{
    private readonly Type _repeaterFor;

    /// <summary>
    /// The BaseModel type used for each row in this repeater.
    /// </summary>
    internal Type RepeaterFor => _repeaterFor;

    private readonly GroupRenderStyle _groupRenderStyle;
    private readonly RepeatUseAccordion _useAccordion;

    private readonly string _addButtonLabel;

    public readonly int? MinimumRows;

    public readonly int? MaximumRows;

    public Repeater(
        string label, string instructions,
        Type repeaterFor, string addButtonLabel,
        GroupRenderStyle groupRenderStyle = GroupRenderStyle.Full,
        RepeatUseAccordion useAccordion = RepeatUseAccordion.Yes)
    {
        Type = FieldType.Repeater;

        Label = label;
        Instructions = instructions;

        _repeaterFor = repeaterFor;
        _addButtonLabel = addButtonLabel;

        _groupRenderStyle = groupRenderStyle;
        _useAccordion = useAccordion;
    }
    public Repeater(
        string label, string instructions,
        Type repeaterFor, string addButtonLabel,
        int minimumRows, int maximumRows,
        GroupRenderStyle groupRenderStyle = GroupRenderStyle.Full,
        RepeatUseAccordion useAccordion = RepeatUseAccordion.Yes)
    {
        Type = FieldType.Repeater;

        Label = label;
        Instructions = instructions;

        _repeaterFor = repeaterFor;
        _addButtonLabel = addButtonLabel;

        MinimumRows = minimumRows;
        MaximumRows = maximumRows;

        _groupRenderStyle = groupRenderStyle;
        _useAccordion = useAccordion;
    }

    public override async Task<OperationResult<bool>> SanityCheckAsync(
        int entityId,
        JObject haystack,
        string jNeedleFieldName,
        string jsObjectPathIncludingThis,
        EntityOperationState operationState,
        CancellationToken cancellationToken)
    {
        if (!haystack.ContainsKey(jNeedleFieldName))
        {
            return OperationResult<bool>.Success(true);
        }

        if (haystack[jNeedleFieldName] is { Type: JTokenType.Null })
        {
            haystack[jNeedleFieldName] = new JArray();
        }

        if (haystack[jNeedleFieldName] is not { Type: JTokenType.Array })
        {
            return OperationResult<bool>.Failure($"{Label}: Type is not array.", HttpStatusCode.BadRequest);
        }

        var casted = (JArray)haystack[jNeedleFieldName].NotNull();

        if (MinimumRows.HasValue && casted.Count < MinimumRows.Value)
        {
            return OperationResult<bool>.Failure($"{Label}: Should have at least {MinimumRows.Value} elements. (Has {casted.Count})", HttpStatusCode.BadRequest);
        }

        if (MaximumRows.HasValue && casted.Count > MaximumRows)
        {
            return OperationResult<bool>.Failure($"{Label}: Could have maximum {MaximumRows} elements. (Has {casted.Count})", HttpStatusCode.BadRequest);
        }

        var i = 0;
        foreach (var arrayItemToken in casted)
        {
            if (arrayItemToken.Type != JTokenType.Object)
            {
                return OperationResult<bool>.Failure($"{Label}: Type is array, but found an array element being non-object.", HttpStatusCode.BadRequest);
            }

            var arrayItemCasted = (JObject)arrayItemToken;
            var sanityCheckResult = await EntitySanityChecker.JObjectStaticSanityCheckAsync(
                entityId,
                _repeaterFor,
                arrayItemCasted,
                $"{jsObjectPathIncludingThis}[{i++}]",
                operationState,
                cancellationToken);
            if (!sanityCheckResult.IsSuccessful)
            {
                return sanityCheckResult;
            }
        }

        return OperationResult<bool>.Success(true);
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
    //If isParentAReserveElement is true;
    //We are in a child array for creating a reserve element for parent array.
        bool isParentAReserveElement,
        CancellationToken cancellationToken)
    {
        var randomStringForScript = StringUtilities.GenerateRandomString(8);

        var (tableWrapper, tableElement, _, body) = elementWrapper.CreateTableOnCard(createElement, false);

        tableElement.AddClasses("fields_table_element");
        if (_useAccordion == RepeatUseAccordion.Yes)
        {
            tableElement.AddClasses("accordion");
        }
        tableElement.Id = $"table_{randomStringForScript}";

        //
        // Reserve elements for add operation
        //
        JObject? defaultItemJObject = null;
        //
        //
        //

        JArray arrayObject;
        if (nullableCurrentValueJToken is { Type: JTokenType.Array })
        {
            arrayObject = (JArray)nullableCurrentValueJToken;
            if (isParentAReserveElement)
            {
                if (arrayObject.Count > 0)
                {
                    defaultItemJObject = (JObject)arrayObject[0];
                }
            }
        }
        else
        {
            arrayObject = [];
        }

        if (!parentObjectOfCurrentValueJToken.ContainsKey(jFieldName))
            parentObjectOfCurrentValueJToken.Add(jFieldName, arrayObject);
        else
            parentObjectOfCurrentValueJToken[jFieldName] = arrayObject;

        //
        // Reserve elements for add operation
        //
        if (defaultItemJObject == null)
        {
            var fullDefaultReflectiveField = await EntityModelDefaultsBuilder.CreateDefaultEntityFieldsObjectAsync(
                entityName,
                operationState,
                false/*This is for reserve element creation. Unique id will be set by js on add.*/,
                cancellationToken);
            var defaultArray = (JArray)fullDefaultReflectiveField.SelectToken(EntityModelDefaultsBuilder.JsObjectPathSetAllArrayIndexesToZero(jsObjectPathIncludingThis).TrimStart('.')).NotNull();
            defaultItemJObject = (JObject)defaultArray[0];
        }
        //
        //
        //

        if (MinimumRows.HasValue && arrayObject.Count < MinimumRows.Value)
        {
            for (var i = arrayObject.Count; i < MinimumRows.Value; i++)
            {
                var localClonedDefaultObject = (JObject)defaultItemJObject.DeepClone();
                EntityModelDefaultsBuilder.IterativelyChangeUniqueFieldIdsWithRandomIds(localClonedDefaultObject);
                arrayObject.Add(localClonedDefaultObject);
            }
        }
        if (MaximumRows.HasValue && arrayObject.Count > MaximumRows)
        {
            for (var i = arrayObject.Count - 1; i >= MaximumRows; i--)
            {
                arrayObject.RemoveAt(i);
            }
        }

        var setupTableRowWithButtonsScriptElement = createElement.Invoke<IHtmlScriptElement>();
        tableWrapper.AppendChild(setupTableRowWithButtonsScriptElement);
        setupTableRowWithButtonsScriptElement.InnerHtml = $$"""

            // Setup function using external RF.Repeater module
            window.setup_table_row_with_buttons_{{randomStringForScript}} = function(_table_element, _initial_row_index) {
                RF.Repeater.setupRowButtons(
                    _table_element,
                    _initial_row_index,
                    '{{jsObjectPathIncludingThis}}',
                    {{(_useAccordion == RepeatUseAccordion.Yes ? "true" : "false")}}
                );
            };
            """;
        for (var i = 0; i < arrayObject.Count; i++)
        {
            var itemJObject = (JObject)arrayObject[i];

            var rowElement = createElement.Invoke<IHtmlTableRowElement>();
            body.AppendChild(rowElement);

            var cellElement = createElement.Invoke<IHtmlTableDataCellElement>();
            rowElement.AppendChild(cellElement);
            var (arrayElementWrapper, arrayElementHeader, arrayElementContent) = cellElement.CreateCardOnCol(createElement);

            arrayElementWrapper.RemoveClasses("border-left-primary").AddClasses("mx-0", "mx-xl-4");

            var cardHeaderCol = arrayElementHeader.QuerySelector(".card-header-col");
            cardHeaderCol.RemoveClasses("col").AddClasses("mx-2", "d-xl-flex", "justify-content-xl-start");
            if (_useAccordion == RepeatUseAccordion.Yes)
            {
                var contentId = StringUtilities.GenerateRandomString(32, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase);
                var expandButtonAreaId = StringUtilities.GenerateRandomString(32, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase);

                arrayElementContent.Id = contentId;

                var expandButtonArea = createElement.Invoke<IHtmlDivElement>();
                expandButtonArea.Id = expandButtonAreaId;
                cardHeaderCol?.AppendChild(expandButtonArea);
                {
                    var h2 = createElement.Invoke("h2");
                    cardHeaderCol?.AppendChild(h2);
                    {
                        var expandButton = h2.CreateButtonOnElement(createElement, $"{Label}: {(i + 1)}", "fa-solid fa-arrows-up-down");
                        expandButton.SetAttribute("data-toggle", "collapse");
                        expandButton.SetAttribute("data-target", $"#{contentId}");
                        expandButton.SetAttribute("aria-expanded", arrayObject.Count == 1 ? "true" : "false");
                        expandButton.SetAttribute("aria-controls", contentId);
                    }
                }

                arrayElementContent.AddClasses("collapse");
                if (arrayObject.Count == 1) arrayElementContent.AddClasses("show");
                arrayElementContent.SetAttribute("aria-labelledby", expandButtonAreaId);
                arrayElementContent.SetAttribute("data-parent", $"#{tableElement.Id}");
            }
            else if (cardHeaderCol != null)
            {
                cardHeaderCol.InnerHtml = $"{Label}: {i + 1}";
            }

            var deleteMoveButtonsCol = arrayElementHeader.CreateColFitContentLeftAlignedOnRow(createElement).AddClasses("align-items-center");
            deleteMoveButtonsCol.CreateButtonOnElement(createElement, "", "fa-solid fa-circle-chevron-up").AddClasses("mx-1", "row_move_up_button");
            deleteMoveButtonsCol.CreateButtonOnElement(createElement, "", "fa-solid fa-circle-chevron-down").AddClasses("mx-1", "row_move_down_button");
            deleteMoveButtonsCol.CreateButtonOnElement(createElement, "", "fa-solid fa-trash").AddClasses("mx-1", "row_delete_button");

            await EntityViewBuilder.JObjectGenerateAdminFrontendHtmlAsync(
                entityName,
                createElement,
                arrayElementContent,
                _repeaterFor,
                itemJObject,
                $"{jsObjectPathIncludingThis}.find(e => e.{BaseModel.UniqueFieldIdPropertyName} === '{(itemJObject[BaseModel.UniqueFieldIdPropertyName]?.Value<string>()).NotNull()}')",
                depth + 1,
                _groupRenderStyle,
                operationState,
                isParentAReserveElement,
                cancellationToken);
        }
        //
        //Reserve elements for add operation
        //
        var reserveElementDiv = createElement.Invoke<IHtmlDivElement>();
        tableWrapper.AppendChild(reserveElementDiv);
        reserveElementDiv.ClassList.Add("fields_reserve_element", "d-none");

        if (isParentAReserveElement)
        {
            await EntityViewBuilder.JObjectGenerateAdminFrontendHtmlAsync(
                entityName,
                createElement,
                reserveElementDiv,
                _repeaterFor,
                defaultItemJObject,
                $"{jsObjectPathIncludingThis}.find(e => e.{BaseModel.UniqueFieldIdPropertyName} === '{(defaultItemJObject[BaseModel.UniqueFieldIdPropertyName]?.Value<string>()).NotNull()}')",
                depth + 1,
                _groupRenderStyle,
                operationState,
                true,
                cancellationToken); //true because it is a reserve element
        }
        else
        {
            var defaultObjectUniqueFieldId = StringUtilities.GenerateRandomString(16, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase);

            var ix = 1;
            IterativelyChangeUniqueFieldIds(defaultItemJObject, defaultObjectUniqueFieldId, ref ix);

            await EntityViewBuilder.JObjectGenerateAdminFrontendHtmlAsync(
                entityName,
                createElement,
                reserveElementDiv,
                _repeaterFor,
                defaultItemJObject,
                $"{jsObjectPathIncludingThis}.find(e => e.{BaseModel.UniqueFieldIdPropertyName} === '{defaultObjectUniqueFieldId}_1')",
                depth + 1,
                _groupRenderStyle,
                operationState,
                true,
                cancellationToken); //true because it is reserve element
        }
        //
        //
        //

        if ((!MinimumRows.HasValue || !MaximumRows.HasValue || MinimumRows.Value != MaximumRows)
            && this._addButtonLabel.Length > 0)
        {
            var addButtonElement = tableWrapper.CreateButtonOnElement(createElement, _addButtonLabel, "fa-solid fa-plus").AddClasses("fields_repeater_add_button");

            // Use external RF.Repeater.addItem function
            var defaultItemJson = defaultItemJObject.ToString(Newtonsoft.Json.Formatting.None).Replace("'", "\\'");
            addButtonElement.SetAttribute("onclick", $$"""
                RF.Repeater.addItem(
                    this,
                    JSON.parse('{{defaultItemJson}}'),
                    '{{jsObjectPathIncludingThis}}',
                    '{{this.Label}}',
                    {{(_useAccordion == RepeatUseAccordion.Yes ? "true" : "false")}},
                    'setup_table_row_with_buttons_{{randomStringForScript}}'
                );
                """);
        }

        var jsFunctionOnPageSetup = createElement.Invoke<IHtmlScriptElement>();
        tableWrapper.AppendChild(jsFunctionOnPageSetup);
        jsFunctionOnPageSetup.Type = "text/javascript";
        jsFunctionOnPageSetup.InnerHtml = $$"""

                                            if (typeof window.last_table_element_waiting_to_be_processed_for_setup_buttons !== 'undefined' && window.last_table_element_waiting_to_be_processed_for_setup_buttons !== null) {
                                            let local_table_element = window.last_table_element_waiting_to_be_processed_for_setup_buttons.querySelector('.fields_table_element');
                                            for (let i = 0; i < local_table_element.rows.length; i++) {
                                                window.setup_table_row_with_buttons_{{randomStringForScript}}(local_table_element, i);
                                            }
                                            }
                                            else {
                                            const local_table_element = document.getElementById('table_{{randomStringForScript}}');
                                            for (let i = 0; i < local_table_element.rows.length; i++) {
                                                window.setup_table_row_with_buttons_{{randomStringForScript}}(local_table_element, i);
                                            }
                                            }
                                            """;
    }

    private static void IterativelyChangeUniqueFieldIds(JObject clonedDefaultObject, string newUniqueFieldId, ref int uniqueFieldIx)
    {
        if (clonedDefaultObject.ContainsKey(BaseModel.UniqueFieldIdPropertyName))
        {
            clonedDefaultObject[BaseModel.UniqueFieldIdPropertyName] = $"{newUniqueFieldId}_{uniqueFieldIx}";
            uniqueFieldIx++;
        }

        foreach (var item in clonedDefaultObject)
        {
            switch (item.Value)
            {
                case { Type: JTokenType.Object }:
                    IterativelyChangeUniqueFieldIds((JObject)item.Value, newUniqueFieldId, ref uniqueFieldIx);
                    break;
                case { Type: JTokenType.Array }:
                {
                    var asArr = (JArray)item.Value;
                    foreach (var arrIt in asArr)
                    {
                        IterativelyChangeUniqueFieldIds((JObject)arrIt, newUniqueFieldId, ref uniqueFieldIx);
                    }

                    break;
                }
            }
        }
    }


    public override void SetDefaultValue(EntityOperationState operationState, Action<object> setValue)
    {
        //Not relevant
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
        if (currentValueJToken is { Type: JTokenType.Array })
        {
            var arrayObject = (JArray)currentValueJToken;
            if (arrayObject.Count > 0)
            {
                var (_, tableElement, _, body) = elementWrapper.CreateTableOnCard(createElement, false);
                tableElement.ClassList.Add("fields_table_element");
                if (_useAccordion == RepeatUseAccordion.Yes)
                {
                    tableElement.ClassList.Add("accordion");
                }
                tableElement.Id = $"table_{StringUtilities.GenerateRandomString(8, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase)}";

                for (var i = 0; i < arrayObject.Count; i++)
                {
                    var itemJObject = (JObject)arrayObject[i];

                    var rowElement = createElement.Invoke<IHtmlTableRowElement>();
                    body.AppendChild(rowElement);

                    var cellElement = createElement.Invoke<IHtmlTableDataCellElement>();
                    rowElement.AppendChild(cellElement);

                    var (arrayElementWrapper, arrayElementHeader, arrayElementContent) = cellElement.CreateCardOnCol(createElement);
                    arrayElementWrapper.RemoveClasses("border-left-primary").AddClasses("mx-0", "mx-xl-4");

                    var cardHeaderCol = arrayElementHeader.QuerySelector(".card-header-col");
                    cardHeaderCol.RemoveClasses("col").AddClasses("mx-2", "d-xl-flex", "justify-content-xl-start");

                    if (_useAccordion == RepeatUseAccordion.Yes)
                    {
                        var contentId = StringUtilities.GenerateRandomString(32, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase);
                        var expandButtonAreaId = StringUtilities.GenerateRandomString(32, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase);
                        arrayElementContent.Id = contentId;

                        var expandButtonArea = createElement.Invoke<IHtmlDivElement>();
                        expandButtonArea.Id = expandButtonAreaId;
                        cardHeaderCol?.AppendChild(expandButtonArea);
                        {
                            var h2 = createElement.Invoke("h2");
                            cardHeaderCol?.AppendChild(h2);
                            {
                                var expandButton = h2.CreateButtonOnElement(createElement, $"{this.Label}: {(i + 1)}", "fa-solid fa-arrows-up-down");
                                expandButton.SetAttribute("data-toggle", "collapse");
                                expandButton.SetAttribute("data-target", $"#{contentId}");
                                expandButton.SetAttribute("aria-expanded", arrayObject.Count == 1 ? "true" : "false");
                                expandButton.SetAttribute("aria-controls", contentId);
                            }
                        }

                        arrayElementContent.AddClasses("collapse");
                        if (arrayObject.Count == 1) arrayElementContent.AddClasses("show");
                        arrayElementContent.SetAttribute("aria-labelledby", expandButtonAreaId);
                        arrayElementContent.SetAttribute("data-parent", $"#{tableElement.Id}");
                    }
                    else if (cardHeaderCol != null)
                    {
                        cardHeaderCol.InnerHtml = $"{Label}: {i + 1}";
                    }

                    await EntityViewBuilder.JObjectGenerateViewFrontendHtmlAsync(
                        entityName,
                        createElement,
                        arrayElementContent,
                        _repeaterFor,
                        itemJObject,
                        depth + 1,
                        _groupRenderStyle,
                        operationState,
                        cancellationToken);
                }
                return;
            }
        }

        elementWrapper.Remove();
    }

    protected override void OverrideDefaultValue(object? value)
    {
        //Irrelevant
    }
}

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
            return OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Type is not array.", HttpStatusCode.BadRequest);
        }

        var casted = (JArray)haystack[jNeedleFieldName].NotNull();

        if (MinimumRows.HasValue && casted.Count < MinimumRows.Value)
        {
            return OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Should have at least {MinimumRows.Value} elements. (Has {casted.Count})", HttpStatusCode.BadRequest);
        }

        if (MaximumRows.HasValue && casted.Count > MaximumRows)
        {
            return OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Could have maximum {MaximumRows} elements. (Has {casted.Count})", HttpStatusCode.BadRequest);
        }

        var i = 0;
        foreach (var arrayItemToken in casted)
        {
            if (arrayItemToken.Type != JTokenType.Object)
            {
                return OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Type is array, but found an array element being non-object.", HttpStatusCode.BadRequest);
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

            window.setup_table_row_with_buttons_{{randomStringForScript}} = function(_table_element, _initial_row_index) {
            let initial_card_header_row = _table_element.rows[_initial_row_index].cells[0].querySelector('.content-and-header-col').querySelector('.card-header-row');
            let initial_delete_button = initial_card_header_row.querySelector('.row_delete_button');
            let initial_move_up_button = initial_card_header_row.querySelector('.row_move_up_button');
            let initial_move_down_button = initial_card_header_row.querySelector('.row_move_down_button');

            let removed_element = null;

            initial_delete_button.addEventListener('click', function() {
            if (_table_element) {
                let parent = this.parentElement;
                while (parent && parent.tagName !== 'TR') parent = parent.parentElement;
                if (parent) {
                    let row_index = Array.from(_table_element.rows).indexOf(parent);
                    let cell_content_and_header = _table_element.rows[row_index].cells[0].querySelector('div').querySelector('.content-and-header-col');
            {{(_useAccordion == RepeatUseAccordion.Yes ? $"""

                         let cell_expand_button_parent = cell_content_and_header.querySelector('.card-header-row').querySelector('.card-header-col').querySelector('h2');

             """ : "")}}
                    let cell_content = cell_content_and_header.querySelector('.card-content');
                    let cell_move_up_button = cell_content_and_header.querySelector('.row_move_up_button');
                    let cell_move_down_button = cell_content_and_header.querySelector('.row_move_down_button');

                    if (this.hasAttribute('data-undo')) {
                        cell_content.remove_bs_class('d-none');
                        cell_move_up_button.remove_bs_class('d-none');
                        cell_move_down_button.remove_bs_class('d-none');
            {{(_useAccordion == RepeatUseAccordion.Yes ? $"""

                             cell_expand_button_parent.remove_bs_class('d-none');

             """ : "")}}
                        this.querySelector('.icon').innerHTML = '<i class="fa-solid fa-trash"></i>';
                        this.removeAttribute('data-undo');

                        window.current_fields_state{{jsObjectPathIncludingThis}}[row_index] = removed_element;
                        removed_element = null;
                    }
                    else {
                        cell_content.add_bs_class('d-none');
                        cell_move_up_button.add_bs_class('d-none');
                        cell_move_down_button.add_bs_class('d-none');
            {{(_useAccordion == RepeatUseAccordion.Yes ? $"""

                             cell_expand_button_parent.add_bs_class('d-none');

             """ : "")}}

                        this.querySelector('.icon').innerHTML = '<i class="fa-solid fa-arrow-rotate-left"></i>';
                        this.setAttribute('data-undo', '');

                        removed_element = window.current_fields_state{{jsObjectPathIncludingThis}}[row_index];
                        window.current_fields_state{{jsObjectPathIncludingThis}}[row_index] = window.deleted;
                    }
                }
            }
            });
            const shift_row_element_divs = function(old_row_index, new_row_index) {
            let old_cell_div = _table_element.rows[old_row_index].cells[0].querySelector('div');
            let new_cell_div = _table_element.rows[new_row_index].cells[0].querySelector('div');
            window.switch_elements(old_cell_div, new_cell_div);

            tmp = window.current_fields_state{{jsObjectPathIncludingThis}}[old_row_index];
            window.current_fields_state{{jsObjectPathIncludingThis}}[old_row_index] = window.current_fields_state{{jsObjectPathIncludingThis}}[new_row_index];
            window.current_fields_state{{jsObjectPathIncludingThis}}[new_row_index] = tmp;

            let old_card_header_and_content = old_cell_div.querySelector('.content-and-header-col');
            let new_card_header_and_content = new_cell_div.querySelector('.content-and-header-col');

            let old_card_header_row = old_card_header_and_content.querySelector('.card-header-row');
            let new_card_header_row = new_card_header_and_content.querySelector('.card-header-row');

            let old_header_header_col = old_card_header_row.querySelector('.card-header-col');
            let new_header_header_col = new_card_header_row.querySelector('.card-header-col');

            {{(_useAccordion == RepeatUseAccordion.Yes ? $"""

                 window.switch_elements(old_header_header_col, new_header_header_col);

                 let old_header_expand_button_parent = old_header_header_col.querySelector('h2');
                 let new_header_expand_button_parent = new_header_header_col.querySelector('h2');
                 window.switch_dnone_status_of_elements(old_header_expand_button_parent, new_header_expand_button_parent);

                 let old_card_content = old_card_header_and_content.querySelector('.card-content');
                 let new_card_content = new_card_header_and_content.querySelector('.card-content');

                 let old_card_content_id = old_card_content.id;
                 let new_card_content_id = new_card_content.id;
                 old_card_content.id = '';
                 new_card_content.id = old_card_content_id;
                 old_card_content.id = new_card_content_id;

                 let old_card_content_labelled_by = old_card_content.getAttribute('aria-labelledby');
                 let new_card_content_labelled_by = new_card_content.getAttribute('aria-labelledby');
                 old_card_content.setAttribute('aria-labelledby', '');
                 new_card_content.setAttribute('aria-labelledby', old_card_content_labelled_by);
                 old_card_content.setAttribute('aria-labelledby', new_card_content_labelled_by);

             """ : $"""

                        window.switch_inner_html_of_elements(old_header_header_col, new_header_header_col);

                    """)}}
            old_card_header_row.scrollIntoView({ behavior: 'smooth', block: 'start', inline: 'nearest' });
            };
            initial_move_up_button.addEventListener('click', function() {
            if (_table_element) {
                let parent = this.parentElement;
                while (parent && parent.tagName !== 'TR') parent = parent.parentElement;
                if (parent) {
                    let old_row_index = Array.from(_table_element.rows).indexOf(parent);
                    if (old_row_index === 0) return;
                    shift_row_element_divs(old_row_index, old_row_index - 1);
                }
            }
            });
            initial_move_down_button.addEventListener('click', function() {
            if (_table_element) {
                let parent = this.parentElement;
                while (parent && parent.tagName !== 'TR') parent = parent.parentElement;
                if (parent) {
                    let old_row_index = Array.from(_table_element.rows).indexOf(parent);
                    if (old_row_index === (_table_element.rows.length - 1)) return;
                    shift_row_element_divs(old_row_index, old_row_index + 1);
                }
            }
            });

            {{(_useAccordion == RepeatUseAccordion.Yes ? $$"""

              $(_table_element).on('shown.bs.collapse', function (e) {
              document.getElementById($(e.target).attr('id')).parentElement.scrollIntoView({ behavior: 'smooth', block: 'start', inline: 'nearest' });
              });

              """ : "")}}

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

            addButtonElement.SetAttribute("onclick", $$"""

                                                       let deep_cloned_new_jobject = {{defaultItemJObject.ToString(Newtonsoft.Json.Formatting.None)}};

                                                       let table_element = null;
                                                       let reserve_element = null;
                                                       for (let i = 0; i < this.parentElement.childNodes.length; i++) {
                                                       let child = this.parentElement.childNodes[i];
                                                       if (child instanceof HTMLTableElement) {
                                                           table_element = child;
                                                       }
                                                       else if (typeof child.classList !== 'undefined' && child.classList.contains('fields_reserve_element')) {
                                                           reserve_element = child;
                                                       }
                                                       }

                                                       let new_element_cell = table_element.insertRow(-1).insertCell(-1);
                                                       let latest_row_index = table_element.rows.length - 1;

                                                       let new_element_card_obj = create_card_on_col(new_element_cell, '', '', true);

                                                       new_element_card_obj.wrapper.remove_bs_class('border-left-primary').add_bs_class('mx-0', 'mx-xl-4');

                                                       let new_element_cloned = reserve_element.cloneNode(true);
                                                       new_element_cloned.remove_bs_class('fields_reserve_element', 'd-none');
                                                       new_element_card_obj.content.innerHTML = new_element_cloned.innerHTML;

                                                       const card_header_col = new_element_card_obj.header_row.querySelector('.card-header-col');
                                                       card_header_col.remove_bs_class('col').add_bs_class('mx-2', 'd-xl-flex', 'justify-content-xl-start');

                                                       {{(_useAccordion == RepeatUseAccordion.Yes ? $$"""

                                                                 const expand_button_area_id = make_id(32);
                                                                 const content_id = make_id(32);

                                                                 new_element_card_obj.content.id = content_id;
                                                                 {
                                                                 const expand_button_area = card_header_col.appendChild(document.createElement('div'));
                                                                 expand_button_area.id = expand_button_area_id;
                                                                 {
                                                                     const h2 = expand_button_area.appendChild(document.createElement('h2'));
                                                                     {
                                                                         const expand_button = create_button_on_element(h2, `{{this.Label}}: ${latest_row_index + 1}`, 'fa-solid fa-arrows-up-down', 'btn-primary');
                                                                         expand_button.setAttribute('data-toggle', 'collapse');
                                                                         expand_button.setAttribute('data-target', `#${content_id}`);
                                                                         expand_button.setAttribute('aria-expanded', 'false');
                                                                         expand_button.setAttribute('aria-controls', content_id);
                                                                     }
                                                                 }
                                                                 }
                                                                 new_element_card_obj.content.add_bs_class('collapse');
                                                                 new_element_card_obj.content.setAttribute('aria-labelledby', expand_button_area_id);
                                                                 new_element_card_obj.content.setAttribute('data-parent', `#${table_element.id}`);

                                                                 """ : $$"""

                                                                         card_header_col.innerHTML = `{{this.Label}}: ${latest_row_index + 1}`;

                                                                         """)}}

                                                       window.change_object_and_element_recursively_with_proper_unique_field_ids(new_element_card_obj.content, deep_cloned_new_jobject);

                                                       const delete_move_buttons_col = create_col_fit_content_left_aligned_on_row(new_element_card_obj.header_row).add_bs_class('align-items-center');
                                                       create_button_on_element(delete_move_buttons_col, '', 'fa-solid fa-circle-chevron-up', 'btn-primary').add_bs_class('mx-1', 'row_move_up_button');
                                                       create_button_on_element(delete_move_buttons_col, '', 'fa-solid fa-circle-chevron-down', 'btn-primary').add_bs_class('mx-1', 'row_move_down_button');
                                                       create_button_on_element(delete_move_buttons_col, '', 'fa-solid fa-trash', 'btn-primary').add_bs_class('mx-1', 'row_delete_button');

                                                       window.setup_table_row_with_buttons_{{randomStringForScript}}(table_element, latest_row_index);

                                                       window.current_fields_state{{jsObjectPathIncludingThis}}[latest_row_index] = deep_cloned_new_jobject;

                                                       {{(_useAccordion == RepeatUseAccordion.Yes ? $"""

                                                            window.collapse_show(new_element_card_obj.content);

                                                            """ : "")}}


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

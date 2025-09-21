// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Reflection;
using AngleSharp.Html.Dom;
using CrossCloudKit.Utilities.Common;
using Jint;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.Operation;

internal static class EntityViewBuilder
{
    internal static async Task JObjectGenerateAdminFrontendHtmlAsync(
        string entityName,
        CreateElement createElement,
        IHtmlElement groupContainerElement,
        Type groupFor,
        JObject groupObject,
        string jsObjectPath,
        int depth,
        GroupRenderStyle renderStyle,
        EntityOperationState operationState,
        bool isForReserveParentElement,
        CancellationToken cancellationToken)
    {
        var fields = groupFor.GetFields(BindingFlags.Instance | BindingFlags.Public);

        var rowForAllElements = groupContainerElement.CreateRow(createElement);

        object? groupCasted = null;

        foreach (var field in fields)
        {
            if (!Attribute.IsDefined(field, typeof(Field), true)) continue;
            string? jFieldName;
            if (Attribute.IsDefined(field, typeof(JsonPropertyAttribute), true))
            {
                var jPropNameAttribute = field.GetCustomAttribute<JsonPropertyAttribute>(true);
                jFieldName = jPropNameAttribute?.PropertyName;
            }
            else
            {
                jFieldName = field.Name;
            }

            JToken? nullableDefaultValueJToken = null;
            if (jFieldName != null && groupObject.TryGetValue(jFieldName, out var value))
            {
                nullableDefaultValueJToken = value;
            }

            var fieldAttribute = field.GetCustomAttribute<Field>(true);
            if (fieldAttribute == null) continue;

            (IHtmlDivElement Wrapper, IHtmlDivElement HeaderRow, IHtmlDivElement Content) card;
            if (renderStyle == GroupRenderStyle.Full)
            {
                card = rowForAllElements.CreateCol1OnRow(createElement).CreateCardOnCol(createElement, "", fieldAttribute.Label);
            }
            else
            {
                card = renderStyle switch
                {
                    GroupRenderStyle.Grid2ElementsInRow => rowForAllElements.CreateCol2OnRow(createElement)
                        .CreateCardOnCol(createElement, "", fieldAttribute.Label),
                    GroupRenderStyle.Grid3ElementsInRow => rowForAllElements.CreateCol3OnRow(createElement)
                        .CreateCardOnCol(createElement, "", fieldAttribute.Label),
                    GroupRenderStyle.Grid4ElementsInRow => rowForAllElements.CreateCol4OnRow(createElement)
                        .CreateCardOnCol(createElement, "", fieldAttribute.Label),
                    _ => rowForAllElements.CreateCol6OnRow(createElement)
                        .CreateCardOnCol(createElement, "", fieldAttribute.Label)
                };

                card.Wrapper.AddClasses("h-100");
            }

            card.Wrapper.AddClasses("mt-3");
            if (depth > 0)
            {
                card.Wrapper.RemoveClasses("border-left-primary").AddClasses("mx-0", "mx-xl-4");
            }

            if (fieldAttribute.Instructions is { Length: > 0 })
            {
                var elementInstructions = createElement.Invoke<IHtmlParagraphElement>();
                card.Content.AppendChild(elementInstructions);
                elementInstructions.InnerHtml = fieldAttribute.Instructions;
            }

            if (Attribute.IsDefined(field, typeof(DisplayCondition), true))
            {
                var fieldDisplayConditionAttribute = field.GetCustomAttribute<DisplayCondition>(true);
                card.Wrapper.SetAttribute("data-display-condition", $"{jsObjectPath}.{fieldDisplayConditionAttribute?.Condition}".TrimStart('.'));
            }

            var dynamicDefaultValueFunction = groupFor.GetMethod($"{field.Name}___DynamicDefaultValueAsync");
            if (dynamicDefaultValueFunction != null)
            {
                groupCasted ??= groupObject.ToObjectWithPolymorphism(groupFor);

                var dynamicDefaultValueTask = (Task<object?>)dynamicDefaultValueFunction.Invoke(
                    groupCasted,
                    [cancellationToken]
                ).NotNull();

                var newDefaultValue = await dynamicDefaultValueTask;
                if (newDefaultValue != null)
                {
                    fieldAttribute.CalculatedDynamicDefaultValueNullable = newDefaultValue;
                }

            }

            if (fieldAttribute is Select select)
            {
                var dynamicChoicesFunctionCompileTime = groupFor.GetMethod($"{field.Name}___DynamicChoicesCompileTimeAsync");
                if (dynamicChoicesFunctionCompileTime != null)
                {
                    groupCasted ??= groupObject.ToObjectWithPolymorphism(groupFor);

                    var dynamicChoicesCTimeTask = (Task<string[]>)dynamicChoicesFunctionCompileTime.Invoke(
                        groupCasted,
                        [cancellationToken]
                    ).NotNull();

                    select.Choices = await dynamicChoicesCTimeTask;
                }

                var dynamicChoicesFunctionRuntime = groupFor.GetMethod($"{field.Name}___DynamicChoicesRuntimeAsync");
                if (dynamicChoicesFunctionRuntime != null)
                {
                    groupCasted ??= groupObject.ToObjectWithPolymorphism(groupFor);

                    var dynamicChoicesRTimeTask = (Task<string>)dynamicChoicesFunctionRuntime.Invoke(
                        groupCasted,
                        [cancellationToken]
                    ).NotNull();

                    select.RuntimeChoiceJsFunction = await dynamicChoicesRTimeTask;
                }

            }

            if (nullableDefaultValueJToken != null && jFieldName != null)
                await fieldAttribute.GenerateAdminEditHtmlElementAsync(
                    entityName,
                    createElement,
                    card.Content,
                    groupObject,
                    nullableDefaultValueJToken,
                    $"{jsObjectPath}.{jFieldName}",
                    jFieldName,
                    depth + 1,
                    operationState,
                    isForReserveParentElement,
                    cancellationToken);
        }
    }

    internal static async Task JObjectGenerateViewFrontendHtmlAsync(
        string entityName,
        CreateElement createElement,
        IHtmlElement groupContainerElement,
        Type groupFor,
        JObject groupObject,
        int depth,
        GroupRenderStyle renderStyle,
        EntityOperationState operationState,
        CancellationToken cancellationToken)
    {
        var fields = groupFor.GetFields(BindingFlags.Instance | BindingFlags.Public);

        var rowForAllElements = groupContainerElement.CreateRow(createElement);

        var displayConditions = new Dictionary<string, List<IHtmlDivElement>>();

        foreach (var field in fields)
        {
            if (Attribute.IsDefined(field, typeof(Field), true))
            {
                string? jFieldName;
                if (Attribute.IsDefined(field, typeof(JsonPropertyAttribute), true))
                {
                    var jPropNameAttribute = field.GetCustomAttribute<JsonPropertyAttribute>(true);
                    jFieldName = jPropNameAttribute?.PropertyName;
                }
                else
                {
                    jFieldName = field.Name;
                }

                var fieldAttribute = field.GetCustomAttribute<Field>(true);

                // ReSharper disable once ConvertIfStatementToSwitchStatement
                if (fieldAttribute == null) continue;

                if (fieldAttribute is Select select)
                {
                    var dynamicChoicesFunction = groupFor.GetMethod($"{field.Name}___DynamicChoicesCompileTimeAsync");
                    if (dynamicChoicesFunction != null)
                    {
                        var groupCasted = groupObject.ToObjectWithPolymorphism(groupFor);

                        var dynamicChoicesCTimeTask = (Task<string[]>)dynamicChoicesFunction.Invoke(
                            groupCasted,
                            [cancellationToken]
                        ).NotNull();

                        select.Choices = await dynamicChoicesCTimeTask;
                    }
                }


                (IHtmlDivElement Wrapper, IHtmlDivElement HeaderRow, IHtmlDivElement Content) card;
                if (renderStyle == GroupRenderStyle.Full)
                {
                    card = rowForAllElements.CreateCol1OnRow(createElement).CreateCardOnCol(createElement, "", fieldAttribute.Label);
                }
                else
                {
                    card = renderStyle switch
                    {
                        GroupRenderStyle.Grid2ElementsInRow => rowForAllElements
                            .CreateCol2OnRow(createElement)
                            .CreateCardOnCol(createElement, "", fieldAttribute.Label),
                        GroupRenderStyle.Grid3ElementsInRow => rowForAllElements
                            .CreateCol3OnRow(createElement)
                            .CreateCardOnCol(createElement, "", fieldAttribute.Label),
                        GroupRenderStyle.Grid4ElementsInRow => rowForAllElements
                            .CreateCol4OnRow(createElement)
                            .CreateCardOnCol(createElement, "", fieldAttribute.Label),
                        _ => rowForAllElements.CreateCol6OnRow(createElement)
                            .CreateCardOnCol(createElement, "", fieldAttribute.Label)
                    };

                    card.Wrapper.AddClasses("h-100");
                }

                card.Wrapper.AddClasses("mt-3");
                if (depth > 0)
                {
                    card.Wrapper.RemoveClasses("border-left-primary").AddClasses("mx-0", "mx-xl-4");
                }

                if (Attribute.IsDefined(field, typeof(DisplayCondition), true))
                {
                    var fieldDisplayConditionAttribute = field.GetCustomAttribute<DisplayCondition>(true);
                    if (fieldDisplayConditionAttribute?.Condition != null && !displayConditions.TryGetValue(fieldDisplayConditionAttribute.Condition, out var conditionListeners))
                    {
                        conditionListeners = (List<IHtmlDivElement>)[];
                        displayConditions.Add(fieldDisplayConditionAttribute.Condition, conditionListeners);
                    }

                    if (fieldDisplayConditionAttribute?.Condition != null)
                        displayConditions[fieldDisplayConditionAttribute.Condition].Add(card.Wrapper);
                }

                if (jFieldName == null) continue;
                if (groupObject.TryGetValue(jFieldName, out var value))
                {
                    await fieldAttribute.GenerateViewHtmlElementAsync(
                        entityName,
                        createElement,
                        card.Content,
                        value,
                        jFieldName,
                        depth + 1,
                        operationState,
                        cancellationToken);
                }
            }
        }

        if (displayConditions.Count <= 0) return;
        var jsEngine = new Engine().Execute($$"""

                                              var test_object = {{groupObject.ToString(Formatting.None)}};
                                              function test_condition(condition) {
                                              return eval('test_object.' + condition);
                                              }
                                              """);
        foreach (var conditionListener in from condition in displayConditions let bEvaluationResult = (bool)jsEngine.Invoke("test_condition", condition.Key).ToObject().NotNull() where !bEvaluationResult from conditionListener in condition.Value select conditionListener)
        {
            conditionListener.Remove();
        }
    }

}

// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using Newtonsoft.Json.Linq;
using AngleSharp.Html.Dom;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Operation;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.Attributes.Fields;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class Group : Field
{
    private readonly Type _groupFor;

    private readonly GroupRenderStyle _renderStyle;

    public Group(
        string label,
        string instructions,
        Type groupFor,
        GroupRenderStyle renderStyle = GroupRenderStyle.Full)
    {
        Type = FieldType.Group;

        Label = label;
        Instructions = instructions;

        _groupFor = groupFor;
        _renderStyle = renderStyle;
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
            haystack[jNeedleFieldName] = new JObject();
        }

        if (haystack[jNeedleFieldName] is not { Type: JTokenType.Object })
        {
            return OperationResult<bool>.Failure($"{Label}: Type is incorrect.", HttpStatusCode.BadRequest);
        }

        var casted = haystack[jNeedleFieldName]?.Value<JObject>();
        return await EntitySanityChecker.JObjectStaticSanityCheckAsync(entityId, _groupFor, casted, jsObjectPathIncludingThis, operationState, cancellationToken);
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
        JObject? groupObject;
        if (nullableCurrentValueJToken is { Type: JTokenType.Object })
        {
            groupObject = (JObject)nullableCurrentValueJToken;
        }
        else
        {
            var fullDefaultReflectiveField = await EntityModelDefaultsBuilder.CreateDefaultEntityFieldsObjectAsync(entityName, operationState, false, cancellationToken);
            groupObject = (JObject?)fullDefaultReflectiveField.SelectToken(EntityModelDefaultsBuilder.JsObjectPathSetAllArrayIndexesToZero(jsObjectPathIncludingThis).TrimStart('.'));
            if (groupObject != null & !isForReserveParentElement)
            {
                EntityModelDefaultsBuilder.IterativelyChangeUniqueFieldIdsWithRandomIds(groupObject.NotNull());
            }

            // SelectToken above returns null when the group's path cannot be resolved in the
            // default entity structure. This happens when the group lives inside a repeater or
            // parent whose default serialisation omits the key entirely — e.g. a repeater field
            // declared with NullValueHandling.Ignore and a null/empty default, or a repeater
            // with minimumRows: 0 producing an empty array so that [0].child_group has no match.
            // In these cases there is no default to populate, so we silently skip rendering.
            if (groupObject == null)
            {
                return;
            }

            if (!parentObjectOfCurrentValueJToken.ContainsKey(jFieldName))
                parentObjectOfCurrentValueJToken.Add(jFieldName, groupObject);
            else
                parentObjectOfCurrentValueJToken[jFieldName] = groupObject;
        }

        var element = createElement.Invoke<IHtmlDivElement>();
        elementWrapper.AppendChild(element);

        await EntityViewBuilder.JObjectGenerateAdminFrontendHtmlAsync(
            entityName,
            createElement,
            element,
            _groupFor,
            groupObject,
            jsObjectPathIncludingThis,
            depth + 1,
            _renderStyle,
            operationState,
            isForReserveParentElement,
            cancellationToken);
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
        if (currentValueJToken is not { Type: JTokenType.Object }) return;

        var groupObject = (JObject)currentValueJToken;

        var element = createElement.Invoke<IHtmlDivElement>();
        elementWrapper.AppendChild(element);

        await EntityViewBuilder.JObjectGenerateViewFrontendHtmlAsync(
            entityName,
            createElement,
            element,
            _groupFor,
            groupObject,
            depth + 1,
            _renderStyle,
            operationState,
            cancellationToken);
    }

    protected override void OverrideDefaultValue(object? value)
    {
        //Irrelevant
    }
}

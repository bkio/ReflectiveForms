// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using Newtonsoft.Json.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Operation;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.PageGenerators
{
    internal sealed class ForSingleEntityView
    {
        private const string Template = "<html><body></body></html>";
        public ForSingleEntityView(
            string entityType,
            int entityId,
            bool requesterUserSuperAdmin)
        {
            _entityType = entityType;
            _entityId = entityId;

            _requesterUserSuperAdmin = requesterUserSuperAdmin;

            var defaultObject = RfConfiguration.EntityNameToConfiguration[_entityType].DefaultJObject;
            _supportsTitle = defaultObject.ContainsKey(EntityModelAttributes.Title);
            _supportsFields = defaultObject.ContainsKey(EntityModelAttributes.Fields);

            _operationState = EntityOperationState.CreateStateForGeneralPurposes();
        }
        private IHtmlDocument? _fullDocument;
        private IHtmlElement? _containerElement;

        private readonly bool _supportsTitle;
        private readonly bool _supportsFields;

        private readonly string _entityType;
        private readonly int _entityId;

        private readonly bool _requesterUserSuperAdmin;

        private readonly EntityOperationState _operationState;

        private CreateElement? _x;

        public async Task<OperationResult<string>> GenerateAsync(CancellationToken cancellationToken)
        {
            var configuration = RfConfiguration.EntityNameToConfiguration[_entityType].EntityConfiguration;

            switch (configuration.ShallSupportFrontendEdit)
            {
                case SupportsFrontendEdit.No:
                    return OperationResult<string>.Failure($"Entity type {_entityType} is not a candidate to be viewed with an admin frontend.", HttpStatusCode.NotImplemented);
                case SupportsFrontendEdit.ForSuperAdminOnly
                    when !_requesterUserSuperAdmin:
                    return OperationResult<string>.Failure($"Forbidden", HttpStatusCode.Forbidden);
                case SupportsFrontendEdit.ForAllAuthorized:
                default:
                    break;
            }

            var dbResult = await _operationState.GetEntityInOperationAsync(
                _entityType,
                _entityId,
                cancellationToken);
            if (!dbResult.IsSuccessful)
            {
                return OperationResult<string>.Failure(dbResult.ErrorMessage, dbResult.StatusCode);
            }
            var entityObj = dbResult.Data;

            _fullDocument = await new HtmlParser().ParseDocumentAsync(Template, cancellationToken);
            _x = _fullDocument.AsCreateElement();
            _containerElement = _fullDocument.CreateElement<IHtmlDivElement>();
            _fullDocument.Body?.AppendChild(_containerElement);

            var compileEntityResult = await CompileEntityAsync(configuration.EntityFieldsModelType, entityObj, cancellationToken);
            if (!compileEntityResult.IsSuccessful)
            {
                return OperationResult<string>.Failure($"Compilation failed for {_entityType} for {EntityModelAttributes.Id} {_entityId}. Error: {compileEntityResult.ErrorMessage}.", HttpStatusCode.InternalServerError);
            }

            var error = "";
            return HtmlUtility.ConvertHtmlDocumentToHtmlString(
                _fullDocument,
                out var compiled,
                (err) => error += err + Environment.NewLine)
                ? OperationResult<string>.Success(compiled.NotNull())
                : OperationResult<string>.Failure($"ConvertHTMLDocumentToHTMLString has failed for {_entityType} for {EntityModelAttributes.Id} {_entityId}. Error: {error}", HttpStatusCode.InternalServerError);
        }

        private async Task<OperationResult<bool>> CompileEntityAsync(Type fieldsModelTypeNullable, JObject entityObject, CancellationToken cancellationToken)
        {
            string? titleNullable = null;
            if (_supportsTitle)
            {
                if (!entityObject.TryGetTypedValue(EntityModelAttributes.Title, out JObject? entityTitleObject)
                    || !entityTitleObject.NotNull().TryGetTypedValue(EntityModelAttributes.TitleRendered, out titleNullable))
                {
                    return OperationResult<bool>.Failure($"Missing field: {EntityModelAttributes.Title}->{EntityModelAttributes.TitleRendered}. For entity {_entityType}, {EntityModelAttributes.Id} {_entityId}", HttpStatusCode.BadRequest);
                }
            }

            JObject? entityFieldsObjectNullable = null;
            if (_supportsFields)
            {
                if (!entityObject.TryGetTypedValue(EntityModelAttributes.Fields, out entityFieldsObjectNullable))
                {
                    return OperationResult<bool>.Failure($"Missing field: -{EntityModelAttributes.Fields}-. For entity {_entityType}, {EntityModelAttributes.Id} {_entityId}", HttpStatusCode.BadRequest);
                }
            }

            if (_supportsTitle)
            {
                _containerElement
                    .CreateRow(_x)
                    .CreateCol1OnRow(_x)
                    .CreateCardOnCol(_x).Content
                    .InnerHtml = $"<h3>{titleNullable}</h3>";
            }

            if (_supportsFields)
            {
                await EntityViewBuilder.JObjectGenerateViewFrontendHtmlAsync(
                    _entityType,
                    _fullDocument.AsCreateElement(),
                    _containerElement.NotNull(),
                    fieldsModelTypeNullable,
                    entityFieldsObjectNullable.NotNull(),
                    0,
                    GroupRenderStyle.Full,
                    _operationState,
                    cancellationToken);
            }

            return OperationResult<bool>.Success(true);
        }
    }
}

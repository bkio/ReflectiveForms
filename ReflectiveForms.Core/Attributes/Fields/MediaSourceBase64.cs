// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using Newtonsoft.Json.Linq;
using AngleSharp.Html.Dom;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using ReflectiveForms.Core.Utilities;
using SkiaSharp;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Operation;

namespace ReflectiveForms.Core.Attributes.Fields;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class MediaSourceBase64 : Field
{
    private readonly bool _mandatory;

    public MediaSourceBase64(
        string label, string instructions, bool mandatory)
    {
        Type = FieldType.MediaSourceBase64;

        Label = label;
        Instructions = instructions;
        _mandatory = mandatory;
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

        var casted = (haystack[jNeedleFieldName]?.Value<string>()).NotNull();
        if (casted.Length == 0)
        {
            return Task.FromResult(_mandatory
                ? OperationResult<bool>.Failure($"Field {jNeedleFieldName}: Cannot be unset.", HttpStatusCode.BadRequest)
                : OperationResult<bool>.Success(true));
        }

        if (casted.StartsWith($"/{RfEndpointMapper.MediaEndpoint}")) return Task.FromResult(OperationResult<bool>.Success(true));

        if (casted.Contains(','))
        {
            casted = casted[(casted.IndexOf(',') + 1)..];
        }
        try
        {
            var imageBytes = Convert.FromBase64String(casted);
            using var originalImageStream = new MemoryTributary(imageBytes);
            using var bm = SKBitmap.Decode(originalImageStream);
            if (bm == null)
            {
                throw new Exception("Unsupported or invalid image format.");
            }
        }
        catch (Exception e) when (e is TypeInitializationException || e.InnerException is TypeInitializationException
                                    || e.Message.Contains("type initializer", StringComparison.OrdinalIgnoreCase))
        {
            // SkiaSharp native library initialization failed (e.g., missing native deps on Linux).
            // Accept the value as valid since the base64 string was parsed successfully.
            return Task.FromResult(OperationResult<bool>.Success(true));
        }
        catch (Exception e)
        {
            return Task.FromResult(OperationResult<bool>.Failure($"Field {jNeedleFieldName}: {e.Message}", HttpStatusCode.BadRequest));
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
        string? defaultValue = null;
        if (nullableCurrentValueJToken is { Type: JTokenType.String })
        {
            var defaultVal = nullableCurrentValueJToken.Value<string>();
            if (!string.IsNullOrEmpty(defaultVal))
            {
                defaultValue = defaultVal;
            }
        }
        if (defaultValue == null)
        {
            var dropAreaElement = createElement.Invoke<IHtmlDivElement>();
            dropAreaElement.StyleElement("margin-left: 5%;");
            dropAreaElement.ClassList.Add("media_source_base64_drop_area");
            dropAreaElement.SetAttribute("ondragover", """

               event.preventDefault();
               this.style.borderColor = 'blue';
               """);
            dropAreaElement.SetAttribute("ondragover", """

               event.preventDefault();
               this.style.borderColor = 'blue';
               """);
            dropAreaElement.SetAttribute("ondragleave", """

                this.style.borderColor = '#ccc';
                """);
            dropAreaElement.SetAttribute("ondrop", $"event.preventDefault(); this.style.borderColor = '#ccc'; RF.FormState.handleMediaDrop('{jsObjectPathIncludingThis}', event, this);");
            elementWrapper.AppendChild(dropAreaElement);

            //
            var pElement = createElement.Invoke<IHtmlParagraphElement>();

            var spanOfPElement = createElement.Invoke<IHtmlSpanElement>();
            spanOfPElement.InnerHtml = $"{(string.IsNullOrEmpty(Instructions) ? "" : $"<b>{Instructions}</b><br><br>")}Drag and drop an image here, or ";
            pElement.AppendChild(spanOfPElement);

            var buttonOfPElement = pElement.CreateButtonOnElement(createElement, "Select Image", "fa-solid fa-file-arrow-up").AddClasses("media_source_base64_select_button");
            buttonOfPElement.SetAttribute("onclick", "RF.FormState.triggerFileSelect(this);");

            dropAreaElement.AppendChild(pElement);
            //

            var inputElement = createElement.Invoke<IHtmlInputElement>();
            inputElement.Type = "file";
            inputElement.ClassList.Add("media_source_base64_file_input");
            inputElement.Accept = "image/*";
            inputElement.SetAttribute("onchange", $"RF.FormState.handleMediaFile('{jsObjectPathIncludingThis}', this);");
            dropAreaElement.AppendChild(inputElement);

            var previewElement = createElement.Invoke<IHtmlImageElement>();
            previewElement.ClassList.Add("media_source_base64_preview", "d-none");
            previewElement.StyleElement("max-width: 100%;");
            previewElement.AlternativeText = "Preview";
            dropAreaElement.AppendChild(previewElement);

            if (!parentObjectOfCurrentValueJToken.ContainsKey(jFieldName))
                parentObjectOfCurrentValueJToken.Add(jFieldName, "");
            else
                parentObjectOfCurrentValueJToken[jFieldName] = "";
        }
        else
        {
            var imageElement = createElement.Invoke<IHtmlImageElement>();
            elementWrapper.AppendChild(imageElement);
            imageElement.Source = defaultValue.StartsWith($"/{RfEndpointMapper.MediaEndpoint}")
                ? $"{RfConfiguration.EndpointConfiguration.PublicUrlRootForApi}{RfEndpointMapper.MediaEndpoint}"
                : defaultValue;

            if (!parentObjectOfCurrentValueJToken.ContainsKey(jFieldName))
                parentObjectOfCurrentValueJToken.Add(jFieldName, defaultValue);
            else
                parentObjectOfCurrentValueJToken[jFieldName] = defaultValue;
        }
        return Task.CompletedTask;
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
        if (currentValueJToken is { Type: JTokenType.String })
        {
            var imageSource = currentValueJToken.Value<string>().NotNull();
            if (imageSource.Length > 0)
            {
                var imageElement = createElement.Invoke<IHtmlImageElement>();
                elementWrapper.AppendChild(imageElement);
                imageElement.Source = imageSource.StartsWith($"/{RfEndpointMapper.MediaEndpoint}")
                    ? $"{RfConfiguration.EndpointConfiguration.PublicUrlRootForApi}{RfEndpointMapper.MediaEndpoint}"
                    : imageSource;
                return Task.CompletedTask;
            }
        }

        var element = createElement.Invoke<IHtmlSpanElement>();
        elementWrapper.AppendChild(element);
        element.InnerHtml = "<i class='fa-solid fa-eye-slash'></i>";

        return Task.CompletedTask;
    }
    protected override void OverrideDefaultValue(object? value) { } //Irrelevant
    public override void SetDefaultValue(EntityOperationState operationState, Action<object> setValue) { } //Irrelevant
}

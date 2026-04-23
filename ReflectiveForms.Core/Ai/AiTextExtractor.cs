// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using AngleSharp;
using AngleSharp.Html.Parser;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Schema;
using ReflectiveForms.Core.Schema.Models;

namespace ReflectiveForms.Core.Ai;

/// <summary>
/// Extracts embeddable text from a JObject entity using the entity's FieldSchema.
/// Concatenates text from Title, TextArea, WysiwygEditor, and long Text fields.
/// </summary>
internal static class AiTextExtractor
{
    private static readonly IHtmlParser HtmlParser = new HtmlParser(new HtmlParserOptions(), BrowsingContext.New(Configuration.Default));

    /// <summary>
    /// Extracts text suitable for embedding from an entity JObject.
    /// Returns null if no meaningful text is found.
    /// </summary>
    internal static string? ExtractText(string entityName, JObject entity)
    {
        var schemaResult = EntitySchemaGenerator.GenerateSchema(entityName);
        if (!schemaResult.IsSuccessful)
            return null;

        var parts = new List<string>();

        // Always include the title — repeated to boost its weight in the embedding
        var title = entity[EntityModelAttributes.Title]?[EntityModelAttributes.TitleRendered]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(title))
        {
            parts.Add(title);
            parts.Add(title);
        }

        // Walk the fields
        var fieldsObj = entity[EntityModelAttributes.Fields];
        if (fieldsObj is JObject fields)
        {
            ExtractFromFields(fields, schemaResult.Data.Fields, parts);
        }

        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    private static void ExtractFromFields(JObject data, List<FieldSchema> fieldSchemas, List<string> parts)
    {
        foreach (var fieldSchema in fieldSchemas)
        {
            var token = data[fieldSchema.Name];
            if (token == null || token.Type == JTokenType.Null)
                continue;

            switch (fieldSchema.Type)
            {
                case FieldSchemaType.TextArea:
                {
                    var text = token.Value<string>();
                    if (!string.IsNullOrWhiteSpace(text))
                        parts.Add(text);
                    break;
                }
                case FieldSchemaType.WysiwygEditor:
                {
                    var html = token.Value<string>();
                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        var plainText = StripHtml(html);
                        if (!string.IsNullOrWhiteSpace(plainText))
                            parts.Add(plainText);
                    }
                    break;
                }
                case FieldSchemaType.Text:
                {
                    // Only include text fields with max_length > 100 (short fields are noise)
                    var maxLength = fieldSchema.TextOptions?.MaxLength;
                    if (maxLength is > 100)
                    {
                        var text = token.Value<string>();
                        if (!string.IsNullOrWhiteSpace(text))
                            parts.Add(text);
                    }
                    break;
                }
                case FieldSchemaType.Group:
                {
                    if (token is JObject groupData && fieldSchema.GroupOptions?.ChildSchema != null)
                    {
                        ExtractFromFields(groupData, fieldSchema.GroupOptions.ChildSchema, parts);
                    }
                    break;
                }
                case FieldSchemaType.Repeater:
                {
                    if (token is JArray repeaterArray && fieldSchema.RepeaterOptions?.ItemSchema != null)
                    {
                        foreach (var item in repeaterArray)
                        {
                            if (item is JObject itemData)
                            {
                                ExtractFromFields(itemData, fieldSchema.RepeaterOptions.ItemSchema, parts);
                            }
                        }
                    }
                    break;
                }
            }
        }
    }

    private static string StripHtml(string html)
    {
        var document = HtmlParser.ParseDocument($"<body>{html}</body>");
        return document.Body?.TextContent?.Trim() ?? string.Empty;
    }
}

// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Operation;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Sample1.Models;

/// <summary>
/// Nested model for SEO metadata – demonstrates Group with Grid layout.
/// </summary>
[StickyTitle("meta_title")]
internal class SeoMetadataModel : BaseModel
{
    [JsonProperty("meta_title"),
     Text(
         label: "Meta Title",
         instructions: "Page title for search engines (max 60 chars recommended).",
         mandatory: false,
         placeholderText: "Enter SEO title")]
    public string MetaTitle = "";

    [JsonProperty("meta_description"),
     TextArea(
         label: "Meta Description",
         instructions: "Description shown in search results (max 160 chars recommended).",
         mandatory: false,
         placeholderText: "Enter SEO description")]
    public string MetaDescription = "";

    [JsonProperty("meta_keywords"),
     Text(
         label: "Meta Keywords",
         instructions: "Comma-separated keywords for SEO.",
         mandatory: false,
         placeholderText: "keyword1, keyword2, keyword3")]
    public string MetaKeywords = "";

    [JsonProperty("canonical_url"),
     Url(
         label: "Canonical URL",
         instructions: "Set this if this content is syndicated from another URL.",
         mandatory: false,
         placeholderText: "https://example.com/original-article")]
    public string CanonicalUrl = "";
}

/// <summary>
/// Nested model for external links within a post.
/// </summary>
[StickyTitle("link_title")]
internal class ExternalLinkModel : BaseModel
{
    [JsonProperty("link_title"),
     Text(
         label: "Link Title",
         instructions: "",
         mandatory: true,
         placeholderText: "e.g. Related Research Paper")]
    public string LinkTitle = "";

    [JsonProperty("link_url"),
     Url(
         label: "URL",
         instructions: "",
         mandatory: true,
         placeholderText: "https://example.com")]
    public string LinkUrl = "";
}

/// <summary>
/// Blog post entity – demonstrates:
/// - WysiwygEditor for rich content
/// - MediaSourceBase64 for featured image
/// - Group with Grid2ElementsInRow for SEO metadata
/// - Repeater with min/max rows for external links
/// - Select with static choices
/// - Checkbox for featured/published flags
/// - DatePicker for scheduling
/// - DisplayCondition for conditional field visibility
/// - LogicSanityCheckAsync for cross-entity validation
/// - DynamicChoicesCompileTimeAsync for year-based choices
/// - Number field with min/max
/// </summary>
internal class BlogPostModel : EntityFieldsModel
{
    [JsonProperty("content"),
     WysiwygEditor(
         label: "Post Content",
         instructions: "Write your blog post content here. Supports rich text formatting.",
         mandatory: true)]
    public string Content = "";

    [JsonProperty("excerpt"),
     TextArea(
         label: "Excerpt",
         instructions: "A short summary displayed in post listings and previews.",
         mandatory: false,
         placeholderText: "Write a brief summary of the post...")]
    public string Excerpt = "";

    [JsonProperty("featured_image"),
     MediaSourceBase64(
         label: "Featured Image",
         instructions: "Upload an image to display as the post's hero/banner image.",
         mandatory: false)]
    public string FeaturedImage = "";

    [JsonProperty("status"),
     Select(
         label: "Post Status",
         instructions: "Controls the visibility of this post.",
         defaultValue: "draft",
         choices:
         [
             "draft : Draft",
             "published : Published",
             "scheduled : Scheduled",
             "archived : Archived"
         ])]
    public string Status = "draft";

    [JsonProperty("scheduled_date"),
     DisplayCondition("status == 'scheduled'"),
     DatePicker(
         label: "Scheduled Publish Date",
         instructions: "The date this post will automatically become visible. Only applicable when status is <b>Scheduled</b>.",
         mandatory: true,
         dateFormat: "yyyyMMdd")]
    public string ScheduledDate = "";

    [JsonProperty("is_featured"),
     Checkbox(
         label: "Featured Post",
         instructions: "Featured posts are highlighted on the homepage and in listings.",
         defaultValue: false)]
    public bool IsFeatured;

    [JsonProperty("allow_comments"),
     Checkbox(
         label: "Allow Comments",
         instructions: "Enable or disable the comment section for this post.",
         defaultValue: true)]
    public bool AllowComments;

    [JsonProperty("reading_time_minutes"),
     Number(
         label: "Estimated Reading Time (minutes)",
         instructions: "Approximate time to read this post.",
         mandatory: false,
         placeholderText: "e.g. 5",
         minimumMaximumValues: [1, 120])]
    public double ReadingTimeMinutes;

    [JsonProperty("seo_metadata"),
     Group(
         label: "SEO Metadata",
         instructions: "Search engine optimization settings for this post.",
         groupFor: typeof(SeoMetadataModel),
         renderStyle: GroupRenderStyle.Grid2ElementsInRow)]
    public SeoMetadataModel SeoMetadata = new();

    [JsonProperty("external_links", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "External Links",
         instructions: "Add related links that will appear at the bottom of the post.",
         repeaterFor: typeof(ExternalLinkModel),
         addButtonLabel: "Add External Link",
         minimumRows: 0,
         maximumRows: 10,
         groupRenderStyle: GroupRenderStyle.Grid2ElementsInRow,
         useAccordion: RepeatUseAccordion.No)]
    public List<ExternalLinkModel> ExternalLinks = [];

    [JsonProperty("publication_year"),
     Select(
         label: "Publication Year",
         instructions: "Year this content was originally published.",
         defaultValue: "",
         choices: null)]
    public string PublicationYear { get; init; } = "";
    public static Task<string[]> PublicationYear___DynamicChoicesCompileTimeAsync(CancellationToken cancellationToken)
    {
        var currentYear = DateTime.Now.Year;
        var result = new List<string> { " : Select Year" };
        for (var y = currentYear + 1; y >= currentYear - 5; y--)
            result.Add($"{y} : {y}");
        return Task.FromResult(result.ToArray());
    }

    [JsonProperty("slug"),
     Text(
         label: "URL Slug",
         instructions: "The URL-friendly identifier for this post. Must be unique across all blog posts.",
         mandatory: true,
         placeholderText: "my-awesome-post")]
    public string Slug = "";
    public async Task<string?> Slug___LogicSanityCheckAsync(int entityId, EntityOperationState operationState, JObject parentJObject, CancellationToken cancellationToken)
    {
        var allEntities = await operationState.GetAllEntitiesInOperationAsync("blog-post", cancellationToken);
        if (!allEntities.IsSuccessful)
            return allEntities.ErrorMessage;

        foreach (var entity in allEntities.Data)
        {
            var casted = entity.ToObjectWithPolymorphism<EntityModel<BlogPostModel>>().NotNull();
            if (casted.Fields.Slug == Slug && casted.Id != entityId)
                return "This slug is already in use by another blog post.";
        }
        return null;
    }
}

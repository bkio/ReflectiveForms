// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ReflectiveForms.Core.Schema.Models;

/// <summary>
/// Represents the complete schema for an entity type.
/// This schema is consumed by frontend applications to dynamically render forms.
/// </summary>
public class EntitySchema
{
    [JsonProperty("entity_name")]
    public required string EntityName { get; init; }

    [JsonProperty("readable_name")]
    public required ReadableName ReadableName { get; init; }

    [JsonProperty("features")]
    public required EntityFeatures Features { get; init; }

    [JsonProperty("fields")]
    public required List<FieldSchema> Fields { get; init; }

    [JsonProperty("api_endpoints")]
    public required ApiEndpoints ApiEndpoints { get; init; }

    [JsonProperty("schema_version")]
    public string SchemaVersion { get; init; } = "1.0";
}

public class ReadableName
{
    [JsonProperty("singular")]
    public required string Singular { get; init; }

    [JsonProperty("plural")]
    public required string Plural { get; init; }
}

public class EntityFeatures
{
    [JsonProperty("has_author")]
    public bool HasAuthor { get; init; }

    [JsonProperty("has_tags")]
    public bool HasTags { get; init; }

    [JsonProperty("has_categories")]
    public bool HasCategories { get; init; }

    [JsonProperty("has_parent_child")]
    public bool HasParentChild { get; init; }

    [JsonProperty("require_title_uniqueness")]
    public bool RequireTitleUniqueness { get; init; }

    [JsonProperty("supports_frontend_edit")]
    public bool SupportsFrontendEdit { get; init; }

    [JsonProperty("has_individual_sharing")]
    public bool HasIndividualSharing { get; init; }

    [JsonProperty("custom_frontend_list_route")]
    public string? CustomFrontendListRoute { get; init; }
}

public class ApiEndpoints
{
    [JsonProperty("crud")]
    public required string Crud { get; init; }

    [JsonProperty("sanity_check")]
    public required string SanityCheck { get; init; }

    [JsonProperty("entity_lock")]
    public required string EntityLock { get; init; }

    [JsonProperty("media")]
    public required string Media { get; init; }
}

/// <summary>
/// Represents the schema for a single field in an entity.
/// </summary>
public class FieldSchema
{
    [JsonProperty("name")]
    public required string Name { get; init; }

    [JsonProperty("type")]
    [JsonConverter(typeof(StringEnumConverter))]
    public required FieldSchemaType Type { get; init; }

    [JsonProperty("label")]
    public required string Label { get; init; }

    [JsonProperty("instructions")]
    public string? Instructions { get; init; }

    [JsonProperty("required")]
    public bool Required { get; init; }

    [JsonProperty("default_value")]
    public object? DefaultValue { get; init; }

    [JsonProperty("display_condition")]
    public string? DisplayCondition { get; init; }

    // Type-specific options (only one will be populated based on Type)

    [JsonProperty("text_options")]
    public TextFieldOptions? TextOptions { get; init; }

    [JsonProperty("select_options")]
    public SelectFieldOptions? SelectOptions { get; init; }

    [JsonProperty("number_options")]
    public NumberFieldOptions? NumberOptions { get; init; }

    [JsonProperty("date_options")]
    public DateFieldOptions? DateOptions { get; init; }

    [JsonProperty("relation_options")]
    public RelationFieldOptions? RelationOptions { get; init; }

    [JsonProperty("repeater_options")]
    public RepeaterFieldOptions? RepeaterOptions { get; init; }

    [JsonProperty("group_options")]
    public GroupFieldOptions? GroupOptions { get; init; }

    [JsonProperty("media_options")]
    public MediaFieldOptions? MediaOptions { get; init; }

    [JsonProperty("has_dynamic_choices_runtime")]
    public bool HasDynamicChoicesRuntime { get; init; }

    [JsonProperty("has_dynamic_choices_compile_time")]
    public bool HasDynamicChoicesCompileTime { get; init; }

    [JsonProperty("has_logic_sanity_check")]
    public bool HasLogicSanityCheck { get; init; }
}

public enum FieldSchemaType
{
    Text,
    TextArea,
    WysiwygEditor,
    Number,
    Range,
    Email,
    Url,
    Select,
    Checkbox,
    Relation,
    DatePicker,
    Group,
    Repeater,
    MediaSourceBase64
}

#region Field-specific Options

public class TextFieldOptions
{
    [JsonProperty("placeholder")]
    public string? Placeholder { get; init; }

    [JsonProperty("max_length")]
    public int? MaxLength { get; init; }

    [JsonProperty("is_multiline")]
    public bool IsMultiline { get; init; }
}

public class SelectFieldOptions
{
    [JsonProperty("choices")]
    public List<SelectChoice>? Choices { get; init; }

    [JsonProperty("allow_multiple")]
    public bool AllowMultiple { get; init; }

    [JsonProperty("dynamic_choices_js_function")]
    public string? DynamicChoicesJsFunction { get; init; }
}

public class SelectChoice
{
    [JsonProperty("value")]
    public required string Value { get; init; }

    [JsonProperty("label")]
    public required string Label { get; init; }
}

public class NumberFieldOptions
{
    [JsonProperty("min")]
    public double? Min { get; init; }

    [JsonProperty("max")]
    public double? Max { get; init; }

    [JsonProperty("step")]
    public double? Step { get; init; }

    [JsonProperty("is_range")]
    public bool IsRange { get; init; }
}

public class DateFieldOptions
{
    [JsonProperty("format")]
    public required string Format { get; init; }

    [JsonProperty("include_time")]
    public bool IncludeTime { get; init; }
}

public class RelationFieldOptions
{
    [JsonProperty("relation_entity_name")]
    public required string RelationEntityName { get; init; }

    [JsonProperty("is_relation_entity_not_exists_ok")]
    public bool IsRelationEntityNotExistsOk { get; init; }

    [JsonProperty("allow_multiple")]
    public bool AllowMultiple { get; init; }
}

public class RepeaterFieldOptions
{
    [JsonProperty("item_schema")]
    public required List<FieldSchema> ItemSchema { get; init; }

    [JsonProperty("min_items")]
    public int? MinItems { get; init; }

    [JsonProperty("max_items")]
    public int? MaxItems { get; init; }

    [JsonProperty("add_button_label")]
    public required string AddButtonLabel { get; init; }

    [JsonProperty("use_accordion")]
    public bool UseAccordion { get; init; }

    [JsonProperty("sticky_title_field")]
    public string? StickyTitleField { get; init; }

    [JsonProperty("render_style")]
    [JsonConverter(typeof(StringEnumConverter))]
    public GroupRenderStyleSchema RenderStyle { get; init; }
}

public class GroupFieldOptions
{
    [JsonProperty("child_schema")]
    public required List<FieldSchema> ChildSchema { get; init; }

    [JsonProperty("sticky_title_field")]
    public string? StickyTitleField { get; init; }

    [JsonProperty("render_style")]
    [JsonConverter(typeof(StringEnumConverter))]
    public GroupRenderStyleSchema RenderStyle { get; init; }
}

public enum GroupRenderStyleSchema
{
    Full,
    Grid2,
    Grid3,
    Grid4,
    Grid6
}

public class MediaFieldOptions
{
    [JsonProperty("max_file_size_mb")]
    public int MaxFileSizeMb { get; init; } = 8;

    [JsonProperty("accepted_types")]
    public List<string>? AcceptedTypes { get; init; }

    [JsonProperty("preview_enabled")]
    public bool PreviewEnabled { get; init; } = true;
}

#endregion

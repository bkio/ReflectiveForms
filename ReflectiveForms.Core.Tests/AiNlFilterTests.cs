using System.Reflection;
using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using CrossCloudKit.Utilities.Common;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Repositories;
using ReflectiveForms.Core.Schema.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Tests for AI natural language filter handler (plan Section 7.52-7.53).
/// </summary>
[Collection("AI")]
public class AiNlFilterTests : IDisposable
{
    public void Dispose()
    {
        var backingField = typeof(AiConfiguration).GetField("<IsInitialized>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic);
        backingField?.SetValue(null, false);

        var rfConfigField = typeof(RfConfiguration).GetField("_configuration",
            BindingFlags.Static | BindingFlags.NonPublic);
        var rfInitField = typeof(RfConfiguration).GetField("_initialized",
            BindingFlags.Static | BindingFlags.NonPublic);
        rfConfigField?.SetValue(null, null);
        rfInitField?.SetValue(null, false);
    }

    #region 7.52 — Filter tools returned correctly

    [Fact]
    public void BuildFilterTools_ReturnsAllExpectedTools()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildFilterTools", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var tools = (LLMToolDefinition[])method!.Invoke(null, null)!;

        var toolNames = tools.Select(t => t.Name).ToList();
        toolNames.Should().Contain("filter_title_search");
        toolNames.Should().Contain("filter_equals");
        toolNames.Should().Contain("filter_not_equals");
        toolNames.Should().Contain("filter_greater_than");
        toolNames.Should().Contain("filter_less_than");
        toolNames.Should().Contain("filter_greater_or_equal");
        toolNames.Should().Contain("filter_less_or_equal");
        toolNames.Should().Contain("filter_contains");
        toolNames.Should().Contain("combine_and");
        toolNames.Should().Contain("combine_or");
        toolNames.Should().HaveCount(10);
    }

    [Fact]
    public void BuildFilterTools_EachHasParameters()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildFilterTools", BindingFlags.NonPublic | BindingFlags.Static);

        var tools = (LLMToolDefinition[])method!.Invoke(null, null)!;

        foreach (var tool in tools)
        {
            tool.Parameters.Should().NotBeNull($"tool '{tool.Name}' should have parameters");
            tool.Parameters["type"]!.Value<string>().Should().Be("object");
        }
    }

    #endregion

    #region 7.53 — Schema context building

    [Fact]
    public void BuildSchemaContext_DescribesFields()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildSchemaContext", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var schema = CreateTestSchema();
        var context = (string)method!.Invoke(null, [schema])!;

        context.Should().NotBeNullOrEmpty();
        context.Should().Contain("fields.status");
        context.Should().Contain("fields.title");
        context.Should().Contain("fields.body");
    }

    [Fact]
    public void BuildSchemaContext_IncludesSelectChoices()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildSchemaContext", BindingFlags.NonPublic | BindingFlags.Static);

        var schema = CreateSchemaWithSelect();
        var context = (string)method!.Invoke(null, [schema])!;

        context.Should().Contain("draft");
        context.Should().Contain("published");
    }

    [Fact]
    public void BuildSchemaContext_HandlesNestedGroup()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildSchemaContext", BindingFlags.NonPublic | BindingFlags.Static);

        var schema = CreateSchemaWithGroup();
        var context = (string)method!.Invoke(null, [schema])!;

        context.Should().Contain("fields.seo.meta_title");
        context.Should().Contain("fields.seo.meta_description");
    }

    #endregion

    #region Field path validation

    [Fact]
    public void IsValidFieldPath_ValidPaths()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var schema = CreateTestSchema();

        ((bool)method!.Invoke(null, ["fields.title", schema])!).Should().BeTrue();
        ((bool)method!.Invoke(null, ["fields.body", schema])!).Should().BeTrue();
        ((bool)method!.Invoke(null, ["fields.status", schema])!).Should().BeTrue();
    }

    [Fact]
    public void IsValidFieldPath_RejectsWithoutFieldsPrefix()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", BindingFlags.NonPublic | BindingFlags.Static);

        var schema = CreateTestSchema();

        ((bool)method!.Invoke(null, ["title", schema])!).Should().BeFalse();
        ((bool)method!.Invoke(null, ["id", schema])!).Should().BeFalse();
        ((bool)method!.Invoke(null, ["shared_users", schema])!).Should().BeFalse();
    }

    [Fact]
    public void IsValidFieldPath_RejectsNonexistentField()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", BindingFlags.NonPublic | BindingFlags.Static);

        var schema = CreateTestSchema();

        ((bool)method!.Invoke(null, ["fields.nonexistent", schema])!).Should().BeFalse();
    }

    [Fact]
    public void IsValidFieldPath_NestedGroupPaths()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", BindingFlags.NonPublic | BindingFlags.Static);

        var schema = CreateSchemaWithGroup();

        ((bool)method!.Invoke(null, ["fields.seo.meta_title", schema])!).Should().BeTrue();
        ((bool)method!.Invoke(null, ["fields.seo.nonexistent", schema])!).Should().BeFalse();
        ((bool)method!.Invoke(null, ["fields.nonexistent.meta_title", schema])!).Should().BeFalse();
    }

    #endregion

    #region ParseValueToPrimitive

    [Fact]
    public void ParseValueToPrimitive_Strings()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("ParseValueToPrimitive", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, ["active"]);
        result.Should().NotBeNull();
    }

    [Fact]
    public void ParseValueToPrimitive_Integers()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("ParseValueToPrimitive", BindingFlags.NonPublic | BindingFlags.Static);

        var result = method!.Invoke(null, ["42"]);
        result.Should().NotBeNull();
    }

    [Fact]
    public void ParseValueToPrimitive_Doubles()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("ParseValueToPrimitive", BindingFlags.NonPublic | BindingFlags.Static);

        var result = method!.Invoke(null, ["3.14"]);
        result.Should().NotBeNull();
    }

    [Fact]
    public void ParseValueToPrimitive_Booleans()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("ParseValueToPrimitive", BindingFlags.NonPublic | BindingFlags.Static);

        var result = method!.Invoke(null, ["true"]);
        result.Should().NotBeNull();
    }

    #endregion

    #region ProcessToolCalls — title search

    [Fact]
    public void ProcessToolCalls_TitleSearch_ReturnsTitleSearchTermAndInterpretedFilter()
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_title_search", Arguments = """{"search_text":"data optimization"}""" }
        };

        var (conditions, filters, combination, titleSearchTerms) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        conditions.Should().BeNull("title search does not produce DB conditions");
        titleSearchTerms.Should().ContainSingle().Which.Should().Be("data optimization");
        filters.Should().ContainSingle();
        filters[0].Field.Should().Be("title");
        filters[0].Operator.Should().Be("contains");
        filters[0].Value.Should().Be("data optimization");
    }

    [Fact]
    public void ProcessToolCalls_TitleSearchWithEmptyText_IsIgnored()
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_title_search", Arguments = """{"search_text":""}""" },
            new() { Id = "c2", Name = "filter_title_search", Arguments = """{"search_text":"  "}""" }
        };

        var (_, filters, _, titleSearchTerms) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        titleSearchTerms.Should().BeEmpty();
        filters.Should().BeEmpty();
    }

    [Fact]
    public void ProcessToolCalls_TitleSearchWithNullArguments_IsIgnored()
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_title_search", Arguments = """{}""" }
        };

        var (_, filters, _, titleSearchTerms) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        titleSearchTerms.Should().BeEmpty();
        filters.Should().BeEmpty();
    }

    [Fact]
    public void ProcessToolCalls_MultipleTitleSearches_CollectsAll()
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_title_search", Arguments = """{"search_text":"optimization"}""" },
            new() { Id = "c2", Name = "filter_title_search", Arguments = """{"search_text":"security"}""" }
        };

        var (_, filters, _, titleSearchTerms) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        titleSearchTerms.Should().HaveCount(2);
        titleSearchTerms.Should().Contain("optimization");
        titleSearchTerms.Should().Contain("security");
        filters.Should().HaveCount(2);
    }

    #endregion

    #region ProcessToolCalls — field filters

    [Fact]
    public void ProcessToolCalls_SingleEquals_ProducesCondition()
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_equals", Arguments = """{"field_name":"fields.status","value":"active"}""" }
        };

        var (conditions, filters, combination, titleSearchTerms) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        conditions.Should().NotBeNull();
        titleSearchTerms.Should().BeEmpty();
        filters.Should().ContainSingle();
        filters[0].Field.Should().Be("fields.status");
        filters[0].Operator.Should().Be("equals");
        filters[0].Value.Should().Be("active");
        combination.Should().Be("and");
    }

    [Fact]
    public void ProcessToolCalls_InvalidFieldPath_IsSkipped()
    {
        SetupConditionBuilder();
        EnsureRfConfigurationInitialized();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_equals", Arguments = """{"field_name":"fields.nonexistent","value":"x"}""" },
            new() { Id = "c2", Name = "filter_equals", Arguments = """{"field_name":"shared_users","value":"x"}""" }
        };

        var (conditions, filters, _, _) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        conditions.Should().BeNull("invalid field paths should be skipped");
        filters.Should().BeEmpty();
    }

    [Fact]
    public void ProcessToolCalls_MissingFieldNameOrValue_IsSkipped()
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_equals", Arguments = """{"field_name":"fields.status"}""" },
            new() { Id = "c2", Name = "filter_equals", Arguments = """{"value":"active"}""" },
            new() { Id = "c3", Name = "filter_equals", Arguments = """{}""" }
        };

        var (conditions, filters, _, _) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        conditions.Should().BeNull();
        filters.Should().BeEmpty();
    }

    [Fact]
    public void ProcessToolCalls_UnknownToolName_IsSkipped()
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_unknown", Arguments = """{"field_name":"fields.status","value":"x"}""" }
        };

        var (conditions, filters, _, _) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        conditions.Should().BeNull();
        filters.Should().BeEmpty();
    }

    [Fact]
    public void ProcessToolCalls_MultipleFiltersAutoAndedWithoutCombine()
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_equals", Arguments = """{"field_name":"fields.status","value":"active"}""" },
            new() { Id = "c2", Name = "filter_greater_than", Arguments = """{"field_name":"fields.body","value":"10"}""" }
        };

        var (conditions, filters, combination, _) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        conditions.Should().NotBeNull("multiple filters should be auto-ANDed");
        filters.Should().HaveCount(2);
        combination.Should().Be("and");
    }

    [Fact]
    public void ProcessToolCalls_CombineOr_SetsCombinationToOr()
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_equals", Arguments = """{"field_name":"fields.status","value":"draft"}""" },
            new() { Id = "c2", Name = "filter_equals", Arguments = """{"field_name":"fields.status","value":"published"}""" },
            new() { Id = "c3", Name = "combine_or", Arguments = """{"filter_ids":["f0","f1"]}""" }
        };

        var (conditions, filters, combination, _) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        conditions.Should().NotBeNull();
        combination.Should().Be("or");
        filters.Should().HaveCount(2);
    }

    [Fact]
    public void ProcessToolCalls_CombineWithInvalidFilterIds_GracefullyHandled()
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_equals", Arguments = """{"field_name":"fields.status","value":"active"}""" },
            new() { Id = "c2", Name = "combine_and", Arguments = """{"filter_ids":["f0","nonexistent"]}""" }
        };

        var (conditions, filters, _, _) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        conditions.Should().NotBeNull("valid filter IDs should still produce a condition");
        filters.Should().ContainSingle();
    }

    [Fact]
    public void ProcessToolCalls_CombineWithEmptyFilterIds_NoFinalConditionFromCombine()
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_equals", Arguments = """{"field_name":"fields.status","value":"active"}""" },
            new() { Id = "c2", Name = "combine_and", Arguments = """{"filter_ids":[]}""" }
        };

        var (conditions, filters, _, _) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        // The single filter should still be auto-ANDed even though combine had empty ids
        conditions.Should().NotBeNull();
        filters.Should().ContainSingle();
    }

    [Fact]
    public void ProcessToolCalls_NullArguments_DoesNotThrow()
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_equals", Arguments = null }
        };

        var act = () => InvokeProcessToolCalls(toolCalls, CreateTestSchema());
        act.Should().NotThrow();
    }

    #endregion

    #region ProcessToolCalls — mixed title + field filters

    [Fact]
    public void ProcessToolCalls_MixedTitleAndFieldFilter_ReturnsBoth()
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = "filter_title_search", Arguments = """{"search_text":"optimization"}""" },
            new() { Id = "c2", Name = "filter_equals", Arguments = """{"field_name":"fields.status","value":"active"}""" }
        };

        var (conditions, filters, _, titleSearchTerms) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        conditions.Should().NotBeNull("field filter should produce a condition");
        titleSearchTerms.Should().ContainSingle().Which.Should().Be("optimization");
        filters.Should().HaveCount(2);
        filters.Should().Contain(f => f.Field == "title" && f.Value == "optimization");
        filters.Should().Contain(f => f.Field == "fields.status" && f.Value == "active");
    }

    #endregion

    #region ProcessToolCalls — all operator types

    [Theory]
    [InlineData("filter_not_equals", "not_equals")]
    [InlineData("filter_greater_than", "greater_than")]
    [InlineData("filter_less_than", "less_than")]
    [InlineData("filter_greater_or_equal", "greater_or_equal")]
    [InlineData("filter_less_or_equal", "less_or_equal")]
    [InlineData("filter_contains", "contains")]
    public void ProcessToolCalls_AllOperatorTypes_ProduceConditions(string toolName, string expectedOperator)
    {
        SetupConditionBuilder();

        var toolCalls = new List<LLMToolCall>
        {
            new() { Id = "c1", Name = toolName, Arguments = """{"field_name":"fields.status","value":"test"}""" }
        };

        var (conditions, filters, _, _) = InvokeProcessToolCalls(toolCalls, CreateTestSchema());

        conditions.Should().NotBeNull();
        filters.Should().ContainSingle();
        filters[0].Operator.Should().Be(expectedOperator);
    }

    #endregion

    #region BuildSchemaContext — edge cases

    [Fact]
    public void BuildSchemaContext_EmptyFields_ReturnsEmpty()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildSchemaContext", BindingFlags.NonPublic | BindingFlags.Static)!;

        var schema = new EntitySchema
        {
            EntityName = "empty",
            ReadableName = new ReadableName { Singular = "Empty", Plural = "Empties" },
            Features = new EntityFeatures(),
            ApiEndpoints = new ApiEndpoints { Crud = "", SanityCheck = "", EntityLock = "", Media = "" },
            Fields = []
        };

        var context = (string)method.Invoke(null, [schema])!;
        context.Should().BeEmpty();
    }

    [Fact]
    public void BuildSchemaContext_NullFields_ReturnsEmpty()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildSchemaContext", BindingFlags.NonPublic | BindingFlags.Static)!;

        var schema = new EntitySchema
        {
            EntityName = "empty",
            ReadableName = new ReadableName { Singular = "Empty", Plural = "Empties" },
            Features = new EntityFeatures(),
            ApiEndpoints = new ApiEndpoints { Crud = "", SanityCheck = "", EntityLock = "", Media = "" },
            Fields = null
        };

        var context = (string)method.Invoke(null, [schema])!;
        context.Should().BeEmpty();
    }

    [Fact]
    public void BuildSchemaContext_RepeaterFields_UsesArrayNotation()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildSchemaContext", BindingFlags.NonPublic | BindingFlags.Static)!;

        var schema = CreateSchemaWithRepeater();
        var context = (string)method.Invoke(null, [schema])!;

        context.Should().Contain("fields.items[]");
        context.Should().Contain("fields.items[].item_name");
    }

    [Fact]
    public void BuildSchemaContext_NumberOptions_IncludesMinMax()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildSchemaContext", BindingFlags.NonPublic | BindingFlags.Static)!;

        var schema = CreateSchemaWithNumber();
        var context = (string)method.Invoke(null, [schema])!;

        context.Should().Contain("[min: 0]");
        context.Should().Contain("[max: 100]");
    }

    [Fact]
    public void BuildSchemaContext_IncludesFieldLabels()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildSchemaContext", BindingFlags.NonPublic | BindingFlags.Static)!;

        var schema = CreateTestSchema();
        var context = (string)method.Invoke(null, [schema])!;

        context.Should().Contain("(label: \"Status\")");
        context.Should().Contain("(label: \"Title\")");
        context.Should().Contain("(label: \"Body\")");
    }

    [Fact]
    public void BuildSchemaContext_FieldWithoutLabel_OmitsLabelTag()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildSchemaContext", BindingFlags.NonPublic | BindingFlags.Static)!;

        var schema = new EntitySchema
        {
            EntityName = "test",
            ReadableName = new ReadableName { Singular = "Test", Plural = "Tests" },
            Features = new EntityFeatures(),
            ApiEndpoints = new ApiEndpoints { Crud = "", SanityCheck = "", EntityLock = "", Media = "" },
            Fields = [new FieldSchema { Name = "raw", Type = FieldSchemaType.Text, Label = "" }]
        };

        var context = (string)method.Invoke(null, [schema])!;
        context.Should().Contain("fields.raw");
        context.Should().NotContain("(label:");
    }

    #endregion

    #region IsValidFieldPath — edge cases

    [Fact]
    public void IsValidFieldPath_EmptyString_ReturnsFalse()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", BindingFlags.NonPublic | BindingFlags.Static)!;

        var schema = CreateTestSchema();
        ((bool)method.Invoke(null, ["", schema])!).Should().BeFalse();
    }

    [Fact]
    public void IsValidFieldPath_JustFieldsPrefix_ReturnsFalse()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", BindingFlags.NonPublic | BindingFlags.Static)!;

        var schema = CreateTestSchema();
        ((bool)method.Invoke(null, ["fields.", schema])!).Should().BeFalse();
    }

    [Fact]
    public void IsValidFieldPath_RepeaterChildPath_IsValid()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", BindingFlags.NonPublic | BindingFlags.Static)!;

        var schema = CreateSchemaWithRepeater();

        ((bool)method.Invoke(null, ["fields.items.item_name", schema])!).Should().BeTrue();
    }

    [Fact]
    public void IsValidFieldPath_NestedPathOnLeafField_ReturnsFalse()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", BindingFlags.NonPublic | BindingFlags.Static)!;

        var schema = CreateTestSchema();

        // "title" is a leaf Text field — cannot have children
        ((bool)method.Invoke(null, ["fields.title.something", schema])!).Should().BeFalse();
    }

    [Fact]
    public void IsValidFieldPath_NullFields_ReturnsFalse()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", BindingFlags.NonPublic | BindingFlags.Static)!;

        var schema = new EntitySchema
        {
            EntityName = "test",
            ReadableName = new ReadableName { Singular = "Test", Plural = "Tests" },
            Features = new EntityFeatures(),
            ApiEndpoints = new ApiEndpoints { Crud = "", SanityCheck = "", EntityLock = "", Media = "" },
            Fields = null
        };

        ((bool)method.Invoke(null, ["fields.anything", schema])!).Should().BeFalse();
    }

    #endregion

    #region ParseValueToPrimitive — edge cases

    [Fact]
    public void ParseValueToPrimitive_NegativeInteger()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("ParseValueToPrimitive", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (Primitive)method.Invoke(null, ["-42"])!;
        result.AsInteger.Should().Be(-42L);
    }

    [Fact]
    public void ParseValueToPrimitive_NegativeDouble()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("ParseValueToPrimitive", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (Primitive)method.Invoke(null, ["-3.14"])!;
        result.AsDouble.Should().BeApproximately(-3.14, 0.001);
    }

    [Fact]
    public void ParseValueToPrimitive_EmptyString_ReturnsPrimitive()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("ParseValueToPrimitive", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (Primitive)method.Invoke(null, [""])!;
        result.AsString.Should().Be("");
    }

    [Fact]
    public void ParseValueToPrimitive_FalseBoolean()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("ParseValueToPrimitive", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (Primitive)method.Invoke(null, ["false"])!;
        result.Should().NotBeNull();
        // Primitive stores bool — verify it was parsed as boolean false
        result.AsBoolean.Should().BeFalse();
    }

    [Fact]
    public void ParseValueToPrimitive_LargeNumber()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("ParseValueToPrimitive", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (Primitive)method.Invoke(null, ["9999999999999"])!;
        result.AsInteger.Should().Be(9999999999999L);
    }

    [Fact]
    public void ParseValueToPrimitive_StringThatLooksLikeNumber_ButIsNot()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("ParseValueToPrimitive", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (Primitive)method.Invoke(null, ["abc123"])!;
        result.AsString.Should().Be("abc123");
    }

    #endregion

    #region BuildFilterTools — structure validation

    [Fact]
    public void BuildFilterTools_TitleSearchTool_HasSearchTextParameter()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildFilterTools", BindingFlags.NonPublic | BindingFlags.Static)!;

        var tools = (LLMToolDefinition[])method.Invoke(null, null)!;
        var titleTool = tools.First(t => t.Name == "filter_title_search");

        titleTool.Parameters["properties"]!["search_text"].Should().NotBeNull();
        titleTool.Parameters["required"]!.ToObject<string[]>().Should().Contain("search_text");
    }

    [Fact]
    public void BuildFilterTools_FieldFilterTools_HaveFieldNameAndValueParameters()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildFilterTools", BindingFlags.NonPublic | BindingFlags.Static)!;

        var tools = (LLMToolDefinition[])method.Invoke(null, null)!;
        var fieldTools = tools.Where(t => t.Name.StartsWith("filter_") && t.Name != "filter_title_search");

        foreach (var tool in fieldTools)
        {
            tool.Parameters["properties"]!["field_name"].Should().NotBeNull($"tool '{tool.Name}' should have field_name");
            tool.Parameters["properties"]!["value"].Should().NotBeNull($"tool '{tool.Name}' should have value");
        }
    }

    [Fact]
    public void BuildFilterTools_CombineTools_HaveFilterIdsArrayParameter()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildFilterTools", BindingFlags.NonPublic | BindingFlags.Static)!;

        var tools = (LLMToolDefinition[])method.Invoke(null, null)!;
        var combineTools = tools.Where(t => t.Name.StartsWith("combine_"));

        foreach (var tool in combineTools)
        {
            tool.Parameters["properties"]!["filter_ids"].Should().NotBeNull($"tool '{tool.Name}' should have filter_ids");
            tool.Parameters["properties"]!["filter_ids"]!["type"]!.Value<string>().Should().Be("array");
        }
    }

    #endregion

    #region Vector Fallback

    [Fact]
    public void NlFilterResult_UsedVectorFallback_DefaultsToFalse()
    {
        var result = new NlFilterResult([], "and", null, []);
        result.UsedVectorFallback.Should().BeFalse();
    }

    [Fact]
    public void NlFilterResult_UsedVectorFallback_CanBeSetToTrue()
    {
        var result = new NlFilterResult([], "and", "Semantic fallback", [], UsedVectorFallback: true);
        result.UsedVectorFallback.Should().BeTrue();
    }

    [Fact]
    public void VectorFallbackSearchAsync_MethodExists()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("VectorFallbackSearchAsync", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("VectorFallbackSearchAsync should exist as a private static method");
        method!.ReturnType.Should().Be(typeof(Task<List<JObject>>));
    }

    #endregion

    #region Helpers

    private static EntitySchema CreateTestSchema()
    {
        return new EntitySchema
        {
            EntityName = "test-entity",
            ReadableName = new ReadableName { Singular = "Test", Plural = "Tests" },
            Features = new EntityFeatures(),
            ApiEndpoints = new ApiEndpoints { Crud = "", SanityCheck = "", EntityLock = "", Media = "" },
            Fields =
            [
                new FieldSchema { Name = "status", Type = FieldSchemaType.Select, Label = "Status" },
                new FieldSchema { Name = "title", Type = FieldSchemaType.Text, Label = "Title" },
                new FieldSchema { Name = "body", Type = FieldSchemaType.TextArea, Label = "Body" }
            ]
        };
    }

    private static EntitySchema CreateSchemaWithSelect()
    {
        return new EntitySchema
        {
            EntityName = "test-entity",
            ReadableName = new ReadableName { Singular = "Test", Plural = "Tests" },
            Features = new EntityFeatures(),
            ApiEndpoints = new ApiEndpoints { Crud = "", SanityCheck = "", EntityLock = "", Media = "" },
            Fields =
            [
                new FieldSchema
                {
                    Name = "status",
                    Type = FieldSchemaType.Select,
                    Label = "Status",
                    SelectOptions = new SelectFieldOptions
                    {
                        Choices = [new SelectChoice { Value = "draft", Label = "Draft" }, new SelectChoice { Value = "published", Label = "Published" }]
                    }
                }
            ]
        };
    }

    private static EntitySchema CreateSchemaWithGroup()
    {
        return new EntitySchema
        {
            EntityName = "test-entity",
            ReadableName = new ReadableName { Singular = "Test", Plural = "Tests" },
            Features = new EntityFeatures(),
            ApiEndpoints = new ApiEndpoints { Crud = "", SanityCheck = "", EntityLock = "", Media = "" },
            Fields =
            [
                new FieldSchema
                {
                    Name = "seo",
                    Type = FieldSchemaType.Group,
                    Label = "SEO",
                    GroupOptions = new GroupFieldOptions
                    {
                        ChildSchema =
                        [
                            new FieldSchema { Name = "meta_title", Type = FieldSchemaType.Text, Label = "Meta Title" },
                            new FieldSchema { Name = "meta_description", Type = FieldSchemaType.TextArea, Label = "Meta Description" }
                        ]
                    }
                }
            ]
        };
    }

    private static EntitySchema CreateSchemaWithRepeater()
    {
        return new EntitySchema
        {
            EntityName = "test-entity",
            ReadableName = new ReadableName { Singular = "Test", Plural = "Tests" },
            Features = new EntityFeatures(),
            ApiEndpoints = new ApiEndpoints { Crud = "", SanityCheck = "", EntityLock = "", Media = "" },
            Fields =
            [
                new FieldSchema
                {
                    Name = "items",
                    Type = FieldSchemaType.Repeater,
                    Label = "Items",
                    RepeaterOptions = new RepeaterFieldOptions
                    {
                        ItemSchema =
                        [
                            new FieldSchema { Name = "item_name", Type = FieldSchemaType.Text, Label = "Item Name" }
                        ],
                        AddButtonLabel = "Add Item"
                    }
                }
            ]
        };
    }

    private static EntitySchema CreateSchemaWithNumber()
    {
        return new EntitySchema
        {
            EntityName = "test-entity",
            ReadableName = new ReadableName { Singular = "Test", Plural = "Tests" },
            Features = new EntityFeatures(),
            ApiEndpoints = new ApiEndpoints { Crud = "", SanityCheck = "", EntityLock = "", Media = "" },
            Fields =
            [
                new FieldSchema
                {
                    Name = "score",
                    Type = FieldSchemaType.Number,
                    Label = "Score",
                    NumberOptions = new NumberFieldOptions { Min = 0, Max = 100 }
                }
            ]
        };
    }

    private static Mock<IDatabaseService> SetupConditionBuilder()
    {
        var mockDb = new Mock<IDatabaseService>();
        mockDb.Setup(d => d.IsInitialized).Returns(true);

        // Use a concrete ValueCondition subclass as our stub — Condition itself is abstract.
        var stubCondition = new ValueCondition(ConditionType.AttributeEquals, "stub", new Primitive("stub"));

        mockDb.Setup(d => d.AttributeEquals(It.IsAny<string>(), It.IsAny<Primitive>()))
            .Returns(stubCondition);
        mockDb.Setup(d => d.AttributeNotEquals(It.IsAny<string>(), It.IsAny<Primitive>()))
            .Returns(stubCondition);
        mockDb.Setup(d => d.AttributeIsGreaterThan(It.IsAny<string>(), It.IsAny<Primitive>()))
            .Returns(stubCondition);
        mockDb.Setup(d => d.AttributeIsLessThan(It.IsAny<string>(), It.IsAny<Primitive>()))
            .Returns(stubCondition);
        mockDb.Setup(d => d.AttributeIsGreaterOrEqual(It.IsAny<string>(), It.IsAny<Primitive>()))
            .Returns(stubCondition);
        mockDb.Setup(d => d.AttributeIsLessOrEqual(It.IsAny<string>(), It.IsAny<Primitive>()))
            .Returns(stubCondition);
        mockDb.Setup(d => d.ArrayElementExists(It.IsAny<string>(), It.IsAny<Primitive>()))
            .Returns(stubCondition);

        // Set ConditionBuilder.DatabaseService via reflection (internal set)
        typeof(ConditionBuilder)
            .GetProperty("DatabaseService", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, mockDb.Object);

        return mockDb;
    }

    private static (ConditionCoupling? conditions, List<NlInterpretedFilter> filters, string combination, List<string> titleSearchTerms)
        InvokeProcessToolCalls(List<LLMToolCall> toolCalls, EntitySchema schema)
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("ProcessToolCalls", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = method.Invoke(null, [toolCalls as IReadOnlyList<LLMToolCall>, "test-entity", schema])!;

        // Deconstruct the ValueTuple
        var tupleType = result.GetType();
        var conditions = (ConditionCoupling?)tupleType.GetField("Item1")!.GetValue(result);
        var filters = (List<NlInterpretedFilter>)tupleType.GetField("Item2")!.GetValue(result)!;
        var combination = (string)tupleType.GetField("Item3")!.GetValue(result)!;
        var titleSearchTerms = (List<string>)tupleType.GetField("Item4")!.GetValue(result)!;

        return (conditions, filters, combination, titleSearchTerms);
    }

    private static void EnsureRfConfigurationInitialized()
    {
        var configField = typeof(RfConfiguration).GetField("_configuration",
            BindingFlags.Static | BindingFlags.NonPublic);
        var initializedField = typeof(RfConfiguration).GetField("_initialized",
            BindingFlags.Static | BindingFlags.NonPublic);

        if (initializedField?.GetValue(null) is true) return;

        var mockDb = new Mock<IDatabaseService>();
        mockDb.Setup(d => d.IsInitialized).Returns(true);
        var mockMem = new Mock<IMemoryService>();
        mockMem.Setup(m => m.IsInitialized).Returns(true);
        var mockPubSub = new Mock<IPubSubService>();
        mockPubSub.Setup(p => p.IsInitialized).Returns(true);
        var mockFile = new Mock<IFileService>();
        mockFile.Setup(f => f.IsInitialized).Returns(true);

        var builder = new RfConfigurationBuilder
        {
            RepositoryServiceConfiguration = new EntityRepositoryServiceConfiguration(
                mockDb.Object, mockMem.Object, mockPubSub.Object,
                new FileServiceConfiguration(mockFile.Object, "test-bucket")),
            RootUserCredentials = new RootUserCredentials("root@test.com", "password"),
            Logger = new Mock<Microsoft.Extensions.Logging.ILogger>().Object,
            EndpointConfiguration = new EndpointConfiguration
            {
                RootPath = "/rf",
                PublicUrlRootForApi = "http://localhost/rf/api/",
                PublicFrontendBaseUrl = "http://localhost:3000",
                JwtSecret = "test-secret-key-12345678901234567890"
            },
            EntityTypes = new List<EntityConfigurationBuilderBase>
            {
                new EntityConfigurationBuilder<EntityFieldsModel>
                {
                    EntityName = "test-entity",
                    EntityReadableNameSingular = "Test",
                    EntityReadableNamePlural = "Tests",
                    SupportsFrontendEdit = true,
                    HasParentChildRelationship = false,
                    HasAuthor = false,
                    HasTags = false,
                    HasCategories = false,
                    RequireGlobalTitleUniqueness = false,
                    OptionalTitleSanityCheck = null
                }
            }
        };
        configField?.SetValue(null, builder);
        initializedField?.SetValue(null, true);
    }

    #endregion
}

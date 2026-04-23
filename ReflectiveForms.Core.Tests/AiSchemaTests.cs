using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Schema.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class AiSchemaTests
{
    [Fact]
    public void EntityFeatures_AiFlags_DefaultToFalse()
    {
        var features = new EntityFeatures();

        features.SupportsSemanticSearch.Should().BeFalse();
        features.SupportsAiGeneration.Should().BeFalse();
        features.SupportsAiDiffSummary.Should().BeFalse();
        features.SupportsNaturalLanguageFilter.Should().BeFalse();
    }

    [Fact]
    public void EntityFeatures_AiFlags_CanBeSetToTrue()
    {
        var features = new EntityFeatures
        {
            SupportsSemanticSearch = true,
            SupportsAiGeneration = true,
            SupportsAiDiffSummary = true,
            SupportsNaturalLanguageFilter = true
        };

        features.SupportsSemanticSearch.Should().BeTrue();
        features.SupportsAiGeneration.Should().BeTrue();
        features.SupportsAiDiffSummary.Should().BeTrue();
        features.SupportsNaturalLanguageFilter.Should().BeTrue();
    }

    [Fact]
    public void EntityFeatures_AiFlags_SerializeToJson()
    {
        var features = new EntityFeatures
        {
            HasAuthor = true,
            SupportsSemanticSearch = true,
            SupportsAiGeneration = false,
            SupportsAiDiffSummary = true,
            SupportsNaturalLanguageFilter = false
        };

        var json = JsonConvert.SerializeObject(features);
        var parsed = JObject.Parse(json);

        parsed["supports_semantic_search"]!.Value<bool>().Should().BeTrue();
        parsed["supports_ai_generation"]!.Value<bool>().Should().BeFalse();
        parsed["supports_ai_diff_summary"]!.Value<bool>().Should().BeTrue();
        parsed["supports_natural_language_filter"]!.Value<bool>().Should().BeFalse();
        parsed["has_author"]!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public void ApiEndpoints_AiEndpoints_DefaultToNull()
    {
        var endpoints = new ApiEndpoints
        {
            Crud = "/rf/api/crud",
            SanityCheck = "/rf/api/sanity_check",
            EntityLock = "/rf/api/entity_lock_control",
            Media = "/rf/api/media"
        };

        endpoints.Ai.Should().BeNull();
        endpoints.OpenApi.Should().BeNull();
    }

    [Fact]
    public void ApiEndpoints_AiEndpoints_CanBeSet()
    {
        var endpoints = new ApiEndpoints
        {
            Crud = "/rf/api/crud",
            SanityCheck = "/rf/api/sanity_check",
            EntityLock = "/rf/api/entity_lock_control",
            Media = "/rf/api/media",
            Ai = new AiApiEndpoints
            {
                SemanticSearch = "/rf/api/ai/semantic_search",
                Generate = "/rf/api/ai/generate",
                Suggest = "/rf/api/ai/suggest",
                SanityCheck = "/rf/api/ai/sanity_check",
                DiffSummary = "/rf/api/ai/diff_summary",
                NlFilter = "/rf/api/ai/nl_filter",
                RelationSuggest = "/rf/api/ai/relation_suggest",
                Chat = "/rf/api/ai/chat"
            },
            OpenApi = "/rf/api/openapi.json"
        };

        endpoints.Ai.Should().NotBeNull();
        endpoints.Ai!.SemanticSearch.Should().Be("/rf/api/ai/semantic_search");
        endpoints.Ai.Generate.Should().Be("/rf/api/ai/generate");
        endpoints.OpenApi.Should().Be("/rf/api/openapi.json");
    }

    [Fact]
    public void AiApiEndpoints_SerializesToJson()
    {
        var aiEndpoints = new AiApiEndpoints
        {
            SemanticSearch = "/rf/api/ai/semantic_search",
            Generate = "/rf/api/ai/generate",
            Suggest = "/rf/api/ai/suggest",
            SanityCheck = "/rf/api/ai/sanity_check",
            DiffSummary = "/rf/api/ai/diff_summary",
            NlFilter = "/rf/api/ai/nl_filter",
            RelationSuggest = "/rf/api/ai/relation_suggest",
            Chat = "/rf/api/ai/chat"
        };

        var json = JsonConvert.SerializeObject(aiEndpoints);
        var parsed = JObject.Parse(json);

        parsed["semantic_search"]!.Value<string>().Should().Be("/rf/api/ai/semantic_search");
        parsed["generate"]!.Value<string>().Should().Be("/rf/api/ai/generate");
        parsed["suggest"]!.Value<string>().Should().Be("/rf/api/ai/suggest");
        parsed["sanity_check"]!.Value<string>().Should().Be("/rf/api/ai/sanity_check");
        parsed["diff_summary"]!.Value<string>().Should().Be("/rf/api/ai/diff_summary");
        parsed["nl_filter"]!.Value<string>().Should().Be("/rf/api/ai/nl_filter");
        parsed["relation_suggest"]!.Value<string>().Should().Be("/rf/api/ai/relation_suggest");
    }

    [Fact]
    public void EntitySchema_WithAiFeatures_FullRoundTrip()
    {
        var schema = new EntitySchema
        {
            EntityName = "blog-post",
            ReadableName = new ReadableName { Singular = "Blog Post", Plural = "Blog Posts" },
            Features = new EntityFeatures
            {
                HasAuthor = true,
                HasTags = true,
                HasCategories = true,
                HasParentChild = false,
                RequireTitleUniqueness = true,
                SupportsFrontendEdit = true,
                SupportsSemanticSearch = true,
                SupportsAiGeneration = true,
                SupportsAiDiffSummary = true,
                SupportsNaturalLanguageFilter = true
            },
            Fields = [],
            ApiEndpoints = new ApiEndpoints
            {
                Crud = "/rf/api/crud",
                SanityCheck = "/rf/api/sanity_check",
                EntityLock = "/rf/api/entity_lock_control",
                Media = "/rf/api/media",
                Ai = new AiApiEndpoints
                {
                    SemanticSearch = "/rf/api/ai/semantic_search",
                    Generate = "/rf/api/ai/generate",
                    Suggest = "/rf/api/ai/suggest",
                    SanityCheck = "/rf/api/ai/sanity_check",
                    DiffSummary = "/rf/api/ai/diff_summary",
                    NlFilter = "/rf/api/ai/nl_filter",
                    RelationSuggest = "/rf/api/ai/relation_suggest",
                    Chat = "/rf/api/ai/chat"
                },
                OpenApi = "/rf/api/openapi.json"
            }
        };

        var json = JsonConvert.SerializeObject(schema, Formatting.Indented);
        var parsed = JObject.Parse(json);

        // Verify AI features are in the JSON
        parsed["features"]!["supports_semantic_search"]!.Value<bool>().Should().BeTrue();
        parsed["features"]!["supports_ai_generation"]!.Value<bool>().Should().BeTrue();
        parsed["features"]!["supports_ai_diff_summary"]!.Value<bool>().Should().BeTrue();
        parsed["features"]!["supports_natural_language_filter"]!.Value<bool>().Should().BeTrue();

        // Verify AI endpoints are in the JSON
        parsed["api_endpoints"]!["ai"]!["semantic_search"]!.Value<string>().Should().Be("/rf/api/ai/semantic_search");
        parsed["api_endpoints"]!["openapi"]!.Value<string>().Should().Be("/rf/api/openapi.json");
    }

    [Fact]
    public void EntitySchema_WithoutAi_NullAiEndpoints()
    {
        var schema = new EntitySchema
        {
            EntityName = "simple-entity",
            ReadableName = new ReadableName { Singular = "Simple", Plural = "Simples" },
            Features = new EntityFeatures
            {
                HasAuthor = false,
                SupportsFrontendEdit = true
            },
            Fields = [],
            ApiEndpoints = new ApiEndpoints
            {
                Crud = "/rf/api/crud",
                SanityCheck = "/rf/api/sanity_check",
                EntityLock = "/rf/api/entity_lock_control",
                Media = "/rf/api/media"
            }
        };

        var json = JsonConvert.SerializeObject(schema, Formatting.Indented);
        var parsed = JObject.Parse(json);

        // AI features should be false
        parsed["features"]!["supports_semantic_search"]!.Value<bool>().Should().BeFalse();
        // AI endpoints should be null (not in JSON)
        parsed["api_endpoints"]!["ai"]!.Type.Should().Be(JTokenType.Null);
        parsed["api_endpoints"]!["openapi"]!.Type.Should().Be(JTokenType.Null);
    }
}

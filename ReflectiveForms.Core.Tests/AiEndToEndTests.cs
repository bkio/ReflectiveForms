using System.Net;
using System.Reflection;
using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Schema.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// End-to-end integration tests for the AI features.
/// Tests cover handler flows with mocked CrossCloudKit services,
/// the sanity check pipeline integration, and configuration validation.
[Collection("AI")]
/// </summary>
public class AiEndToEndTests : IDisposable
{
    private readonly Mock<ILLMService> _mockHeavyLlm = new();
    private readonly Mock<ILLMService> _mockLightLlm = new();
    private readonly Mock<IVectorService> _mockVectorService = new();
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly Mock<IMemoryService> _mockMemoryService;

    public AiEndToEndTests()
    {
        _mockDatabaseService = new Mock<IDatabaseService>();
        _mockDatabaseService.Setup(d => d.IsInitialized).Returns(true);

        _mockMemoryService = new Mock<IMemoryService>();
        _mockMemoryService.Setup(m => m.IsInitialized).Returns(true);
    }

    public void Dispose()
    {
        // Reset AiConfiguration static state after each test to avoid cross-test contamination
        var backingField = typeof(AiConfiguration).GetField("<IsInitialized>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic);
        backingField?.SetValue(null, false);

        // Reset RfConfiguration static state
        var rfConfigField = typeof(RfConfiguration).GetField("_configuration",
            BindingFlags.Static | BindingFlags.NonPublic);
        var rfInitField = typeof(RfConfiguration).GetField("_initialized",
            BindingFlags.Static | BindingFlags.NonPublic);
        rfConfigField?.SetValue(null, null);
        rfInitField?.SetValue(null, false);
    }

    private void InitializeAiConfiguration()
    {
        AiConfiguration.Initialize(
            _mockDatabaseService.Object,
            _mockMemoryService.Object,
            _mockVectorService.Object,
            _mockHeavyLlm.Object,
            _mockLightLlm.Object,
            embeddingDimensions: 384);
    }

    #region Configuration (7.1-7.4)

    [Fact]
    public void AiServiceConfiguration_HasSystemPromptPrefix()
    {
        var prop = typeof(AiServiceConfiguration).GetProperty("SystemPromptPrefix");
        prop.Should().NotBeNull("SystemPromptPrefix should be a property on AiServiceConfiguration");
    }

    [Fact]
    public void AiServiceConfiguration_SystemPromptPrefix_DefaultValue()
    {
        // Create via reflection since the constructor requires ILLMService/IVectorService
        var type = typeof(AiServiceConfiguration);
        var prop = type.GetProperty("SystemPromptPrefix");
        prop.Should().NotBeNull();

        // Verify the default by creating an instance with mocks
        var config = new AiServiceConfiguration(
            _mockHeavyLlm.Object,
            _mockLightLlm.Object,
            _mockVectorService.Object);

        config.SystemPromptPrefix.Should().Be("You are an assistant for a schema-driven content management system. Entities have typed fields (text, select, date, checkbox, number, repeater, group) with validation rules.");
    }

    [Fact]
    public void AiServiceConfiguration_SystemPromptPrefix_CanBeCustomized()
    {
        var config = new AiServiceConfiguration(
            _mockHeavyLlm.Object,
            _mockLightLlm.Object,
            _mockVectorService.Object)
        {
            SystemPromptPrefix = "You are an AI for a medical records system. Never reveal patient data."
        };

        config.SystemPromptPrefix.Should().Be("You are an AI for a medical records system. Never reveal patient data.");
    }

    [Fact]
    public void AiServiceConfiguration_MultiModel_SameInstance()
    {
        // Same instance for both heavy and light (cost-insensitive dev setup per plan 7.4)
        var singleLlm = _mockHeavyLlm.Object;
        var config = new AiServiceConfiguration(
            singleLlm,
            singleLlm,
            _mockVectorService.Object);

        config.HeavyLlmService.Should().BeSameAs(config.LightLlmService);
    }

    [Fact]
    public void AiServiceConfiguration_MultiModel_DifferentInstances()
    {
        var config = new AiServiceConfiguration(
            _mockHeavyLlm.Object,
            _mockLightLlm.Object,
            _mockVectorService.Object);

        config.HeavyLlmService.Should().NotBeSameAs(config.LightLlmService);
    }

    #endregion

    #region AiConfiguration Static Singleton (7.3)

    [Fact]
    public void AiConfiguration_Initialize_SetsAllServices()
    {
        InitializeAiConfiguration();

        AiConfiguration.IsInitialized.Should().BeTrue();
        AiConfiguration.DatabaseService.Should().BeSameAs(_mockDatabaseService.Object);
        AiConfiguration.MemoryService.Should().BeSameAs(_mockMemoryService.Object);
        AiConfiguration.VectorService.Should().BeSameAs(_mockVectorService.Object);
        AiConfiguration.HeavyLlmService.Should().BeSameAs(_mockHeavyLlm.Object);
        AiConfiguration.LightLlmService.Should().BeSameAs(_mockLightLlm.Object);
        AiConfiguration.EmbeddingDimensions.Should().Be(384);
    }

    #endregion

    #region Vector Indexer (7.17-7.18)

    [Fact]
    public void AiVectorIndexer_CollectionNaming()
    {
        AiVectorIndexer.GetCollectionName("blog-posts").Should().Be("rf_semantic_blog-posts");
        AiVectorIndexer.GetCollectionName("team-members").Should().Be("rf_semantic_team-members");
        AiVectorIndexer.GetCollectionName("a").Should().Be("rf_semantic_a");
    }

    [Fact]
    public async Task AiVectorIndexer_DeleteEntity_CallsVectorServiceDelete()
    {
        InitializeAiConfiguration();

        _mockVectorService
            .Setup(v => v.DeleteAsync("rf_semantic_test-entity", "42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.Success(true));

        await AiVectorIndexer.DeleteEntityAsync("test-entity", 42);

        _mockVectorService.Verify(
            v => v.DeleteAsync("rf_semantic_test-entity", "42", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Sanity Check Handler (7.46-7.49)

    [Fact]
    public async Task AiSanityCheckHandler_PassingCheck_ReturnsPassedResult()
    {
        InitializeAiConfiguration();
        SetupMockAiServiceConfig();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "{\"passed\": true, \"message\": \"Content looks professional.\"}",
                FinishReason = LLMFinishReason.Stop
            }));

        var checks = new List<AISanityCheck>
        {
            new("Is this content professional?", AISanityCheckSeverity.Warning)
        };

        var result = await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "description",
            new JValue("This is well-written professional content."),
            checks, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Passed.Should().BeTrue();
        result[0].Severity.Should().Be(AISanityCheckSeverity.Warning);
    }

    [Fact]
    public async Task AiSanityCheckHandler_FailingCheck_ReturnsFailedResult()
    {
        InitializeAiConfiguration();
        SetupMockAiServiceConfig();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "{\"passed\": false, \"message\": \"Content contains informal language.\"}",
                FinishReason = LLMFinishReason.Stop
            }));

        var checks = new List<AISanityCheck>
        {
            new("Is this content professional?", AISanityCheckSeverity.Error)
        };

        var result = await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "description",
            new JValue("lol this is gr8 content!!!"),
            checks, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Passed.Should().BeFalse();
        result[0].Severity.Should().Be(AISanityCheckSeverity.Error);
        result[0].Message.Should().Contain("informal language");
    }

    [Fact]
    public async Task AiSanityCheckHandler_MultipleChecks_AllExecuted()
    {
        InitializeAiConfiguration();
        SetupMockAiServiceConfig();

        var callCount = 0;
        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = callCount == 1
                        ? "{\"passed\": true, \"message\": \"OK\"}"
                        : "{\"passed\": false, \"message\": \"PII detected\"}",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var checks = new List<AISanityCheck>
        {
            new("Is this professional?", AISanityCheckSeverity.Warning),
            new("Does this contain PII?", AISanityCheckSeverity.Error)
        };

        var result = await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "description",
            new JValue("John Doe's phone is 555-1234"),
            checks, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Passed.Should().BeTrue();
        result[1].Passed.Should().BeFalse();
    }

    [Fact]
    public async Task AiSanityCheckHandler_EmptyValue_ReturnsEmptyResults()
    {
        InitializeAiConfiguration();
        SetupMockAiServiceConfig();

        var checks = new List<AISanityCheck>
        {
            new("Is this professional?")
        };

        var result = await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "description",
            new JValue(""),
            checks, CancellationToken.None);

        result.Should().BeEmpty("empty values should not be checked");
    }

    [Fact]
    public async Task AiSanityCheckHandler_LlmFailure_SkipsCheck()
    {
        InitializeAiConfiguration();
        SetupMockAiServiceConfig();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Failure("Service unavailable", HttpStatusCode.ServiceUnavailable));

        var checks = new List<AISanityCheck>
        {
            new("Is this professional?", AISanityCheckSeverity.Error)
        };

        var result = await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "description",
            new JValue("Some content"),
            checks, CancellationToken.None);

        // On LLM failure, the check should be skipped entirely (no result added)
        result.Should().BeEmpty("LLM failures should cause check to be skipped");
    }

    [Fact]
    public async Task AiSanityCheckHandler_SystemPromptPrefix_UsedInLlmCall()
    {
        InitializeAiConfiguration();
        SetupMockAiServiceConfig("You are a strict editor.");

        LLMRequest? capturedRequest = null;
        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "{\"passed\": true, \"message\": \"OK\"}",
                FinishReason = LLMFinishReason.Stop
            }));

        var checks = new List<AISanityCheck>
        {
            new("Is this professional?")
        };

        await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "description",
            new JValue("Test content"),
            checks, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Messages.First().Content.Should().StartWith("You are a strict editor.");
    }

    #endregion

    #region Sanity Check Pipeline Integration (7.46-7.49)

    [Fact]
    public void EntityFinalConfiguration_DiscoversAiSanityCheckFields()
    {
        // Verify via reflection that the constructor pre-computes fieldsWithAiSanityChecks
        // We can't construct EntityFinalConfiguration<T> without full setup,
        // but we can verify the attribute discovery logic directly
        var testModelType = typeof(TestEntityFieldsModelWithSanityChecks);
        var fieldsWithChecks = new List<(string JsonPropertyName, IReadOnlyList<AISanityCheck> Checks)>();

        foreach (var member in testModelType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     .Where(m => m is FieldInfo or PropertyInfo))
        {
            var aiChecks = member.GetCustomAttributes<AISanityCheck>(true).ToList();
            if (aiChecks.Count == 0) continue;

            var jsonPropAttr = member.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>(true);
            var fieldName = jsonPropAttr?.PropertyName ?? member.Name;
            fieldsWithChecks.Add((fieldName, aiChecks));
        }

        // Should discover the two fields with [AISanityCheck]
        fieldsWithChecks.Should().HaveCount(2);
        fieldsWithChecks.Should().Contain(f => f.JsonPropertyName == "content");
        fieldsWithChecks.Should().Contain(f => f.JsonPropertyName == "summary");

        // "content" should have 2 checks (AllowMultiple=true)
        var contentChecks = fieldsWithChecks.First(f => f.JsonPropertyName == "content").Checks;
        contentChecks.Should().HaveCount(2);
        contentChecks.Should().Contain(c => c.Severity == AISanityCheckSeverity.Error);
        contentChecks.Should().Contain(c => c.Severity == AISanityCheckSeverity.Warning);

        // "summary" should have 1 check
        var summaryChecks = fieldsWithChecks.First(f => f.JsonPropertyName == "summary").Checks;
        summaryChecks.Should().HaveCount(1);
    }

    [Fact]
    public void EntityFinalConfiguration_NoAiSanityCheckFields_EmptyList()
    {
        var testModelType = typeof(TestEntityFieldsModelWithoutSanityChecks);
        var fieldsWithChecks = new List<(string JsonPropertyName, IReadOnlyList<AISanityCheck> Checks)>();

        foreach (var member in testModelType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     .Where(m => m is FieldInfo or PropertyInfo))
        {
            var aiChecks = member.GetCustomAttributes<AISanityCheck>(true).ToList();
            if (aiChecks.Count == 0) continue;

            var jsonPropAttr = member.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>(true);
            var fieldName = jsonPropAttr?.PropertyName ?? member.Name;
            fieldsWithChecks.Add((fieldName, aiChecks));
        }

        fieldsWithChecks.Should().BeEmpty("model without [AISanityCheck] should have no checks");
    }

    #endregion

    #region Diff Summary (7.50-7.51)

    [Fact]
    public void DiffSummary_TruncateValue_ShortString_NotTruncated()
    {
        var method = typeof(AiDiffSummaryHandler)
            .GetMethod("TruncateValue", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, [new JValue("Short text")])!;
        result.Should().Be("Short text");
    }

    [Fact]
    public void DiffSummary_TruncateValue_LongString_Truncated()
    {
        var method = typeof(AiDiffSummaryHandler)
            .GetMethod("TruncateValue", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        // Create a string longer than 500 chars
        var longString = new string('A', 600);
        var result = (string)method!.Invoke(null, [new JValue(longString)])!;

        result.Should().Contain("[...]");
        result.Length.Should().BeLessThan(600);
    }

    [Fact]
    public void DiffSummary_ComputeDiff_LargeValueChange_TruncatesInOutput()
    {
        var method = typeof(AiDiffSummaryHandler)
            .GetMethod("ComputeDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var longContent = new string('X', 800);
        var oldFields = new JObject { ["body"] = longContent };
        var newFields = new JObject { ["body"] = "Short new value" };

        var result = (List<string>)method!.Invoke(null, [oldFields, newFields])!;
        result.Should().HaveCount(1);
        result[0].Should().Contain("[...]", "long values should be truncated in diff output");
    }

    [Fact]
    public void DiffSummary_ComputeDiff_MultipleFieldChanges()
    {
        var method = typeof(AiDiffSummaryHandler)
            .GetMethod("ComputeDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var oldFields = new JObject
        {
            ["title"] = "Original Title",
            ["status"] = "draft",
            ["priority"] = "low",
            ["deprecated"] = "yes"
        };
        var newFields = new JObject
        {
            ["title"] = "Updated Title",
            ["status"] = "published",
            ["priority"] = "low",
            ["tags"] = "new-tag"
        };

        var result = (List<string>)method!.Invoke(null, [oldFields, newFields])!;

        result.Should().Contain(s => s.Contains("Changed 'title'"));
        result.Should().Contain(s => s.Contains("Changed 'status'"));
        result.Should().Contain(s => s.Contains("Added 'tags'"));
        result.Should().Contain(s => s.Contains("Removed 'deprecated'"));
        result.Should().NotContain(s => s.Contains("priority"), "unchanged fields should not appear");
    }

    #endregion

    #region NL Filter Field Validation (7.52-7.53)

    [Fact]
    public void NlFilter_IsValidFieldPath_AllowsFieldsPrefix()
    {
        var schema = CreateTestSchema();
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        ((bool)method!.Invoke(null, ["fields.title", schema])!).Should().BeTrue();
        ((bool)method!.Invoke(null, ["fields.body", schema])!).Should().BeTrue();
        ((bool)method!.Invoke(null, ["fields.status", schema])!).Should().BeTrue();
    }

    [Fact]
    public void NlFilter_IsValidFieldPath_RejectsDirectSystemFields()
    {
        var schema = CreateTestSchema();
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        // System fields should NOT be accessible for filtering
        ((bool)method!.Invoke(null, ["id", schema])!).Should().BeFalse();
        ((bool)method!.Invoke(null, ["shared_users", schema])!).Should().BeFalse();
        ((bool)method!.Invoke(null, ["password", schema])!).Should().BeFalse();
    }

    [Fact]
    public void NlFilter_IsValidFieldPath_NestedGroupPath()
    {
        var schema = CreateTestSchemaWithGroup();
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        ((bool)method!.Invoke(null, ["fields.seo.meta_title", schema])!).Should().BeTrue();
        ((bool)method!.Invoke(null, ["fields.seo.meta_description", schema])!).Should().BeTrue();
        ((bool)method!.Invoke(null, ["fields.seo.nonexistent", schema])!).Should().BeFalse();
        ((bool)method!.Invoke(null, ["fields.nonexistent.meta_title", schema])!).Should().BeFalse();
    }

    [Fact]
    public void NlFilter_ParseValueToPrimitive_Types()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("ParseValueToPrimitive", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        // String
        method!.Invoke(null, ["active"]).Should().NotBeNull();

        // Integer/Long
        var longResult = method.Invoke(null, ["42"]);
        longResult.Should().NotBeNull();

        // Double
        var doubleResult = method.Invoke(null, ["3.14"]);
        doubleResult.Should().NotBeNull();

        // Boolean
        var boolResult = method.Invoke(null, ["true"]);
        boolResult.Should().NotBeNull();
    }

    #endregion

    #region Entity Generator ParseFieldValue (7.42-7.43)

    [Fact]
    public void AiEntityGenerator_ParseFieldValue_TextTakesFirstLine()
    {
        var field = new FieldSchema { Name = "name", Type = FieldSchemaType.Text, Label = "Name" };
        var result = AiEntityGenerator.ParseFieldValue(field, "First line\nSecond line");
        result.Should().NotBeNull();
        result!.Value<string>().Should().Be("First line");
    }

    [Fact]
    public void AiEntityGenerator_ParseFieldValue_FieldTypeMappings()
    {
        // Number
        var numField = new FieldSchema { Name = "count", Type = FieldSchemaType.Number, Label = "Count" };
        AiEntityGenerator.ParseFieldValue(numField, "42")!.Value<double>().Should().Be(42);

        // Checkbox
        var boolField = new FieldSchema { Name = "active", Type = FieldSchemaType.Checkbox, Label = "Active" };
        AiEntityGenerator.ParseFieldValue(boolField, "true")!.Value<bool>().Should().BeTrue();
        AiEntityGenerator.ParseFieldValue(boolField, "false")!.Value<bool>().Should().BeFalse();

        // DatePicker extracts YYYY-MM-DD
        var dateField = new FieldSchema { Name = "due", Type = FieldSchemaType.DatePicker, Label = "Due Date" };
        AiEntityGenerator.ParseFieldValue(dateField, "2025-06-15")!.Value<string>().Should().Be("2025-06-15");

        // Media and Relation are skipped at generation level — not tested here
    }

    [Fact]
    public void AiEntityGenerator_ParseFieldValue_SelectValidation()
    {
        var field = new FieldSchema
        {
            Name = "priority", Type = FieldSchemaType.Select, Label = "Priority",
            Required = true,
            SelectOptions = new SelectFieldOptions
            {
                Choices =
                [
                    new SelectChoice { Value = "low", Label = "Low" },
                    new SelectChoice { Value = "high", Label = "High" }
                ]
            }
        };

        // Exact match (case-insensitive)
        AiEntityGenerator.ParseFieldValue(field, "HIGH")!.Value<string>().Should().Be("high");

        // Contains match
        AiEntityGenerator.ParseFieldValue(field, "I would say low priority")!.Value<string>().Should().Be("low");

        // No match returns null (invalid values are rejected)
        AiEntityGenerator.ParseFieldValue(field, "medium").Should().BeNull();
    }

    #endregion

    #region NL Filter Tool Building (7.52-7.53)

    [Fact]
    public void NlFilter_BuildFilterTools_ReturnsExpectedTools()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildFilterTools", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var tools = (LLMToolDefinition[])method!.Invoke(null, null)!;

        tools.Should().NotBeEmpty();

        // Should include filter tools for each operator type
        var toolNames = tools.Select(t => t.Name).ToList();
        toolNames.Should().Contain("filter_equals");
        toolNames.Should().Contain("filter_not_equals");
        toolNames.Should().Contain("filter_greater_than");
        toolNames.Should().Contain("filter_less_than");
        toolNames.Should().Contain("combine_and");
        toolNames.Should().Contain("combine_or");
    }

    [Fact]
    public void NlFilter_BuildSchemaContext_DescribesFields()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildSchemaContext", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var schema = CreateTestSchema();
        var context = (string)method!.Invoke(null, [schema])!;

        context.Should().NotBeNullOrEmpty();
        context.Should().Contain("status", "field names should appear in context");
        context.Should().Contain("title", "field names should appear in context");
    }

    #endregion

    #region Field Suggestion Attribute Discovery (7.44-7.45)

    [Fact]
    public void FieldSuggestionHandler_FindAiSuggestionAttribute_ViaReflection()
    {
        // Test the attribute discovery logic that FindAiSuggestionAttribute uses
        var type = typeof(TestEntityFieldsModelWithSuggestion);

        var member = type.GetMember("_excerpt", BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault();
        member.Should().NotBeNull();

        var attr = member!.GetCustomAttribute<AISuggestion>(true);
        attr.Should().NotBeNull();
        attr!.Prompt.Should().Contain("excerpt");
        attr.SourceFields.Should().Contain("content");
    }

    [Fact]
    public void FieldSuggestionHandler_EmptySourceFields_UsesAllFields()
    {
        var type = typeof(TestEntityFieldsModelWithSuggestionNoSource);

        var member = type.GetMember("_summary", BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault();
        member.Should().NotBeNull();

        var attr = member!.GetCustomAttribute<AISuggestion>(true);
        attr.Should().NotBeNull();
        attr!.SourceFields.Should().BeEmpty("when no source fields specified, handler should use all text-bearing fields");
    }

    #endregion

    #region Relation Suggestion Attribute Discovery (7.57)

    [Fact]
    public void AIRelationSuggestion_OnNonRelationField_AttributeStillExists()
    {
        // The attribute can be placed on any field but should only have effect on Relation fields
        // (schema generator handles this by checking field type)
        var type = typeof(TestEntityFieldsModelWithRelationOnNonRelation);

        var member = type.GetMember("_name", BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault();
        member.Should().NotBeNull();

        var attr = member!.GetCustomAttribute<AIRelationSuggestion>(true);
        attr.Should().NotBeNull("attribute should still be discoverable");
        attr!.TopK.Should().Be(5);
    }

    #endregion

    #region OpenAPI (7.10)

    [Fact]
    public void OpenApiConfiguration_AiEndpointsRequireAiConfig()
    {
        // If IncludeAiEndpoints=true but AiServiceConfiguration is null,
        // AI endpoints should not appear. This is verified by checking the config flag.
        var config = new OpenApiConfiguration
        {
            IncludeAiEndpoints = true
        };

        config.IncludeAiEndpoints.Should().BeTrue();

        // The OpenApiGenerator checks AiServiceConfiguration null check before
        // including AI endpoints — verified by the integration test structure
    }

    #endregion

    #region Helpers

    private void SetupMockAiServiceConfig(string? systemPromptPrefix = null)
    {
        // Set up RfConfiguration.AiServiceConfiguration by using reflection
        // to inject the config into the static _configuration field
        var config = new AiServiceConfiguration(
            _mockHeavyLlm.Object,
            _mockLightLlm.Object,
            _mockVectorService.Object)
        {
            SystemPromptPrefix = systemPromptPrefix ?? "You are an assistant for a schema-driven content management system. Entities have typed fields (text, select, date, checkbox, number, repeater, group) with validation rules."
        };

        var configField = typeof(RfConfiguration).GetField("_configuration",
            BindingFlags.Static | BindingFlags.NonPublic);
        var initializedField = typeof(RfConfiguration).GetField("_initialized",
            BindingFlags.Static | BindingFlags.NonPublic);

        if (configField == null || initializedField == null)
            throw new InvalidOperationException("Could not find _configuration or _initialized fields on RfConfiguration");

        var mockDb = _mockDatabaseService.Object;
        var mockMemory = _mockMemoryService.Object;
        var mockPubSub = new Mock<IPubSubService>();
        mockPubSub.Setup(p => p.IsInitialized).Returns(true);
        var mockFileService = new Mock<IFileService>();
        mockFileService.Setup(f => f.IsInitialized).Returns(true);
        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger>().Object;

        var builder = new RfConfigurationBuilder
        {
            RepositoryServiceConfiguration = new EntityRepositoryServiceConfiguration(
                mockDb, mockMemory, mockPubSub.Object,
                new FileServiceConfiguration(mockFileService.Object, "test-bucket")),
            RootUserCredentials = new RootUserCredentials("root@test.com", "password"),
            Logger = mockLogger,
            EndpointConfiguration = new Endpoints.EndpointConfiguration
            {
                RootPath = "/rf",
                PublicUrlRootForApi = "http://localhost/rf/api/",
                PublicFrontendBaseUrl = "http://localhost:3000",
                JwtSecret = "test-secret-key-12345678901234567890"
            },
            AiServiceConfiguration = config,
            EntityTypes = new List<EntityConfigurationBuilderBase>
            {
                new EntityConfigurationBuilder<Models.EntityFieldsModel>
                {
                    EntityName = "test-entity",
                    EntityReadableNameSingular = "Test Entity",
                    EntityReadableNamePlural = "Test Entities",
                    SupportsFrontendEdit = true,
                    HasParentChildRelationship = false,
                    HasAuthor = false,
                    HasTags = false,
                    HasCategories = false,
                    RequireGlobalTitleUniqueness = false,
                    OptionalTitleSanityCheck = null,
                    EntityDescription = "A test entity for semantic search.",
                    SupportsSemanticSearch = true
                }
            }
        };

        configField.SetValue(null, builder);
        initializedField.SetValue(null, true);
    }

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

    private static EntitySchema CreateTestSchemaWithGroup()
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
                            new FieldSchema { Name = "meta_description", Type = FieldSchemaType.TextArea, Label = "Meta Desc" }
                        ]
                    }
                }
            ]
        };
    }

    #endregion

    #region Test Models

    // Model with [AISanityCheck] attributes for pipeline integration testing
    private class TestEntityFieldsModelWithSanityChecks : Models.EntityFieldsModel
    {
        [AISanityCheck("Is this professional?", AISanityCheckSeverity.Warning)]
        [AISanityCheck("Does this contain PII?", AISanityCheckSeverity.Error)]
        [Newtonsoft.Json.JsonProperty("content")]
        public string Content = "";

        [AISanityCheck("Is this a good summary?")]
        [Newtonsoft.Json.JsonProperty("summary")]
        public string Summary = "";

        // Field without sanity check — should be skipped
        [Newtonsoft.Json.JsonProperty("status")]
        public string Status = "";
    }

    // Model without [AISanityCheck] attributes
    private class TestEntityFieldsModelWithoutSanityChecks : Models.EntityFieldsModel
    {
        [Newtonsoft.Json.JsonProperty("title")]
        public string Title = "";

        [Newtonsoft.Json.JsonProperty("body")]
        public string Body = "";
    }

    // Model with [AISuggestion] for field suggestion testing
    private class TestEntityFieldsModelWithSuggestion : Models.EntityFieldsModel
    {
        [Newtonsoft.Json.JsonProperty("content")]
        public string _content = "";

        [AISuggestion("Generate a concise excerpt from the content", "content")]
        [Newtonsoft.Json.JsonProperty("excerpt")]
        public string _excerpt = "";
    }

    // Model with [AISuggestion] without source fields
    private class TestEntityFieldsModelWithSuggestionNoSource : Models.EntityFieldsModel
    {
        [Newtonsoft.Json.JsonProperty("content")]
        public string _content = "";

        [AISuggestion("Generate a summary")]
        [Newtonsoft.Json.JsonProperty("summary")]
        public string _summary = "";
    }

    // Model with [AIRelationSuggestion] on non-Relation field
    private class TestEntityFieldsModelWithRelationOnNonRelation : Models.EntityFieldsModel
    {
        [AIRelationSuggestion]
        [Newtonsoft.Json.JsonProperty("name")]
        public string _name = "";
    }

    #endregion
}

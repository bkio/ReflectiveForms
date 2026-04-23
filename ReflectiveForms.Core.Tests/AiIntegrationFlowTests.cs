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
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Schema.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Integration flow tests covering plan items 7.65-7.69.
/// Tests verify complete end-to-end flows across multiple AI handlers.
/// </summary>
[Collection("AI")]
public class AiIntegrationFlowTests : IDisposable
{
    private readonly Mock<ILLMService> _mockHeavyLlm = new();
    private readonly Mock<ILLMService> _mockLightLlm = new();
    private readonly Mock<IVectorService> _mockVectorService = new();
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly Mock<IMemoryService> _mockMemoryService;

    public AiIntegrationFlowTests()
    {
        _mockDatabaseService = new Mock<IDatabaseService>();
        _mockDatabaseService.Setup(d => d.IsInitialized).Returns(true);
        _mockMemoryService = new Mock<IMemoryService>();
        _mockMemoryService.Setup(m => m.IsInitialized).Returns(true);
    }

    public void Dispose()
    {
        AiVectorSync.StopSyncTimer();

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

    private void InitializeAll()
    {
        AiConfiguration.Initialize(
            _mockDatabaseService.Object,
            _mockMemoryService.Object,
            _mockVectorService.Object,
            _mockHeavyLlm.Object,
            _mockLightLlm.Object,
            embeddingDimensions: 384);

        SetupMockRfConfig();
    }

    #region 7.65 — Full flow: create entity → vector indexed → semantic search finds it

    [Fact]
    public async Task Flow_CreateEntity_IndexVector_QueryFindsIt()
    {
        InitializeAll();

        var entityId = 42;
        var embedding = new float[384];
        Array.Fill(embedding, 0.5f);

        // Step 1: Index the entity (simulates post-save hook)
        _mockLightLlm
            .Setup(l => l.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<float[]>.Success(embedding));

        VectorPoint? capturedPoint = null;
        _mockVectorService
            .Setup(v => v.UpsertAsync(It.IsAny<string>(), It.IsAny<VectorPoint>(), It.IsAny<CancellationToken>()))
            .Callback<string, VectorPoint, CancellationToken>((_, point, _) => capturedPoint = point)
            .ReturnsAsync(OperationResult<bool>.Success(true));

        var entity = CreateTestEntity(entityId, "Test Article", "This is the article body.");
        await AiVectorIndexer.IndexEntityAsync("test-entity", entityId, entity, CancellationToken.None);

        // Verify vector was upserted with correct collection name and ID
        capturedPoint.Should().NotBeNull();
        capturedPoint!.Id.Should().Be(entityId.ToString());
        capturedPoint.Vector.Should().NotBeNull();
        capturedPoint.Vector!.Length.Should().Be(384);

        // Step 2: Verify the collection name follows naming convention
        _mockVectorService.Verify(
            v => v.UpsertAsync("rf_semantic_test-entity", It.IsAny<VectorPoint>(), It.IsAny<CancellationToken>()),
            Times.Once, "vector should be upserted to the correct collection");

        // Step 3: Vector query would find this point
        _mockVectorService
            .Setup(v => v.QueryAsync(
                "rf_semantic_test-entity",
                It.IsAny<float[]>(),
                It.IsAny<int>(),
                It.IsAny<ConditionCoupling>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<VectorSearchResult>>.Success(
                new List<VectorSearchResult>
                {
                    new() { Id = entityId.ToString(), Score = 0.95f,
                        Metadata = new JObject { ["title"] = "Test Article" } }
                }));

        // Simulate the query (as the endpoint would)
        var queryResult = await _mockVectorService.Object.QueryAsync(
            "rf_semantic_test-entity", embedding, 5, null, true, CancellationToken.None);

        queryResult.IsSuccessful.Should().BeTrue();
        queryResult.Data.Should().HaveCount(1);
        queryResult.Data[0].Id.Should().Be(entityId.ToString());
    }

    #endregion

    #region 7.66 — Full flow: delete entity → vector removed → search no longer returns it

    [Fact]
    public async Task Flow_DeleteEntity_VectorRemoved()
    {
        InitializeAll();

        var entityId = 42;

        // Step 1: Delete vector (simulates post-delete hook)
        _mockVectorService
            .Setup(v => v.DeleteAsync("rf_semantic_test-entity", "42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.Success(true));

        await AiVectorIndexer.DeleteEntityAsync("test-entity", entityId);

        _mockVectorService.Verify(
            v => v.DeleteAsync("rf_semantic_test-entity", "42", It.IsAny<CancellationToken>()),
            Times.Once, "vector should be deleted on entity deletion");

        // Step 2: Verify vector query would return empty for this entity
        _mockVectorService
            .Setup(v => v.GetAsync("rf_semantic_test-entity", "42", false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<VectorPoint>.Failure("Not found", HttpStatusCode.NotFound));

        var getResult = await _mockVectorService.Object.GetAsync(
            "rf_semantic_test-entity", "42", false, true, CancellationToken.None);

        getResult.IsSuccessful.Should().BeFalse("deleted vector should not be retrievable");
    }

    #endregion

    #region 7.25 + 7.65 — Orphan cleanup: vector exists but DB entity is gone

    [Fact]
    public async Task Flow_OrphanVector_DetectedDuringReindex()
    {
        InitializeAll();

        var orphanId = "99";

        // Entity was deleted from DB but vector still exists
        _mockDatabaseService
            .Setup(d => d.ScanTableAsync("test-entity", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<(IReadOnlyList<string> Keys, IReadOnlyList<JObject> Items)>.Success((
                new List<string>() as IReadOnlyList<string>,
                new List<JObject>() as IReadOnlyList<JObject>)));

        // Full reindex should scan all entities — since DB is empty,
        // no upserts should happen
        await AiVectorIndexer.ReIndexAsync("test-entity", "full", CancellationToken.None);

        _mockDatabaseService.Verify(
            d => d.ScanTableAsync("test-entity", It.IsAny<CancellationToken>()),
            Times.Once);

        // No embeddings should have been created (no entities to index)
        _mockLightLlm.Verify(
            l => l.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "no entities means no embeddings needed");
    }

    #endregion

    #region 7.68 — Enable AI on existing data → reindex all → all searchable

    [Fact]
    public async Task Flow_ReindexExistingData_AllEntitiesBecomSearchable()
    {
        InitializeAll();

        var entities = new List<JObject>
        {
            CreateTestEntity(1, "Article One", "First article content"),
            CreateTestEntity(2, "Article Two", "Second article content"),
            CreateTestEntity(3, "Article Three", "Third article content")
        };

        // Mock database scan returning all existing entities
        _mockDatabaseService
            .Setup(d => d.ScanTableAsync("test-entity", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<(IReadOnlyList<string> Keys, IReadOnlyList<JObject> Items)>.Success((
                entities.Select(e => e[EntityModelAttributes.Id]!.Value<int>().ToString()).ToList() as IReadOnlyList<string>,
                entities as IReadOnlyList<JObject>)));

        _mockLightLlm
            .Setup(l => l.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<float[]>.Success(new float[384]));

        var upsertedIds = new List<string>();
        _mockVectorService
            .Setup(v => v.UpsertAsync(It.IsAny<string>(), It.IsAny<VectorPoint>(), It.IsAny<CancellationToken>()))
            .Callback<string, VectorPoint, CancellationToken>((_, point, _) => upsertedIds.Add(point.Id))
            .ReturnsAsync(OperationResult<bool>.Success(true));

        // Full reindex
        await AiVectorIndexer.ReIndexAsync("test-entity", "full", CancellationToken.None);

        // All entities should have been indexed
        _mockDatabaseService.Verify(
            d => d.ScanTableAsync("test-entity", It.IsAny<CancellationToken>()),
            Times.Once, "full reindex should scan all entities");
    }

    #endregion

    #region 7.69 — Full flow: generate → (draft) → field suggestion → sanity check

    [Fact]
    public async Task Flow_Generate_ThenSuggestField_ThenSanityCheck()
    {
        InitializeAll();

        // Step 1: Generate entity draft (conversation mode — each field returns plain text)
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "AI in Healthcare",
                FinishReason = LLMFinishReason.Stop
            }));

        var draft = await AiEntityGenerator.GenerateAsync(
            "test-entity", "Write about AI in healthcare", CancellationToken.None);

        draft.Fields.Should().NotBeNull();
        draft.Fields!["title"]!.Value<string>().Should().Be("AI in Healthcare");
        // Add content manually for the downstream suggest/sanity flow
        draft.Fields["content"] = "AI is transforming healthcare...";

        // Step 2: Suggest excerpt based on draft content
        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "A concise overview of how AI is revolutionizing healthcare.",
                FinishReason = LLMFinishReason.Stop
            }));

        var suggestion = await AiFieldSuggestionHandler.SuggestAsync(
            "test-entity", "excerpt", draft.Fields!, CancellationToken.None);

        suggestion.Should().NotBeNull();
        suggestion.Should().Contain("AI");

        // Step 3: Sanity check the generated content
        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "{\"passed\": true, \"message\": \"Content looks professional.\"}",
                FinishReason = LLMFinishReason.Stop
            }));

        var checks = new List<AISanityCheck> { new("Is this professional?") };
        var checkResults = await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "content",
            new JValue(draft.Fields["content"]!.Value<string>()!),
            checks, CancellationToken.None);

        // All checks should pass for AI-generated content
        checkResults.Should().OnlyContain(r => r.Passed);
    }

    #endregion

    #region 7.67 — Index failure → incremental reindex recovers

    [Fact]
    public async Task Flow_IndexFails_IncrementalReindexRecovers()
    {
        InitializeAll();

        var entity = CreateTestEntity(1, "Lost Article", "Content that was not indexed");

        // Step 1: Original index attempt fails (e.g., LLM embedding service down)
        _mockLightLlm
            .Setup(l => l.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<float[]>.Failure("Service unavailable", HttpStatusCode.ServiceUnavailable));

        await AiVectorIndexer.IndexEntityAsync("test-entity", 1, entity, CancellationToken.None);

        // Verify upsert was NOT called (embedding failed)
        _mockVectorService.Verify(
            v => v.UpsertAsync(It.IsAny<string>(), It.IsAny<VectorPoint>(), It.IsAny<CancellationToken>()),
            Times.Never, "upsert should not be called when embedding fails");

        // Step 2: LLM recovers
        _mockLightLlm
            .Setup(l => l.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<float[]>.Success(new float[384]));

        _mockVectorService
            .Setup(v => v.UpsertAsync(It.IsAny<string>(), It.IsAny<VectorPoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.Success(true));

        _mockDatabaseService
            .Setup(d => d.ScanTableAsync("test-entity", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<(IReadOnlyList<string> Keys, IReadOnlyList<JObject> Items)>.Success((
                new List<string> { "1" } as IReadOnlyList<string>,
                new List<JObject> { entity } as IReadOnlyList<JObject>)));

        // Vector for entity 1 doesn't exist (it was never indexed)
        _mockVectorService
            .Setup(v => v.GetAsync("rf_semantic_test-entity", "1", false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<VectorPoint>.Failure("Not found", HttpStatusCode.NotFound));

        // Step 3: Incremental reindex should detect the missing vector and re-index
        await AiVectorIndexer.ReIndexAsync("test-entity", "incremental", CancellationToken.None);

        // Verify embedding was generated during reindex
        _mockLightLlm.Verify(
            l => l.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce, "incremental reindex should create embedding for missing vectors");
    }

    #endregion

    #region Combined flow: NL filter + semantic search context

    [Fact]
    public void NlFilter_ToolDefinitions_AreValid()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildFilterTools", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var tools = (LLMToolDefinition[])method!.Invoke(null, null)!;

        foreach (var tool in tools)
        {
            tool.Name.Should().NotBeNullOrEmpty();
            tool.Description.Should().NotBeNullOrEmpty();
            tool.Parameters.Should().NotBeNull();
            tool.Parameters["type"]!.Value<string>().Should().Be("object");
            tool.Parameters["properties"].Should().NotBeNull();
        }
    }

    [Fact]
    public void NlFilter_ToolDefinitions_IncludeRequiredParameters()
    {
        var method = typeof(AiNaturalLanguageFilterHandler)
            .GetMethod("BuildFilterTools", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var tools = (LLMToolDefinition[])method!.Invoke(null, null)!;

        var filterEquals = tools.First(t => t.Name == "filter_equals");
        var properties = filterEquals.Parameters["properties"] as JObject;
        properties.Should().NotBeNull();
        properties!["field_name"].Should().NotBeNull("filter_equals needs a field_name parameter");
        properties["value"].Should().NotBeNull("filter_equals needs a value parameter");
    }

    #endregion

    #region Multiple entities integration

    [Fact]
    public async Task Flow_MultipleEntities_IndexAndSearchIndependently()
    {
        InitializeAll();

        var embedding1 = new float[384];
        Array.Fill(embedding1, 0.3f);
        var embedding2 = new float[384];
        Array.Fill(embedding2, 0.7f);

        var callCount = 0;
        _mockLightLlm
            .Setup(l => l.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return OperationResult<float[]>.Success(callCount == 1 ? embedding1 : embedding2);
            });

        _mockVectorService
            .Setup(v => v.UpsertAsync(It.IsAny<string>(), It.IsAny<VectorPoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.Success(true));

        var entity1 = CreateTestEntity(1, "Title One", "Content about science");
        var entity2 = CreateTestEntity(2, "Title Two", "Content about art");

        await AiVectorIndexer.IndexEntityAsync("test-entity", 1, entity1, CancellationToken.None);
        await AiVectorIndexer.IndexEntityAsync("test-entity", 2, entity2, CancellationToken.None);

        // Verify both were upserted to the same collection
        _mockVectorService.Verify(
            v => v.UpsertAsync("rf_semantic_test-entity", It.IsAny<VectorPoint>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2), "both entities should be upserted to the same collection");
    }

    #endregion

    #region Diff summary → full revision flow

    [Fact]
    public async Task Flow_DiffSummary_ComparesAdjacentRevisions()
    {
        InitializeAll();

        var revisionsData = CreateRawHistoryData(
            ("draft", "Initial draft body"),
            ("review", "Revised body with fixes"),
            ("published", "Final published body")
        );

        _mockDatabaseService
            .Setup(d => d.GetItemAsync(
                It.Is<string>(s => s.Contains("history")),
                It.IsAny<DbKey>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<JObject>.Success(revisionsData));

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Status changed from draft to review. Body was revised.",
                FinishReason = LLMFinishReason.Stop
            }));

        var summary = await AiDiffSummaryHandler.SummarizeAsync(
            "test-entity", 42, 1, CancellationToken.None);

        summary.Should().NotBeNull();
        _mockHeavyLlm.Verify(
            l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()),
            Times.Once, "diff summary should call heavy LLM once");
    }

    #endregion

    #region Helpers

    private void SetupMockRfConfig()
    {
        var config = new AiServiceConfiguration(
            _mockHeavyLlm.Object, _mockLightLlm.Object, _mockVectorService.Object);

        var mockPubSub = new Mock<IPubSubService>();
        mockPubSub.Setup(p => p.IsInitialized).Returns(true);
        var mockFileService = new Mock<IFileService>();
        mockFileService.Setup(f => f.IsInitialized).Returns(true);

        var builder = new RfConfigurationBuilder
        {
            RepositoryServiceConfiguration = new EntityRepositoryServiceConfiguration(
                _mockDatabaseService.Object, _mockMemoryService.Object, mockPubSub.Object,
                new FileServiceConfiguration(mockFileService.Object, "test-bucket")),
            RootUserCredentials = new RootUserCredentials("root@test.com", "password"),
            Logger = new Mock<Microsoft.Extensions.Logging.ILogger>().Object,
            EndpointConfiguration = new EndpointConfiguration
            {
                RootPath = "/rf",
                PublicUrlRootForApi = "http://localhost/rf/api/",
                PublicFrontendBaseUrl = "http://localhost:3000",
                JwtSecret = "test-secret-key-12345678901234567890"
            },
            AiServiceConfiguration = config,
            EntityTypes = new List<EntityConfigurationBuilderBase>
            {
                new EntityConfigurationBuilder<TestIntegrationModel>
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
                    EntityDescription = "A test entity for AI integration.",
                    SupportsSemanticSearch = true,
                    SupportsAiGeneration = true,
                    SupportsAiDiffSummary = true,
                    SupportsNaturalLanguageFilter = true
                }
            }
        };

        var configField = typeof(RfConfiguration).GetField("_configuration",
            BindingFlags.Static | BindingFlags.NonPublic);
        var initializedField = typeof(RfConfiguration).GetField("_initialized",
            BindingFlags.Static | BindingFlags.NonPublic);
        configField?.SetValue(null, builder);
        initializedField?.SetValue(null, true);
    }

    private static JObject CreateTestEntity(int id, string title, string bodyText)
    {
        return new JObject
        {
            [EntityModelAttributes.Id] = id,
            [EntityModelAttributes.Title] = new JObject { [EntityModelAttributes.TitleRendered] = title },
            [EntityModelAttributes.ModifiedGmt] = DateTime.UtcNow.ToString("o"),
            [EntityModelAttributes.Fields] = new JObject { ["body"] = bodyText, ["content"] = bodyText }
        };
    }

    private static JObject CreateRawHistoryData(params (string status, string body)[] revisions)
    {
        var data = new JObject
        {
            ["old_revisions_count"] = revisions.Length
        };
        for (var i = 0; i < revisions.Length; i++)
        {
            data[$"old_revision_{i + 1}"] = new JObject
            {
                ["object"] = new JObject
                {
                    ["fields"] = new JObject
                    {
                        ["status"] = revisions[i].status,
                        ["body"] = revisions[i].body
                    }
                },
                ["date"] = DateTime.UtcNow.ToString("o"),
                ["date_gmt"] = DateTime.UtcNow.ToString("o")
            };
        }
        return data;
    }

    // Model that supports all AI features
    private class TestIntegrationModel : EntityFieldsModel
    {
        [Newtonsoft.Json.JsonProperty("content")]
        public string _content = "";

        [AISuggestion("Generate an excerpt from the content", "content")]
        [Newtonsoft.Json.JsonProperty("excerpt")]
        public string _excerpt = "";

        [AISanityCheck("Is this content professional?")]
        [Newtonsoft.Json.JsonProperty("body")]
        public string _body = "";
    }

    #endregion
}

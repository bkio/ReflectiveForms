using System.Net;
using System.Reflection;
using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Records;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Tests for sharing enforcement on AI features (plan Section 7.38-7.41).
/// These validate that relation suggestion and vector indexer respect
/// sharing access patterns.
/// </summary>
[Collection("AI")]
public class AiSharingTests : IDisposable
{
    private readonly Mock<ILLMService> _mockHeavyLlm = new();
    private readonly Mock<ILLMService> _mockLightLlm = new();
    private readonly Mock<IVectorService> _mockVectorService = new();
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly Mock<IMemoryService> _mockMemoryService;

    public AiSharingTests()
    {
        _mockDatabaseService = new Mock<IDatabaseService>();
        _mockDatabaseService.Setup(d => d.IsInitialized).Returns(true);
        _mockMemoryService = new Mock<IMemoryService>();
        _mockMemoryService.Setup(m => m.IsInitialized).Returns(true);
    }

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

    #region 7.38 — Semantic search returns only accessible entities (orphan cleanup)

    [Fact]
    public async Task SemanticSearch_OrphanVector_GetsCleanedUp()
    {
        InitializeAiConfiguration();

        // Setup: entity does not exist (orphan)
        _mockDatabaseService
            .Setup(d => d.GetItemAsync(
                "target-entity",
                It.IsAny<DbKey>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<JObject>.Failure("Not found", HttpStatusCode.NotFound));

        _mockVectorService
            .Setup(v => v.DeleteAsync("rf_semantic_target-entity", "99", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.Success(true));

        // AiRelationSuggestionHandler.SuggestAsync will clean up orphans
        SetupMockRfConfig();

        // Mock the embedding step (SemanticSearchAsync is an extension that embeds then queries)
        _mockLightLlm
            .Setup(l => l.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<float[]>.Success(new float[384]));

        // Mock the vector query step
        _mockVectorService
            .Setup(v => v.QueryAsync(
                "rf_semantic_target-entity",
                It.IsAny<float[]>(),
                It.IsAny<int>(),
                It.IsAny<ConditionCoupling>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<VectorSearchResult>>.Success(
                new List<VectorSearchResult>
                {
                    new() { Id = "99", Score = 0.95f, Metadata = new JObject { ["entity_name"] = "target-entity" } }
                }));

        var results = await AiRelationSuggestionHandler.SuggestAsync(
            "target-entity", "test query", 5, CancellationToken.None);

        // Entity doesn't exist, so result should be empty and orphan should be cleaned up
        results.Should().BeEmpty("orphan vectors should be cleaned up, not returned");

        _mockVectorService.Verify(
            v => v.DeleteAsync("rf_semantic_target-entity", "99", It.IsAny<CancellationToken>()),
            Times.Once, "orphan vector should be deleted");
    }

    #endregion

    #region 7.41 — Relation suggestions: only accessible targets returned

    [Fact]
    public async Task RelationSuggest_ReturnsOnlyExistingEntities()
    {
        InitializeAiConfiguration();
        SetupMockRfConfig();

        var entity1 = new JObject
        {
            [EntityModelAttributes.Id] = 1,
            [EntityModelAttributes.Title] = new JObject { [EntityModelAttributes.TitleRendered] = "Existing Entity" }
        };

        // Entity 1 exists, entity 2 doesn't
        _mockDatabaseService
            .Setup(d => d.GetItemAsync("target-entity",
                It.Is<DbKey>(k => k.Value.AsInteger == 1L),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<JObject>.Success(entity1));

        _mockDatabaseService
            .Setup(d => d.GetItemAsync("target-entity",
                It.Is<DbKey>(k => k.Value.AsInteger == 2L),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<JObject>.Failure("Not found", HttpStatusCode.NotFound));

        _mockVectorService
            .Setup(v => v.DeleteAsync(It.IsAny<string>(), "2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.Success(true));

        // Mock the embedding step
        _mockLightLlm
            .Setup(l => l.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<float[]>.Success(new float[384]));

        // Mock the vector query step
        _mockVectorService
            .Setup(v => v.QueryAsync(
                "rf_semantic_target-entity",
                It.IsAny<float[]>(),
                It.IsAny<int>(),
                It.IsAny<ConditionCoupling>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<VectorSearchResult>>.Success(
                new List<VectorSearchResult>
                {
                    new() { Id = "1", Score = 0.9f, Metadata = new JObject { ["entity_name"] = "target-entity" } },
                    new() { Id = "2", Score = 0.8f, Metadata = new JObject { ["entity_name"] = "target-entity" } }
                }));

        var results = await AiRelationSuggestionHandler.SuggestAsync(
            "target-entity", "search text", 5, CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].Id.Should().Be(1);
        results[0].Title.Should().Be("Existing Entity");
    }

    #endregion

    #region Relation suggestion respects SupportsSemanticSearch

    [Fact]
    public async Task RelationSuggest_EntityWithoutSemanticSearch_ReturnsEmpty()
    {
        InitializeAiConfiguration();
        SetupMockRfConfig(supportsSemanticSearch: false);

        var results = await AiRelationSuggestionHandler.SuggestAsync(
            "target-entity", "search text", 5, CancellationToken.None);

        results.Should().BeEmpty("entity type without semantic search should return no suggestions");
    }

    #endregion

    #region Over-fetch

    [Fact]
    public async Task RelationSuggest_OverFetchesByFactor2()
    {
        InitializeAiConfiguration();
        SetupMockRfConfig();

        int? capturedTopK = null;

        // Mock the embedding step
        _mockLightLlm
            .Setup(l => l.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<float[]>.Success(new float[384]));

        // Mock the vector query step and capture topK
        _mockVectorService
            .Setup(v => v.QueryAsync(
                "rf_semantic_target-entity",
                It.IsAny<float[]>(),
                It.IsAny<int>(),
                It.IsAny<ConditionCoupling>(),
                true,
                It.IsAny<CancellationToken>()))
            .Callback<string, float[], int, ConditionCoupling?, bool, CancellationToken>(
                (_, _, topK, _, _, _) => capturedTopK = topK)
            .ReturnsAsync(OperationResult<IReadOnlyList<VectorSearchResult>>.Success(
                new List<VectorSearchResult>()));

        await AiRelationSuggestionHandler.SuggestAsync(
            "target-entity", "test", 5, CancellationToken.None);

        capturedTopK.Should().Be(10, "should over-fetch by factor of 2 (topK * 2)");
    }

    #endregion

    #region Helpers

    private void SetupMockRfConfig(bool supportsSemanticSearch = true)
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
                new EntityConfigurationBuilder<EntityFieldsModel>
                {
                    EntityName = "target-entity",
                    EntityReadableNameSingular = "Target",
                    EntityReadableNamePlural = "Targets",
                    SupportsFrontendEdit = true,
                    HasParentChildRelationship = false,
                    HasAuthor = false,
                    HasTags = false,
                    HasCategories = false,
                    RequireGlobalTitleUniqueness = false,
                    OptionalTitleSanityCheck = null,
                    EntityDescription = supportsSemanticSearch ? "A test entity for sharing." : null,
                    SupportsSemanticSearch = supportsSemanticSearch
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

    #endregion
}

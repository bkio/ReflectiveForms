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
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Tests for AI diff summary handler (plan Section 7.50-7.51).
/// </summary>
[Collection("AI")]
public class AiDiffSummaryTests : IDisposable
{
    private readonly Mock<ILLMService> _mockHeavyLlm = new();
    private readonly Mock<ILLMService> _mockLightLlm = new();
    private readonly Mock<IVectorService> _mockVectorService = new();
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly Mock<IMemoryService> _mockMemoryService;

    public AiDiffSummaryTests()
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

    #region 7.50 — TruncateValue

    [Fact]
    public void TruncateValue_ShortString_Unchanged()
    {
        var method = typeof(AiDiffSummaryHandler)
            .GetMethod("TruncateValue", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, [new JValue("Short")])!;
        result.Should().Be("Short");
    }

    [Fact]
    public void TruncateValue_LongString_Truncated()
    {
        var method = typeof(AiDiffSummaryHandler)
            .GetMethod("TruncateValue", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var longValue = new string('X', 600);
        var result = (string)method!.Invoke(null, [new JValue(longValue)])!;

        result.Should().Contain("[...]");
        result.Length.Should().BeLessThan(longValue.Length);
    }

    [Fact]
    public void TruncateValue_Exactly500Chars_NotTruncated()
    {
        var method = typeof(AiDiffSummaryHandler)
            .GetMethod("TruncateValue", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var exact = new string('A', 500);
        var result = (string)method!.Invoke(null, [new JValue(exact)])!;

        result.Should().Be(exact);
    }

    #endregion

    #region 7.50 — ComputeDiff

    [Fact]
    public void ComputeDiff_DetectsAddedFields()
    {
        var method = typeof(AiDiffSummaryHandler)
            .GetMethod("ComputeDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var oldFields = new JObject { ["title"] = "Hello" };
        var newFields = new JObject { ["title"] = "Hello", ["tags"] = "new-tag" };

        var result = (List<string>)method!.Invoke(null, [oldFields, newFields])!;

        result.Should().Contain(s => s.Contains("Added") && s.Contains("tags"));
    }

    [Fact]
    public void ComputeDiff_DetectsRemovedFields()
    {
        var method = typeof(AiDiffSummaryHandler)
            .GetMethod("ComputeDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var oldFields = new JObject { ["title"] = "Hello", ["legacy"] = "old" };
        var newFields = new JObject { ["title"] = "Hello" };

        var result = (List<string>)method!.Invoke(null, [oldFields, newFields])!;

        result.Should().Contain(s => s.Contains("Removed") && s.Contains("legacy"));
    }

    [Fact]
    public void ComputeDiff_DetectsChangedFields()
    {
        var method = typeof(AiDiffSummaryHandler)
            .GetMethod("ComputeDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var oldFields = new JObject { ["status"] = "draft" };
        var newFields = new JObject { ["status"] = "published" };

        var result = (List<string>)method!.Invoke(null, [oldFields, newFields])!;

        result.Should().Contain(s => s.Contains("Changed") && s.Contains("status"));
    }

    [Fact]
    public void ComputeDiff_IgnoresUnchangedFields()
    {
        var method = typeof(AiDiffSummaryHandler)
            .GetMethod("ComputeDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var oldFields = new JObject { ["title"] = "Same", ["body"] = "Same body" };
        var newFields = new JObject { ["title"] = "Same", ["body"] = "Same body" };

        var result = (List<string>)method!.Invoke(null, [oldFields, newFields])!;

        result.Should().BeEmpty();
    }

    #endregion

    #region 7.51 — Long content truncated in prompt

    [Fact]
    public void ComputeDiff_LongValues_TruncatedInOutput()
    {
        var method = typeof(AiDiffSummaryHandler)
            .GetMethod("ComputeDiff", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var longContent = new string('Z', 800);
        var oldFields = new JObject { ["body"] = longContent };
        var newFields = new JObject { ["body"] = "Short new content" };

        var result = (List<string>)method!.Invoke(null, [oldFields, newFields])!;

        result.Should().HaveCount(1);
        result[0].Should().Contain("[...]", "long field values should be truncated");
    }

    #endregion

    #region SummarizeAsync

    [Fact]
    public async Task SummarizeAsync_InvalidRevisionIndex_ReturnsNull()
    {
        InitializeAll();

        // Mock GetEntityRevisionsAsync to return 3 revisions
        var revisionsData = CreateRawHistoryData(
            ("draft", "body1"), ("published", "body2"), ("published", "body3"));

        SetupRepositoryService(revisionsData);

        // revisionIndex 0 is invalid (must be >= 1)
        var result = await AiDiffSummaryHandler.SummarizeAsync(
            "test-entity", 42, 0, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SummarizeAsync_RevisionIndexBeyondMax_ReturnsNull()
    {
        InitializeAll();

        var revisionsData = CreateRawHistoryData(
            ("draft", "body1"), ("published", "body2"), ("published", "body3"));

        SetupRepositoryService(revisionsData);

        // revisionIndex > revisionsCount is invalid
        var result = await AiDiffSummaryHandler.SummarizeAsync(
            "test-entity", 42, 4, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SummarizeAsync_RevisionIndexAtMax_ComparesWithCurrentEntity()
    {
        InitializeAll();

        var revisionsData = CreateRawHistoryData(
            ("draft", "body1"), ("published", "body2"), ("published", "body3"));

        SetupRepositoryService(revisionsData);

        // Mock the current entity fetch (for comparing last revision with live entity)
        _mockDatabaseService
            .Setup(d => d.GetItemAsync(
                "test-entity",
                It.IsAny<CrossCloudKit.Interfaces.Classes.DbKey>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<JObject>.Success(new JObject
            {
                ["fields"] = new JObject
                {
                    ["status"] = "published",
                    ["body"] = "Final body after edits"
                }
            }));

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "The body was updated from body3 to the final version.",
                FinishReason = LLMFinishReason.Stop
            }));

        // revisionIndex == revisionsCount should compare last old revision with current entity
        var result = await AiDiffSummaryHandler.SummarizeAsync(
            "test-entity", 42, 3, CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().Contain("body");

        _mockHeavyLlm.Verify(
            l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SummarizeAsync_ValidRevision_CallsHeavyLlm()
    {
        InitializeAll();

        var revisionsData = CreateRawHistoryData(
            ("draft", "Original body"), ("published", "Updated body"), ("published", "Latest body"));

        SetupRepositoryService(revisionsData);

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "The body was updated from draft to published.",
                FinishReason = LLMFinishReason.Stop
            }));

        var result = await AiDiffSummaryHandler.SummarizeAsync(
            "test-entity", 42, 1, CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().Contain("updated");

        _mockHeavyLlm.Verify(
            l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SummarizeAsync_LlmFails_ReturnsNull()
    {
        InitializeAll();

        var revisionsData = CreateRawHistoryData(
            ("draft", "Body 1"), ("published", "Body 2"));

        SetupRepositoryService(revisionsData);

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Failure("Timeout", HttpStatusCode.RequestTimeout));

        var result = await AiDiffSummaryHandler.SummarizeAsync(
            "test-entity", 42, 1, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SummarizeAsync_NoDifferences_ReturnsNoDifferencesMessage()
    {
        InitializeAll();

        var revisionsData = CreateRawHistoryData(
            ("draft", "Same body"), ("draft", "Same body"));

        SetupRepositoryService(revisionsData);

        var result = await AiDiffSummaryHandler.SummarizeAsync(
            "test-entity", 42, 1, CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().Contain("No meaningful changes");
    }

    #endregion

    #region Helpers

    private void InitializeAll()
    {
        AiConfiguration.Initialize(
            _mockDatabaseService.Object,
            _mockMemoryService.Object,
            _mockVectorService.Object,
            _mockHeavyLlm.Object,
            _mockLightLlm.Object,
            embeddingDimensions: 384);

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
                    EntityDescription = "A test entity for diff summaries.",
                    SupportsAiDiffSummary = true
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

    private void SetupRepositoryService(JObject revisionsData)
    {
        // GetEntityRevisionsAsync queries "{entityName}-history" table
        // The raw DB format uses "old_revisions_count" and "old_revision_{N}" keys.
        // GetEntityRevisionsAsync transforms this into the normalized format.
        _mockDatabaseService
            .Setup(d => d.GetItemAsync(
                It.Is<string>(s => s.Contains("history")),
                It.IsAny<CrossCloudKit.Interfaces.Classes.DbKey>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<JObject>.Success(revisionsData));
    }

    /// <summary>
    /// Creates raw database-format revision data matching what EntityRepositoryService expects.
    /// The raw format stores "old_revisions_count" and "old_revision_{i}" keys.
    /// </summary>
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

    #endregion
}

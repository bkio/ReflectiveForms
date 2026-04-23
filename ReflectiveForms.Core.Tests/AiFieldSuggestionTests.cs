using System.Net;
using System.Reflection;
using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Schema.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Tests for AI field suggestion handler (plan Section 7.44-7.45).
/// </summary>
[Collection("AI")]
public class AiFieldSuggestionTests : IDisposable
{
    private readonly Mock<ILLMService> _mockHeavyLlm = new();
    private readonly Mock<ILLMService> _mockLightLlm = new();
    private readonly Mock<IVectorService> _mockVectorService = new();
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly Mock<IMemoryService> _mockMemoryService;

    public AiFieldSuggestionTests()
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
                new EntityConfigurationBuilder<TestSuggestionModel>
                {
                    EntityName = "suggestion-entity",
                    EntityReadableNameSingular = "Article",
                    EntityReadableNamePlural = "Articles",
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

        var configField = typeof(RfConfiguration).GetField("_configuration",
            BindingFlags.Static | BindingFlags.NonPublic);
        var initializedField = typeof(RfConfiguration).GetField("_initialized",
            BindingFlags.Static | BindingFlags.NonPublic);
        configField?.SetValue(null, builder);
        initializedField?.SetValue(null, true);
    }

    #region 7.44 — Suggestion uses prompt and source fields

    [Fact]
    public async Task SuggestAsync_UsesSourceFieldsFromAttribute()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "A concise summary of the content.",
                FinishReason = LLMFinishReason.Stop
            }));

        var currentFields = new JObject
        {
            ["content"] = "This is a long article about AI and machine learning.",
            ["excerpt"] = ""
        };

        var result = await AiFieldSuggestionHandler.SuggestAsync(
            "suggestion-entity", "excerpt", currentFields, CancellationToken.None);

        result.Should().NotBeNull();
        result.Should().Be("A concise summary of the content.");

        capturedRequest.Should().NotBeNull();
        var userMessage = capturedRequest!.Messages.Last().Content;
        userMessage.Should().Contain("excerpt", "should mention target field");
        userMessage.Should().Contain("content:", "should include source field value");
    }

    [Fact]
    public async Task SuggestAsync_UsesLightLlm()
    {
        InitializeAll();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Suggested text",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiFieldSuggestionHandler.SuggestAsync(
            "suggestion-entity", "excerpt",
            new JObject { ["content"] = "Some content" },
            CancellationToken.None);

        _mockLightLlm.Verify(
            l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockHeavyLlm.Verify(
            l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region 7.45 — Empty source fields uses all text-bearing fields

    [Fact]
    public async Task SuggestAsync_NoSourceFields_UsesAllTextFields()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Generated summary",
                FinishReason = LLMFinishReason.Stop
            }));

        var currentFields = new JObject
        {
            ["content"] = "Main article content",
            ["summary"] = ""
        };

        var result = await AiFieldSuggestionHandler.SuggestAsync(
            "suggestion-entity", "summary", currentFields, CancellationToken.None);

        result.Should().NotBeNull();
    }

    #endregion

    #region LLM failure

    [Fact]
    public async Task SuggestAsync_LlmFailure_ReturnsNull()
    {
        InitializeAll();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Failure("Error", HttpStatusCode.InternalServerError));

        var result = await AiFieldSuggestionHandler.SuggestAsync(
            "suggestion-entity", "excerpt",
            new JObject { ["content"] = "Some content" },
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SuggestAsync_UnknownEntity_ReturnsNull()
    {
        InitializeAll();

        var result = await AiFieldSuggestionHandler.SuggestAsync(
            "nonexistent-entity", "field",
            new JObject(),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SuggestAsync_NoAttribute_ReturnsNull()
    {
        InitializeAll();

        var result = await AiFieldSuggestionHandler.SuggestAsync(
            "suggestion-entity", "content", // content doesn't have [AISuggestion]
            new JObject { ["content"] = "test" },
            CancellationToken.None);

        result.Should().BeNull();
    }

    #endregion

    #region SystemPromptPrefix

    [Fact]
    public async Task SuggestAsync_UsesSystemPromptPrefix()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Suggestion",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiFieldSuggestionHandler.SuggestAsync(
            "suggestion-entity", "excerpt",
            new JObject { ["content"] = "Some content" },
            CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Messages.First().Content.Should().StartWith(
            "You are an assistant for a schema-driven content management system.");
    }

    #endregion

    #region Test Model

    private class TestSuggestionModel : EntityFieldsModel
    {
        [JsonProperty("content")]
        public string _content = "";

        [AISuggestion("Generate a concise excerpt from the content", "content")]
        [JsonProperty("excerpt")]
        public string _excerpt = "";

        [AISuggestion("Generate a summary")]
        [JsonProperty("summary")]
        public string _summary = "";
    }

    #endregion
}

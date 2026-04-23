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
/// Tests for AI sanity check handler and pipeline integration (plan Section 7.46-7.49).
/// </summary>
[Collection("AI")]
public class AiSanityCheckTests : IDisposable
{
    private readonly Mock<ILLMService> _mockHeavyLlm = new();
    private readonly Mock<ILLMService> _mockLightLlm = new();
    private readonly Mock<IVectorService> _mockVectorService = new();
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly Mock<IMemoryService> _mockMemoryService;

    public AiSanityCheckTests()
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

    private void SetupMockRfConfig(string? systemPromptPrefix = null)
    {
        var config = new AiServiceConfiguration(
            _mockHeavyLlm.Object, _mockLightLlm.Object, _mockVectorService.Object)
        {
            SystemPromptPrefix = systemPromptPrefix ?? "You are an assistant for a schema-driven content management system. Entities have typed fields (text, select, date, checkbox, number, repeater, group) with validation rules."
        };

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

    #region 7.46 — Error severity failing check blocks save

    [Fact]
    public async Task CheckFieldAsync_ErrorSeverity_FailingCheck_ReturnsFailed()
    {
        InitializeAll();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "{\"passed\": false, \"message\": \"Contains inappropriate content\"}",
                FinishReason = LLMFinishReason.Stop
            }));

        var checks = new List<AISanityCheck>
        {
            new("Is this appropriate?", AISanityCheckSeverity.Error)
        };

        var results = await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "body", new JValue("Bad content"),
            checks, CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].Passed.Should().BeFalse();
        results[0].Severity.Should().Be(AISanityCheckSeverity.Error);
        results[0].Message.Should().Be("Contains inappropriate content");
    }

    #endregion

    #region 7.47 — Warning severity doesn't block

    [Fact]
    public async Task CheckFieldAsync_WarningSeverity_FailingCheck_ReturnsWarning()
    {
        InitializeAll();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "{\"passed\": false, \"message\": \"Could be more professional\"}",
                FinishReason = LLMFinishReason.Stop
            }));

        var checks = new List<AISanityCheck>
        {
            new("Is this professional?", AISanityCheckSeverity.Warning)
        };

        var results = await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "body", new JValue("lol content"),
            checks, CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].Passed.Should().BeFalse();
        results[0].Severity.Should().Be(AISanityCheckSeverity.Warning);
    }

    #endregion

    #region 7.48 — Multiple checks all executed

    [Fact]
    public async Task CheckFieldAsync_MultipleChecks_AllRun()
    {
        InitializeAll();

        var callIndex = 0;
        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callIndex++;
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = callIndex == 1
                        ? "{\"passed\": true, \"message\": \"OK\"}"
                        : "{\"passed\": false, \"message\": \"Issue found\"}",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var checks = new List<AISanityCheck>
        {
            new("Check 1", AISanityCheckSeverity.Warning),
            new("Check 2", AISanityCheckSeverity.Error)
        };

        var results = await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "body", new JValue("Some content"),
            checks, CancellationToken.None);

        results.Should().HaveCount(2);
        results[0].Passed.Should().BeTrue();
        results[1].Passed.Should().BeFalse();
    }

    #endregion

    #region 7.49 — No AI config skips checks

    [Fact]
    public async Task CheckFieldAsync_LlmFailure_SkipsGracefully()
    {
        InitializeAll();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Failure("Timeout", HttpStatusCode.RequestTimeout));

        var checks = new List<AISanityCheck>
        {
            new("Check quality", AISanityCheckSeverity.Error)
        };

        var results = await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "body", new JValue("Content"),
            checks, CancellationToken.None);

        results.Should().BeEmpty("LLM failure should skip the check entirely");
    }

    #endregion

    #region Empty value

    [Fact]
    public async Task CheckFieldAsync_EmptyValue_NoChecksRun()
    {
        InitializeAll();

        var checks = new List<AISanityCheck>
        {
            new("Check quality", AISanityCheckSeverity.Error)
        };

        var results = await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "body", new JValue(""),
            checks, CancellationToken.None);

        results.Should().BeEmpty();
        _mockLightLlm.Verify(
            l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Null value

    [Fact]
    public async Task CheckFieldAsync_NullValue_NoChecksRun()
    {
        InitializeAll();

        var checks = new List<AISanityCheck>
        {
            new("Check quality")
        };

        var results = await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "body", JValue.CreateNull(),
            checks, CancellationToken.None);

        results.Should().BeEmpty();
    }

    #endregion

    #region Invalid JSON response from LLM

    [Fact]
    public async Task CheckFieldAsync_InvalidJsonResponse_TreatedAsPass()
    {
        InitializeAll();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "This is not JSON at all",
                FinishReason = LLMFinishReason.Stop
            }));

        var checks = new List<AISanityCheck>
        {
            new("Check quality", AISanityCheckSeverity.Error)
        };

        var results = await AiSanityCheckHandler.CheckFieldAsync(
            "test-entity", "body", new JValue("Some content"),
            checks, CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].Passed.Should().BeTrue("invalid JSON should be treated as a pass");
    }

    #endregion

    #region Pipeline — attribute discovery

    [Fact]
    public void Pipeline_DiscoversFieldsWithAiSanityChecks()
    {
        var type = typeof(TestModelWithSanityChecks);
        var fieldsWithChecks = new List<(string Name, int CheckCount)>();

        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     .Where(m => m is FieldInfo or PropertyInfo))
        {
            var aiChecks = member.GetCustomAttributes<AISanityCheck>(true).ToList();
            if (aiChecks.Count == 0) continue;
            var jsonPropAttr = member.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>(true);
            var fieldName = jsonPropAttr?.PropertyName ?? member.Name;
            fieldsWithChecks.Add((fieldName, aiChecks.Count));
        }

        fieldsWithChecks.Should().ContainSingle(f => f.Name == "body" && f.CheckCount == 2);
        fieldsWithChecks.Should().ContainSingle(f => f.Name == "summary" && f.CheckCount == 1);
        fieldsWithChecks.Should().HaveCount(2);
    }

    #endregion

    #region Test Model

    private class TestModelWithSanityChecks : EntityFieldsModel
    {
        [AISanityCheck("Is this professional?", AISanityCheckSeverity.Warning)]
        [AISanityCheck("Does this contain PII?", AISanityCheckSeverity.Error)]
        [Newtonsoft.Json.JsonProperty("body")]
        public string _body = "";

        [AISanityCheck("Is this a good summary?")]
        [Newtonsoft.Json.JsonProperty("summary")]
        public string _summary = "";

        [Newtonsoft.Json.JsonProperty("status")]
        public string _status = "";
    }

    #endregion
}

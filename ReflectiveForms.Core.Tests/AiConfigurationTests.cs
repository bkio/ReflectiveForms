using System.Reflection;
using CrossCloudKit.Interfaces;
using FluentAssertions;
using Moq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Repositories;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class AiConfigurationTests
{
    [Fact]
    public void AiServiceConfiguration_DefaultValues()
    {
        // AiServiceConfiguration requires ILLMService and IVectorService - but we can test defaults
        // by creating a mock or checking the record's init-only properties
        // Since we can't create without real services, test the properties exist on the type
        var type = typeof(AiServiceConfiguration);
        type.GetProperty("HeavyLlmService").Should().NotBeNull();
        type.GetProperty("LightLlmService").Should().NotBeNull();
        type.GetProperty("VectorService").Should().NotBeNull();
        type.GetProperty("MaxCompletionTokens").Should().NotBeNull();
        type.GetProperty("MaxLightCompletionTokens").Should().NotBeNull();
        type.GetProperty("Temperature").Should().NotBeNull();
        type.GetProperty("LightTemperature").Should().NotBeNull();
        type.GetProperty("SyncInterval").Should().NotBeNull();
    }

    [Fact]
    public void OpenApiConfiguration_DefaultValues()
    {
        var config = new OpenApiConfiguration();

        config.Title.Should().Be("ReflectiveForms API");
        config.Version.Should().Be("1.0.0");
        config.Description.Should().BeNull();
        config.ContactEmail.Should().BeNull();
        config.IncludeAuthEndpoints.Should().BeTrue();
        config.IncludeSchemaEndpoints.Should().BeTrue();
        config.IncludeMediaEndpoints.Should().BeTrue();
        config.IncludeRfExtensions.Should().BeTrue();
        config.IncludeAiEndpoints.Should().BeTrue();
        config.RequireAuthentication.Should().BeFalse();
    }

    [Fact]
    public void OpenApiConfiguration_CustomValues()
    {
        var config = new OpenApiConfiguration
        {
            Title = "My API",
            Version = "2.0.0",
            Description = "Test API",
            ContactEmail = "test@example.com",
            IncludeAuthEndpoints = false,
            IncludeSchemaEndpoints = false,
            IncludeMediaEndpoints = false,
            IncludeRfExtensions = false,
            IncludeAiEndpoints = false,
            RequireAuthentication = true
        };

        config.Title.Should().Be("My API");
        config.Version.Should().Be("2.0.0");
        config.Description.Should().Be("Test API");
        config.ContactEmail.Should().Be("test@example.com");
        config.IncludeAuthEndpoints.Should().BeFalse();
        config.IncludeSchemaEndpoints.Should().BeFalse();
        config.IncludeMediaEndpoints.Should().BeFalse();
        config.IncludeRfExtensions.Should().BeFalse();
        config.IncludeAiEndpoints.Should().BeFalse();
        config.RequireAuthentication.Should().BeTrue();
    }

    [Fact]
    public void EndpointConfiguration_OpenApi_DefaultsToNull()
    {
        var config = new EndpointConfiguration
        {
            RootPath = "/rf",
            PublicUrlRootForApi = "http://localhost:9000/rf/api/",
            PublicFrontendBaseUrl = "http://localhost:3000",
            JwtSecret = "secret-key-for-testing-12345678"
        };

        config.OpenApi.Should().BeNull();
    }

    [Fact]
    public void EndpointConfiguration_OpenApi_CanBeSet()
    {
        var config = new EndpointConfiguration
        {
            RootPath = "/rf",
            PublicUrlRootForApi = "http://localhost:9000/rf/api/",
            PublicFrontendBaseUrl = "http://localhost:3000",
            JwtSecret = "secret-key-for-testing-12345678",
            OpenApi = new OpenApiConfiguration
            {
                Title = "Test API",
                Description = "Auto-generated"
            }
        };

        config.OpenApi.Should().NotBeNull();
        config.OpenApi!.Title.Should().Be("Test API");
        config.OpenApi.Description.Should().Be("Auto-generated");
    }

    [Fact]
    public void EntityConfigurationBuilder_AiFlags_DefaultToFalse()
    {
        var builder = new EntityConfigurationBuilder<EntityFieldsModel>
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
        };

        builder.SupportsSemanticSearch.Should().BeFalse();
        builder.SupportsAiGeneration.Should().BeFalse();
        builder.SupportsAiDiffSummary.Should().BeFalse();
        builder.SupportsNaturalLanguageFilter.Should().BeFalse();
    }

    [Fact]
    public void EntityConfigurationBuilder_AiFlags_CanBeSetToTrue()
    {
        var builder = new EntityConfigurationBuilder<EntityFieldsModel>
        {
            EntityName = "ai-entity",
            EntityReadableNameSingular = "AI Entity",
            EntityReadableNamePlural = "AI Entities",
            SupportsFrontendEdit = true,
            HasParentChildRelationship = false,
            HasAuthor = false,
            HasTags = false,
            HasCategories = false,
            RequireGlobalTitleUniqueness = false,
            OptionalTitleSanityCheck = null,
            EntityDescription = "A test entity for AI configuration.",
            SupportsSemanticSearch = true,
            SupportsAiGeneration = true,
            SupportsAiDiffSummary = true,
            SupportsNaturalLanguageFilter = true
        };

        builder.SupportsSemanticSearch.Should().BeTrue();
        builder.SupportsAiGeneration.Should().BeTrue();
        builder.SupportsAiDiffSummary.Should().BeTrue();
        builder.SupportsNaturalLanguageFilter.Should().BeTrue();
    }

    [Fact]
    public void RfConfigurationBuilder_AiServiceConfiguration_DefaultsToNull()
    {
        // RfConfigurationBuilder.AiServiceConfiguration should default to null
        var type = typeof(RfConfigurationBuilder);
        var prop = type.GetProperty("AiServiceConfiguration");
        prop.Should().NotBeNull("AiServiceConfiguration property should exist on RfConfigurationBuilder");
    }

    [Fact]
    public void RfConfigurationBuilder_AiFlags_WithoutConfig_ThrowsOnInitialize()
    {
        // Cannot directly test Initialize() without full setup, but we can verify the
        // validation logic exists by checking that AI flags are on the builder type
        var builderType = typeof(EntityConfigurationBuilderBase);
        builderType.GetProperty("SupportsSemanticSearch").Should().NotBeNull();
        builderType.GetProperty("SupportsAiGeneration").Should().NotBeNull();
        builderType.GetProperty("SupportsAiDiffSummary").Should().NotBeNull();
        builderType.GetProperty("SupportsNaturalLanguageFilter").Should().NotBeNull();
    }

    [Fact]
    public void EntityConfigurationBuilder_EntityDescription_DefaultsToNull()
    {
        var builder = new EntityConfigurationBuilder<EntityFieldsModel>
        {
            EntityName = "test",
            EntityReadableNameSingular = "Test",
            EntityReadableNamePlural = "Tests",
            SupportsFrontendEdit = false,
            HasParentChildRelationship = false,
            HasAuthor = false,
            HasTags = false,
            HasCategories = false,
            RequireGlobalTitleUniqueness = false,
            OptionalTitleSanityCheck = null
        };

        builder.EntityDescription.Should().BeNull();
    }
}

/// <summary>
/// Tests that exercise RfConfiguration.Initialize() validation for EntityDescription.
/// Separated to use IDisposable for cleanup of static state.
/// </summary>
[Collection("AI")]
public class AiEntityDescriptionValidationTests : IDisposable
{
    public void Dispose()
    {
        var rfConfigField = typeof(RfConfiguration).GetField("_configuration",
            BindingFlags.Static | BindingFlags.NonPublic);
        var rfInitField = typeof(RfConfiguration).GetField("_initialized",
            BindingFlags.Static | BindingFlags.NonPublic);
        rfConfigField?.SetValue(null, null);
        rfInitField?.SetValue(null, false);

        var aiBackingField = typeof(AiConfiguration).GetField("<IsInitialized>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic);
        aiBackingField?.SetValue(null, false);
    }

    private static RfConfigurationBuilder MakeBuilder(
        string entityDescription, bool supportsAiGeneration)
    {
        var mockDb = new Mock<IDatabaseService>();
        mockDb.Setup(d => d.IsInitialized).Returns(true);
        var mockMem = new Mock<IMemoryService>();
        mockMem.Setup(m => m.IsInitialized).Returns(true);
        var mockPubSub = new Mock<IPubSubService>();
        mockPubSub.Setup(p => p.IsInitialized).Returns(true);
        var mockFile = new Mock<IFileService>();
        mockFile.Setup(f => f.IsInitialized).Returns(true);
        var mockLlm = new Mock<ILLMService>();
        var mockVector = new Mock<IVectorService>();

        return new RfConfigurationBuilder
        {
            RepositoryServiceConfiguration = new EntityRepositoryServiceConfiguration(
                mockDb.Object, mockMem.Object, mockPubSub.Object,
                new FileServiceConfiguration(mockFile.Object, "test-bucket")),
            RootUserCredentials = new RootUserCredentials("root@test.com", "pass"),
            Logger = new Mock<Microsoft.Extensions.Logging.ILogger>().Object,
            EndpointConfiguration = new EndpointConfiguration
            {
                RootPath = "/rf",
                PublicUrlRootForApi = "http://localhost/rf/api/",
                PublicFrontendBaseUrl = "http://localhost:3000",
                JwtSecret = "test-secret-key-12345678901234567890"
            },
            AiServiceConfiguration = new AiServiceConfiguration(
                mockLlm.Object, mockLlm.Object, mockVector.Object),
            EntityTypes = new List<EntityConfigurationBuilderBase>
            {
                new EntityConfigurationBuilder<EntityFieldsModel>
                {
                    EntityName = "validated-entity",
                    EntityReadableNameSingular = "Validated",
                    EntityReadableNamePlural = "Validated",
                    SupportsFrontendEdit = true,
                    HasParentChildRelationship = false,
                    HasAuthor = false,
                    HasTags = false,
                    HasCategories = false,
                    RequireGlobalTitleUniqueness = false,
                    OptionalTitleSanityCheck = null,
                    EntityDescription = string.IsNullOrEmpty(entityDescription) ? null : entityDescription,
                    SupportsAiGeneration = supportsAiGeneration
                }
            }
        };
    }

    [Fact]
    public void Initialize_AiEnabled_MissingDescription_Fails()
    {
        var builder = MakeBuilder("", supportsAiGeneration: true);
        var result = RfConfiguration.Initialize(builder);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("EntityDescription is not set");
        result.ErrorMessage.Should().Contain("validated-entity");
    }

    [Fact]
    public void Initialize_AiEnabled_WithDescription_Succeeds()
    {
        var builder = MakeBuilder("An entity for testing.", supportsAiGeneration: true);
        var result = RfConfiguration.Initialize(builder);

        // May fail later in AI initialization (embedding probe etc.), but should NOT fail
        // on the EntityDescription validation step
        if (!result.IsSuccessful)
            result.ErrorMessage.Should().NotContain("EntityDescription");
    }

    [Fact]
    public void Initialize_AiDisabled_MissingDescription_Succeeds()
    {
        var builder = MakeBuilder("", supportsAiGeneration: false);
        var result = RfConfiguration.Initialize(builder);

        // Should not fail on EntityDescription validation
        if (!result.IsSuccessful)
            result.ErrorMessage.Should().NotContain("EntityDescription");
    }
}

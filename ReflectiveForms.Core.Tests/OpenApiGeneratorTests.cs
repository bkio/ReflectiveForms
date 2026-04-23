using System.Net;
using System.Reflection;
using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Records;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Schema;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Tests for OpenAPI spec generation (plan Section 7.5-7.11).
/// </summary>
[Collection("AI")]
public class OpenApiGeneratorTests : IDisposable
{
    public OpenApiGeneratorTests()
    {
        // Clear the OpenApiGenerator cache so each test starts fresh
        var cacheField = typeof(OpenApiGenerator).GetField("_cachedSpec",
            BindingFlags.Static | BindingFlags.NonPublic);
        cacheField?.SetValue(null, null);
    }

    public void Dispose()
    {
        var rfConfigField = typeof(RfConfiguration).GetField("_configuration",
            BindingFlags.Static | BindingFlags.NonPublic);
        var rfInitField = typeof(RfConfiguration).GetField("_initialized",
            BindingFlags.Static | BindingFlags.NonPublic);
        rfConfigField?.SetValue(null, null);
        rfInitField?.SetValue(null, false);

        var backingField = typeof(AiConfiguration).GetField("<IsInitialized>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic);
        backingField?.SetValue(null, false);

        var cacheField = typeof(OpenApiGenerator).GetField("_cachedSpec",
            BindingFlags.Static | BindingFlags.NonPublic);
        cacheField?.SetValue(null, null);
    }

    private void SetupRfConfiguration(
        OpenApiConfiguration? openApi = null,
        AiServiceConfiguration? aiConfig = null,
        List<EntityConfigurationBuilderBase>? entities = null)
    {
        var mockDb = new Mock<IDatabaseService>();
        mockDb.Setup(d => d.IsInitialized).Returns(true);
        var mockMemory = new Mock<IMemoryService>();
        mockMemory.Setup(m => m.IsInitialized).Returns(true);
        var mockPubSub = new Mock<IPubSubService>();
        mockPubSub.Setup(p => p.IsInitialized).Returns(true);
        var mockFileService = new Mock<IFileService>();
        mockFileService.Setup(f => f.IsInitialized).Returns(true);
        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger>().Object;

        var builder = new RfConfigurationBuilder
        {
            RepositoryServiceConfiguration = new EntityRepositoryServiceConfiguration(
                mockDb.Object, mockMemory.Object, mockPubSub.Object,
                new FileServiceConfiguration(mockFileService.Object, "test-bucket")),
            RootUserCredentials = new RootUserCredentials("root@test.com", "password"),
            Logger = mockLogger,
            EndpointConfiguration = new EndpointConfiguration
            {
                RootPath = "/rf",
                PublicUrlRootForApi = "http://localhost/rf/api/",
                PublicFrontendBaseUrl = "http://localhost:3000",
                JwtSecret = "test-secret-key-12345678901234567890",
                OpenApi = openApi
            },
            AiServiceConfiguration = aiConfig,
            EntityTypes = entities ?? new List<EntityConfigurationBuilderBase>
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
                    EntityDescription = "A test entity for OpenAPI generation.",
                    SupportsSemanticSearch = true
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

    #region 7.5 — Generate spec as valid JSON

    [Fact]
    public void Generate_ReturnsValidOpenApiJson()
    {
        SetupRfConfiguration(new OpenApiConfiguration());

        var spec = OpenApiGenerator.Generate();

        spec.Should().NotBeNull();
        spec["openapi"]!.Value<string>().Should().Be("3.1.0");
        spec["info"].Should().NotBeNull();
        spec["paths"].Should().NotBeNull();
        spec["components"].Should().NotBeNull();
    }

    #endregion

    #region 7.6 — Info block matches configuration

    [Fact]
    public void Generate_InfoBlockMatchesConfig()
    {
        var config = new OpenApiConfiguration
        {
            Title = "My Custom API",
            Version = "2.5.0",
            Description = "A test API description",
            ContactEmail = "admin@example.com"
        };
        SetupRfConfiguration(config);

        var spec = OpenApiGenerator.Generate();
        var info = spec["info"]!;

        info["title"]!.Value<string>().Should().Be("My Custom API");
        info["version"]!.Value<string>().Should().Be("2.5.0");
        info["description"]!.Value<string>().Should().Be("A test API description");
        info["contact"]!["email"]!.Value<string>().Should().Be("admin@example.com");
    }

    #endregion

    #region 7.7 — CRUD paths generated per entity

    [Fact]
    public void Generate_IncludesCrudPathsPerEntity()
    {
        SetupRfConfiguration(new OpenApiConfiguration());

        var spec = OpenApiGenerator.Generate();
        var paths = spec["paths"]! as JObject;

        paths.Should().NotBeNull();
        // Should have CRUD operations
        paths!.Properties().Select(p => p.Name).Should()
            .Contain(p => p.Contains("operation=CREATE") && p.Contains("type=test-entity"));
        paths.Properties().Select(p => p.Name).Should()
            .Contain(p => p.Contains("operation=READ") && p.Contains("type=test-entity"));
        paths.Properties().Select(p => p.Name).Should()
            .Contain(p => p.Contains("operation=UPDATE") && p.Contains("type=test-entity"));
        paths.Properties().Select(p => p.Name).Should()
            .Contain(p => p.Contains("operation=DELETE") && p.Contains("type=test-entity"));
        paths.Properties().Select(p => p.Name).Should()
            .Contain(p => p.Contains("operation=PEEK_ALL") && p.Contains("type=test-entity"));
    }

    #endregion

    #region 7.8 — Schema and auth endpoints conditional

    [Fact]
    public void Generate_IncludesSchemaEndpoints_WhenEnabled()
    {
        SetupRfConfiguration(new OpenApiConfiguration { IncludeSchemaEndpoints = true });

        var spec = OpenApiGenerator.Generate();
        var paths = spec["paths"]! as JObject;

        paths!.Properties().Select(p => p.Name).Should()
            .Contain(p => p.Contains("/schema"));
    }

    [Fact]
    public void Generate_IncludesAuthEndpoints_WhenEnabled()
    {
        SetupRfConfiguration(new OpenApiConfiguration { IncludeAuthEndpoints = true });

        var spec = OpenApiGenerator.Generate();
        var paths = spec["paths"]! as JObject;

        paths!.Properties().Select(p => p.Name).Should().Contain("/login");
        paths.Properties().Select(p => p.Name).Should().Contain("/logout");
    }

    [Fact]
    public void Generate_ExcludesAuthEndpoints_WhenDisabled()
    {
        SetupRfConfiguration(new OpenApiConfiguration { IncludeAuthEndpoints = false });

        var spec = OpenApiGenerator.Generate();
        var paths = spec["paths"]! as JObject;

        paths!.Properties().Select(p => p.Name).Should().NotContain("/login");
        paths.Properties().Select(p => p.Name).Should().NotContain("/logout");
    }

    #endregion

    #region 7.9 — Entity wrapper schema includes optional fields

    [Fact]
    public void Generate_WrapperSchema_IncludesParent_WhenConfigured()
    {
        SetupRfConfiguration(new OpenApiConfiguration(), entities: new List<EntityConfigurationBuilderBase>
        {
            new EntityConfigurationBuilder<EntityFieldsModel>
            {
                EntityName = "child-entity",
                EntityReadableNameSingular = "Child",
                EntityReadableNamePlural = "Children",
                SupportsFrontendEdit = true,
                HasParentChildRelationship = true,
                HasAuthor = true,
                HasTags = true,
                HasCategories = true,
                RequireGlobalTitleUniqueness = false,
                OptionalTitleSanityCheck = null
            }
        });

        var spec = OpenApiGenerator.Generate();
        var entitySchema = spec["components"]!["schemas"]!["child-entity_entity"]! as JObject;

        entitySchema!["properties"]!["parent"].Should().NotBeNull();
        entitySchema["properties"]!["author"].Should().NotBeNull();
        entitySchema["properties"]!["tags"].Should().NotBeNull();
        entitySchema["properties"]!["categories"].Should().NotBeNull();
    }

    [Fact]
    public void Generate_WrapperSchema_ExcludesOptionals_WhenNotConfigured()
    {
        SetupRfConfiguration(new OpenApiConfiguration(), entities: new List<EntityConfigurationBuilderBase>
        {
            new EntityConfigurationBuilder<EntityFieldsModel>
            {
                EntityName = "simple-entity",
                EntityReadableNameSingular = "Simple",
                EntityReadableNamePlural = "Simples",
                SupportsFrontendEdit = true,
                HasParentChildRelationship = false,
                HasAuthor = false,
                HasTags = false,
                HasCategories = false,
                RequireGlobalTitleUniqueness = false,
                OptionalTitleSanityCheck = null
            }
        });

        var spec = OpenApiGenerator.Generate();
        var entitySchema = spec["components"]!["schemas"]!["simple-entity_entity"]! as JObject;

        entitySchema!["properties"]!["parent"].Should().BeNull();
        entitySchema["properties"]!["author"].Should().BeNull();
        entitySchema["properties"]!["tags"].Should().BeNull();
        entitySchema["properties"]!["categories"].Should().BeNull();
    }

    #endregion

    #region 7.10 — AI endpoints conditional on AI config + flag

    [Fact]
    public void Generate_AiPaths_IncludedWhenAiConfigured()
    {
        var mockHeavy = new Mock<ILLMService>();
        var mockLight = new Mock<ILLMService>();
        var mockVector = new Mock<IVectorService>();
        var aiConfig = new AiServiceConfiguration(mockHeavy.Object, mockLight.Object, mockVector.Object);

        SetupRfConfiguration(
            new OpenApiConfiguration { IncludeAiEndpoints = true },
            aiConfig);

        var spec = OpenApiGenerator.Generate();
        var paths = spec["paths"]! as JObject;

        paths!.Properties().Select(p => p.Name).Should().Contain("/ai/semantic_search");
        paths.Properties().Select(p => p.Name).Should().Contain("/ai/generate");
        paths.Properties().Select(p => p.Name).Should().Contain("/ai/suggest");
        paths.Properties().Select(p => p.Name).Should().Contain("/ai/sanity_check");
        paths.Properties().Select(p => p.Name).Should().Contain("/ai/diff_summary");
        paths.Properties().Select(p => p.Name).Should().Contain("/ai/nl_filter");
        paths.Properties().Select(p => p.Name).Should().Contain("/ai/relation_suggest");
        paths.Properties().Select(p => p.Name).Should().Contain("/ai/reindex");
    }

    [Fact]
    public void Generate_AiPaths_ExcludedWhenAiNotConfigured()
    {
        SetupRfConfiguration(new OpenApiConfiguration { IncludeAiEndpoints = true });

        var spec = OpenApiGenerator.Generate();
        var paths = spec["paths"]! as JObject;

        paths!.Properties().Select(p => p.Name).Should().NotContain("/ai/semantic_search");
        paths.Properties().Select(p => p.Name).Should().NotContain("/ai/generate");
    }

    [Fact]
    public void Generate_AiPaths_ExcludedWhenFlagDisabled()
    {
        var mockHeavy = new Mock<ILLMService>();
        var mockLight = new Mock<ILLMService>();
        var mockVector = new Mock<IVectorService>();
        var aiConfig = new AiServiceConfiguration(mockHeavy.Object, mockLight.Object, mockVector.Object);

        SetupRfConfiguration(
            new OpenApiConfiguration { IncludeAiEndpoints = false },
            aiConfig);

        var spec = OpenApiGenerator.Generate();
        var paths = spec["paths"]! as JObject;

        paths!.Properties().Select(p => p.Name).Should().NotContain("/ai/semantic_search");
    }

    #endregion

    #region 7.11 — Security schemes present

    [Fact]
    public void Generate_SecuritySchemes_Present()
    {
        SetupRfConfiguration(new OpenApiConfiguration());

        var spec = OpenApiGenerator.Generate();
        var securitySchemes = spec["components"]!["securitySchemes"]!;

        securitySchemes["bearerAuth"].Should().NotBeNull();
        securitySchemes["bearerAuth"]!["type"]!.Value<string>().Should().Be("http");
        securitySchemes["bearerAuth"]!["scheme"]!.Value<string>().Should().Be("bearer");

        securitySchemes["cookieAuth"].Should().NotBeNull();
        securitySchemes["cookieAuth"]!["type"]!.Value<string>().Should().Be("apiKey");
    }

    #endregion

    #region Caching

    [Fact]
    public void Generate_CachesResult()
    {
        SetupRfConfiguration(new OpenApiConfiguration());

        var spec1 = OpenApiGenerator.Generate();
        var spec2 = OpenApiGenerator.Generate();

        spec1.Should().BeSameAs(spec2, "spec should be cached in-memory");
    }

    #endregion

    #region Components

    [Fact]
    public void Generate_ComponentSchemas_IncludesEntityFields()
    {
        SetupRfConfiguration(new OpenApiConfiguration());

        var spec = OpenApiGenerator.Generate();
        var schemas = spec["components"]!["schemas"]! as JObject;

        schemas!["test-entity_fields"].Should().NotBeNull();
        schemas["test-entity_entity"].Should().NotBeNull();
    }

    #endregion
}

// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using CrossCloudKit.Database.Basic;
using CrossCloudKit.File.Basic;
using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using CrossCloudKit.Memory.Basic;
using CrossCloudKit.PubSub.Basic;
using CrossCloudKit.Vector.Basic;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Models.ReservedEntityTypes;
using ReflectiveForms.Core.Repositories;
using ReflectiveForms.Core.Schema;
using ReflectiveForms.Sample1.Models;
using Xunit;

namespace ReflectiveForms.Sample1.Tests;

/// <summary>
/// True end-to-end tests for the entity generation pipeline. Every mock callback
/// uses Task.Yield() to force real async context switches — exercising the full
/// async machinery (AsyncLocal flow, cancellation, thread-pool scheduling).
/// Tests verify actual generated output: field presence, type correctness, derivations,
/// and display condition logic — not just mock routing.
/// </summary>
[Collection("SampleE2E")]
public class AiEntityGenerationE2eTests : IDisposable
{
    private readonly Mock<ILLMService> _mockHeavyLlm = new();
    private readonly Mock<ILLMService> _mockLightLlm = new();
    private readonly Mock<IVectorService> _mockVectorService = new();
    private readonly IPubSubService _pubSubService;
    private readonly IMemoryService _memoryService;
    private readonly IDatabaseService _databaseService;
    private readonly IFileService _fileService;

    public AiEntityGenerationE2eTests()
    {
        _pubSubService = new PubSubServiceBasic();
        _memoryService = new MemoryServiceBasic(_pubSubService);
        _fileService = new FileServiceBasic(_memoryService, _pubSubService);
        _databaseService = new DatabaseServiceBasic(
            "entity-gen-e2e-tests", _memoryService, Path.GetTempPath());
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

        var cbProp = typeof(ConditionBuilder).GetProperty("DatabaseService",
            BindingFlags.Static | BindingFlags.NonPublic);
        cbProp?.SetValue(null, null);

        var iamCacheField = typeof(RfConfiguration).GetField("_iamRoleEntitiesCache",
            BindingFlags.Static | BindingFlags.NonPublic);
        iamCacheField?.SetValue(null, null);
    }

    private void InitializeSampleConfiguration(AiGenerationStrategy strategy = AiGenerationStrategy.Auto)
    {
        var aiConfig = new AiServiceConfiguration(
            _mockHeavyLlm.Object,
            _mockLightLlm.Object,
            _mockVectorService.Object)
        {
            GenerationStrategy = strategy
        };

        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger>().Object;

        var builder = new RfConfigurationBuilder
        {
            Logger = mockLogger,
            RootUserCredentials = new RootUserCredentials("admin@test.com", "password123"),
            RepositoryServiceConfiguration = new EntityRepositoryServiceConfiguration(
                _databaseService, _memoryService, _pubSubService,
                new FileServiceConfiguration(_fileService, "test-media-bucket")),
            EndpointConfiguration = new EndpointConfiguration
            {
                JwtSecret = "test-secret-key-1234567890-abcdef",
                RootPath = "/rf",
                PublicUrlRootForApi = "http://localhost:9000/rf/api/",
                PublicFrontendBaseUrl = "http://localhost:3000"
            },
            AiServiceConfiguration = aiConfig,
            EntityTypes =
            [
                new EntityConfigurationBuilder<RfObjectiveExampleModel>
                {
                    EntityName = "objective",
                    EntityReadableNamePlural = "Objectives",
                    EntityReadableNameSingular = "Objective",
                    EntityDescription = "An OKR goal with measurable key results and root cause analysis.",
                    SupportsFrontendEdit = true,
                    HasAuthor = true,
                    HasTags = true,
                    HasCategories = true,
                    HasParentChildRelationship = true,
                    RequireGlobalTitleUniqueness = true,
                    SupportsSemanticSearch = true,
                    SupportsAiGeneration = true,
                    SupportsAiDiffSummary = true,
                    SupportsNaturalLanguageFilter = true,
                    OptionalTitleSanityCheck = null
                },
                new EntityConfigurationBuilder<BlogPostModel>
                {
                    EntityName = "blog-post",
                    EntityReadableNamePlural = "Blog Posts",
                    EntityReadableNameSingular = "Blog Post",
                    EntityDescription = "A blog article with rich-text content, SEO metadata, and publication workflow.",
                    SupportsFrontendEdit = true,
                    HasAuthor = true,
                    HasTags = true,
                    HasCategories = true,
                    HasParentChildRelationship = false,
                    RequireGlobalTitleUniqueness = true,
                    SupportsSemanticSearch = true,
                    SupportsAiGeneration = true,
                    SupportsAiDiffSummary = true,
                    SupportsNaturalLanguageFilter = true,
                    OptionalTitleSanityCheck = null
                },
                new EntityConfigurationBuilder<TeamMemberModel>
                {
                    EntityName = "team-member",
                    EntityReadableNamePlural = "Team Members",
                    EntityReadableNameSingular = "Team Member",
                    EntityDescription = "A team member profile with contact info, department, and bio.",
                    SupportsFrontendEdit = true,
                    HasAuthor = false,
                    HasTags = false,
                    HasCategories = false,
                    HasParentChildRelationship = false,
                    RequireGlobalTitleUniqueness = true,
                    SupportsSemanticSearch = true,
                    OptionalTitleSanityCheck = null
                },
                new EntityConfigurationBuilder<EventModel>
                {
                    EntityName = "event",
                    EntityReadableNamePlural = "Events",
                    EntityReadableNameSingular = "Event",
                    EntityDescription = "A conference or meetup event with sessions and sponsors.",
                    SupportsFrontendEdit = true,
                    HasAuthor = true,
                    HasTags = false,
                    HasCategories = true,
                    HasParentChildRelationship = false,
                    RequireGlobalTitleUniqueness = false,
                    SupportsSemanticSearch = true,
                    SupportsAiGeneration = true,
                    SupportsNaturalLanguageFilter = true,
                    OptionalTitleSanityCheck = null
                },
                new EntityConfigurationBuilder<ProductModel>
                {
                    EntityName = "product",
                    EntityReadableNamePlural = "Products",
                    EntityReadableNameSingular = "Product",
                    SupportsFrontendEdit = true,
                    HasAuthor = true,
                    HasTags = true,
                    HasCategories = true,
                    HasParentChildRelationship = true,
                    RequireGlobalTitleUniqueness = false,
                    OptionalTitleSanityCheck = null
                },
                new EntityConfigurationBuilder<SurveyModel>
                {
                    EntityName = "survey",
                    EntityReadableNamePlural = "Surveys",
                    EntityReadableNameSingular = "Survey",
                    EntityDescription = "A multi-section survey with questions, scoring, and multiple question types.",
                    SupportsFrontendEdit = true,
                    HasAuthor = true,
                    HasTags = false,
                    HasCategories = false,
                    HasParentChildRelationship = false,
                    RequireGlobalTitleUniqueness = false,
                    SupportsAiGeneration = true,
                    SupportsAiDiffSummary = true,
                    OptionalTitleSanityCheck = null
                }
            ]
        };

        var configField = typeof(RfConfiguration).GetField("_configuration",
            BindingFlags.Static | BindingFlags.NonPublic);
        var initializedField = typeof(RfConfiguration).GetField("_initialized",
            BindingFlags.Static | BindingFlags.NonPublic);
        configField!.SetValue(null, builder);
        initializedField!.SetValue(null, true);

        AiConfiguration.Initialize(
            _databaseService, _memoryService, _mockVectorService.Object,
            _mockHeavyLlm.Object, _mockLightLlm.Object,
            embeddingDimensions: 384);
    }

    /// <summary>
    /// Sets up the IAM Role cache with an admin role (ID=10) that has full permissions
    /// on all entity types. Required for ChatAsync tests that check CanUserDo.
    /// </summary>
    private void SetupIamCacheWithAdminRole()
    {
        var iamCache = (IamRoleEntitiesCache)FormatterServices.GetUninitializedObject(typeof(IamRoleEntitiesCache));

        var baseLockField = typeof(EntitiesCacheBase<IamRoleEntityFieldsModel>)
            .GetField("_entitiesLock", BindingFlags.Instance | BindingFlags.NonPublic)!;
        baseLockField.SetValue(iamCache, new object());

        var baseEntitiesField = typeof(EntitiesCacheBase<IamRoleEntityFieldsModel>)
            .GetField("_entities", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var entityTypes = new[] { "objective", "blog-post", "team-member", "event", "product", "survey" };
        var capabilities = entityTypes.Select(et => new IamRoleCapabilitiesModel
        {
            EntityType = et,
            AllowPeekAll = true, AllowRead = true,
            AllowUpdate = true, AllowCreate = true, AllowDelete = true
        }).ToList();

        var adminRole = new EntityModel<IamRoleEntityFieldsModel>
        {
            Id = 10,
            Title = new TitleRenderedModel(),
            Fields = new IamRoleEntityFieldsModel { Capabilities = capabilities }
        };

        var entities = new Dictionary<int, EntityModel<IamRoleEntityFieldsModel>> { [10] = adminRole };
        baseEntitiesField.SetValue(iamCache, entities);

        var iamCacheField = typeof(RfConfiguration).GetField("_iamRoleEntitiesCache",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        iamCacheField.SetValue(null, iamCache);
    }

    // ════════════════════════════════════════════════════════════════
    // Async-yielding mock helper — forces real thread-pool scheduling
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets up the heavy LLM mock with Task.Yield() before every response.
    /// This forces the continuation onto a different thread-pool thread, catching
    /// bugs like [ThreadStatic] misuse across await boundaries.
    /// </summary>
    private void SetupAsyncMock(Func<string, string> respond)
    {
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken _) =>
            {
                // Force async context switch — catches ThreadStatic/AsyncLocal bugs
                await Task.Yield();

                var lastUserMsg = req.Messages.LastOrDefault(m => m.Role == LLMRole.User)?.Content ?? "";
                var content = respond(lastUserMsg);

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = content,
                    FinishReason = LLMFinishReason.Stop
                });
            });
    }

    /// <summary>
    /// Sets up an async mock that returns a specific batch JSON for "Fill in ALL" requests
    /// and delegates to a per-field function for other requests.
    /// </summary>
    private void SetupAsyncBatchMock(string batchJson, Func<string, string> fieldResponder)
    {
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken _) =>
            {
                await Task.Yield();

                var lastUserMsg = req.Messages.LastOrDefault(m => m.Role == LLMRole.User)?.Content ?? "";

                if (lastUserMsg.Contains("Fill in ALL", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = batchJson,
                        FinishReason = LLMFinishReason.Stop
                    });

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = fieldResponder(lastUserMsg),
                    FinishReason = LLMFinishReason.Stop
                });
            });
    }

    // ════════════════════════════════════════════════════════════════
    // BlogPost — Full pipeline through real async paths
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BlogPost_FullPipeline_GeneratesAllExpectedFields()
    {
        InitializeSampleConfiguration();

        var batchJson = new JObject
        {
            ["status"] = "published",
            ["is_featured"] = true,
            ["allow_comments"] = true,
            ["reading_time_minutes"] = 5
        }.ToString();

        SetupAsyncBatchMock(batchJson, msg =>
        {
            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                return "The Future of Edge Computing";

            if (msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase))
                return "<h2>Edge Computing Revolution</h2><p>Edge computing is reshaping how " +
                       "enterprises process data by moving computation closer to where it is generated. " +
                       "This paradigm shift reduces latency, saves bandwidth, and enables real-time " +
                       "applications like autonomous vehicles, smart manufacturing, and augmented reality. " +
                       "The global edge computing market is projected to grow exponentially as 5G networks " +
                       "expand and IoT device proliferation accelerates across industries.</p>" +
                       "<h2>Architecture Patterns</h2><p>Modern edge architectures employ a hierarchical " +
                       "model with device edge, near edge, and far edge tiers that distribute workloads " +
                       "based on latency requirements and resource constraints.</p>";

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "Write about edge computing trends", CancellationToken.None);

        result.Fields.Should().NotBeNull("generation must produce a result");
        var fields = result.Fields!;

        // Title set from LLM response
        fields["title"]!.Value<string>().Should().Be("The Future of Edge Computing");

        // Batch-generated structured fields
        fields["status"]!.Value<string>().Should().Be("published");
        fields["is_featured"]!.Value<bool>().Should().BeTrue();
        fields["allow_comments"]!.Value<bool>().Should().BeTrue();

        // Content must exist and be substantial (not null, not empty, not echo of title)
        var content = fields["content"]?.Value<string>();
        content.Should().NotBeNullOrEmpty("WYSIWYG content must be generated");
        content!.Length.Should().BeGreaterThan(100, "content should be substantial");

        // Slug derived from title
        fields["slug"]?.Value<string>().Should().Be("the-future-of-edge-computing");

        // Excerpt derived from content (post-processed, not LLM-generated)
        var excerpt = fields["excerpt"]?.Value<string>();
        excerpt.Should().NotBeNullOrEmpty("excerpt should be derived from content");

        // Reading time computed from word count (overrides batch value)
        var readingTime = fields["reading_time_minutes"]?.Value<int>();
        readingTime.Should().BeGreaterOrEqualTo(1);

        // SEO group derived from title + content
        var seo = fields["seo_metadata"] as JObject;
        seo.Should().NotBeNull("SEO group should be derived");
        seo!["meta_title"]?.Value<string>().Should().Be("The Future of Edge Computing");

        // MediaSourceBase64 field must not be generated
        fields["featured_image"].Should().BeNull("MediaSourceBase64 cannot be generated by LLM");

        // Conversation log must exist with multiple entries
        result.Conversation.Should().HaveCountGreaterThan(3,
            "generation should produce multiple conversation entries");
    }

    [Fact]
    public async Task BlogPost_ScheduledStatus_GeneratesConditionalScheduledDate()
    {
        InitializeSampleConfiguration();

        var batchJson = new JObject
        {
            ["status"] = "scheduled",
            ["is_featured"] = false,
            ["allow_comments"] = true
        }.ToString();

        SetupAsyncBatchMock(batchJson, msg =>
        {
            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                return "Upcoming Launch Announcement";

            if (msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase))
                return "<p>We are thrilled to announce our upcoming product launch event scheduled " +
                       "for next quarter. This release represents months of engineering effort and " +
                       "introduces groundbreaking features that will transform how teams collaborate " +
                       "on distributed systems. Stay tuned for more details on the keynote speakers.</p>";

            // scheduled_date conditional field — return a date
            if (msg.Contains("date", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("YYYY-MM-DD", StringComparison.OrdinalIgnoreCase))
                return "2026-09-15";

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "Write about our upcoming launch", CancellationToken.None);

        result.Fields.Should().NotBeNull();
        var fields = result.Fields!;

        fields["status"]!.Value<string>().Should().Be("scheduled");

        // Display condition: status == 'scheduled' → scheduled_date should be generated
        var scheduledDate = fields["scheduled_date"]?.Value<string>();
        scheduledDate.Should().NotBeNullOrEmpty(
            "scheduled_date must be generated when status is 'scheduled' (display condition met)");
    }

    [Fact]
    public async Task BlogPost_DraftStatus_OmitsScheduledDate()
    {
        InitializeSampleConfiguration();

        var batchJson = new JObject
        {
            ["status"] = "draft",
            ["is_featured"] = false,
            ["allow_comments"] = true
        }.ToString();

        SetupAsyncBatchMock(batchJson, msg =>
        {
            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                return "Draft Ideas for Q3";

            if (msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase))
                return "<p>Brainstorming initial concepts for our third quarter content strategy " +
                       "focusing on developer education, community building, and technical deep-dives " +
                       "into distributed systems patterns, microservices resilience, and API design best " +
                       "practices that our engineering team has refined over the past year.</p>";

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "Draft ideas for Q3 content", CancellationToken.None);

        result.Fields.Should().NotBeNull();

        // Display condition: status == 'scheduled' NOT met → scheduled_date should NOT be generated
        result.Fields!["scheduled_date"].Should().BeNull(
            "scheduled_date display condition requires status=='scheduled', but status is 'draft'");
    }

    [Fact]
    public async Task BlogPost_BatchJsonInMarkdownFences_ExtractedCorrectly()
    {
        InitializeSampleConfiguration();

        // Wrap the batch JSON in markdown code fences — common LLM behavior
        var batchJson = "```json\n" + new JObject
        {
            ["status"] = "published",
            ["is_featured"] = false,
            ["allow_comments"] = true
        }.ToString() + "\n```";

        SetupAsyncBatchMock(batchJson, msg =>
        {
            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                return "Markdown Fence Handling";

            if (msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase))
                return "<p>Testing that the generation pipeline correctly extracts JSON from " +
                       "markdown code fences, which many language models emit by default when " +
                       "asked to produce structured output in their completion responses.</p>";

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "Test markdown fence handling", CancellationToken.None);

        result.Fields.Should().NotBeNull();
        result.Fields!["status"]!.Value<string>().Should().Be("published",
            "batch JSON wrapped in markdown fences should be parsed correctly");
    }

    [Fact]
    public async Task BlogPost_BatchJsonMalformed_FallsBackToFieldByField()
    {
        InitializeSampleConfiguration();

        SetupAsyncMock(msg =>
        {
            // Return malformed JSON for batch request
            if (msg.Contains("Fill in ALL", StringComparison.OrdinalIgnoreCase))
                return "Sure! Here are the fields: {status: published, oops this isn't valid JSON";

            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                return "Fallback Test Post";

            // Fallback: field-by-field asks individual questions
            if (msg.Contains("Pick one", StringComparison.OrdinalIgnoreCase))
            {
                if (msg.Contains("draft", StringComparison.OrdinalIgnoreCase))
                    return "published";
                return "SKIP";
            }

            if (msg.Contains("yes or no", StringComparison.OrdinalIgnoreCase))
                return "yes";

            if (msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase))
                return "<p>Content generated after the batch JSON fallback occurred, proving " +
                       "that the pipeline gracefully degrades to field-by-field generation " +
                       "when the LLM returns invalid structured output.</p>";

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "Test batch fallback", CancellationToken.None);

        // Should still produce output despite batch failure
        result.Fields.Should().NotBeNull("should fall back to field-by-field on batch failure");
        result.Fields!["title"]!.Value<string>().Should().Be("Fallback Test Post");
    }

    [Fact]
    public async Task BlogPost_TitleFallback_UsesUserPromptWhenLlmSkips()
    {
        InitializeSampleConfiguration();

        SetupAsyncMock(msg =>
        {
            // LLM returns SKIP for title
            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                return "SKIP";

            if (msg.Contains("Fill in ALL", StringComparison.OrdinalIgnoreCase))
                return new JObject { ["status"] = "draft" }.ToString();

            if (msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase))
                return "<p>When the language model cannot produce a title, the system falls back " +
                       "to capitalizing the user's original prompt as a reasonable default title " +
                       "that preserves the user's intent while maintaining proper formatting.</p>";

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "edge computing in healthcare", CancellationToken.None);

        result.Fields.Should().NotBeNull();
        // Fallback: CultureInfo.ToTitleCase(prompt.ToLowerInvariant())
        result.Fields!["title"]!.Value<string>().Should().Be("Edge Computing In Healthcare");
    }

    [Fact]
    public async Task BlogPost_PostProcessing_SlugAndExcerptDerived()
    {
        InitializeSampleConfiguration();

        SetupAsyncBatchMock(
            new JObject { ["status"] = "draft", ["is_featured"] = false, ["allow_comments"] = true }.ToString(),
            msg =>
            {
                if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return "Kubernetes Best Practices: A Guide";

                if (msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase))
                    return "<p>Container orchestration with Kubernetes requires careful attention to " +
                           "resource limits, pod disruption budgets, health probes, and namespace isolation. " +
                           "This guide covers the essential patterns every platform engineer should master " +
                           "when deploying production workloads to managed Kubernetes clusters.</p>";

                return "SKIP";
            });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "Kubernetes best practices guide", CancellationToken.None);

        result.Fields.Should().NotBeNull();

        // Slug derived from title (not LLM-generated)
        result.Fields!["slug"]!.Value<string>().Should().Be("kubernetes-best-practices-a-guide");

        // Excerpt derived from content (not LLM-generated)
        var excerpt = result.Fields["excerpt"]?.Value<string>();
        excerpt.Should().NotBeNullOrEmpty();
        excerpt.Should().Contain("Container orchestration",
            "excerpt should be derived from content text, not generated by LLM");
    }

    // ════════════════════════════════════════════════════════════════
    // Event — Display conditions, groups, repeaters
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Event_OfflineEvent_GeneratesVenueGroup()
    {
        InitializeSampleConfiguration();

        var batchJson = new JObject
        {
            ["event_type"] = "conference",
            ["is_online"] = false,
            ["max_attendees"] = 500,
            ["ticket_price"] = 150
        }.ToString();

        SetupAsyncBatchMock(batchJson, msg =>
        {
            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                return "Global AI Summit 2026";

            if (msg.Contains("Event Description", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase))
                return "<p>The Global AI Summit brings together researchers, practitioners, and " +
                       "industry leaders from around the world to explore artificial intelligence " +
                       "breakthroughs, responsible AI governance frameworks, and practical deployment " +
                       "strategies for enterprise organizations across diverse industry verticals.</p>";

            // Venue group fields (is_online=false → venue group display condition met)
            if (msg.Contains("Venue Name", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("venue_name", StringComparison.OrdinalIgnoreCase))
                return "Moscone Center";

            if (msg.Contains("capacity", StringComparison.OrdinalIgnoreCase))
                return "2000";

            // Address sub-group
            if (msg.Contains("street", StringComparison.OrdinalIgnoreCase))
                return "747 Howard Street";
            if (msg.Contains("city", StringComparison.OrdinalIgnoreCase))
                return "San Francisco";
            if (msg.Contains("country", StringComparison.OrdinalIgnoreCase))
                return "United States";

            // Date fields
            if (msg.Contains("date", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("YYYY-MM-DD", StringComparison.OrdinalIgnoreCase))
                return "2026-11-15";

            // Email
            if (msg.Contains("email", StringComparison.OrdinalIgnoreCase))
                return "info@aisummit2026.org";

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "event", "A large AI conference in San Francisco", CancellationToken.None);

        result.Fields.Should().NotBeNull();
        var fields = result.Fields!;

        fields["title"]!.Value<string>().Should().Be("Global AI Summit 2026");
        fields["event_type"]!.Value<string>().Should().Be("conference");
        fields["is_online"]!.Value<bool>().Should().BeFalse();

        // Display condition: is_online == false → venue group should exist
        var venue = fields["venue"] as JObject;
        venue.Should().NotBeNull("venue group should be generated when is_online is false");

        // meeting_url should NOT be generated (is_online == false)
        fields["meeting_url"].Should().BeNull(
            "meeting_url display condition requires is_online==true");
    }

    [Fact]
    public async Task Event_OnlineEvent_SkipsVenue()
    {
        InitializeSampleConfiguration();

        var batchJson = new JObject
        {
            ["event_type"] = "webinar",
            ["is_online"] = true,
            ["max_attendees"] = 1000,
            ["ticket_price"] = 0
        }.ToString();

        SetupAsyncBatchMock(batchJson, msg =>
        {
            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                return "Monthly AI Research Webinar";

            if (msg.Contains("Event Description", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase))
                return "<p>Join our monthly research webinar series covering the latest advances " +
                       "in transformer architectures, reinforcement learning from human feedback, " +
                       "and synthetic data generation techniques used by leading research labs.</p>";

            if (msg.Contains("date", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("YYYY-MM-DD", StringComparison.OrdinalIgnoreCase))
                return "2026-06-01";

            if (msg.Contains("email", StringComparison.OrdinalIgnoreCase))
                return "webinars@airesearch.org";

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "event", "Monthly AI research webinar series", CancellationToken.None);

        result.Fields.Should().NotBeNull();
        var fields = result.Fields!;

        fields["is_online"]!.Value<bool>().Should().BeTrue();
        fields["event_type"]!.Value<string>().Should().Be("webinar");

        // venue must NOT exist when is_online is true
        fields["venue"].Should().BeNull(
            "venue display condition requires is_online==false, but is_online is true");
    }

    [Fact]
    public async Task Event_SessionRepeater_GeneratesItems()
    {
        InitializeSampleConfiguration();

        var batchJson = new JObject
        {
            ["event_type"] = "workshop",
            ["is_online"] = true,
            ["max_attendees"] = 50,
            ["ticket_price"] = 75
        }.ToString();

        SetupAsyncBatchMock(batchJson, msg =>
        {
            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase)
                && !msg.Contains("Session", StringComparison.OrdinalIgnoreCase))
                return "Hands-On ML Workshop";

            if (msg.Contains("Event Description", StringComparison.OrdinalIgnoreCase)
                || (msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                    && !msg.Contains("Session", StringComparison.OrdinalIgnoreCase)))
                return "<p>An intensive hands-on workshop covering practical machine learning " +
                       "workflows from data preparation through model training, evaluation, and " +
                       "deployment using modern MLOps toolchains and cloud infrastructure.</p>";

            // Session repeater fields
            if (msg.Contains("Session Title", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("session_title", StringComparison.OrdinalIgnoreCase))
                return "Introduction to Neural Networks";

            if (msg.Contains("Speaker Name", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("speaker_name", StringComparison.OrdinalIgnoreCase))
                return "Dr. Sarah Chen";

            if (msg.Contains("Duration", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("duration_minutes", StringComparison.OrdinalIgnoreCase))
                return "90";

            if (msg.Contains("session_type", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Pick one", StringComparison.OrdinalIgnoreCase))
            {
                if (msg.Contains("keynote", StringComparison.OrdinalIgnoreCase))
                    return "workshop";
                return "SKIP";
            }

            if (msg.Contains("Session Description", StringComparison.OrdinalIgnoreCase))
                return "A foundational session covering the building blocks of deep learning " +
                       "architectures with practical coding exercises using PyTorch and TensorFlow.";

            if (msg.Contains("date", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("YYYY-MM-DD", StringComparison.OrdinalIgnoreCase))
                return "2026-08-20";

            if (msg.Contains("email", StringComparison.OrdinalIgnoreCase))
                return "workshop@mlconf.io";

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "event", "Machine learning workshop with sessions", CancellationToken.None);

        result.Fields.Should().NotBeNull();

        // Sessions repeater should have items (no URL fields in EventSessionModel → not skipped)
        var sessions = result.Fields!["sessions"] as JArray;
        sessions.Should().NotBeNull("sessions repeater should be generated (no URL fields)");
        sessions!.Count.Should().BeGreaterThan(0);

        // Each session item should have some fields
        var firstSession = sessions[0] as JObject;
        firstSession.Should().NotBeNull();
        firstSession!.Count.Should().BeGreaterThan(0, "session items should have generated fields");

        // Sponsors repeater should be skipped (has sponsor_url which is a URL field)
        result.Fields["sponsors"].Should().BeNull(
            "sponsors repeater contains URL fields and should be skipped");
    }

    // ════════════════════════════════════════════════════════════════
    // Objective — Content fields, key_results repeater
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Objective_GeneratesStructuredFieldsAndContent()
    {
        InitializeSampleConfiguration();

        var batchJson = new JObject
        {
            ["objective_type"] = "short_term"
        }.ToString();

        SetupAsyncBatchMock(batchJson, msg =>
        {
            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase)
                && !msg.Contains("Key Result", StringComparison.OrdinalIgnoreCase))
                return "Reduce Mean Time to Recovery";

            // root_cause is a TextArea (content field)
            if (msg.Contains("Root Cause", StringComparison.OrdinalIgnoreCase)
                || (msg.Contains("plain text", StringComparison.OrdinalIgnoreCase)
                    && !msg.Contains("Key Result", StringComparison.OrdinalIgnoreCase)))
                return "Incident response procedures lack automation and clear escalation paths. " +
                       "Teams spend excessive time diagnosing issues due to insufficient observability " +
                       "tooling and scattered runbooks that are outdated and inconsistently maintained.";

            // Key results repeater fields
            if (msg.Contains("key_result", StringComparison.OrdinalIgnoreCase)
                || (msg.Contains("plain text", StringComparison.OrdinalIgnoreCase)
                    && msg.Contains("Key Result", StringComparison.OrdinalIgnoreCase)))
                return "Implement automated incident detection with PagerDuty integration reducing " +
                       "MTTR from 45 minutes to under 15 minutes by end of quarter.";

            if (msg.Contains("yes or no", StringComparison.OrdinalIgnoreCase))
                return "no";

            if (msg.Contains("date", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("YYYY-MM-DD", StringComparison.OrdinalIgnoreCase))
                return "2026-04-01";

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "objective", "Reduce incident response time for production systems", CancellationToken.None);

        result.Fields.Should().NotBeNull();
        var fields = result.Fields!;

        fields["title"]!.Value<string>().Should().Be("Reduce Mean Time to Recovery");
        fields["objective_type"]!.Value<string>().Should().Be("short_term");

        // root_cause is a TextArea content field — should contain the LLM response
        var rootCause = fields["root_cause"]?.Value<string>();
        rootCause.Should().NotBeNullOrEmpty("root_cause TextArea should be generated");
        rootCause!.Should().Contain("Incident",
            "root_cause should contain topic-relevant content");

        // key_results repeater should have items
        var keyResults = fields["key_results"] as JArray;
        keyResults.Should().NotBeNull("key_results repeater should be generated");
        keyResults!.Count.Should().BeGreaterThan(0);
    }

    // ════════════════════════════════════════════════════════════════
    // Survey — Deep nesting (3 levels), complex display conditions
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Survey_GeneratesTopLevelAndSectionRepeater()
    {
        InitializeSampleConfiguration();

        var batchJson = new JObject
        {
            ["is_anonymous"] = false,
            ["survey_status"] = "draft"
        }.ToString();

        SetupAsyncBatchMock(batchJson, msg =>
        {
            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase)
                && !msg.Contains("Section", StringComparison.OrdinalIgnoreCase))
                return "Employee Engagement Survey 2026";

            // survey_description TextArea
            if (msg.Contains("Survey Description", StringComparison.OrdinalIgnoreCase)
                || (msg.Contains("plain text", StringComparison.OrdinalIgnoreCase)
                    && !msg.Contains("Section", StringComparison.OrdinalIgnoreCase)
                    && !msg.Contains("Question", StringComparison.OrdinalIgnoreCase)))
                return "An annual survey to measure employee satisfaction, engagement levels, " +
                       "and workplace culture across all departments and geographic locations.";

            // response_limit conditional (is_anonymous == false → visible)
            if (msg.Contains("number", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Response Limit", StringComparison.OrdinalIgnoreCase))
                return "500";

            // Date
            if (msg.Contains("date", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("YYYY-MM-DD", StringComparison.OrdinalIgnoreCase))
                return "2026-12-31";

            // Section repeater fields
            if (msg.Contains("Section Title", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("section_title", StringComparison.OrdinalIgnoreCase))
                return "Work Environment";

            if (msg.Contains("Section Description", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("section_description", StringComparison.OrdinalIgnoreCase))
                return "Questions about your physical and virtual workspace setup.";

            if (msg.Contains("yes or no", StringComparison.OrdinalIgnoreCase))
                return "no";

            // Question repeater fields
            if (msg.Contains("Question Text", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("question_text", StringComparison.OrdinalIgnoreCase))
                return "How satisfied are you with your current workspace and remote work tools?";

            if (msg.Contains("question_type", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Pick one", StringComparison.OrdinalIgnoreCase))
            {
                if (msg.Contains("text", StringComparison.OrdinalIgnoreCase)
                    && msg.Contains("choice", StringComparison.OrdinalIgnoreCase))
                    return "rating";
                return "SKIP";
            }

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "survey", "Annual employee engagement survey", CancellationToken.None);

        result.Fields.Should().NotBeNull();
        var fields = result.Fields!;

        fields["title"]!.Value<string>().Should().Be("Employee Engagement Survey 2026");
        fields["is_anonymous"]!.Value<bool>().Should().BeFalse();

        // sections repeater (min 1) should have items
        var sections = fields["sections"] as JArray;
        sections.Should().NotBeNull("sections repeater with min=1 should be generated");
        sections!.Count.Should().BeGreaterThan(0);
    }

    // ════════════════════════════════════════════════════════════════
    // FieldByField strategy — legacy path
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BlogPost_FieldByField_GeneratesWithoutBatchCall()
    {
        InitializeSampleConfiguration(AiGenerationStrategy.FieldByField);

        var capturedMessages = new List<string>();

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken _) =>
            {
                await Task.Yield();
                var lastUserMsg = req.Messages.LastOrDefault(m => m.Role == LLMRole.User)?.Content ?? "";
                capturedMessages.Add(lastUserMsg);

                if (lastUserMsg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "Legacy Mode Blog Post",
                        FinishReason = LLMFinishReason.Stop
                    });

                if (lastUserMsg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                    || lastUserMsg.Contains("Post Content", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "<p>Content generated using the legacy field-by-field strategy " +
                                  "where each field gets its own isolated LLM call with a focused prompt.</p>",
                        FinishReason = LLMFinishReason.Stop
                    });

                if (lastUserMsg.Contains("Pick one", StringComparison.OrdinalIgnoreCase)
                    && lastUserMsg.Contains("draft", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "draft",
                        FinishReason = LLMFinishReason.Stop
                    });

                if (lastUserMsg.Contains("yes or no", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "no",
                        FinishReason = LLMFinishReason.Stop
                    });

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "SKIP",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "Test legacy field by field", CancellationToken.None);

        result.Fields.Should().NotBeNull();
        result.Fields!["title"]!.Value<string>().Should().Be("Legacy Mode Blog Post");

        // FieldByField should NOT use batch ("Fill in ALL") call
        capturedMessages.Should().NotContain(m => m.Contains("Fill in ALL"),
            "FieldByField strategy should not send batch requests");
    }

    // ════════════════════════════════════════════════════════════════
    // Async safety — concurrent generation (catches ThreadStatic bugs)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentGeneration_AsyncLocalFlowsCorrectly()
    {
        InitializeSampleConfiguration();

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken _) =>
            {
                var count = Interlocked.Increment(ref callCount);
                // Add varying delays to increase chance of thread interleaving
                await Task.Yield();
                if (count % 3 == 0) await Task.Delay(1);

                var lastUserMsg = req.Messages.LastOrDefault(m => m.Role == LLMRole.User)?.Content ?? "";

                if (lastUserMsg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = $"Title {count}",
                        FinishReason = LLMFinishReason.Stop
                    });

                if (lastUserMsg.Contains("Fill in ALL", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = new JObject
                        {
                            ["event_type"] = "conference",
                            ["is_online"] = false,
                            ["max_attendees"] = count * 100,
                            ["ticket_price"] = count * 10
                        }.ToString(),
                        FinishReason = LLMFinishReason.Stop
                    });

                if (lastUserMsg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                    || lastUserMsg.Contains("Event Description", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = $"<p>Content for request {count}. This is a unique piece of text " +
                                  $"generated specifically for concurrent execution test number {count} " +
                                  "to verify that AsyncLocal properly isolates generation context.</p>",
                        FinishReason = LLMFinishReason.Stop
                    });

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "SKIP",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        // Run two generations concurrently — this would NullRef with [ThreadStatic]
        var task1 = AiEntityGenerator.GenerateAsync(
            "event", "Conference about AI", CancellationToken.None);
        var task2 = AiEntityGenerator.GenerateAsync(
            "event", "Conference about cloud", CancellationToken.None);

        var results = await Task.WhenAll(task1, task2);

        // Both should complete without NullReferenceException
        results[0].Fields.Should().NotBeNull("first concurrent generation should succeed");
        results[1].Fields.Should().NotBeNull("second concurrent generation should succeed");

        // Both should have event_type from their batch responses
        results[0].Fields!["event_type"]!.Value<string>().Should().Be("conference");
        results[1].Fields!["event_type"]!.Value<string>().Should().Be("conference");
    }

    // ════════════════════════════════════════════════════════════════
    // Error handling
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LlmReturnsFailure_GenerationHandlesGracefully()
    {
        InitializeSampleConfiguration();

        var callIndex = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken _) =>
            {
                await Task.Yield();
                var idx = Interlocked.Increment(ref callIndex);

                // Let title succeed so we get a result object
                if (idx == 1)
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "Failure Test Post",
                        FinishReason = LLMFinishReason.Stop
                    });

                // All subsequent calls fail
                return OperationResult<LLMResponse>.Failure(
                    "Service unavailable", HttpStatusCode.InternalServerError);
            });

        // Should not throw — handles LLM failures gracefully
        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "Test LLM failure handling", CancellationToken.None);

        // May return null or a partial result, but should NOT throw
        if (result.Fields != null)
        {
            result.Fields["title"]!.Value<string>().Should().Be("Failure Test Post");
        }
    }

    [Fact]
    public async Task LlmReturnsEmpty_AllCalls_GenerationStillReturns()
    {
        InitializeSampleConfiguration();

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken _) =>
            {
                await Task.Yield();
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        // All-empty responses: should not throw, title should be fallback from prompt
        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "empty response test", CancellationToken.None);

        // Should not throw — title falls back to capitalized prompt
        if (result.Fields != null)
        {
            result.Fields["title"]!.Value<string>().Should().Be("Empty Response Test");
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Prompt verification — what actually gets sent to the LLM
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BlogPost_PromptEngineering_BatchIncludesFieldSchemas()
    {
        InitializeSampleConfiguration();

        var capturedBatchPrompt = "";

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken _) =>
            {
                await Task.Yield();
                var lastUserMsg = req.Messages.LastOrDefault(m => m.Role == LLMRole.User)?.Content ?? "";

                if (lastUserMsg.Contains("Fill in ALL", StringComparison.OrdinalIgnoreCase))
                {
                    capturedBatchPrompt = lastUserMsg;
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = new JObject { ["status"] = "draft" }.ToString(),
                        FinishReason = LLMFinishReason.Stop
                    });
                }

                if (lastUserMsg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "Prompt Inspection Post",
                        FinishReason = LLMFinishReason.Stop
                    });

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "SKIP",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        await AiEntityGenerator.GenerateAsync(
            "blog-post", "Test prompt structure", CancellationToken.None);

        // The batch prompt should include field names and type info
        capturedBatchPrompt.Should().NotBeEmpty("batch prompt should have been captured");
        capturedBatchPrompt.Should().Contain("status",
            "batch prompt should include the status field");
        capturedBatchPrompt.Should().Contain("JSON",
            "batch prompt should ask for JSON output");

        // Select field choices should be included in the schema description
        capturedBatchPrompt.Should().Contain("draft",
            "batch prompt should include select choice values");
    }

    [Fact]
    public async Task BlogPost_ContentPrompt_IncludesHtmlHint()
    {
        InitializeSampleConfiguration();

        var capturedContentPrompt = "";

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken _) =>
            {
                await Task.Yield();
                var lastUserMsg = req.Messages.LastOrDefault(m => m.Role == LLMRole.User)?.Content ?? "";

                if (lastUserMsg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "Content Prompt Test",
                        FinishReason = LLMFinishReason.Stop
                    });

                if (lastUserMsg.Contains("Fill in ALL", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = new JObject { ["status"] = "draft" }.ToString(),
                        FinishReason = LLMFinishReason.Stop
                    });

                if (lastUserMsg.Contains("Post Content", StringComparison.OrdinalIgnoreCase)
                    || lastUserMsg.Contains("HTML content", StringComparison.OrdinalIgnoreCase))
                {
                    capturedContentPrompt = lastUserMsg;
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "<p>HTML-formatted content testing prompt engineering correctness " +
                                  "for WYSIWYG editor fields that require structured HTML output.</p>",
                        FinishReason = LLMFinishReason.Stop
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "SKIP",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        await AiEntityGenerator.GenerateAsync(
            "blog-post", "Test content prompts", CancellationToken.None);

        // WYSIWYG content prompt should include HTML hint
        capturedContentPrompt.Should().NotBeEmpty("content prompt should have been captured");
        capturedContentPrompt.Should().Contain("HTML",
            "WYSIWYG content prompt should hint at HTML formatting");
    }

    [Fact]
    public async Task BlogPost_MediaFieldNeverAsked()
    {
        InitializeSampleConfiguration();

        var allPrompts = new List<string>();

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken _) =>
            {
                await Task.Yield();
                var lastUserMsg = req.Messages.LastOrDefault(m => m.Role == LLMRole.User)?.Content ?? "";
                allPrompts.Add(lastUserMsg);

                if (lastUserMsg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "Media Skip Test",
                        FinishReason = LLMFinishReason.Stop
                    });

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "SKIP",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        await AiEntityGenerator.GenerateAsync(
            "blog-post", "Test media field skipping", CancellationToken.None);

        var allText = string.Join(" ", allPrompts);
        allText.Should().NotContain("featured_image",
            "MediaSourceBase64 fields should never be sent to the LLM");
        allText.Should().NotContain("banner_image",
            "image fields should be completely skipped");
    }

    // ════════════════════════════════════════════════════════════════
    // Validation and auto-fix
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BlogPost_NumberClamping_RangeEnforced()
    {
        InitializeSampleConfiguration();

        // Batch returns reading_time_minutes = 999 (way above max 120)
        var batchJson = new JObject
        {
            ["status"] = "draft",
            ["is_featured"] = false,
            ["allow_comments"] = true,
            ["reading_time_minutes"] = 999
        }.ToString();

        SetupAsyncBatchMock(batchJson, msg =>
        {
            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                return "Number Clamping Test";

            if (msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase))
                return "<p>Testing that numeric fields outside their valid range are automatically " +
                       "clamped by the validation auto-fix layer during post-generation processing.</p>";

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "Number clamping test", CancellationToken.None);

        result.Fields.Should().NotBeNull();

        // reading_time_minutes is derived from content word count (PostProcessFields),
        // so the batch value of 999 gets overwritten with the computed value
        var readingTime = result.Fields!["reading_time_minutes"]?.Value<int>();
        readingTime.Should().NotBeNull();
        readingTime.Should().BeInRange(1, 120, "reading time should be within valid range [1, 120]");
    }

    [Fact]
    public async Task BlogPost_SelectValidation_InvalidChoiceFallsBack()
    {
        InitializeSampleConfiguration();

        // Batch returns an invalid status choice
        var batchJson = new JObject
        {
            ["status"] = "ready_for_review",  // Not a valid choice
            ["is_featured"] = false,
            ["allow_comments"] = true
        }.ToString();

        SetupAsyncBatchMock(batchJson, msg =>
        {
            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                return "Invalid Select Test";

            if (msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase))
                return "<p>Testing that select fields with values not matching any known choice " +
                       "fall back to the default value or first available choice option.</p>";

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "Select validation test", CancellationToken.None);

        result.Fields.Should().NotBeNull();

        // "ready_for_review" is not a valid choice → ParseFieldValue rejects it.
        // The field either won't be set, or the auto-fix will correct it.
        var status = result.Fields!["status"]?.Value<string>();
        if (status != null)
        {
            status.Should().BeOneOf("draft", "published", "scheduled", "archived",
                "if status is present, it must be a valid choice");
        }
        // Either way, the invalid value "ready_for_review" should NOT be in the output
        status.Should().NotBe("ready_for_review",
            "invalid select values should never appear in the final result");
    }

    [Fact]
    public async Task BlogPost_CheckboxCoercion_StringToBool()
    {
        InitializeSampleConfiguration();

        // Batch returns booleans as strings — LLM sometimes does this
        var batchJson = new JObject
        {
            ["status"] = "draft",
            ["is_featured"] = "true",       // string, not bool
            ["allow_comments"] = "false"     // string, not bool
        }.ToString();

        SetupAsyncBatchMock(batchJson, msg =>
        {
            if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                return "Checkbox Coercion Test";

            if (msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase))
                return "<p>Testing that string representations of boolean values are properly " +
                       "coerced to actual booleans by the validation and auto-fix pipeline.</p>";

            return "SKIP";
        });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "Checkbox coercion test", CancellationToken.None);

        result.Fields.Should().NotBeNull();
        // String "true"/"false" should be coerced to actual booleans
        result.Fields!["is_featured"]!.Type.Should().Be(JTokenType.Boolean);
        result.Fields["allow_comments"]!.Type.Should().Be(JTokenType.Boolean);
    }

    // ════════════════════════════════════════════════════════════════
    // Edge cases
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EdgeCase_VeryLongPrompt_HandledGracefully()
    {
        InitializeSampleConfiguration();

        var longPrompt = string.Join(", ",
            Enumerable.Range(1, 50).Select(i => $"topic number {i} about different subjects"));

        SetupAsyncBatchMock(
            new JObject { ["status"] = "draft", ["is_featured"] = false, ["allow_comments"] = true }.ToString(),
            msg =>
            {
                if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return "Multi-Topic Article";

                if (msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase))
                    return "<p>Covering a wide range of topics as specified in the very long prompt. " +
                           "Enterprise systems must handle arbitrarily long user inputs gracefully " +
                           "without crashing or silently truncating important context.</p>";

                return "SKIP";
            });

        // Should not throw or crash with a very long prompt
        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", longPrompt, CancellationToken.None);

        result.Fields.Should().NotBeNull();
        result.Fields!["title"].Should().NotBeNull();
    }

    [Fact]
    public async Task EdgeCase_UnicodePrompt_HandledCorrectly()
    {
        InitializeSampleConfiguration();

        SetupAsyncBatchMock(
            new JObject { ["status"] = "draft" }.ToString(),
            msg =>
            {
                if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return "AIの最新トレンド";

                if (msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase))
                    return "<p>この記事は、AI生成テストにおけるUnicode文字の処理をテストするものです。" +
                           "日本語、中国語、韓国語などの多言語テキストを適切に処理できることを確認します。" +
                           "マルチバイト文字の長さ計算やスラグ生成が正しく動作するかを検証します。</p>";

                return "SKIP";
            });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "AIの最新トレンドについて書いてください", CancellationToken.None);

        result.Fields.Should().NotBeNull();
        result.Fields!["title"]!.Value<string>().Should().Contain("AI");
    }

    [Fact]
    public async Task EdgeCase_ContentEchoesTitle_Rejected()
    {
        InitializeSampleConfiguration();

        SetupAsyncBatchMock(
            new JObject { ["status"] = "draft", ["is_featured"] = false, ["allow_comments"] = true }.ToString(),
            msg =>
            {
                if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return "Understanding GraphQL APIs";

                // Return content that just echoes the title — should be rejected
                if (msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase))
                    return "Understanding GraphQL APIs";

                return "SKIP";
            });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "Write about GraphQL", CancellationToken.None);

        result.Fields.Should().NotBeNull();

        // Content that echoes the title should be rejected by quality checks
        var content = result.Fields!["content"]?.Value<string>();
        if (content != null)
        {
            content.Should().NotBe("Understanding GraphQL APIs",
                "content that merely echoes the title should be rejected");
        }
    }

    [Fact]
    public async Task EdgeCase_RepetitiveGarbage_Rejected()
    {
        InitializeSampleConfiguration();

        SetupAsyncBatchMock(
            new JObject { ["status"] = "draft" }.ToString(),
            msg =>
            {
                if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return "Garbage Test";

                // Return repetitive text — should be rejected by unique word ratio check
                if (msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase))
                    return "<p>" + string.Join(" ", Enumerable.Repeat("test test test garbage", 30)) + "</p>";

                return "SKIP";
            });

        var result = await AiEntityGenerator.GenerateAsync(
            "blog-post", "Test repetitive content rejection", CancellationToken.None);

        // Generation should still complete (not throw)
        result.Fields.Should().NotBeNull();

        // Content should be null (rejected) or replaced by fallback
        var content = result.Fields!["content"]?.Value<string>();
        if (content != null)
        {
            // If a fallback produced content, it should not be the repetitive garbage
            content.Should().NotContain("test test test garbage");
        }
    }

    [Fact]
    public async Task Cancellation_MidGeneration_DoesNotThrowNullRef()
    {
        InitializeSampleConfiguration();

        var callCount = 0;
        using var cts = new CancellationTokenSource();

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken ct) =>
            {
                await Task.Yield();
                var count = Interlocked.Increment(ref callCount);

                // Cancel after 3rd call
                if (count >= 3)
                    await cts.CancelAsync();

                ct.ThrowIfCancellationRequested();

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = count == 1 ? "Cancellation Test" : "SKIP",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        // Should throw OperationCanceledException, NOT NullReferenceException
        Func<Task> act = () => AiEntityGenerator.GenerateAsync(
            "blog-post", "Test cancellation", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "cancellation should propagate cleanly, not cause NullReferenceException");
    }

    [Fact]
    public async Task NonAiEntity_ThrowsOrFails()
    {
        InitializeSampleConfiguration();

        // product does not have SupportsAiGeneration set
        var act = async () => await AiEntityGenerator.GenerateAsync(
            "product", "Create a product", CancellationToken.None);

        await act.Should().ThrowAsync<Exception>(
            "entities without SupportsAiGeneration should not be generable");
    }

    // ════════════════════════════════════════════════════════════════
    // Relation and URL fields — skipping
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Event_RelationFieldNeverAsked()
    {
        InitializeSampleConfiguration();

        var allPrompts = new List<string>();

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken _) =>
            {
                await Task.Yield();
                var lastUserMsg = req.Messages.LastOrDefault(m => m.Role == LLMRole.User)?.Content ?? "";
                allPrompts.Add(lastUserMsg);

                if (lastUserMsg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "Relation Skip Test Event",
                        FinishReason = LLMFinishReason.Stop
                    });

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "SKIP",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        await AiEntityGenerator.GenerateAsync(
            "event", "Test relation field skipping", CancellationToken.None);

        var allText = string.Join(" ", allPrompts);
        allText.Should().NotContain("event_coordinator",
            "Relation fields should not be asked to the LLM");
    }

    [Fact]
    public async Task Event_SponsorsRepeaterSkipped_DueToUrlField()
    {
        InitializeSampleConfiguration();

        SetupAsyncBatchMock(
            new JObject
            {
                ["event_type"] = "summit",
                ["is_online"] = true,
                ["max_attendees"] = 200,
                ["ticket_price"] = 0
            }.ToString(),
            msg =>
            {
                if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return "URL Repeater Skip Test";

                if (msg.Contains("Event Description", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase))
                    return "<p>Testing that repeaters containing URL fields in their item schema " +
                           "are automatically skipped because LLMs tend to fabricate URLs.</p>";

                if (msg.Contains("date", StringComparison.OrdinalIgnoreCase))
                    return "2026-07-01";

                if (msg.Contains("email", StringComparison.OrdinalIgnoreCase))
                    return "test@example.org";

                return "SKIP";
            });

        var result = await AiEntityGenerator.GenerateAsync(
            "event", "Test URL repeater skipping", CancellationToken.None);

        result.Fields.Should().NotBeNull();

        // sponsors repeater has sponsor_url (URL field) → should be skipped entirely
        result.Fields!["sponsors"].Should().BeNull(
            "repeaters with URL fields should be skipped to avoid fabricated URLs");
    }

    // ════════════════════════════════════════════════════════════════
    // Agentic strategy — tool-calling path
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Agentic_ToolCalling_GeneratesFields()
    {
        InitializeSampleConfiguration(AiGenerationStrategy.Agentic);

        var iteration = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken _) =>
            {
                await Task.Yield();
                var iter = Interlocked.Increment(ref iteration);

                // First call: tool call to get_schema
                if (iter == 1)
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "",
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1",
                                Name = "get_schema",
                                Arguments = "{}"
                            }
                        ]
                    });

                // Second call: tool call to set_fields with actual values
                if (iter == 2)
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "",
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_2",
                                Name = "set_fields",
                                Arguments = new JObject
                                {
                                    ["fields"] = new JObject
                                    {
                                        ["event_type"] = "hackathon",
                                        ["is_online"] = true,
                                        ["max_attendees"] = 100,
                                        ["description"] = "<p>An exciting hackathon event where developers " +
                                                          "collaborate to build innovative solutions using " +
                                                          "cutting-edge AI and cloud technologies over a " +
                                                          "weekend-long coding marathon.</p>"
                                    }
                                }.ToString()
                            }
                        ]
                    });

                // Third call: final response (stop)
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Generation complete.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiEntityGenerator.GenerateAsync(
            "event", "Organize an AI hackathon", CancellationToken.None);

        result.Fields.Should().NotBeNull("agentic generation should produce a result");
        result.Fields!["event_type"]?.Value<string>().Should().Be("hackathon");
    }

    [Fact]
    public async Task Agentic_FlatStringRepeaterItems_NormalizedToObjects()
    {
        InitializeSampleConfiguration(AiGenerationStrategy.Agentic);

        var iteration = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken _) =>
            {
                await Task.Yield();
                var iter = Interlocked.Increment(ref iteration);

                if (iter == 1)
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "",
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1",
                                Name = "get_schema",
                                Arguments = "{}"
                            }
                        ]
                    });

                // LLM sends repeater items as flat strings — the bug scenario
                if (iter == 2)
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "",
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_2",
                                Name = "set_fields",
                                Arguments = new JObject
                                {
                                    ["fields"] = new JObject
                                    {
                                        ["title"] = "Buy Apartment 2027",
                                        ["objective_type"] = "long_term",
                                        ["root_cause"] = "Need stable housing for long-term financial security " +
                                                         "and family planning goals that require homeownership.",
                                        // BUG: LLM sends key_results as flat strings instead of objects
                                        ["key_results"] = new JArray(
                                            "Save $150,000 by December 2026 for down payment",
                                            "Research and shortlist three potential apartments by June 2027",
                                            "Secure mortgage pre-approval from lender by September 2027"
                                        )
                                    }
                                }.ToString()
                            }
                        ]
                    });

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Done.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiEntityGenerator.GenerateAsync(
            "objective", "Plans for 2027, buying an apartment", CancellationToken.None);

        result.Fields.Should().NotBeNull();
        var fields = result.Fields!;

        // key_results should be a JArray of JObjects, NOT flat strings
        var keyResults = fields["key_results"] as JArray;
        keyResults.Should().NotBeNull("key_results repeater should be present");
        keyResults!.Count.Should().Be(3, "all three items should be preserved");

        foreach (var item in keyResults)
        {
            item.Should().BeOfType<JObject>(
                "each repeater item must be a JObject, not a flat string");
            var obj = (JObject)item;
            obj["key_result"]?.Value<string>().Should().NotBeNullOrEmpty(
                "each key result item must have its primary text field (key_result) populated");
        }

        // Verify the actual text content is preserved
        keyResults[0]!["key_result"]!.Value<string>().Should().Contain("$150,000");
        keyResults[1]!["key_result"]!.Value<string>().Should().Contain("shortlist");
        keyResults[2]!["key_result"]!.Value<string>().Should().Contain("mortgage");
    }

    // ════════════════════════════════════════════════════════════════
    // HeavyLlm vs LightLlm — correct service routing
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Generation_UsesHeavyLlm_NotLightLlm()
    {
        InitializeSampleConfiguration();

        SetupAsyncBatchMock(
            new JObject { ["status"] = "draft" }.ToString(),
            msg =>
            {
                if (msg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return "LLM Routing Test";

                if (msg.Contains("HTML content", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("Post Content", StringComparison.OrdinalIgnoreCase))
                    return "<p>Testing that entity generation correctly routes to the heavy LLM " +
                           "service and does not accidentally use the light model.</p>";

                return "SKIP";
            });

        await AiEntityGenerator.GenerateAsync(
            "blog-post", "Test LLM routing", CancellationToken.None);

        // Heavy LLM should have been called (title + batch + content at minimum)
        _mockHeavyLlm.Verify(
            l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(3),
            "heavy LLM should be used for generation");

        // Light LLM should NOT have been called for generation
        // (light is used for embeddings and sanity checks, not generation)
        _mockLightLlm.Verify(
            l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "light LLM should not be used for entity generation");
    }

    // ════════════════════════════════════════════════════════════════
    // propose_create_entity merge — cached arrays must not be overwritten
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProposeCreate_CachedRepeaterArrays_NotOverwrittenByLlmMangledStrings()
    {
        // This test reproduces the exact production bug:
        // 1. generate_entity produces proper repeater items: [{key_result: "...", achieved: false}, ...]
        // 2. LLM calls propose_create_entity with mangled flat strings for the same field
        // 3. MergeArrayHandling.Replace used to overwrite the good cached data
        // 4. Result: every repeater item showed the same blob text
        //
        // After the fix, cached complex fields (arrays, groups) are preserved.

        InitializeSampleConfiguration();
        SetupIamCacheWithAdminRole();

        var outerCallCount = 0;

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (LLMRequest req, CancellationToken _) =>
            {
                await Task.Yield();
                var lastUserMsg = req.Messages.LastOrDefault(m => m.Role == LLMRole.User)?.Content ?? "";

                // Outer ChatAsync loop sends tool definitions (generate_entity, propose_create_entity, etc.)
                // Inner AiEntityGenerator calls do NOT send tools (Auto/FieldByField strategy).
                var isOuterChatLoop = req.Tools is { Count: > 0 };

                if (isOuterChatLoop)
                {
                    var outerIdx = Interlocked.Increment(ref outerCallCount);

                    if (outerIdx == 1)
                    {
                        // First turn: LLM calls generate_entity
                        return OperationResult<LLMResponse>.Success(new LLMResponse
                        {
                            Content = null,
                            FinishReason = LLMFinishReason.ToolCall,
                            ToolCalls =
                            [
                                new LLMToolCall
                                {
                                    Id = "call_gen",
                                    Name = "generate_entity",
                                    Arguments = new JObject
                                    {
                                        ["entity_type"] = "objective",
                                        ["prompt"] = "2027 housing goals"
                                    }.ToString()
                                }
                            ]
                        });
                    }

                    if (outerIdx == 2)
                    {
                        // Second turn: LLM calls propose_create_entity with MANGLED data
                        var mangledBlob = "Objective: Achieve 2027 Housing Goals\nKey Result 1: Save target amount";
                        return OperationResult<LLMResponse>.Success(new LLMResponse
                        {
                            Content = null,
                            FinishReason = LLMFinishReason.ToolCall,
                            ToolCalls =
                            [
                                new LLMToolCall
                                {
                                    Id = "call_propose",
                                    Name = "propose_create_entity",
                                    Arguments = new JObject
                                    {
                                        ["entity_type"] = "objective",
                                        ["title"] = "Achieve 2027 Housing Goals",
                                        ["fields"] = new JObject
                                        {
                                            ["objective_type"] = "long_term",
                                            // BUG: LLM sends same blob for every repeater item
                                            ["key_results"] = new JArray(mangledBlob, mangledBlob, mangledBlob),
                                            // Also mangles the group
                                            ["creator_comment"] = new JObject { ["comment"] = mangledBlob },
                                            ["objective_comments"] = new JArray(
                                                new JObject { ["comment"] = mangledBlob })
                                        }
                                    }.ToString()
                                }
                            ]
                        });
                    }

                    // Final turn
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "Proposed creating the objective. Please approve.",
                        FinishReason = LLMFinishReason.Stop
                    });
                }

                // Inner generation calls (AiEntityGenerator.GenerateAsync)
                if (lastUserMsg.Contains("title", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "Achieve 2027 Housing Goals",
                        FinishReason = LLMFinishReason.Stop
                    });

                if (lastUserMsg.Contains("Fill in ALL", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = new JObject { ["objective_type"] = "long_term" }.ToString(),
                        FinishReason = LLMFinishReason.Stop
                    });

                if (lastUserMsg.Contains("Root Cause", StringComparison.OrdinalIgnoreCase)
                    || (lastUserMsg.Contains("plain text", StringComparison.OrdinalIgnoreCase)
                        && !lastUserMsg.Contains("Key Result", StringComparison.OrdinalIgnoreCase)))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "Lack of structured savings plan and real estate market knowledge " +
                                  "preventing timely apartment acquisition.",
                        FinishReason = LLMFinishReason.Stop
                    });

                if (lastUserMsg.Contains("Key Result", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "Save $50,000 for down payment by December 2027",
                        FinishReason = LLMFinishReason.Stop
                    });

                if (lastUserMsg.Contains("yes or no", StringComparison.OrdinalIgnoreCase))
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "no",
                        FinishReason = LLMFinishReason.Stop
                    });

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "SKIP",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var user = new EntityModel<UserEntityFieldsModel>
        {
            Id = 1,
            Title = new TitleRenderedModel(),
            Fields = new UserEntityFieldsModel
            {
                EmailAddress = "admin@test.com",
                Roles = [new UserRoleAssignmentModel { RoleId = 10 }]
            }
        };

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Create an objective about 2027 housing goals",
                Context = new AgentContext { CurrentPage = "dashboard" }
            },
            user, CancellationToken.None);

        result.ProposedActions.Should().HaveCountGreaterThan(0,
            "should propose creating the objective");

        var action = result.ProposedActions[0];
        action.ActionType.Should().Be("create_entity");
        action.EntityType.Should().Be("objective");

        var proposedFields = action.Payload?["fields"] as JObject;
        proposedFields.Should().NotBeNull();

        // KEY ASSERTION: key_results should be the cached version (proper JObjects
        // from generate_entity), NOT the mangled flat strings from propose_create_entity
        var keyResults = proposedFields!["key_results"] as JArray;
        if (keyResults != null && keyResults.Count > 0)
        {
            foreach (var item in keyResults)
            {
                item.Should().BeOfType<JObject>(
                    "cached key_results items should be proper objects, not flat strings");
                var obj = (JObject)item;
                var text = obj["key_result"]?.Value<string>() ?? "";
                // The cached version has unique text per item, the mangled version has the same blob
                text.Should().NotStartWith("Objective:",
                    "repeater items should not contain the mangled blob from propose_create_entity");
            }
        }

        // root_cause should be the cached version (proper text from generation),
        // not the mangled blob
        var rootCause = proposedFields["root_cause"]?.Value<string>();
        if (rootCause != null)
        {
            rootCause.Should().Contain("savings plan",
                "root_cause should be the properly generated text, not the mangled blob");
        }
    }
}

// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Database.Basic;
using CrossCloudKit.File.Basic;
using CrossCloudKit.LLM.Basic.Completion;
using CrossCloudKit.LLM.Basic.Embeddings;
using CrossCloudKit.Memory.Basic;
using CrossCloudKit.PubSub.Basic;
using CrossCloudKit.Vector.Basic;
using ReflectiveForms.Core;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Sample1.Models;

namespace ReflectiveForms.Sample1;

public static class RfBuilder
{
    public static RfConfigurationBuilder Build(ILogger logger)
    {
        var pubSubService = new PubSubServiceBasic();
        var memoryService = new MemoryServiceBasic(pubSubService);
        var fileService = new FileServiceBasic(memoryService, pubSubService);
        var dbService = new DatabaseServiceBasic("reflective-forms-tests-1", memoryService, Path.GetTempPath());

        // AI services — local bundled models (no external dependencies).
        // LLMCompletionServiceBasic: SmolLM2-135M (Q8_0, ~139 MB)
        // LLMEmbeddingServiceBasic: snowflake-arctic-embed-m-long (Q8_0)
        var completionService = new LLMCompletionServiceBasic();
        var embeddingService = new LLMEmbeddingServiceBasic();
        var vectorService = new VectorServiceBasic();

        return new RfConfigurationBuilder
        {
            Logger = logger,
            RootUserCredentials = new RootUserCredentials("admin@karasoftware.com", "123456"),
            RepositoryServiceConfiguration = new EntityRepositoryServiceConfiguration(
                dbService,
                memoryService,
                pubSubService,
                new FileServiceConfiguration(fileService, "reflective-forms-media")),
            EndpointConfiguration = new EndpointConfiguration
            {
                JwtSecret = "my-awesome-secret-key-1234567890",
                RootPath = "/rf",
                PublicUrlRootForApi = "http://localhost:9000/rf/api/",
                PublicFrontendBaseUrl = "http://localhost:3000",
                OpenApi = new OpenApiConfiguration
                {
                    Title = "ReflectiveForms Sample1 API",
                    Version = "1.0.0",
                    Description = "Sample application demonstrating all ReflectiveForms features including AI.",
                    ContactEmail = "admin@karasoftware.com"
                }
            },
            AiServiceConfiguration = new AiServiceConfiguration(
                HeavyLlmService: completionService,
                LightLlmService: embeddingService,
                VectorService: vectorService),
            EntityTypes =
            [
                // ──────────────────────────────────────────────────────────────
                // 1. Objectives (OKR) – original example
                //    Features: HasAuthor, HasTags, HasCategories, HasParent,
                //              title uniqueness, title sanity check, hooks,
                //              DisplayCondition, LogicSanityCheckAsync,
                //              DynamicChoicesCompileTimeAsync,
                //              DynamicChoicesRuntimeAsync,
                //              Repeater, Group, Relation, DatePicker,
                //              Select (static/dynamic), TextArea, Url, Checkbox
                // ──────────────────────────────────────────────────────────────
                new EntityConfigurationBuilder<RfObjectiveExampleModel>
                {
                    EntityName = "objective",
                    EntityReadableNamePlural = "Objectives",
                    EntityReadableNameSingular = "Objective",
                    EntityDescription = "An OKR (Objectives and Key Results) goal with measurable key results, root cause analysis, and team comments. Tracks short-term or long-term strategic objectives.",
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
                    OptionalTitleSanityCheck = async title => await Task.FromResult(title.Text != "Forbidden title example"),
                    HooksSetup = new EntityOnChangedHooksSetup<RfObjectiveExampleModel>
                    {
                        PostCreateHook = (p, _) =>
                        {
                            logger.LogInformation("{Entity}({Id}) - created", p.EntityName, p.NewId);
                            return Task.CompletedTask;
                        },
                        PostUpdateHook = (p, _) =>
                        {
                            logger.LogInformation("{Entity}({Id}) - updated", p.EntityName, p.Id);
                            return Task.CompletedTask;
                        },
                        PostDeleteHook = (p, _) =>
                        {
                            logger.LogInformation("{Entity}({Id}) - deleted", p.EntityName, p.Id);
                            return Task.CompletedTask;
                        }
                    }
                },

                // ──────────────────────────────────────────────────────────────
                // 2. Blog Posts
                //    Features: HasAuthor, HasTags, HasCategories,
                //              WysiwygEditor, MediaSourceBase64, DisplayCondition,
                //              Select (static), Checkbox, DatePicker,
                //              Number (min/max), Group (Grid2Elements),
                //              Repeater (min/max rows, no accordion),
                //              DynamicChoicesCompileTimeAsync,
                //              LogicSanityCheckAsync (slug uniqueness),
                //              title uniqueness, hooks
                // ──────────────────────────────────────────────────────────────
                new EntityConfigurationBuilder<BlogPostModel>
                {
                    EntityName = "blog-post",
                    EntityReadableNamePlural = "Blog Posts",
                    EntityReadableNameSingular = "Blog Post",
                    EntityDescription = "A blog article with rich-text content, excerpt, SEO metadata group, publication status workflow (draft/published/scheduled), featured image, and external links.",
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
                    OptionalTitleSanityCheck = null,
                    HooksSetup = new EntityOnChangedHooksSetup<BlogPostModel>
                    {
                        PostCreateHook = (p, _) =>
                        {
                            logger.LogInformation("Blog post created: {Id}", p.NewId);
                            return Task.CompletedTask;
                        },
                        PostUpdateHook = (p, _) =>
                        {
                            logger.LogInformation("Blog post updated: {Id}", p.Id);
                            return Task.CompletedTask;
                        },
                        PostDeleteHook = (p, _) =>
                        {
                            logger.LogInformation("Blog post deleted: {Id}", p.Id);
                            return Task.CompletedTask;
                        }
                    }
                },

                // ──────────────────────────────────────────────────────────────
                // 3. Team Members
                //    Features: Email, MediaSourceBase64 (avatar),
                //              Number (stepSize, min/max, default),
                //              Range slider, DisplayCondition (remote worker),
                //              Group (Grid3Elements for address),
                //              Repeater (accordion for social links),
                //              Repeater (min/max rows, Grid2Elements for contacts),
                //              Relation to blog-post, Checkbox, DatePicker,
                //              WysiwygEditor (bio), Text (default value),
                //              Select (many options)
                //    Config: No tags/categories/parent
                // ──────────────────────────────────────────────────────────────
                new EntityConfigurationBuilder<TeamMemberModel>
                {
                    EntityName = "team-member",
                    EntityReadableNamePlural = "Team Members",
                    EntityReadableNameSingular = "Team Member",
                    EntityDescription = "A team member profile with contact info, department, role, bio, office address, social links, and emergency contacts.",
                    SupportsFrontendEdit = true,
                    HasAuthor = false,
                    HasTags = false,
                    HasCategories = false,
                    HasParentChildRelationship = false,
                    RequireGlobalTitleUniqueness = true,
                    SupportsSemanticSearch = true,
                    OptionalTitleSanityCheck = null,
                    HooksSetup = null
                },

                // ──────────────────────────────────────────────────────────────
                // 4. Products (E-Commerce)
                //    Features: TextArea, WysiwygEditor,
                //              Select (static), DynamicChoicesRuntimeAsync
                //              (category → subcategory), MediaSourceBase64,
                //              Repeater × 3 (gallery, variants, specs),
                //              Group (Grid4Elements for shipping dimensions),
                //              Number (various step/min/max combos),
                //              Range (discount), Checkbox × 2,
                //              DisplayCondition (digital vs. physical),
                //              Relation to team-member, DatePicker, Url
                //    Config: HasTags, HasCategories, HasParent
                // ──────────────────────────────────────────────────────────────
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
                    OptionalTitleSanityCheck = null,
                    HooksSetup = new EntityOnChangedHooksSetup<ProductModel>
                    {
                        PostCreateHook = (p, _) =>
                        {
                            logger.LogInformation("Product created: {Title} (ID: {Id})", p.FinalBody.Title, p.NewId);
                            return Task.CompletedTask;
                        },
                        PostUpdateHook = null,
                        PostDeleteHook = null
                    }
                },

                // ──────────────────────────────────────────────────────────────
                // 5. Events
                //    Features: Deeply nested Groups (Venue → Address),
                //              Repeater × 2 (sessions, sponsors),
                //              DisplayCondition (online vs. in-person),
                //              Range (ticket price), DatePicker × 2,
                //              Email, MediaSourceBase64 (banner),
                //              Select, WysiwygEditor, Url × 2,
                //              Number (max attendees), Checkbox,
                //              Relation to team-member
                //    Config: No parent, no tags, HasCategories only
                // ──────────────────────────────────────────────────────────────
                new EntityConfigurationBuilder<EventModel>
                {
                    EntityName = "event",
                    EntityReadableNamePlural = "Events",
                    EntityReadableNameSingular = "Event",
                    EntityDescription = "A conference, workshop, or meetup event with sessions, sponsors, venue details, ticket pricing, and attendance tracking.",
                    SupportsFrontendEdit = true,
                    HasAuthor = true,
                    HasTags = false,
                    HasCategories = true,
                    HasParentChildRelationship = false,
                    RequireGlobalTitleUniqueness = false,
                    SupportsSemanticSearch = true,
                    SupportsAiGeneration = true,
                    SupportsNaturalLanguageFilter = true,
                    OptionalTitleSanityCheck = null,
                    HooksSetup = null
                },

                // ──────────────────────────────────────────────────────────────
                // 6. Surveys
                //    Features: Deeply nested Repeaters (3 levels: sections →
                //              questions → choices), DisplayCondition at every
                //              nesting level, Select, Checkbox, Number, TextArea,
                //              DatePicker, min/max rows enforcement.
                //    Config: HasAuthor, no tags/categories/parent
                // ──────────────────────────────────────────────────────────────
                new EntityConfigurationBuilder<SurveyModel>
                {
                    EntityName = "survey",
                    EntityReadableNamePlural = "Surveys",
                    EntityReadableNameSingular = "Survey",
                    EntityDescription = "A multi-section survey with questions (text, choice, or rating types), optional scoring, and response limits. Supports 3-level nesting: sections → questions → choices.",
                    SupportsFrontendEdit = true,
                    HasAuthor = true,
                    HasTags = false,
                    HasCategories = false,
                    HasParentChildRelationship = false,
                    RequireGlobalTitleUniqueness = false,
                    SupportsSemanticSearch = true,
                    SupportsAiGeneration = true,
                    SupportsAiDiffSummary = true,
                    SupportsNaturalLanguageFilter = true,
                    OptionalTitleSanityCheck = null,
                    HooksSetup = null
                }
            ]
        };
    }
}

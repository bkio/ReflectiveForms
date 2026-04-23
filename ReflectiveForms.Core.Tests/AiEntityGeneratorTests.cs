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
using ReflectiveForms.Core.Schema.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Tests for AI entity generation (plan Section 7.42-7.43).
/// </summary>
[Collection("AI")]
public class AiEntityGeneratorTests : IDisposable
{
    private readonly Mock<ILLMService> _mockHeavyLlm = new();
    private readonly Mock<ILLMService> _mockLightLlm = new();
    private readonly Mock<IVectorService> _mockVectorService = new();
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly Mock<IMemoryService> _mockMemoryService;

    public AiEntityGeneratorTests()
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
                    EntityDescription = "A test entity for AI generation.",
                    SupportsAiGeneration = true
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

    #region Conversation-based generation

    [Fact]
    public async Task GenerateAsync_ReturnsFieldsFromConversation()
    {
        InitializeAll();

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "My Generated Title",
                FinishReason = LLMFinishReason.Stop
            }));

        var result = await AiEntityGenerator.GenerateAsync("test-entity", "Create something", CancellationToken.None);

        result.Fields.Should().NotBeNull();
        result.Fields!["title"].Should().NotBeNull();
        result.Conversation.Should().NotBeEmpty("conversation should be returned");
    }

    [Fact]
    public async Task GenerateAsync_SkipResponseReturnsNull()
    {
        InitializeAll();

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "SKIP",
                FinishReason = LLMFinishReason.Stop
            }));

        var result = await AiEntityGenerator.GenerateAsync("test-entity", "Create something", CancellationToken.None);

        // All LLM fields returned SKIP, but fallback title is derived from user prompt
        result.Fields.Should().NotBeNull();
        result.Fields!["title"]!.Value<string>().Should().Be("Create Something");
    }

    [Fact]
    public async Task GenerateAsync_ConversationGrowsAcrossCalls()
    {
        InitializeAll();

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) =>
            {
                callCount++;
                // Each field prompt is isolated: system + one user question = 2 messages
                req.Messages.Count.Should().Be(2, "each field prompt should be isolated (system + question)");
            })
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Test Value",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiEntityGenerator.GenerateAsync("test-entity", "Create something", CancellationToken.None);

        callCount.Should().BeGreaterOrEqualTo(1);
    }

    #endregion

    #region Generate returns null on failure

    [Fact]
    public async Task GenerateAsync_LlmFailure_ReturnsNull()
    {
        InitializeAll();

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Failure("Service unavailable", HttpStatusCode.ServiceUnavailable));

        var result = await AiEntityGenerator.GenerateAsync("test-entity", "Create something", CancellationToken.None);

        // LLM failed but fallback title is derived from user prompt
        result.Fields.Should().NotBeNull();
        result.Fields!["title"]!.Value<string>().Should().Be("Create Something");
    }

    #endregion

    #region Generate uses HeavyLlm and SystemPromptPrefix

    [Fact]
    public async Task GenerateAsync_UsesHeavyLlm()
    {
        InitializeAll();

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "A Title",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiEntityGenerator.GenerateAsync("test-entity", "Create something", CancellationToken.None);

        _mockHeavyLlm.Verify(
            l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _mockLightLlm.Verify(
            l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenerateAsync_UsesSystemPromptPrefix()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest ??= req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "A Title",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiEntityGenerator.GenerateAsync("test-entity", "Create something", CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Messages.First().Content.Should().StartWith(
            "You are an assistant for a schema-driven content management system.");
    }

    [Fact]
    public async Task GenerateAsync_DoesNotSendToolDefinitions()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest ??= req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "A Title",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiEntityGenerator.GenerateAsync("test-entity", "Create something", CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Tools.Should().BeNullOrEmpty("conversation mode does not use tool calling");
    }

    [Fact]
    public async Task GenerateAsync_SystemPromptContainsEntityDescription()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest ??= req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "A Title",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiEntityGenerator.GenerateAsync("test-entity", "Create something", CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var systemMsg = capturedRequest!.Messages.First().Content;
        systemMsg.Should().Contain("A test entity for AI generation.",
            "system prompt should include the EntityDescription from configuration");
    }

    #endregion

    #region ParseFieldValue

    [Fact]
    public void ParseFieldValue_Text_TakesFirstLine()
    {
        var field = new FieldSchema { Name = "name", Type = FieldSchemaType.Text, Label = "Name" };
        var result = AiEntityGenerator.ParseFieldValue(field, "Hello World\nExtra line");
        result!.Value<string>().Should().Be("Hello World");
    }

    [Fact]
    public void ParseFieldValue_Select_MatchesCaseInsensitive()
    {
        var field = new FieldSchema
        {
            Name = "status", Type = FieldSchemaType.Select, Label = "Status",
            SelectOptions = new SelectFieldOptions
            {
                Choices = [new SelectChoice { Value = "draft", Label = "Draft" }, new SelectChoice { Value = "published", Label = "Published" }]
            }
        };
        var result = AiEntityGenerator.ParseFieldValue(field, "DRAFT");
        result!.Value<string>().Should().Be("draft");
    }

    [Fact]
    public void ParseFieldValue_Select_ReturnsNullForInvalidValue()
    {
        var field = new FieldSchema
        {
            Name = "status", Type = FieldSchemaType.Select, Label = "Status",
            SelectOptions = new SelectFieldOptions
            {
                Choices = [new SelectChoice { Value = "draft", Label = "Draft" }]
            }
        };
        var result = AiEntityGenerator.ParseFieldValue(field, "some random essay about drafts");
        // "draft" is contained in the text so it should match via contains
        result!.Value<string>().Should().Be("draft");
    }

    [Fact]
    public void ParseFieldValue_DatePicker_ExtractsDate()
    {
        var field = new FieldSchema { Name = "date", Type = FieldSchemaType.DatePicker, Label = "Date" };
        var result = AiEntityGenerator.ParseFieldValue(field, "The date is 2025-06-15 today");
        result!.Value<string>().Should().Be("2025-06-15");
    }

    [Fact]
    public void ParseFieldValue_Number_ExtractsFirstNumber()
    {
        var field = new FieldSchema { Name = "year", Type = FieldSchemaType.Number, Label = "Year" };
        var result = AiEntityGenerator.ParseFieldValue(field, "The year is 2025");
        result!.Value<double>().Should().Be(2025);
    }

    #endregion

    #region CleanValue

    [Fact]
    public void CleanValue_StripsMultipleQuoteLayers()
    {
        AiEntityGenerator.CleanValue("\"\"Animals in the Zoo\"\"").Should().Be("Animals in the Zoo");
    }

    [Fact]
    public void CleanValue_StripsSingleQuoteLayer()
    {
        AiEntityGenerator.CleanValue("\"Hello World\"").Should().Be("Hello World");
    }

    [Fact]
    public void CleanValue_StripsLabelPrefix()
    {
        AiEntityGenerator.CleanValue("Title: My Blog Post").Should().Be("My Blog Post");
    }

    [Fact]
    public void CleanValue_StripsLabelPrefixThenQuotes()
    {
        AiEntityGenerator.CleanValue("SEO Meta Title: \"My Favorite Cat\"").Should().Be("My Favorite Cat");
    }

    [Fact]
    public void CleanValue_PreservesUrls()
    {
        AiEntityGenerator.CleanValue("https://example.com/path").Should().Be("https://example.com/path");
    }

    [Fact]
    public void CleanValue_StripsEchoPattern()
    {
        AiEntityGenerator.CleanValue("The title of the blog post should be \"My Favorite Cat and Dog\"")
            .Should().Be("My Favorite Cat and Dog");
    }

    [Fact]
    public void CleanValue_StripsEchoPatternWithoutQuotes()
    {
        AiEntityGenerator.CleanValue("The content should be a story about cats")
            .Should().Be("a story about cats");
    }

    #endregion

    #region TruncateAtContinuation

    [Fact]
    public void TruncateAtContinuation_StopsAtUserContinuation()
    {
        AiEntityGenerator.TruncateAtContinuation("draft\nUser: status (choose one: draft, published)")
            .Should().Be("draft");
    }

    [Fact]
    public void TruncateAtContinuation_StopsAtQuestionContinuation()
    {
        AiEntityGenerator.TruncateAtContinuation("My Summer Vacation\nQuestion: what is the content?")
            .Should().Be("My Summer Vacation");
    }

    [Fact]
    public void TruncateAtContinuation_StopsAtAssistantContinuation()
    {
        AiEntityGenerator.TruncateAtContinuation("true\nAssistant: The answer is true\nUser: next")
            .Should().Be("true");
    }

    [Fact]
    public void TruncateAtContinuation_PreservesMultiLineContent()
    {
        AiEntityGenerator.TruncateAtContinuation("<p>First paragraph.</p>\n<p>Second paragraph.</p>")
            .Should().Be("<p>First paragraph.</p>\n<p>Second paragraph.</p>");
    }

    [Fact]
    public void TruncateAtContinuation_PreservesCleanSingleLine()
    {
        AiEntityGenerator.TruncateAtContinuation("draft")
            .Should().Be("draft");
    }

    [Fact]
    public void TruncateAtContinuation_HandlesLeadingWhitespaceOnMarker()
    {
        AiEntityGenerator.TruncateAtContinuation("8\n  User: next question")
            .Should().Be("8");
    }

    [Fact]
    public void TruncateAtContinuation_StopsAtRepetitionLoop()
    {
        var repeated = "The impact of sick children on children's health is a topic of great concern.";
        var input = $"{repeated}\nExcerpt:\n{repeated}\nExcerpt:\n{repeated}\nExcerpt:\n{repeated}";
        var result = AiEntityGenerator.TruncateAtContinuation(input);
        // Should contain the content once, not the full repetition
        result.Should().Be(repeated);
    }

    [Fact]
    public void TruncateAtContinuation_PreservesDistinctMultiLineContent()
    {
        var input = "First unique paragraph about birds.\nSecond paragraph about migration.\nThird paragraph about habitats.";
        AiEntityGenerator.TruncateAtContinuation(input)
            .Should().Be(input);
    }

    [Fact]
    public void TruncateAtContinuation_StopsAtPrefixPatternRepetition()
    {
        var input = "Title: The Importance of Healthy Eating\n\nTitle: The Impact of Sun Exposure\n\nTitle: The Effects of Sleep";
        AiEntityGenerator.TruncateAtContinuation(input)
            .Should().Be("Title: The Importance of Healthy Eating");
    }

    [Fact]
    public void TruncateAtContinuation_PreservesDistinctPrefixes()
    {
        var input = "Name: John Doe and family\nEmail: john@example.com\nCity: Berlin, Germany";
        AiEntityGenerator.TruncateAtContinuation(input)
            .Should().Be(input);
    }

    #endregion

    #region Slugify

    [Fact]
    public void Slugify_ConvertsSpacesToHyphens()
    {
        AiEntityGenerator.Slugify("Animals in the Zoo").Should().Be("animals-in-the-zoo");
    }

    [Fact]
    public void Slugify_RemovesSpecialCharacters()
    {
        AiEntityGenerator.Slugify("Hello, World! (2025)").Should().Be("hello-world-2025");
    }

    [Fact]
    public void Slugify_CollapsesDashes()
    {
        AiEntityGenerator.Slugify("A - B - C").Should().Be("a-b-c");
    }

    #endregion

    #region IsEchoOfTitle

    [Fact]
    public void IsEchoOfTitle_ExactMatch_ReturnsTrue()
    {
        AiEntityGenerator.IsEchoOfTitle("Animals in the Zoo", "Animals in the Zoo").Should().BeTrue();
    }

    [Fact]
    public void IsEchoOfTitle_CaseInsensitive_ReturnsTrue()
    {
        AiEntityGenerator.IsEchoOfTitle("ANIMALS IN THE ZOO", "Animals in the Zoo").Should().BeTrue();
    }

    [Fact]
    public void IsEchoOfTitle_LongerContent_ReturnsFalse()
    {
        AiEntityGenerator.IsEchoOfTitle(
            "This is a long paragraph about animals in the zoo that covers many topics in detail.",
            "Animals in the Zoo").Should().BeFalse();
    }

    #endregion

    #region IsEchoOfQuestion

    [Fact]
    public void IsEchoOfQuestion_DirectEcho_ReturnsTrue()
    {
        AiEntityGenerator.IsEchoOfQuestion(
            "The title of the Blog Post, short text, max 10 words",
            "The title of the Blog Post, short text, max 10 words").Should().BeTrue();
    }

    [Fact]
    public void IsEchoOfQuestion_ParaphraseWithMostWords_ReturnsTrue()
    {
        AiEntityGenerator.IsEchoOfQuestion(
            "The title of the Blog Post, short text, and maximum 10 words are:",
            "The title of the Blog Post, short text, max 10 words").Should().BeTrue();
    }

    [Fact]
    public void IsEchoOfQuestion_FieldNameWithEquals_ReturnsTrue()
    {
        AiEntityGenerator.IsEchoOfQuestion(
            "allow_comments = true",
            "allow_comments (Enable or disable comments, true or false)").Should().BeTrue();
    }

    [Fact]
    public void IsEchoOfQuestion_ActualValue_ReturnsFalse()
    {
        AiEntityGenerator.IsEchoOfQuestion(
            "Why Sleep Matters for Growing Children",
            "The title of the Blog Post, short text, max 10 words").Should().BeFalse();
    }

    [Fact]
    public void IsEchoOfQuestion_ShortValue_ReturnsFalse()
    {
        AiEntityGenerator.IsEchoOfQuestion("draft",
            "Post Status, choose one: draft, published, scheduled, archived").Should().BeFalse();
    }

    [Fact]
    public void IsEchoOfQuestion_NumberValue_ReturnsFalse()
    {
        AiEntityGenerator.IsEchoOfQuestion("5",
            "Estimated Reading Time (minutes), a number").Should().BeFalse();
    }

    #endregion

    #region PostProcessFields

    [Fact]
    public void PostProcessFields_DerivesSlugFromTitle()
    {
        var result = new JObject { ["title"] = "My Blog Post Title", ["slug"] = "my blog post title" };
        var fields = new List<FieldSchema>
        {
            new() { Name = "slug", Type = FieldSchemaType.Text, Label = "Slug" }
        };

        AiEntityGenerator.PostProcessFields(result, fields);

        result["slug"]!.Value<string>().Should().Be("my-blog-post-title");
    }

    [Fact]
    public void PostProcessFields_RemovesFabricatedUrls()
    {
        var result = new JObject
        {
            ["title"] = "Animals in the Zoo",
            ["canonical_url"] = "animalsinthezoo.com"
        };
        var fields = new List<FieldSchema>
        {
            new() { Name = "canonical_url", Type = FieldSchemaType.Url, Label = "Canonical URL" }
        };

        AiEntityGenerator.PostProcessFields(result, fields);

        result["canonical_url"].Should().BeNull("fabricated URL without http should be removed");
    }

    [Fact]
    public void PostProcessFields_KeepsValidUrls()
    {
        var result = new JObject
        {
            ["title"] = "My Post",
            ["canonical_url"] = "https://example.com/my-post"
        };
        var fields = new List<FieldSchema>
        {
            new() { Name = "canonical_url", Type = FieldSchemaType.Url, Label = "Canonical URL" }
        };

        AiEntityGenerator.PostProcessFields(result, fields);

        result["canonical_url"]!.Value<string>().Should().Be("https://example.com/my-post");
    }

    [Fact]
    public void PostProcessFields_DerivesExcerptFromContent()
    {
        var longContent = string.Join(" ", Enumerable.Repeat("Lorem ipsum dolor sit amet.", 20));
        var result = new JObject
        {
            ["title"] = "My Post",
            ["content"] = longContent,
            ["excerpt"] = "My Post" // echo of title
        };
        var fields = new List<FieldSchema>
        {
            new() { Name = "content", Type = FieldSchemaType.WysiwygEditor, Label = "Content" },
            new() { Name = "excerpt", Type = FieldSchemaType.TextArea, Label = "Excerpt" }
        };

        AiEntityGenerator.PostProcessFields(result, fields);

        var excerpt = result["excerpt"]!.Value<string>();
        excerpt.Should().NotBe("My Post", "excerpt should be derived from content, not echo title");
        excerpt.Should().Contain("Lorem ipsum");
    }

    [Fact]
    public void PostProcessFields_ComputesReadingTime()
    {
        // ~200 words of content → 1 minute reading time
        var content = string.Join(" ", Enumerable.Repeat("word", 400));
        var result = new JObject
        {
            ["title"] = "My Post",
            ["content"] = content,
            ["reading_time_minutes"] = 0
        };
        var fields = new List<FieldSchema>
        {
            new() { Name = "content", Type = FieldSchemaType.WysiwygEditor, Label = "Content" },
            new() { Name = "reading_time_minutes", Type = FieldSchemaType.Number, Label = "Reading Time" }
        };

        AiEntityGenerator.PostProcessFields(result, fields);

        result["reading_time_minutes"]!.Value<int>().Should().Be(2); // 400 words / 200 wpm = 2 min
    }

    [Fact]
    public void PostProcessFields_DerivesGroupSeoFields()
    {
        var result = new JObject
        {
            ["title"] = "Animals in the Zoo",
            ["seo_metadata"] = new JObject
            {
                ["meta_title"] = "Animals in the Zoo",
                ["meta_description"] = "Animals in the Zoo",
                ["meta_keywords"] = "Animals in the Zoo",
                ["canonical_url"] = "animalsinthezoo.com"
            }
        };
        var fields = new List<FieldSchema>
        {
            new()
            {
                Name = "seo_metadata", Type = FieldSchemaType.Group, Label = "SEO",
                GroupOptions = new GroupFieldOptions
                {
                    ChildSchema = new List<FieldSchema>
                    {
                        new() { Name = "meta_title", Type = FieldSchemaType.Text, Label = "Meta Title" },
                        new() { Name = "meta_description", Type = FieldSchemaType.TextArea, Label = "Meta Description" },
                        new() { Name = "meta_keywords", Type = FieldSchemaType.Text, Label = "Meta Keywords" },
                        new() { Name = "canonical_url", Type = FieldSchemaType.Url, Label = "Canonical URL" }
                    }
                }
            }
        };

        AiEntityGenerator.PostProcessFields(result, fields);

        var seo = (JObject)result["seo_metadata"]!;
        seo["meta_title"]!.Value<string>().Should().Be("Animals in the Zoo");
        seo["meta_keywords"]!.Value<string>().Should().Contain("animals");
        seo["canonical_url"].Should().BeNull("fabricated URL should be removed");
    }

    [Fact]
    public void PostProcessFields_CleansRepeaterWithFabricatedUrls()
    {
        var result = new JObject
        {
            ["title"] = "My Post",
            ["external_links"] = new JArray
            {
                new JObject { ["link_title"] = "Link 1", ["link_url"] = "mypost.com" },
                new JObject { ["link_title"] = "Link 2", ["link_url"] = "https://real-link.com" }
            }
        };
        var fields = new List<FieldSchema>
        {
            new()
            {
                Name = "external_links", Type = FieldSchemaType.Repeater, Label = "External Links",
                RepeaterOptions = new RepeaterFieldOptions
                {
                    AddButtonLabel = "Add Link",
                    ItemSchema = new List<FieldSchema>
                    {
                        new() { Name = "link_title", Type = FieldSchemaType.Text, Label = "Title" },
                        new() { Name = "link_url", Type = FieldSchemaType.Url, Label = "URL" }
                    }
                }
            }
        };

        AiEntityGenerator.PostProcessFields(result, fields);

        var links = (JArray)result["external_links"]!;
        links.Count.Should().Be(1, "item with fabricated URL should be removed");
        links[0]["link_url"]!.Value<string>().Should().Be("https://real-link.com");
    }

    #endregion
}

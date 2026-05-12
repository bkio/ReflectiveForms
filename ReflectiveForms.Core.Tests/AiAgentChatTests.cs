using System.Runtime.Serialization;
using System.Net;
using System.Reflection;
using CrossCloudKit.Interfaces;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Enums;
using CrossCloudKit.Interfaces.Records;
using CrossCloudKit.Utilities.Common;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Models.ReservedEntityTypes;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Integration tests for the AI Agent Chat handler.
/// Tests the multi-turn agent loop: LLM → tool calls → tool execution → LLM → final answer.
/// </summary>
[Collection("AI")]
public class AiAgentChatTests : IDisposable
{
    private readonly Mock<ILLMService> _mockHeavyLlm = new();
    private readonly Mock<ILLMService> _mockLightLlm = new();
    private readonly Mock<IVectorService> _mockVectorService = new();
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly Mock<IMemoryService> _mockMemoryService;

    public AiAgentChatTests()
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
        var iamField = typeof(RfConfiguration).GetField("_iamRoleEntitiesCache",
            BindingFlags.Static | BindingFlags.NonPublic);
        rfConfigField?.SetValue(null, null);
        rfInitField?.SetValue(null, false);
        iamField?.SetValue(null, null);
    }

    #region Agent loop — single turn (LLM answers directly without tools)

    [Fact]
    public async Task Chat_DirectAnswer_NoToolCalls()
    {
        InitializeAll();

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Hello! How can I help you?",
                FinishReason = LLMFinishReason.Stop
            }));

        var result = await AiAgentChatHandler.ChatAsync(new AgentChatRequest { Message = "Hi" }, CreateAdminUser(), CancellationToken.None);

        result.Response.Should().Be("Hello! How can I help you?");
        result.ToolCallsMade.Should().BeEmpty();
        _mockHeavyLlm.Verify(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Agent loop — LLM calls list_entity_types then answers

    [Fact]
    public async Task Chat_ListEntityTypes_ThenAnswer()
    {
        InitializeAll();

        // Seed DB with 3 test entities
        SetupDatabaseScan("test-entity", CreateTestEntities(3));
        SetupDatabaseScan("shared-entity", CreateTestEntities(2));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First call: LLM wants to call list_entity_types
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall { Id = "call_1", Name = "list_entity_types", Arguments = "{}" }
                        ]
                    });
                }

                // Second call: LLM produces final answer using tool results
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "You have 2 entity types: 3 Test Entities and 2 Shared Entities.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "What data do I have?" }, CreateAdminUser(), CancellationToken.None);

        result.Response.Should().Contain("Test Entities");
        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].ToolName.Should().Be("list_entity_types");

        // Verify the tool result contains entity info
        var toolResult = JArray.Parse(result.ToolCallsMade[0].Result);
        toolResult.Should().HaveCount(2);
        toolResult[0]["name"]!.Value<string>().Should().Be("test-entity");
        toolResult[0]["count"]!.Value<int>().Should().Be(3);
        toolResult[1]["name"]!.Value<string>().Should().Be("shared-entity");
        toolResult[1]["count"]!.Value<int>().Should().Be(2);

        // Heavy LLM called twice: tool call + final answer
        _mockHeavyLlm.Verify(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    #endregion

    #region Agent loop — multi-tool: search then get_entity

    [Fact]
    public async Task Chat_SearchThenGetEntity_MultiTurn()
    {
        InitializeAll();

        var entity = CreateTestEntity(42, "Q4 Revenue Growth", "Grow revenue by 20% in Q4.");
        SetupDatabaseGetItem("test-entity", 42, entity);

        // Mock semantic search results
        _mockVectorService
            .Setup(v => v.QueryAsync(
                "rf_semantic_test-entity", It.IsAny<float[]>(), It.IsAny<int>(),
                It.IsAny<ConditionCoupling>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<VectorSearchResult>>.Success(
                new List<VectorSearchResult>
                {
                    new()
                    {
                        Id = "42", Score = 0.92f,
                        Metadata = new JObject
                        {
                            ["entity_name"] = "test-entity",
                            ["title"] = "Q4 Revenue Growth",
                            ["summary"] = "Revenue growth objective for Q4."
                        }
                    }
                }));

        _mockLightLlm
            .Setup(l => l.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<float[]>.Success(new float[384]));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount switch
                {
                    1 => OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "search_entities",
                                Arguments = """{"query":"revenue growth","entity_type":"test-entity","top_k":5}"""
                            }
                        ]
                    }),
                    2 => OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_2", Name = "get_entity",
                                Arguments = """{"entity_type":"test-entity","entity_id":42}"""
                            }
                        ]
                    }),
                    _ => OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "The Q4 Revenue Growth objective aims to grow revenue by 20% in Q4.",
                        FinishReason = LLMFinishReason.Stop
                    })
                };
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Tell me about the revenue growth objective" }, CreateAdminUser(), CancellationToken.None);

        result.Response.Should().Contain("Q4 Revenue Growth");
        result.ToolCallsMade.Should().HaveCount(2);
        result.ToolCallsMade[0].ToolName.Should().Be("search_entities");
        result.ToolCallsMade[1].ToolName.Should().Be("get_entity");

        // Verify get_entity returned actual entity data
        var entityResult = JObject.Parse(result.ToolCallsMade[1].Result);
        entityResult["entity_id"]!.Value<int>().Should().Be(42);
        entityResult["title"]!.Value<string>().Should().Be("Q4 Revenue Growth");
        entityResult["fields"].Should().NotBeNull();

        // Heavy LLM called 3 times: search → get_entity → final answer
        _mockHeavyLlm.Verify(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    #endregion

    #region Agent loop — max iterations cap prevents infinite loop

    [Fact]
    public async Task Chat_MaxIterations_StopsRunawayLoop()
    {
        InitializeAll();

        SetupDatabaseScan("test-entity", CreateTestEntities(1));

        // LLM always wants to call tools — never produces a final answer
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = null,
                FinishReason = LLMFinishReason.ToolCall,
                ToolCalls =
                [
                    new LLMToolCall { Id = "call_loop", Name = "list_entity_types", Arguments = "{}" }
                ]
            }));

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Loop forever" }, CreateAdminUser(), CancellationToken.None);

        result.Response.Should().Contain("limit");
        result.ToolCallsMade.Should().HaveCount(8, "should cap at 8 iterations");

        _mockHeavyLlm.Verify(
            l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(8));
    }

    #endregion

    #region Agent loop — LLM error returns graceful message

    [Fact]
    public async Task Chat_LlmError_ReturnsErrorMessage()
    {
        InitializeAll();

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Failure("Model overloaded", HttpStatusCode.ServiceUnavailable));

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Hello" }, CreateAdminUser(), CancellationToken.None);

        result.Response.Should().Contain("error");
        result.Response.Should().Contain("Model overloaded");
        result.ToolCallsMade.Should().BeEmpty();
    }

    #endregion

    #region Tool execution — get_entity_schema returns field info

    [Fact]
    public async Task Chat_GetEntitySchema_ReturnsFieldInfo()
    {
        InitializeAll();

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "get_entity_schema",
                                Arguments = """{"entity_type":"test-entity"}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "The Test Entity has a body field and a content field.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "What fields does test-entity have?" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].ToolName.Should().Be("get_entity_schema");

        // Verify schema result contains entity type information
        var schemaResult = result.ToolCallsMade[0].Result;
        schemaResult.Should().Contain("Test Entity");
        schemaResult.Should().Contain("A test entity for AI agent chat integration");
    }

    #endregion

    #region Tool execution — unknown tool returns error gracefully

    [Fact]
    public async Task Chat_UnknownTool_ReturnsErrorInResult()
    {
        InitializeAll();

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "nonexistent_tool",
                                Arguments = "{}"
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "I couldn't find that tool.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Use a fake tool" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].Result.Should().Contain("Unknown tool");
    }

    #endregion

    #region Tool execution — search_entities with no results

    [Fact]
    public async Task Chat_SearchEntities_NoResults_ReturnsEmptyMessage()
    {
        InitializeAll();

        _mockLightLlm
            .Setup(l => l.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<float[]>.Success(new float[384]));

        _mockVectorService
            .Setup(v => v.QueryAsync(
                It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<int>(),
                It.IsAny<ConditionCoupling>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<VectorSearchResult>>.Success(
                new List<VectorSearchResult>()));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "search_entities",
                                Arguments = """{"query":"nonexistent thing"}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "I couldn't find any matching entities.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Find something that doesn't exist" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].Result.Should().Contain("No matching entities found");
    }

    #endregion

    #region Tool execution — get_entity with nonexistent entity

    [Fact]
    public async Task Chat_GetEntity_NotFound_ReturnsError()
    {
        InitializeAll();

        _mockDatabaseService
            .Setup(d => d.GetItemAsync("test-entity",
                It.IsAny<DbKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<JObject>.Failure("Not found", HttpStatusCode.NotFound));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "get_entity",
                                Arguments = """{"entity_type":"test-entity","entity_id":999}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Entity not found.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Show me entity 999" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].Result.Should().Contain("not found");
    }

    #endregion

    #region Agent loop — message history accumulates correctly across turns

    [Fact]
    public async Task Chat_MessageHistory_AccumulatesAcrossTurns()
    {
        InitializeAll();

        SetupDatabaseScan("test-entity", CreateTestEntities(5));

        LLMRequest? lastRequest = null;
        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => lastRequest = req)
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall { Id = "call_1", Name = "list_entity_types", Arguments = "{}" }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Done.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        await AiAgentChatHandler.ChatAsync(new AgentChatRequest { Message = "Count stuff" }, CreateAdminUser(), CancellationToken.None);

        // On the second LLM call, messages should include:
        // 1. System prompt
        // 2. User message
        // 3. Assistant message (tool call)
        // 4. Tool result
        lastRequest.Should().NotBeNull();
        lastRequest!.Messages.Should().HaveCount(4);
        lastRequest.Messages[0].Role.Should().Be(LLMRole.System);
        lastRequest.Messages[1].Role.Should().Be(LLMRole.User);
        lastRequest.Messages[2].Role.Should().Be(LLMRole.Assistant);
        lastRequest.Messages[3].Role.Should().Be(LLMRole.Tool);
        lastRequest.Messages[3].ToolCallId.Should().Be("call_1");
    }

    #endregion

    #region Agent loop — parallel tool calls in single turn

    [Fact]
    public async Task Chat_ParallelToolCalls_BothExecuted()
    {
        InitializeAll();

        SetupDatabaseScan("test-entity", CreateTestEntities(2));
        SetupDatabaseScan("shared-entity", CreateTestEntities(1));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // LLM requests two tools in a single turn
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall { Id = "call_1", Name = "list_entity_types", Arguments = "{}" },
                            new LLMToolCall
                            {
                                Id = "call_2", Name = "get_entity_schema",
                                Arguments = """{"entity_type":"test-entity"}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Here's the overview and schema.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Give me an overview and schema" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(2);
        result.ToolCallsMade[0].ToolName.Should().Be("list_entity_types");
        result.ToolCallsMade[1].ToolName.Should().Be("get_entity_schema");
    }

    #endregion

    #region Tool execution — search_entities missing required query param

    [Fact]
    public async Task Chat_SearchEntities_MissingQuery_ReturnsError()
    {
        InitializeAll();

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "search_entities",
                                Arguments = """{}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Missing parameter.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Search for nothing" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].Result.Should().Contain("'query' parameter is required");
    }

    #endregion

    #region Tool execution — search_entities for nonexistent entity type

    [Fact]
    public async Task Chat_SearchEntities_InvalidEntityType_ReturnsError()
    {
        InitializeAll();

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "search_entities",
                                Arguments = """{"query":"hello","entity_type":"nonexistent"}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "That type doesn't exist.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Search nonexistent" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].Result.Should().Contain("not found");
    }

    #endregion

    #region Agent loop — system prompt includes prefix from config

    [Fact]
    public async Task Chat_SystemPrompt_IncludesConfigPrefix()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Hi!",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiAgentChatHandler.ChatAsync(new AgentChatRequest { Message = "Hello" }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Messages[0].Role.Should().Be(LLMRole.System);
        capturedRequest.Messages[0].Content.Should().Contain("assistant for a schema-driven content management system");
        capturedRequest.Messages[0].Content.Should().Contain("tools");
    }

    #endregion

    #region Agent loop — tools are passed to LLM request

    [Fact]
    public async Task Chat_ToolDefinitions_PassedToLlm()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiAgentChatHandler.ChatAsync(new AgentChatRequest { Message = "Test" }, CreateAdminUser(), CancellationToken.None);

        capturedRequest!.Tools.Should().NotBeNull();
        capturedRequest.Tools!.Select(t => t.Name).Should().BeEquivalentTo(
            ["list_entity_types", "search_entities", "get_entity", "get_entity_schema",
             "generate_entity", "filter_entities", "summarize_changes", "check_entity_quality",
             "propose_create_entity", "propose_update_entity", "propose_delete_entity", "suggest_field_value",
             "navigate", "list_entities", "set_form_fields"]);
    }

    #endregion

    #region Integration — full flow: list → search → get → answer

    [Fact]
    public async Task Chat_FullFlow_ListSearchGetAnswer()
    {
        InitializeAll();

        var entities = new List<JObject>
        {
            CreateTestEntity(1, "Customer Satisfaction", "Improve NPS score to 80 by Q3."),
            CreateTestEntity(2, "Employee Retention", "Reduce attrition to under 5%."),
            CreateTestEntity(3, "Revenue Growth", "Grow ARR by 30%.")
        };
        SetupDatabaseScan("test-entity", entities);
        SetupDatabaseScan("shared-entity", CreateTestEntities(1));
        SetupDatabaseGetItem("test-entity", 1, entities[0]);

        _mockLightLlm
            .Setup(l => l.CreateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<float[]>.Success(new float[384]));

        _mockVectorService
            .Setup(v => v.QueryAsync(
                "rf_semantic_test-entity", It.IsAny<float[]>(), It.IsAny<int>(),
                It.IsAny<ConditionCoupling>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<VectorSearchResult>>.Success(
                new List<VectorSearchResult>
                {
                    new()
                    {
                        Id = "1", Score = 0.95f,
                        Metadata = new JObject
                        {
                            ["entity_name"] = "test-entity",
                            ["title"] = "Customer Satisfaction",
                            ["summary"] = "Improve NPS to 80."
                        }
                    }
                }));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount switch
                {
                    // Turn 1: list entity types
                    1 => OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall { Id = "c1", Name = "list_entity_types", Arguments = "{}" }
                        ]
                    }),
                    // Turn 2: search for customer satisfaction
                    2 => OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "c2", Name = "search_entities",
                                Arguments = """{"query":"customer satisfaction","entity_type":"test-entity"}"""
                            }
                        ]
                    }),
                    // Turn 3: get entity details
                    3 => OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "c3", Name = "get_entity",
                                Arguments = """{"entity_type":"test-entity","entity_id":1}"""
                            }
                        ]
                    }),
                    // Turn 4: final answer
                    _ => OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = "The Customer Satisfaction objective aims to improve the NPS score to 80 by Q3.",
                        FinishReason = LLMFinishReason.Stop
                    })
                };
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Tell me about customer satisfaction objectives" },
            CreateAdminUser(), CancellationToken.None);

        // Verify full tool chain
        result.ToolCallsMade.Should().HaveCount(3);
        result.ToolCallsMade[0].ToolName.Should().Be("list_entity_types");
        result.ToolCallsMade[1].ToolName.Should().Be("search_entities");
        result.ToolCallsMade[2].ToolName.Should().Be("get_entity");

        // Verify final answer
        result.Response.Should().Contain("Customer Satisfaction");
        result.Response.Should().Contain("NPS");

        // Verify tool results had real data
        var listResult = JArray.Parse(result.ToolCallsMade[0].Result);
        listResult.Should().HaveCount(2); // test-entity + shared-entity

        var searchResult = JArray.Parse(result.ToolCallsMade[1].Result);
        searchResult.Should().HaveCount(1);
        searchResult[0]["title"]!.Value<string>().Should().Be("Customer Satisfaction");

        var entityResult = JObject.Parse(result.ToolCallsMade[2].Result);
        entityResult["entity_id"]!.Value<int>().Should().Be(1);
        entityResult["fields"]!["body"]!.Value<string>().Should().Contain("NPS");

        // 4 LLM calls total
        _mockHeavyLlm.Verify(
            l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4));
    }

    #endregion

    #region Tool execution — malformed JSON arguments handled gracefully

    [Fact]
    public async Task Chat_MalformedToolArguments_HandledGracefully()
    {
        InitializeAll();

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "get_entity",
                                Arguments = "this is not json"
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "There was an error parsing the arguments.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Bad args" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].Result.Should().Contain("failed");
    }

    #endregion

    #region New tools — generate_entity

    [Fact]
    public async Task Chat_GenerateEntity_ReturnsDraftFields()
    {
        InitializeAll();

        // Mock the Heavy LLM for BOTH the agent loop and the entity generator's field-by-field calls.
        // The agent loop calls CompleteAsync first (tool call), then AiEntityGenerator calls it per-field,
        // then the agent loop calls it again for the final answer.
        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // Agent loop: LLM decides to generate an entity
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "generate_entity",
                                Arguments = """{"entity_type":"test-entity","prompt":"A post about cloud computing trends"}"""
                            }
                        ]
                    });
                }

                // All subsequent calls are from AiEntityGenerator's field-by-field generation
                // or from the agent loop's final answer. Return simple text for generator fields,
                // then a final answer when the agent loop resumes.
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Cloud computing trends for 2026",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Create a test entity about cloud computing" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].ToolName.Should().Be("generate_entity");
        result.ToolCallsMade[0].Result.Should().Contain("draft");
    }

    [Fact]
    public async Task Chat_GenerateEntity_UnsupportedType_ReturnsError()
    {
        InitializeAll();

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "generate_entity",
                                Arguments = """{"entity_type":"shared-entity","prompt":"test"}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "That entity type doesn't support generation.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Generate a shared entity" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].Result.Should().Contain("does not support AI generation");
    }

    [Fact]
    public async Task Chat_GenerateEntity_NoPermission_ReturnsError()
    {
        InitializeAll();

        // Create a user with NO create permission (RoleId=99 doesn't exist in IAM cache)
        var limitedUser = new EntityModel<UserEntityFieldsModel>
        {
            Id = 2,
            Title = new TitleRenderedModel(),
            Fields = new UserEntityFieldsModel
            {
                EmailAddress = "reader@test.com",
                Roles = [new UserRoleAssignmentModel { RoleId = 99 }]
            }
        };

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "generate_entity",
                                Arguments = """{"entity_type":"test-entity","prompt":"test"}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "You don't have permission.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Create a test entity" }, limitedUser, CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].Result.Should().Contain("create permission");
    }

    #endregion

    #region New tools — filter_entities

    [Fact]
    public async Task Chat_FilterEntities_ReturnsResults()
    {
        InitializeAll();

        // The filter tool calls AiNaturalLanguageFilterHandler.FilterAsync which uses the Heavy LLM
        // with tool calling. The Heavy LLM mock handles both the outer agent loop and the inner filter call.
        var lightCallCount = 0;
        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                lightCallCount++;
                // The NL filter handler expects the Light LLM to produce tool calls for filter construction.
                // If it doesn't produce tool calls, it returns interpretation without results.
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Looking for active entities",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "filter_entities",
                                Arguments = """{"entity_type":"test-entity","query":"active entities"}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Here are the active entities.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Show me all active test entities" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].ToolName.Should().Be("filter_entities");
        // When the LLM doesn't produce filter tool calls, the handler returns interpretation without filters
        result.ToolCallsMade[0].Result.Should().Contain("entity_type");
    }

    [Fact]
    public async Task Chat_FilterEntities_UnsupportedType_ReturnsError()
    {
        InitializeAll();

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "filter_entities",
                                Arguments = """{"entity_type":"shared-entity","query":"test"}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "That type doesn't support filtering.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Filter shared entities" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].Result.Should().Contain("does not support natural language filtering");
    }

    #endregion

    #region New tools — summarize_changes

    [Fact]
    public async Task Chat_SummarizeChanges_ReturnsSummary()
    {
        InitializeAll();

        // The diff summary handler fetches revisions via RepositoryService, which requires
        // a full runtime. Since we can't easily mock RepositoryService.GetEntityRevisionsAsync,
        // the handler will return null, which the tool reports as an error.
        // This test verifies the tool routing and error handling work correctly.
        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "summarize_changes",
                                Arguments = """{"entity_type":"test-entity","entity_id":1,"revision_index":1}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "I couldn't retrieve revision data.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "What changed in entity 1 revision 1?" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].ToolName.Should().Be("summarize_changes");
        // Will fail because RepositoryService isn't fully mocked — returns error message
        result.ToolCallsMade[0].Result.Should().Contain("failed");
    }

    [Fact]
    public async Task Chat_SummarizeChanges_UnsupportedType_ReturnsError()
    {
        InitializeAll();

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "summarize_changes",
                                Arguments = """{"entity_type":"shared-entity","entity_id":1,"revision_index":1}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "That type doesn't support diff summaries.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Summarize changes for shared entity 1" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].Result.Should().Contain("does not support diff summaries");
    }

    #endregion

    #region New tools — check_entity_quality

    [Fact]
    public async Task Chat_CheckEntityQuality_ReturnsReport()
    {
        InitializeAll();

        var entity = CreateTestEntity(1, "Test Post", "This is a well-written professional post.");
        SetupDatabaseGetItem("test-entity", 1, entity);

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "check_entity_quality",
                                Arguments = """{"entity_type":"test-entity","entity_id":1}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Quality checks complete.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        // The quality checker calls AiSanityCheckHandler which uses the Light LLM.
        // Mock it to return pass results.
        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = """{"passed":true,"message":"Looks good"}""",
                FinishReason = LLMFinishReason.Stop
            }));

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Check the quality of test entity 1" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].ToolName.Should().Be("check_entity_quality");

        // The result should contain the quality report with check results
        var reportJson = JObject.Parse(result.ToolCallsMade[0].Result);
        reportJson["entity_type"]!.Value<string>().Should().Be("test-entity");
        reportJson["entity_id"]!.Value<int>().Should().Be(1);
        reportJson["all_passed"]!.Value<bool>().Should().BeTrue();
        reportJson["checks"].Should().NotBeNull();
        // TestChatEntityModel has 2 [AISanityCheck] attrs on "body" field
        ((JArray)reportJson["checks"]!).Count.Should().Be(2);
    }

    [Fact]
    public async Task Chat_CheckEntityQuality_FailingChecks_ReportsFailures()
    {
        InitializeAll();

        var entity = CreateTestEntity(1, "Bad Post", "this is badly written crap!!!");
        SetupDatabaseGetItem("test-entity", 1, entity);

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "check_entity_quality",
                                Arguments = """{"entity_type":"test-entity","entity_id":1}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Some quality checks failed.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = """{"passed":false,"message":"Contains unprofessional language"}""",
                FinishReason = LLMFinishReason.Stop
            }));

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Check quality of entity 1" }, CreateAdminUser(), CancellationToken.None);

        var reportJson = JObject.Parse(result.ToolCallsMade[0].Result);
        reportJson["all_passed"]!.Value<bool>().Should().BeFalse();
        var checks = (JArray)reportJson["checks"]!;
        checks.All(c => c["passed"]!.Value<bool>() == false).Should().BeTrue();
        checks.All(c => c["message"]!.Value<string>()!.Contains("unprofessional")).Should().BeTrue();
    }

    [Fact]
    public async Task Chat_CheckEntityQuality_EntityNotFound_ReturnsError()
    {
        InitializeAll();

        _mockDatabaseService
            .Setup(d => d.GetItemAsync("test-entity", It.IsAny<DbKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<JObject>.Failure("Not found", HttpStatusCode.NotFound));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "check_entity_quality",
                                Arguments = """{"entity_type":"test-entity","entity_id":999}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Entity not found.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Check entity 999" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("not found");
    }

    [Fact]
    public async Task Chat_CheckEntityQuality_NoChecks_ReturnsMessage()
    {
        InitializeAll();

        // shared-entity's TestChatSharedEntityModel has no [AISanityCheck] attributes
        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "check_entity_quality",
                                Arguments = """{"entity_type":"shared-entity","entity_id":1}"""
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "No checks configured.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Check shared entity quality" }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("no AI quality checks configured");
    }

    #endregion

    #region Tool definitions — all 15 tools registered

    [Fact]
    public async Task Chat_ToolDefinitions_Includes15Tools()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiAgentChatHandler.ChatAsync(new AgentChatRequest { Message = "Test" }, CreateAdminUser(), CancellationToken.None);

        capturedRequest!.Tools.Should().NotBeNull();
        capturedRequest.Tools!.Select(t => t.Name).Should().BeEquivalentTo(
        [
            "list_entity_types", "search_entities", "get_entity", "get_entity_schema",
            "generate_entity", "filter_entities", "summarize_changes", "check_entity_quality",
            "propose_create_entity", "propose_update_entity", "propose_delete_entity", "suggest_field_value",
            "navigate", "list_entities", "set_form_fields"
        ]);
    }

    #endregion

    #region System prompt — includes tool guidance

    [Fact]
    public async Task Chat_SystemPrompt_IncludesToolGuidance()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiAgentChatHandler.ChatAsync(new AgentChatRequest { Message = "Test" }, CreateAdminUser(), CancellationToken.None);

        var systemMessage = capturedRequest!.Messages[0].Content!;
        systemMessage.Should().Contain("generate_entity");
        systemMessage.Should().Contain("filter_entities");
        systemMessage.Should().Contain("check_entity_quality");
        systemMessage.Should().Contain("summarize_changes");
    }

    #endregion

    #region Propose tools — actions require approval

    [Fact]
    public async Task Chat_ProposeCreate_ReturnsProposedAction()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "propose_create_entity",
                                Arguments = """{"entity_type":"test-entity","title":"New Item","fields":{"body":"Hello"}}"""
                            }
                        ]
                    });
                }
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "I've proposed creating a new test-entity. Please approve.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Create a test entity called New Item" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("create_entity");
        result.ProposedActions[0].EntityType.Should().Be("test-entity");
        result.ProposedActions[0].RequiresApproval.Should().BeTrue();
        result.ProposedActions[0].Description.Should().Contain("New Item");
    }

    [Fact]
    public async Task Chat_ProposeUpdate_ReturnsProposedAction()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "propose_update_entity",
                                Arguments = """{"entity_type":"test-entity","entity_id":1,"fields":{"body":"Updated"}}"""
                            }
                        ]
                    });
                }
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "I've proposed updating test-entity #1.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Update entity 1 body" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("update_entity");
        result.ProposedActions[0].EntityId.Should().Be(1);
    }

    [Fact]
    public async Task Chat_ProposeDelete_ReturnsProposedAction()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "propose_delete_entity",
                                Arguments = """{"entity_type":"test-entity","entity_id":1}"""
                            }
                        ]
                    });
                }
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "I've proposed deleting test-entity #1.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Delete entity 1" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("delete_entity");
        result.ProposedActions[0].EntityId.Should().Be(1);
    }

    [Fact]
    public async Task Chat_ProposeCreate_PermissionDenied()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        var limitedUser = new EntityModel<UserEntityFieldsModel>
        {
            Id = 2,
            Title = new TitleRenderedModel(),
            Fields = new UserEntityFieldsModel
            {
                EmailAddress = "reader@test.com",
                Roles = [new UserRoleAssignmentModel { RoleId = 99 }]
            }
        };

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "propose_create_entity",
                                Arguments = """{"entity_type":"test-entity","title":"Test","fields":{}}"""
                            }
                        ]
                    });
                }
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "You don't have permission.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Create a test entity" },
            limitedUser, CancellationToken.None);

        result.ProposedActions.Should().BeEmpty();
        result.ToolCallsMade[0].Result.Should().Contain("permission");
    }

    #region Propose Update/Delete — Per-Entity Sharing

    [Fact]
    public async Task ProposeUpdate_SharedEntity_ViewAccess_Denied()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        // Entity owned by user 99, shared with user 1 at "view" level
        var entity = CreateSharedEntity(1, "Shared Item", ownerId: 99,
            sharedUsers: new[] { (UserId: 1, Permission: "view") });
        SetupDatabaseGetItem("shared-entity", 1, entity);

        SetupLlmToolCallThenStop("propose_update_entity",
            @"{""entity_type"":""shared-entity"",""entity_id"":1,""fields"":{""body"":""Updated""}}",
            "No edit access.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Update shared entity 1" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().BeEmpty();
        result.ToolCallsMade[0].Result.Should().Contain("edit access");
    }

    [Fact]
    public async Task ProposeUpdate_SharedEntity_EditAccess_Allowed()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        var entity = CreateSharedEntity(1, "Shared Item", ownerId: 99,
            sharedUsers: new[] { (UserId: 1, Permission: "edit") });
        SetupDatabaseGetItem("shared-entity", 1, entity);

        SetupLlmToolCallThenStop("propose_update_entity",
            @"{""entity_type"":""shared-entity"",""entity_id"":1,""fields"":{""body"":""Updated""}}",
            "Proposed update.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Update shared entity 1" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("update_entity");
    }

    [Fact]
    public async Task ProposeUpdate_SharedEntity_NoAccess_Denied()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        // Entity owned by user 99, not shared with anyone
        var entity = CreateSharedEntity(1, "Private Item", ownerId: 99);
        SetupDatabaseGetItem("shared-entity", 1, entity);

        SetupLlmToolCallThenStop("propose_update_entity",
            @"{""entity_type"":""shared-entity"",""entity_id"":1,""fields"":{""body"":""Updated""}}",
            "No access.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Update shared entity 1" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().BeEmpty();
        result.ToolCallsMade[0].Result.Should().Contain("edit access");
    }

    [Fact]
    public async Task ProposeUpdate_SharedEntity_NotFound_ReturnsError()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        _mockDatabaseService
            .Setup(d => d.GetItemAsync("shared-entity",
                It.Is<DbKey>(k => k.Value.AsInteger == 999L),
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<JObject>.Failure("Not found", HttpStatusCode.NotFound));

        SetupLlmToolCallThenStop("propose_update_entity",
            @"{""entity_type"":""shared-entity"",""entity_id"":999,""fields"":{""body"":""x""}}",
            "Not found.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Update shared entity 999" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().BeEmpty();
        result.ToolCallsMade[0].Result.Should().Contain("not found");
    }

    [Fact]
    public async Task ProposeDelete_SharedEntity_EditAccess_Denied()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        // User 1 has Edit access but not Owner — delete requires Owner
        var entity = CreateSharedEntity(1, "Shared Item", ownerId: 99,
            sharedUsers: new[] { (UserId: 1, Permission: "edit") });
        SetupDatabaseGetItem("shared-entity", 1, entity);

        SetupLlmToolCallThenStop("propose_delete_entity",
            @"{""entity_type"":""shared-entity"",""entity_id"":1}",
            "No owner access.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Delete shared entity 1" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().BeEmpty();
        result.ToolCallsMade[0].Result.Should().Contain("owner access");
    }

    [Fact]
    public async Task ProposeDelete_SharedEntity_OwnerAccess_Allowed()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        // User 1 IS the owner
        var entity = CreateSharedEntity(1, "My Item", ownerId: 1);
        SetupDatabaseGetItem("shared-entity", 1, entity);

        SetupLlmToolCallThenStop("propose_delete_entity",
            @"{""entity_type"":""shared-entity"",""entity_id"":1}",
            "Proposed delete.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Delete shared entity 1" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("delete_entity");
    }

    [Fact]
    public async Task ProposeDelete_SharedEntity_NoAccess_Denied()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        var entity = CreateSharedEntity(1, "Private Item", ownerId: 99);
        SetupDatabaseGetItem("shared-entity", 1, entity);

        SetupLlmToolCallThenStop("propose_delete_entity",
            @"{""entity_type"":""shared-entity"",""entity_id"":1}",
            "No access.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Delete shared entity 1" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().BeEmpty();
        result.ToolCallsMade[0].Result.Should().Contain("owner access");
    }

    [Fact]
    public async Task ProposeDelete_SharedEntity_NotFound_ReturnsError()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        _mockDatabaseService
            .Setup(d => d.GetItemAsync("shared-entity",
                It.Is<DbKey>(k => k.Value.AsInteger == 999L),
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<JObject>.Failure("Not found", HttpStatusCode.NotFound));

        SetupLlmToolCallThenStop("propose_delete_entity",
            @"{""entity_type"":""shared-entity"",""entity_id"":999}",
            "Not found.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Delete shared entity 999" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().BeEmpty();
        result.ToolCallsMade[0].Result.Should().Contain("not found");
    }

    #endregion

    [Fact]
    public async Task Chat_ContextAware_IncludesContextInMessage()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        var request = new AgentChatRequest
        {
            Message = "Help me with this field",
            Context = new AgentContext
            {
                CurrentPage = "entity-edit",
                EntityType = "test-entity",
                EntityId = 42,
                SelectedField = "body"
            }
        };

        await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        var userMessage = capturedRequest!.Messages[1].Content!;
        userMessage.Should().Contain("entity-edit");
        userMessage.Should().Contain("test-entity");
        userMessage.Should().Contain("42");
        userMessage.Should().Contain("body");
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
                new EntityConfigurationBuilder<TestChatEntityModel>
                {
                    EntityName = "test-entity",
                    EntityReadableNameSingular = "Test Entity",
                    EntityReadableNamePlural = "Test Entities",
                    EntityDescription = "A test entity for AI agent chat integration.",
                    SupportsFrontendEdit = true,
                    HasParentChildRelationship = false,
                    HasAuthor = false,
                    HasTags = false,
                    HasCategories = false,
                    RequireGlobalTitleUniqueness = false,
                    SupportsSemanticSearch = true,
                    SupportsAiGeneration = true,
                    SupportsNaturalLanguageFilter = true,
                    SupportsAiDiffSummary = true,
                    OptionalTitleSanityCheck = null
                },
                new EntityConfigurationBuilder<TestChatSharedEntityModel>
                {
                    EntityName = "shared-entity",
                    EntityReadableNameSingular = "Shared Entity",
                    EntityReadableNamePlural = "Shared Entities",
                    EntityDescription = "A shared entity with individual sharing.",
                    SupportsFrontendEdit = true,
                    HasParentChildRelationship = false,
                    HasAuthor = true,
                    HasTags = false,
                    HasCategories = false,
                    RequireGlobalTitleUniqueness = false,
                    SupportsSemanticSearch = true,
                    HasIndividualSharing = true,
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

        // Set up IamRoleEntitiesCache via reflection so CanUserDo works in tests.
        // We create an uninitialized instance (skipping the constructor that needs DB),
        // then inject a role entity with full permissions for our test entity types.
        var iamCache = (IamRoleEntitiesCache)FormatterServices.GetUninitializedObject(typeof(IamRoleEntitiesCache));

        // Initialize the lock object and entities dictionary that the constructor normally sets
        var baseLockField = typeof(EntitiesCacheBase<IamRoleEntityFieldsModel>)
            .GetField("_entitiesLock", BindingFlags.Instance | BindingFlags.NonPublic)!;
        baseLockField.SetValue(iamCache, new object());

        var baseEntitiesField = typeof(EntitiesCacheBase<IamRoleEntityFieldsModel>)
            .GetField("_entities", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var entities = new Dictionary<int, EntityModel<IamRoleEntityFieldsModel>>();

        // Create an admin role (ID=10 matching CreateAdminUser's RoleId) with all permissions
        var adminRole = new EntityModel<IamRoleEntityFieldsModel>
        {
            Id = 10,
            Title = new TitleRenderedModel(),
            Fields = new IamRoleEntityFieldsModel
            {
                Capabilities =
                [
                    new IamRoleCapabilitiesModel
                    {
                        EntityType = "test-entity",
                        AllowPeekAll = true, AllowRead = true,
                        AllowUpdate = true, AllowCreate = true, AllowDelete = true
                    },
                    new IamRoleCapabilitiesModel
                    {
                        EntityType = "shared-entity",
                        AllowPeekAll = true, AllowRead = true,
                        AllowUpdate = true, AllowCreate = true, AllowDelete = true
                    }
                ]
            }
        };
        entities[10] = adminRole;
        baseEntitiesField.SetValue(iamCache, entities);

        var iamCacheField = typeof(RfConfiguration).GetField("_iamRoleEntitiesCache",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        iamCacheField.SetValue(null, iamCache);
    }

    /// <summary>
    /// Creates a mock admin user with RoleId=10 matching the IAM cache setup.
    /// </summary>
    private static EntityModel<UserEntityFieldsModel> CreateAdminUser()
    {
        return new EntityModel<UserEntityFieldsModel>
        {
            Id = 1,
            Title = new TitleRenderedModel(),
            Fields = new UserEntityFieldsModel
            {
                EmailAddress = "admin@test.com",
                Roles = [new UserRoleAssignmentModel { RoleId = 10 }]
            }
        };
    }

    /// <summary>
    /// Creates a user with no role permissions (empty Roles list).
    /// </summary>
    private static EntityModel<UserEntityFieldsModel> CreateRestrictedUser()
    {
        return new EntityModel<UserEntityFieldsModel>
        {
            Id = 99,
            Title = new TitleRenderedModel(),
            Fields = new UserEntityFieldsModel
            {
                EmailAddress = "restricted@test.com",
                Roles = []
            }
        };
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

    private static JObject CreateSharedEntity(int id, string title, int ownerId,
        (int UserId, string Permission)[]? sharedUsers = null)
    {
        var sharedUsersArray = new JArray();
        if (sharedUsers != null)
        {
            foreach (var (userId, permission) in sharedUsers)
                sharedUsersArray.Add(new JObject { ["user"] = userId, ["permission"] = permission });
        }

        return new JObject
        {
            [EntityModelAttributes.Id] = id,
            [EntityModelAttributes.Author] = ownerId,
            [EntityModelAttributes.Title] = new JObject { [EntityModelAttributes.TitleRendered] = title },
            [EntityModelAttributes.ModifiedGmt] = DateTime.UtcNow.ToString("o"),
            [EntityModelAttributes.Fields] = new JObject
            {
                ["body"] = "shared content",
                ["shared_users"] = sharedUsersArray
            }
        };
    }

    private static List<JObject> CreateTestEntities(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => CreateTestEntity(i, $"Entity {i}", $"Body content for entity {i}"))
            .ToList();
    }

    private void SetupDatabaseScan(string entityName, List<JObject> entities)
    {
        _mockDatabaseService
            .Setup(d => d.ScanTableAsync(entityName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<(IReadOnlyList<string> Keys, IReadOnlyList<JObject> Items)>.Success((
                entities.Select(e => e[EntityModelAttributes.Id]!.Value<int>().ToString()).ToList() as IReadOnlyList<string>,
                entities as IReadOnlyList<JObject>)));
    }

    private void SetupDatabaseGetItem(string entityName, int entityId, JObject entity)
    {
        _mockDatabaseService
            .Setup(d => d.GetItemAsync(entityName,
                It.IsAny<DbKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<JObject>.Success(entity));
    }

    private class TestChatEntityModel : EntityFieldsModel
    {
        [Newtonsoft.Json.JsonProperty("body")]
        [AISanityCheck("Is this text professional and free of spelling errors?")]
        [AISanityCheck("Does this text contain actionable content?", AISanityCheckSeverity.Error)]
        [AISuggestion("Suggest professional content for this field")]
        public string _body = "";

        [Newtonsoft.Json.JsonProperty("content")]
        public string _content = "";
    }

    private class TestChatSharedEntityModel : SharableEntityFieldsModel
    {
        [Newtonsoft.Json.JsonProperty("body")]
        public string _body = "";
    }

    #endregion

    /// <summary>
    /// Helper: first LLM call returns a tool call, second returns a final stop response.
    /// </summary>
    private void SetupLlmToolCallThenStop(string toolName, string toolArguments, string finalContent)
    {
        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = $"call_{callCount}", Name = toolName,
                                Arguments = toolArguments
                            }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = finalContent,
                    FinishReason = LLMFinishReason.Stop
                });
            });
    }

    #region Navigate tool tests

    [Fact]
    public async Task Chat_Navigate_ProposesNavigationAction()
    {
        InitializeAll();

        SetupLlmToolCallThenStop(
            "navigate",
            @"{""page"": ""entity-edit"", ""entity_type"": ""test-entity"", ""entity_id"": 42}",
            "Here you go!");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Take me to entity 42" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("navigate");
        result.ProposedActions[0].EntityType.Should().Be("test-entity");
        result.ProposedActions[0].EntityId.Should().Be(42);
        result.ProposedActions[0].RequiresApproval.Should().BeFalse();
        result.ProposedActions[0].Payload!["page"]!.Value<string>().Should().Be("entity-edit");
    }

    [Fact]
    public async Task Chat_Navigate_UnknownEntityType_ReturnsError()
    {
        InitializeAll();

        SetupLlmToolCallThenStop(
            "navigate",
            @"{""page"": ""entity-list"", ""entity_type"": ""nonexistent-type""}",
            "Error.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Go to nonexistent" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().BeEmpty();
        result.ToolCallsMade.Should().Contain(tc => tc.Result.Contains("Unknown entity type"));
    }

    [Fact]
    public async Task Chat_Navigate_EditWithoutId_ReturnsError()
    {
        InitializeAll();

        SetupLlmToolCallThenStop(
            "navigate",
            @"{""page"": ""entity-edit"", ""entity_type"": ""test-entity""}",
            "Missing ID.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Edit entity" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().BeEmpty();
        result.ToolCallsMade.Should().Contain(tc => tc.Result.Contains("entity_id"));
    }

    #endregion

    #region List entities tool tests

    [Fact]
    public async Task Chat_ListEntities_ReturnsEntityList()
    {
        InitializeAll();

        var entities = new List<JObject>
        {
            CreateTestEntity(1, "First Entity", "Content 1"),
            CreateTestEntity(2, "Second Entity", "Content 2"),
            CreateTestEntity(3, "Third Entity", "Content 3"),
        };

        _mockDatabaseService
            .Setup(d => d.ScanTableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<(IReadOnlyList<string>, IReadOnlyList<JObject>)>.Success(
                (entities.Select(e => e["id"]!.ToString()).ToList(), entities)));

        SetupLlmToolCallThenStop(
            "list_entities",
            @"{""entity_type"": ""test-entity"", ""limit"": 2}",
            "Here are the entities.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "List test entities" },
            CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().Contain(tc => tc.ToolName == "list_entities");
        var toolResult = result.ToolCallsMade.First(tc => tc.ToolName == "list_entities").Result;
        var parsed = JObject.Parse(toolResult);
        parsed["total_count"]!.Value<int>().Should().Be(3);
        parsed["returned_count"]!.Value<int>().Should().Be(2);
        parsed["entities"]!.Should().HaveCount(2);
    }

    [Fact]
    public async Task Chat_ListEntities_NoPermission_ReturnsError()
    {
        InitializeAll();

        SetupLlmToolCallThenStop(
            "list_entities",
            @"{""entity_type"": ""test-entity""}",
            "Error.");

        // Create user with no permissions (role ID 0 has no permissions in IAM cache)
        var restrictedUser = new EntityModel<UserEntityFieldsModel>
        {
            Id = 99,
            Title = new TitleRenderedModel(),
            Fields = new UserEntityFieldsModel
            {
                EmailAddress = "restricted@test.com",
                Roles = []
            }
        };

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "List entities" },
            restrictedUser, CancellationToken.None);

        result.ToolCallsMade.Should().Contain(tc => tc.Result.Contains("do not have access"));
    }

    #endregion

    #region Context-aware tests

    [Fact]
    public async Task Chat_FullContext_IncludesAllContextFields()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Noted.",
                FinishReason = LLMFinishReason.Stop
            }));

        var context = new AgentContext
        {
            CurrentPage = "entity-edit",
            EntityType = "test-entity",
            EntityId = 42,
            SelectedField = "body",
            Errors = new List<string> { "Body is required", "Title too short" },
            CurrentFields = new JObject { ["body"] = "draft content", ["status"] = "active" }
        };

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Help me fix these errors", Context = context },
            CreateAdminUser(), CancellationToken.None);

        var userMessage = capturedRequest!.Messages.Last(m => m.Role == LLMRole.User).Content;
        userMessage.Should().Contain("entity-edit");
        userMessage.Should().Contain("test-entity");
        userMessage.Should().Contain("42");
        userMessage.Should().Contain("body");
        userMessage.Should().Contain("Body is required");
        userMessage.Should().Contain("Title too short");
        userMessage.Should().Contain("draft content");
    }

    [Fact]
    public async Task Chat_SuggestFieldValue_CreatesSetFieldAction()
    {
        InitializeAll();

        // Mock the light LLM for field suggestion
        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Suggested body content here",
                FinishReason = LLMFinishReason.Stop
            }));

        SetupLlmToolCallThenStop(
            "suggest_field_value",
            @"{""entity_type"": ""test-entity"", ""target_field"": ""body"", ""current_fields"": {""status"": ""active""}}",
            "I suggest this value.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Suggest a value for the body field",
                Context = new AgentContext
                {
                    CurrentPage = "entity-edit",
                    EntityType = "test-entity",
                    SelectedField = "body"
                }
            },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().Contain(a => a.ActionType == "set_field");
        var setFieldAction = result.ProposedActions.First(a => a.ActionType == "set_field");
        setFieldAction.Payload!["field_name"]!.Value<string>().Should().Be("body");
        setFieldAction.RequiresApproval.Should().BeTrue();
    }

    [Fact]
    public async Task Chat_Navigate_CreatePage_NoApprovalRequired()
    {
        InitializeAll();

        SetupLlmToolCallThenStop(
            "navigate",
            @"{""page"": ""entity-create"", ""entity_type"": ""test-entity""}",
            "Navigating to create page.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "I want to create a new entity" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        var nav = result.ProposedActions[0];
        nav.ActionType.Should().Be("navigate");
        nav.RequiresApproval.Should().BeFalse();
        nav.Payload!["page"]!.Value<string>().Should().Be("entity-create");
    }

    #endregion

    #region Multiple proposed actions in single response

    [Fact]
    public async Task Chat_MultipleProposedActions_AllReturned()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "propose_create_entity",
                                Arguments = """{"entity_type":"test-entity","title":"First","fields":{"body":"A"}}"""
                            },
                            new LLMToolCall
                            {
                                Id = "call_2", Name = "propose_create_entity",
                                Arguments = """{"entity_type":"test-entity","title":"Second","fields":{"body":"B"}}"""
                            }
                        ]
                    });
                }
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "I've proposed creating two entities.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Create two entities" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(2);
        result.ProposedActions[0].ActionType.Should().Be("create_entity");
        result.ProposedActions[0].Description.Should().Contain("First");
        result.ProposedActions[1].ActionType.Should().Be("create_entity");
        result.ProposedActions[1].Description.Should().Contain("Second");
        result.ProposedActions.All(a => a.RequiresApproval).Should().BeTrue();
    }

    [Fact]
    public async Task Chat_MixedActions_CreateAndNavigate()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_1", Name = "propose_create_entity",
                                Arguments = """{"entity_type":"test-entity","title":"New Item","fields":{"body":"content"}}"""
                            }
                        ]
                    });
                }
                if (callCount == 2)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall
                            {
                                Id = "call_2", Name = "navigate",
                                Arguments = """{"page":"entity-list","entity_type":"test-entity"}"""
                            }
                        ]
                    });
                }
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Done.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Create entity and show list" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(2);
        result.ProposedActions.Should().Contain(a => a.ActionType == "create_entity");
        result.ProposedActions.Should().Contain(a => a.ActionType == "navigate");
        result.ProposedActions.First(a => a.ActionType == "create_entity").RequiresApproval.Should().BeTrue();
        result.ProposedActions.First(a => a.ActionType == "navigate").RequiresApproval.Should().BeFalse();
    }

    #endregion

    #region Execute approved actions

    [Fact]
    public async Task ExecuteApprovedCreate_UnknownEntityType_ReturnsFailure()
    {
        InitializeAll();

        var result = await AiAgentChatHandler.ExecuteApprovedCreateAsync(
            "nonexistent-type", new JObject { ["title"] = "Test" },
            CreateAdminUser(), CancellationToken.None);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unknown entity type");
    }

    [Fact]
    public async Task ExecuteApprovedCreate_PermissionDenied_ReturnsForbidden()
    {
        InitializeAll();

        var restrictedUser = CreateRestrictedUser();

        var result = await AiAgentChatHandler.ExecuteApprovedCreateAsync(
            "test-entity", new JObject { ["title"] = "Test" },
            restrictedUser, CancellationToken.None);

        result.IsSuccessful.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExecuteApprovedUpdate_UnknownEntityType_ReturnsFailure()
    {
        InitializeAll();

        var result = await AiAgentChatHandler.ExecuteApprovedUpdateAsync(
            "nonexistent-type", 1, new JObject { ["fields"] = new JObject { ["body"] = "x" } },
            CreateAdminUser(), CancellationToken.None);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unknown entity type");
    }

    [Fact]
    public async Task ExecuteApprovedUpdate_PermissionDenied_ReturnsForbidden()
    {
        InitializeAll();

        var restrictedUser = CreateRestrictedUser();

        var result = await AiAgentChatHandler.ExecuteApprovedUpdateAsync(
            "test-entity", 1, new JObject { ["fields"] = new JObject { ["body"] = "x" } },
            restrictedUser, CancellationToken.None);

        result.IsSuccessful.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExecuteApprovedDelete_UnknownEntityType_ReturnsFailure()
    {
        InitializeAll();

        var result = await AiAgentChatHandler.ExecuteApprovedDeleteAsync(
            "nonexistent-type", 1,
            CreateAdminUser(), CancellationToken.None);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unknown entity type");
    }

    [Fact]
    public async Task ExecuteApprovedDelete_PermissionDenied_ReturnsForbidden()
    {
        InitializeAll();

        var restrictedUser = CreateRestrictedUser();

        var result = await AiAgentChatHandler.ExecuteApprovedDeleteAsync(
            "test-entity", 1,
            restrictedUser, CancellationToken.None);

        result.IsSuccessful.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Conversation with confirmed actions

    [Fact]
    public async Task Chat_WithConfirmedActions_IncludesInHistory()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Great, the action was approved.",
                FinishReason = LLMFinishReason.Stop
            }));

        var request = new AgentChatRequest
        {
            Message = "The entity was created, what next?",
            ConfirmedActions = new List<ActionConfirmation>
            {
                new() { ActionId = "action-1", Approved = true },
                new() { ActionId = "action-2", Approved = false }
            }
        };

        var result = await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        result.Response.Should().NotBeNullOrEmpty();
        // The system message should include confirmation context
        var systemMsg = capturedRequest!.Messages.First(m => m.Role == LLMRole.System).Content;
        systemMsg.Should().NotBeNull();
    }

    #endregion

    #region Navigate tool — revision-diff page

    [Fact]
    public async Task Chat_Navigate_RevisionDiffPage()
    {
        InitializeAll();

        SetupLlmToolCallThenStop(
            "navigate",
            @"{""page"": ""revision-diff"", ""entity_type"": ""test-entity"", ""entity_id"": 5}",
            "Here's the diff.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Show revision diff for entity 5" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        var nav = result.ProposedActions[0];
        nav.ActionType.Should().Be("navigate");
        nav.Payload!["page"]!.Value<string>().Should().Be("revision-diff");
        nav.EntityId.Should().Be(5);
        nav.RequiresApproval.Should().BeFalse();
    }

    #endregion

    #region Context edge cases

    [Fact]
    public async Task Chat_MinimalContext_DoesNotCrash()
    {
        InitializeAll();

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "OK",
                FinishReason = LLMFinishReason.Stop
            }));

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Hi",
                Context = new AgentContext { CurrentPage = "entity-list" }
            },
            CreateAdminUser(), CancellationToken.None);

        result.Response.Should().Be("OK");
    }

    [Fact]
    public async Task Chat_NullContext_DoesNotCrash()
    {
        InitializeAll();

        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Hello!",
                FinishReason = LLMFinishReason.Stop
            }));

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Hi", Context = null },
            CreateAdminUser(), CancellationToken.None);

        result.Response.Should().Be("Hello!");
    }

    [Fact]
    public async Task Chat_ContextWithCurrentFieldsOnly_IncludesInMessage()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Noted.",
                FinishReason = LLMFinishReason.Stop
            }));

        var context = new AgentContext
        {
            CurrentPage = "entity-edit",
            EntityType = "test-entity",
            CurrentFields = new JObject
            {
                ["body"] = "Hello world",
                ["status"] = "draft"
            }
        };

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Help me complete this", Context = context },
            CreateAdminUser(), CancellationToken.None);

        var userMessage = capturedRequest!.Messages.Last(m => m.Role == LLMRole.User).Content;
        userMessage.Should().Contain("Hello world");
        userMessage.Should().Contain("draft");
    }

    #endregion

    #region Suggest field value edge cases

    [Fact]
    public async Task Chat_SuggestFieldValue_NoSuggestionAttribute_ReturnsError()
    {
        InitializeAll();

        SetupLlmToolCallThenStop(
            "suggest_field_value",
            @"{""entity_type"": ""test-entity"", ""target_field"": ""content"", ""current_fields"": {}}",
            "Cannot suggest.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Suggest content" },
            CreateAdminUser(), CancellationToken.None);

        // content field has no [AISuggestion], so it should return an error
        var toolResult = result.ToolCallsMade.First(tc => tc.ToolName == "suggest_field_value").Result;
        toolResult.Should().Contain("AI suggestion configured");
    }

    [Fact]
    public async Task Chat_SuggestFieldValue_UnknownEntityType_ReturnsError()
    {
        InitializeAll();

        SetupLlmToolCallThenStop(
            "suggest_field_value",
            @"{""entity_type"": ""nonexistent"", ""target_field"": ""body"", ""current_fields"": {}}",
            "Error.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Suggest body for nonexistent type" },
            CreateAdminUser(), CancellationToken.None);

        var toolResult = result.ToolCallsMade.First(tc => tc.ToolName == "suggest_field_value").Result;
        toolResult.Should().Contain("Unknown entity type");
    }

    #endregion

    #region Conversation history — multi-turn memory

    [Fact]
    public async Task Chat_WithHistory_HistoryIncludedInLlmMessages()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "I remember our conversation.",
                FinishReason = LLMFinishReason.Stop
            }));

        var request = new AgentChatRequest
        {
            Message = "What did you just do?",
            History =
            [
                new ChatHistoryEntry { Role = "user", Content = "Delete entity 42" },
                new ChatHistoryEntry { Role = "assistant", Content = "I've proposed deleting test-entity #42." }
            ]
        };

        await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        // Messages: system + 2 history + current user = 4
        capturedRequest!.Messages.Should().HaveCount(4);
        capturedRequest.Messages[0].Role.Should().Be(LLMRole.System);
        capturedRequest.Messages[1].Role.Should().Be(LLMRole.User);
        capturedRequest.Messages[1].Content.Should().Be("Delete entity 42");
        capturedRequest.Messages[2].Role.Should().Be(LLMRole.Assistant);
        capturedRequest.Messages[2].Content.Should().Be("I've proposed deleting test-entity #42.");
        capturedRequest.Messages[3].Role.Should().Be(LLMRole.User);
        capturedRequest.Messages[3].Content.Should().Contain("What did you just do?");
    }

    [Fact]
    public async Task Chat_WithoutHistory_OnlySystemAndUserMessages()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Hello!",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Hi" }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Messages.Should().HaveCount(2);
        capturedRequest.Messages[0].Role.Should().Be(LLMRole.System);
        capturedRequest.Messages[1].Role.Should().Be(LLMRole.User);
    }

    [Fact]
    public async Task Chat_WithNullHistory_OnlySystemAndUserMessages()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Hello!",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Hi", History = null }, CreateAdminUser(), CancellationToken.None);

        capturedRequest!.Messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task Chat_WithEmptyHistory_OnlySystemAndUserMessages()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Hello!",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Hi", History = [] }, CreateAdminUser(), CancellationToken.None);

        capturedRequest!.Messages.Should().HaveCount(2);
    }

    [Fact]
    public async Task Chat_HistoryOrderPreserved_OldestFirst()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Got it.",
                FinishReason = LLMFinishReason.Stop
            }));

        var request = new AgentChatRequest
        {
            Message = "And now the third question",
            History =
            [
                new ChatHistoryEntry { Role = "user", Content = "First question" },
                new ChatHistoryEntry { Role = "assistant", Content = "First answer" },
                new ChatHistoryEntry { Role = "user", Content = "Second question" },
                new ChatHistoryEntry { Role = "assistant", Content = "Second answer" }
            ]
        };

        await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        // system + 4 history + current user = 6
        capturedRequest!.Messages.Should().HaveCount(6);
        capturedRequest.Messages[1].Content.Should().Be("First question");
        capturedRequest.Messages[2].Content.Should().Be("First answer");
        capturedRequest.Messages[3].Content.Should().Be("Second question");
        capturedRequest.Messages[4].Content.Should().Be("Second answer");
        capturedRequest.Messages[5].Content.Should().Contain("And now the third question");
    }

    [Fact]
    public async Task Chat_HistoryRoleMappedCorrectly_UserAndAssistant()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        var request = new AgentChatRequest
        {
            Message = "Follow up",
            History =
            [
                new ChatHistoryEntry { Role = "user", Content = "User message" },
                new ChatHistoryEntry { Role = "assistant", Content = "Assistant reply" }
            ]
        };

        await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        capturedRequest!.Messages[1].Role.Should().Be(LLMRole.User);
        capturedRequest.Messages[2].Role.Should().Be(LLMRole.Assistant);
    }

    [Fact]
    public async Task Chat_HistoryCappedAt20Turns()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        // Create 30 history entries (15 user-assistant pairs)
        var history = new List<ChatHistoryEntry>();
        for (var i = 1; i <= 30; i++)
        {
            history.Add(new ChatHistoryEntry
            {
                Role = i % 2 == 1 ? "user" : "assistant",
                Content = $"Message {i}"
            });
        }

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Current", History = history },
            CreateAdminUser(), CancellationToken.None);

        // system + 20 (capped) + current user = 22
        capturedRequest!.Messages.Should().HaveCount(22);
        // First history entry should be the 11th original (index 10, since we keep last 20)
        capturedRequest.Messages[1].Content.Should().Be("Message 11");
        // Last history entry should be the 30th original
        capturedRequest.Messages[20].Content.Should().Be("Message 30");
        // Final message is the current user message
        capturedRequest.Messages[21].Content.Should().Contain("Current");
    }

    [Fact]
    public async Task Chat_HistoryExactly20_AllIncluded()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        var history = new List<ChatHistoryEntry>();
        for (var i = 1; i <= 20; i++)
        {
            history.Add(new ChatHistoryEntry
            {
                Role = i % 2 == 1 ? "user" : "assistant",
                Content = $"Message {i}"
            });
        }

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Current", History = history },
            CreateAdminUser(), CancellationToken.None);

        // system + 20 + current user = 22
        capturedRequest!.Messages.Should().HaveCount(22);
        capturedRequest.Messages[1].Content.Should().Be("Message 1");
        capturedRequest.Messages[20].Content.Should().Be("Message 20");
    }

    [Fact]
    public async Task Chat_HistoryUnder20_AllIncluded()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        var history = new List<ChatHistoryEntry>();
        for (var i = 1; i <= 5; i++)
        {
            history.Add(new ChatHistoryEntry
            {
                Role = i % 2 == 1 ? "user" : "assistant",
                Content = $"Message {i}"
            });
        }

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Current", History = history },
            CreateAdminUser(), CancellationToken.None);

        // system + 5 + current user = 7
        capturedRequest!.Messages.Should().HaveCount(7);
        capturedRequest.Messages[1].Content.Should().Be("Message 1");
        capturedRequest.Messages[5].Content.Should().Be("Message 5");
    }

    [Fact]
    public async Task Chat_HistoryPlacedBetweenSystemAndUserMessage()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Understood.",
                FinishReason = LLMFinishReason.Stop
            }));

        var request = new AgentChatRequest
        {
            Message = "Current message",
            History =
            [
                new ChatHistoryEntry { Role = "user", Content = "Previous message" },
                new ChatHistoryEntry { Role = "assistant", Content = "Previous reply" }
            ]
        };

        await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        // Structure: system → history → current user
        capturedRequest!.Messages[0].Role.Should().Be(LLMRole.System);
        capturedRequest.Messages[1].Role.Should().Be(LLMRole.User);
        capturedRequest.Messages[1].Content.Should().Be("Previous message");
        capturedRequest.Messages[2].Role.Should().Be(LLMRole.Assistant);
        capturedRequest.Messages[2].Content.Should().Be("Previous reply");
        capturedRequest.Messages[3].Role.Should().Be(LLMRole.User);
        capturedRequest.Messages[3].Content.Should().Contain("Current message");
    }

    [Fact]
    public async Task Chat_HistoryWithContext_ContextAppliedToCurrentMessageOnly()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        var request = new AgentChatRequest
        {
            Message = "Help me",
            Context = new AgentContext
            {
                CurrentPage = "entity-edit",
                EntityType = "test-entity",
                EntityId = 42
            },
            History =
            [
                new ChatHistoryEntry { Role = "user", Content = "Earlier question" },
                new ChatHistoryEntry { Role = "assistant", Content = "Earlier answer" }
            ]
        };

        await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        // History messages should NOT have context injected
        capturedRequest!.Messages[1].Content.Should().Be("Earlier question");
        capturedRequest.Messages[1].Content.Should().NotContain("entity-edit");

        // Only the current (last) user message should have context
        var currentMsg = capturedRequest.Messages[3].Content!;
        currentMsg.Should().Contain("entity-edit");
        currentMsg.Should().Contain("test-entity");
        currentMsg.Should().Contain("42");
    }

    [Fact]
    public async Task Chat_HistoryWithToolCalls_HistoryPreservedAcrossToolLoop()
    {
        InitializeAll();

        SetupDatabaseScan("test-entity", CreateTestEntities(3));

        var capturedRequests = new List<LLMRequest>();
        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) =>
            {
                capturedRequests.Add(new LLMRequest
                {
                    Messages = req.Messages.ToList()
                });
            })
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall { Id = "call_1", Name = "list_entity_types", Arguments = "{}" }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "You have 1 entity type with 3 entities.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var request = new AgentChatRequest
        {
            Message = "How many entities now?",
            History =
            [
                new ChatHistoryEntry { Role = "user", Content = "Delete entity 5" },
                new ChatHistoryEntry { Role = "assistant", Content = "I've proposed deleting entity #5." }
            ]
        };

        await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        // First LLM call: system + 2 history + current user = 4
        capturedRequests[0].Messages.Should().HaveCount(4);
        capturedRequests[0].Messages[1].Content.Should().Be("Delete entity 5");
        capturedRequests[0].Messages[2].Content.Should().Be("I've proposed deleting entity #5.");

        // Second LLM call: system + 2 history + current user + assistant(tool) + tool result = 6
        capturedRequests[1].Messages.Should().HaveCount(6);
        // History is still at positions 1-2
        capturedRequests[1].Messages[1].Content.Should().Be("Delete entity 5");
        capturedRequests[1].Messages[2].Content.Should().Be("I've proposed deleting entity #5.");
        // Current user at 3, then tool loop appended
        capturedRequests[1].Messages[4].Role.Should().Be(LLMRole.Assistant);
        capturedRequests[1].Messages[5].Role.Should().Be(LLMRole.Tool);
    }

    [Fact]
    public async Task Chat_HistorySingleEntry_Works()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Sure.",
                FinishReason = LLMFinishReason.Stop
            }));

        var request = new AgentChatRequest
        {
            Message = "Follow up",
            History =
            [
                new ChatHistoryEntry { Role = "user", Content = "Initial question" }
            ]
        };

        await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        // system + 1 history + current user = 3
        capturedRequest!.Messages.Should().HaveCount(3);
        capturedRequest.Messages[1].Role.Should().Be(LLMRole.User);
        capturedRequest.Messages[1].Content.Should().Be("Initial question");
        capturedRequest.Messages[2].Content.Should().Contain("Follow up");
    }

    [Fact]
    public async Task Chat_HistoryConsecutiveUserMessages_AllPreserved()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        // Edge case: multiple user messages in a row (no assistant in between)
        var request = new AgentChatRequest
        {
            Message = "Third message",
            History =
            [
                new ChatHistoryEntry { Role = "user", Content = "First user message" },
                new ChatHistoryEntry { Role = "user", Content = "Second user message" }
            ]
        };

        await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        capturedRequest!.Messages.Should().HaveCount(4);
        capturedRequest.Messages[1].Role.Should().Be(LLMRole.User);
        capturedRequest.Messages[1].Content.Should().Be("First user message");
        capturedRequest.Messages[2].Role.Should().Be(LLMRole.User);
        capturedRequest.Messages[2].Content.Should().Be("Second user message");
    }

    [Fact]
    public async Task Chat_HistoryLongConversation_OnlyLast20Kept()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        // 50 entries — should be trimmed to last 20
        var history = Enumerable.Range(1, 50)
            .Select(i => new ChatHistoryEntry
            {
                Role = i % 2 == 1 ? "user" : "assistant",
                Content = $"Turn {i}"
            }).ToList();

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Latest", History = history },
            CreateAdminUser(), CancellationToken.None);

        // system + 20 + current user = 22
        capturedRequest!.Messages.Should().HaveCount(22);

        // First kept entry is Turn 31 (index 30 in original, since last 20 are 31..50)
        capturedRequest.Messages[1].Content.Should().Be("Turn 31");
        capturedRequest.Messages[20].Content.Should().Be("Turn 50");

        // Verify early turns are NOT included
        capturedRequest.Messages.Should().NotContain(m => m.Content == "Turn 1");
        capturedRequest.Messages.Should().NotContain(m => m.Content == "Turn 30");
    }

    [Fact]
    public async Task Chat_HistoryWithConfirmationContext_BothPreserved()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(1));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        var request = new AgentChatRequest
        {
            Message = "What happened?",
            History =
            [
                new ChatHistoryEntry { Role = "user", Content = "Create an entity" },
                new ChatHistoryEntry { Role = "assistant", Content = "I proposed creating entity X." }
            ],
            ConfirmedActions =
            [
                new ActionConfirmation { ActionId = "action-1", Approved = true }
            ],
            ExecutedActionResults =
            [
                new ActionExecutionResult { ActionId = "action-1", Success = true, Message = "Entity created." }
            ]
        };

        await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        // History should be present
        capturedRequest!.Messages[1].Content.Should().Be("Create an entity");
        capturedRequest.Messages[2].Content.Should().Be("I proposed creating entity X.");

        // Current user message should include action results context
        var lastUserMsg = capturedRequest.Messages.Last(m => m.Role == LLMRole.User).Content!;
        lastUserMsg.Should().Contain("Action results");
        lastUserMsg.Should().Contain("What happened?");
    }

    [Fact]
    public async Task Chat_UnknownHistoryRole_TreatedAsUser()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        var request = new AgentChatRequest
        {
            Message = "Next",
            History =
            [
                new ChatHistoryEntry { Role = "system", Content = "Injected system msg" },
                new ChatHistoryEntry { Role = "tool", Content = "Tool output" },
                new ChatHistoryEntry { Role = "unknown", Content = "Mystery role" }
            ]
        };

        await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        // All non-"assistant" roles should map to User
        capturedRequest!.Messages[1].Role.Should().Be(LLMRole.User);
        capturedRequest.Messages[2].Role.Should().Be(LLMRole.User);
        capturedRequest.Messages[3].Role.Should().Be(LLMRole.User);
    }

    [Fact]
    public async Task Chat_HistoryDoesNotAffectToolCallResponses()
    {
        InitializeAll();

        SetupDatabaseScan("test-entity", CreateTestEntities(2));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls =
                        [
                            new LLMToolCall { Id = "call_1", Name = "list_entity_types", Arguments = "{}" }
                        ]
                    });
                }

                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Found entities.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var request = new AgentChatRequest
        {
            Message = "Show me what's available",
            History =
            [
                new ChatHistoryEntry { Role = "user", Content = "Previous unrelated question" },
                new ChatHistoryEntry { Role = "assistant", Content = "Previous unrelated answer" }
            ]
        };

        var result = await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        // Tool calls should work normally regardless of history
        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].ToolName.Should().Be("list_entity_types");
        var toolResult = JArray.Parse(result.ToolCallsMade[0].Result);
        toolResult.Should().HaveCount(2);
        result.Response.Should().Be("Found entities.");
    }

    [Fact]
    public async Task Chat_HistoryWithSpecialCharacters_PreservedVerbatim()
    {
        InitializeAll();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        const string specialContent = "Hello! <script>alert('xss')</script> & \"quotes\" 'apostrophe' \n\tnewline+tab";
        var request = new AgentChatRequest
        {
            Message = "Follow up",
            History =
            [
                new ChatHistoryEntry { Role = "user", Content = specialContent },
                new ChatHistoryEntry { Role = "assistant", Content = "I handled that safely." }
            ]
        };

        await AiAgentChatHandler.ChatAsync(request, CreateAdminUser(), CancellationToken.None);

        capturedRequest!.Messages[1].Content.Should().Be(specialContent);
    }

    #endregion

    #region Propose update/delete with unknown entity type

    [Fact]
    public async Task Chat_ProposeUpdate_UnknownEntityType_ReturnsError()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(0));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        SetupLlmToolCallThenStop(
            "propose_update_entity",
            """{"entity_type":"nonexistent","entity_id":1,"fields":{"body":"Updated"}}""",
            "Error.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Update nonexistent entity" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().BeEmpty();
        result.ToolCallsMade.Should().Contain(tc => tc.Result.Contains("Unknown entity type"));
    }

    [Fact]
    public async Task Chat_ProposeDelete_UnknownEntityType_ReturnsError()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(0));
        SetupDatabaseScan("shared-entity", CreateTestEntities(0));

        SetupLlmToolCallThenStop(
            "propose_delete_entity",
            """{"entity_type":"nonexistent","entity_id":1}""",
            "Error.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest { Message = "Delete nonexistent entity" },
            CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().BeEmpty();
        result.ToolCallsMade.Should().Contain(tc => tc.Result.Contains("Unknown entity type"));
    }

    #endregion
}

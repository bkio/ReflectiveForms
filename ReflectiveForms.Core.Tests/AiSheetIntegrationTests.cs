using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
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
/// Integration tests for the AI Agent Chat — Sheet system integration.
/// Tests the 6 sheet tools: list_sheets, get_sheet_summary, get_sheet_cell_value,
/// suggest_formula, propose_sheet_formulas, propose_add_sheet_source.
/// </summary>
[Collection("AI")]
public class AiSheetIntegrationTests : IDisposable
{
    private readonly Mock<ILLMService> _mockHeavyLlm = new();
    private readonly Mock<ILLMService> _mockLightLlm = new();
    private readonly Mock<IVectorService> _mockVectorService = new();
    private readonly Mock<IDatabaseService> _mockDatabaseService;
    private readonly Mock<IMemoryService> _mockMemoryService;

    public AiSheetIntegrationTests()
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

    #region list_sheets — Read Tools

    [Fact]
    public async Task ListSheets_ReturnsAllAccessibleSheets()
    {
        InitializeAll();

        var sheets = new List<JObject>
        {
            CreateSheetEntity(1, "Sales Report", author: 1, sources: new[] { "product" }),
            CreateSheetEntity(2, "Inventory", author: 1, sources: new[] { "product", "warehouse" }),
            CreateSheetEntity(3, "Private Sheet", author: 99, isPublic: false)
        };

        SetupSheetScan(sheets);

        SetupLlmToolCallThenStop("list_sheets", "{}", "Here are your sheets.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Show me my sheets",
                Context = new AgentContext { CurrentPage = "sheet-list" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].ToolName.Should().Be("list_sheets");

        var toolResult = JObject.Parse(result.ToolCallsMade[0].Result);
        toolResult["total_count"]!.Value<int>().Should().Be(2);
        var sheetsArr = (JArray)toolResult["sheets"]!;
        sheetsArr.Select(s => s["id"]!.Value<int>()).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task ListSheets_EmptyWhenNoSheets()
    {
        InitializeAll();
        SetupSheetScan(new List<JObject>());

        SetupLlmToolCallThenStop("list_sheets", "{}", "No sheets found.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "List sheets",
                Context = new AgentContext { CurrentPage = "sheet-list" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("No sheets found");
    }

    [Fact]
    public async Task ListSheets_UnauthorizedUser_ReturnsError()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("list_sheets", "{}", "You don't have access.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "List sheets",
                Context = new AgentContext { CurrentPage = "sheet-list" }
            }, CreateRestrictedUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error");
    }

    #endregion

    #region get_sheet_summary — Read Tools

    [Fact]
    public async Task GetSheetSummary_ReturnsFormulaInventory()
    {
        InitializeAll();

        var workbook = new JObject
        {
            ["sheets"] = new JObject
            {
                ["sheet1"] = new JObject
                {
                    ["cellData"] = new JObject
                    {
                        ["0"] = new JObject
                        {
                            ["0"] = new JObject { ["v"] = "Product", ["f"] = "=RF.FIELD(\"product\",1,\"name\")" },
                            ["1"] = new JObject { ["v"] = 100, ["f"] = "=RF.SUM(\"product\",\"price\")" },
                            ["2"] = new JObject { ["v"] = 5, ["f"] = "=RF.COUNT(\"product\")" }
                        },
                        ["1"] = new JObject
                        {
                            ["0"] = new JObject { ["v"] = "Another", ["f"] = "=RF.FIELD(\"product\",2,\"name\")" }
                        }
                    }
                }
            }
        };

        var sheet = CreateSheetEntity(1, "Sales Dashboard", author: 1,
            sources: new[] { "product" }, workbookData: workbook.ToString());
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_summary", @"{""sheet_id"": 1}", "Here's the summary.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Describe sheet 1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].ToolName.Should().Be("get_sheet_summary");
        var summary = JObject.Parse(result.ToolCallsMade[0].Result);
        summary["title"]!.Value<string>().Should().Be("Sales Dashboard");
        summary["cell_count"]!.Value<int>().Should().Be(4);
        summary["formula_inventory"]!["RF.FIELD"]!.Value<int>().Should().Be(2);
        summary["formula_inventory"]!["RF.SUM"]!.Value<int>().Should().Be(1);
        summary["formula_inventory"]!["RF.COUNT"]!.Value<int>().Should().Be(1);
    }

    [Fact]
    public async Task GetSheetSummary_NonExistentSheet_ReturnsError()
    {
        InitializeAll();

        _mockDatabaseService
            .Setup(d => d.GetItemAsync("rf-sheets",
                It.Is<DbKey>(k => k.Value.AsInteger == 999L),
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<JObject>.Failure("Not found", HttpStatusCode.NotFound));

        SetupLlmToolCallThenStop("get_sheet_summary", @"{""sheet_id"": 999}", "Sheet not found.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Describe sheet 999",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("not found");
    }

    [Fact]
    public async Task GetSheetSummary_UnauthorizedUser_ReturnsDenied()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("get_sheet_summary", @"{""sheet_id"": 1}", "Access denied.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Describe sheet 1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateRestrictedUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error");
    }

    #endregion

    #region get_sheet_cell_value — Read Tools

    [Fact]
    public async Task GetSheetCellValue_ReturnsCachedValue()
    {
        InitializeAll();

        var workbook = CreateWorkbookWithCells(new Dictionary<(int Row, int Col), (object? Value, string? Formula)>
        {
            [(0, 0)] = ("Hello", null)
        });

        var sheet = CreateSheetEntity(1, "Test", author: 1, workbookData: workbook);
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_cell_value", @"{""sheet_id"": 1, ""range"": ""A1""}", "The cell value is Hello.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "What's in cell A1?",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].ToolName.Should().Be("get_sheet_cell_value");
        var cellResult = JObject.Parse(result.ToolCallsMade[0].Result);
        var cells = (JArray)cellResult["cells"]!;
        cells.Should().HaveCount(1);
        cells[0]["cell"]!.Value<string>().Should().Be("A1");
        cells[0]["value"]!.Value<string>().Should().Be("Hello");
    }

    [Fact]
    public async Task GetSheetCellValue_ReturnsFormulaAndValue()
    {
        InitializeAll();

        var workbook = CreateWorkbookWithCells(new Dictionary<(int Row, int Col), (object? Value, string? Formula)>
        {
            [(0, 0)] = (42, "=RF.COUNT(\"product\")")
        });

        var sheet = CreateSheetEntity(1, "Test", author: 1, workbookData: workbook);
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_cell_value", @"{""sheet_id"": 1, ""range"": ""A1""}", "Cell A1 has formula.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "What formula is in A1?",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        var cellResult = JObject.Parse(result.ToolCallsMade[0].Result);
        var cells = (JArray)cellResult["cells"]!;
        cells[0]["value"]!.Value<int>().Should().Be(42);
        cells[0]["formula"]!.Value<string>().Should().Be("=RF.COUNT(\"product\")");
    }

    [Fact]
    public async Task GetSheetCellValue_RangeQuery_ReturnsMultipleCells()
    {
        InitializeAll();

        var cellData = new Dictionary<(int Row, int Col), (object? Value, string? Formula)>
        {
            [(0, 0)] = ("A", null),
            [(0, 1)] = ("B", null),
            [(1, 0)] = (1, null),
            [(1, 1)] = (2, null),
            [(2, 0)] = (3, null),
            [(2, 1)] = (4, null)
        };
        var workbook = CreateWorkbookWithCells(cellData);
        var sheet = CreateSheetEntity(1, "Grid", author: 1, workbookData: workbook);
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_cell_value", @"{""sheet_id"": 1, ""range"": ""A1:B3""}", "Here's the data.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Show me A1:B3",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        var cellResult = JObject.Parse(result.ToolCallsMade[0].Result);
        var cells = (JArray)cellResult["cells"]!;
        cells.Should().HaveCount(6);
    }

    [Fact]
    public async Task GetSheetCellValue_EmptyCell_ReturnsNull()
    {
        InitializeAll();

        var workbook = CreateWorkbookWithCells(new Dictionary<(int Row, int Col), (object? Value, string? Formula)>());
        var sheet = CreateSheetEntity(1, "Empty", author: 1, workbookData: workbook);
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_cell_value", @"{""sheet_id"": 1, ""range"": ""C5""}", "Cell is empty.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "What's in C5?",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        var cellResult = JObject.Parse(result.ToolCallsMade[0].Result);
        var cells = (JArray)cellResult["cells"]!;
        cells[0]["value"]!.Type.Should().Be(JTokenType.Null);
    }

    [Fact]
    public async Task GetSheetCellValue_InvalidRange_ReturnsError()
    {
        InitializeAll();

        var sheet = CreateSheetEntity(1, "Test", author: 1);
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_cell_value", @"{""sheet_id"": 1, ""range"": ""INVALID""}", "Invalid range.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Show me cell INVALID",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("Invalid range");
    }

    #endregion

    #region suggest_formula — Mutation Tools

    [Fact]
    public async Task SuggestFormula_SimpleAggregation_ReturnsSumFormula()
    {
        InitializeAll();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "=RF.SUM(\"product\",\"price\")",
                FinishReason = LLMFinishReason.Stop
            }));

        SetupLlmToolCallThenStop("suggest_formula",
            @"{""description"": ""total price of all products"", ""entity_type"": ""test-entity""}",
            "Here's the formula.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "What formula gives total price?",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].ToolName.Should().Be("suggest_formula");
        var toolResult = JObject.Parse(result.ToolCallsMade[0].Result);
        toolResult["formula"]!.Value<string>().Should().StartWith("=RF.SUM");
    }

    [Fact]
    public async Task SuggestFormula_ListRequest_ReturnsListFormula()
    {
        InitializeAll();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "=RF.LIST(\"test-entity\",\"body\")",
                FinishReason = LLMFinishReason.Stop
            }));

        SetupLlmToolCallThenStop("suggest_formula",
            @"{""description"": ""list all body values"", ""entity_type"": ""test-entity"", ""field_name"": ""body""}",
            "Here's the formula.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "How do I list all body values?",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        var toolResult = JObject.Parse(result.ToolCallsMade[0].Result);
        toolResult["formula"]!.Value<string>().Should().Contain("RF.LIST");
    }

    [Fact]
    public async Task SuggestFormula_InvalidEntity_ReturnsError()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("suggest_formula",
            @"{""description"": ""count items"", ""entity_type"": ""nonexistent-entity""}",
            "Entity not found.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Formula for nonexistent entity",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("not found");
    }

    [Fact]
    public async Task SuggestFormula_InvalidField_ReturnsError()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("suggest_formula",
            @"{""description"": ""get value"", ""entity_type"": ""test-entity"", ""field_name"": ""nonexistent_field""}",
            "Field not found.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Formula for nonexistent field",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("does not exist");
    }

    [Fact]
    public async Task SuggestFormula_ComplexNestedRequest_UsesLightLlm()
    {
        InitializeAll();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "=RF.MATCHLIST(\"test-entity\",\"body\",\"contains\",\"cloud\",\"content\")",
                FinishReason = LLMFinishReason.Stop
            }));

        SetupLlmToolCallThenStop("suggest_formula",
            @"{""description"": ""get content where body contains cloud""}",
            "Here's a complex formula.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Complex formula request",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        // Verify Light LLM (not Heavy) was used for formula generation
        _mockLightLlm.Verify(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        var toolResult = JObject.Parse(result.ToolCallsMade[0].Result);
        toolResult["formula"]!.Value<string>().Should().StartWith("=RF.");
    }

    #endregion

    #region propose_sheet_formulas — Mutation Tools

    [Fact]
    public async Task ProposeSheetFormulas_CreatesProposedAction()
    {
        InitializeAll();

        var operations = new JArray
        {
            new JObject { ["row"] = 0, ["col"] = 0, ["value"] = "Name" },
            new JObject { ["row"] = 0, ["col"] = 1, ["value"] = "Price" },
            new JObject { ["row"] = 1, ["col"] = 0, ["formula"] = "=RF.LIST(\"test-entity\",\"body\")" }
        };

        SetupLlmToolCallThenStop("propose_sheet_formulas",
            new JObject { ["sheet_id"] = 1, ["operations"] = operations }.ToString(),
            "I've proposed writing 3 cells.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Create a table with product data",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("sheet_edit");
        result.ProposedActions[0].EntityType.Should().Be("rf-sheets");
        result.ProposedActions[0].EntityId.Should().Be(1);
        result.ProposedActions[0].RequiresApproval.Should().BeTrue();
        result.ProposedActions[0].Payload!["operations"]!.Should().HaveCount(3);
    }

    [Fact]
    public async Task ProposeSheetFormulas_RequiresApproval()
    {
        InitializeAll();

        var operations = new JArray
        {
            new JObject { ["row"] = 0, ["col"] = 0, ["value"] = "Test" }
        };

        SetupLlmToolCallThenStop("propose_sheet_formulas",
            new JObject { ["sheet_id"] = 5, ["operations"] = operations }.ToString(),
            "Proposed!");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Write test to cell A1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].RequiresApproval.Should().BeTrue();
        result.ToolCallsMade[0].Result.Should().Contain("Proposed").And.Contain("approval");
    }

    [Fact]
    public async Task ProposeSheetFormulas_ValidatesEntityReferences()
    {
        InitializeAll();

        var operations = new JArray
        {
            new JObject { ["row"] = 0, ["col"] = 0, ["formula"] = "=RF.LIST(\"nonexistent-entity\",\"field\")" }
        };

        SetupLlmToolCallThenStop("propose_sheet_formulas",
            new JObject { ["sheet_id"] = 1, ["operations"] = operations }.ToString(),
            "Error occurred.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Use a formula with bad entity",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("nonexistent-entity");
        result.ProposedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeSheetFormulas_SingleQuotes_SanitizedToDoubleQuotes()
    {
        InitializeAll();

        // LLM produces single-quoted formula: =RF.IDS('test-entity')
        var operations = new JArray
        {
            new JObject { ["row"] = 0, ["col"] = 0, ["formula"] = "=RF.IDS('test-entity')" }
        };

        SetupLlmToolCallThenStop("propose_sheet_formulas",
            new JObject { ["sheet_id"] = 1, ["operations"] = operations }.ToString(),
            "Applied formulas.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Add IDS formula",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        // Should succeed (sanitized) and produce a proposed action
        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("sheet_edit");
        // The payload formula should have been fixed to double quotes
        var ops = result.ProposedActions[0].Payload!["operations"] as JArray;
        ops![0]!["formula"]!.Value<string>().Should().Be("=RF.IDS(\"test-entity\")");
    }

    [Fact]
    public async Task ProposeSheetFormulas_SingleQuotes_MultipleArgs_AllSanitized()
    {
        InitializeAll();

        var operations = new JArray
        {
            new JObject { ["row"] = 0, ["col"] = 0, ["formula"] = "=RF.LOOKUP('test-entity','name','John','email')" }
        };

        SetupLlmToolCallThenStop("propose_sheet_formulas",
            new JObject { ["sheet_id"] = 1, ["operations"] = operations }.ToString(),
            "Applied lookup.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Add lookup formula",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        var ops = result.ProposedActions[0].Payload!["operations"] as JArray;
        ops![0]!["formula"]!.Value<string>().Should().Be("=RF.LOOKUP(\"test-entity\",\"name\",\"John\",\"email\")");
    }

    [Fact]
    public async Task ProposeSheetFormulas_UnauthorizedUser_Denied()
    {
        InitializeAll();

        var operations = new JArray
        {
            new JObject { ["row"] = 0, ["col"] = 0, ["value"] = "Test" }
        };

        SetupLlmToolCallThenStop("propose_sheet_formulas",
            new JObject { ["sheet_id"] = 1, ["operations"] = operations }.ToString(),
            "Permission denied.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Write to sheet",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateRestrictedUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error");
        result.ProposedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeSheetFormulas_EmptyOperations_ReturnsError()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("propose_sheet_formulas",
            new JObject { ["sheet_id"] = 1, ["operations"] = new JArray() }.ToString(),
            "Empty operations error.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Empty write",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error");
        result.ProposedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeSheetFormulas_MaxOperationsLimit()
    {
        InitializeAll();

        var operations = new JArray();
        for (var i = 0; i < 501; i++)
            operations.Add(new JObject { ["row"] = i, ["col"] = 0, ["value"] = $"val{i}" });

        SetupLlmToolCallThenStop("propose_sheet_formulas",
            new JObject { ["sheet_id"] = 1, ["operations"] = operations }.ToString(),
            "Too many operations.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Write 501 cells",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("500");
        result.ProposedActions.Should().BeEmpty();
    }

    #endregion

    #region propose_add_sheet_source — Mutation Tools

    [Fact]
    public async Task ProposeAddSheetSource_CreatesProposedAction()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("propose_add_sheet_source",
            @"{""sheet_id"": 1, ""entity"": ""test-entity"", ""fields"": [""body"", ""content""]}",
            "Proposed adding source.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Add test-entity as source",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("sheet_add_source");
        result.ProposedActions[0].EntityType.Should().Be("rf-sheets");
        result.ProposedActions[0].EntityId.Should().Be(1);
        result.ProposedActions[0].RequiresApproval.Should().BeTrue();
        result.ProposedActions[0].Payload!["entity"]!.Value<string>().Should().Be("test-entity");
        result.ProposedActions[0].Payload!["fields"]!.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProposeAddSheetSource_InvalidEntity_ReturnsError()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("propose_add_sheet_source",
            @"{""sheet_id"": 1, ""entity"": ""nonexistent-entity""}",
            "Entity not found.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Add nonexistent as source",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("not found");
        result.ProposedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeAddSheetSource_UnauthorizedEntityAccess_ReturnsError()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("propose_add_sheet_source",
            @"{""sheet_id"": 1, ""entity"": ""test-entity""}",
            "No access.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Add test-entity as source",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateRestrictedUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error");
        result.ProposedActions.Should().BeEmpty();
    }

    #endregion

    #region Multi-turn flows

    [Fact]
    public async Task MultiTurn_UserAsksAboutSheet_AiCallsGetSummaryThenResponds()
    {
        InitializeAll();

        var workbook = CreateWorkbookWithCells(new Dictionary<(int Row, int Col), (object? Value, string? Formula)>
        {
            [(0, 0)] = ("Product", null),
            [(0, 1)] = (100, "=RF.SUM(\"test-entity\",\"body\")")
        });
        var sheet = CreateSheetEntity(1, "My Dashboard", author: 1, sources: new[] { "test-entity" }, workbookData: workbook);
        SetupSheetGetItem(1, sheet);

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls = [new LLMToolCall { Id = "c1", Name = "get_sheet_summary", Arguments = @"{""sheet_id"": 1}" }]
                    });
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Your dashboard 'My Dashboard' has 2 cells with an RF.SUM formula pulling from test-entity.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "What does sheet 1 contain?",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].ToolName.Should().Be("get_sheet_summary");
        result.Response.Should().Contain("My Dashboard");
    }

    [Fact]
    public async Task MultiTurn_UserAsksForFormula_AiCallsSuggestFormula()
    {
        InitializeAll();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "=RF.COUNT(\"test-entity\")",
                FinishReason = LLMFinishReason.Stop
            }));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls = [new LLMToolCall { Id = "c1", Name = "suggest_formula", Arguments = @"{""description"": ""count test entities""}" }]
                    });
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Use =RF.COUNT(\"test-entity\") to count all test entities.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "How to count test entities?",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(1);
        result.ToolCallsMade[0].ToolName.Should().Be("suggest_formula");
        result.Response.Should().Contain("RF.COUNT");
    }

    [Fact]
    public async Task MultiTurn_UserWantsTable_AiCallsSchemaAndProposesFormulas()
    {
        InitializeAll();

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls = [new LLMToolCall { Id = "c1", Name = "get_entity_schema", Arguments = @"{""entity_type"": ""test-entity""}" }]
                    });
                if (callCount == 2)
                {
                    var ops = new JArray
                    {
                        new JObject { ["row"] = 0, ["col"] = 0, ["value"] = "Body" },
                        new JObject { ["row"] = 1, ["col"] = 0, ["formula"] = "=RF.LIST(\"test-entity\",\"body\")" }
                    };
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls = [new LLMToolCall
                        {
                            Id = "c2", Name = "propose_sheet_formulas",
                            Arguments = new JObject { ["sheet_id"] = 1, ["operations"] = ops }.ToString()
                        }]
                    });
                }
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "I've proposed a table with headers and RF.LIST formula. Please approve.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Build a table showing all test-entity body values",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(2);
        result.ToolCallsMade[0].ToolName.Should().Be("get_entity_schema");
        result.ToolCallsMade[1].ToolName.Should().Be("propose_sheet_formulas");
        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("sheet_edit");
    }

    [Fact]
    public async Task MultiTurn_UserWantsNewSource_AiCallsListEntityTypesAndProposes()
    {
        InitializeAll();
        SetupDatabaseScan("test-entity", CreateTestEntities(3));

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls = [new LLMToolCall { Id = "c1", Name = "list_entity_types", Arguments = "{}" }]
                    });
                if (callCount == 2)
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls = [new LLMToolCall
                        {
                            Id = "c2", Name = "propose_add_sheet_source",
                            Arguments = @"{""sheet_id"": 1, ""entity"": ""test-entity""}"
                        }]
                    });
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "I've proposed adding test-entity as a source.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Add a data source to my sheet",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(2);
        result.ToolCallsMade[0].ToolName.Should().Be("list_entity_types");
        result.ToolCallsMade[1].ToolName.Should().Be("propose_add_sheet_source");
        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("sheet_add_source");
    }

    [Fact]
    public async Task MultiTurn_AiCombinesSheetAndEntityTools()
    {
        InitializeAll();

        var workbook = CreateWorkbookWithCells(new Dictionary<(int Row, int Col), (object? Value, string? Formula)>
        {
            [(0, 0)] = ("Header", null)
        });
        var sheet = CreateSheetEntity(1, "Combined", author: 1, sources: new[] { "test-entity" }, workbookData: workbook);
        SetupSheetGetItem(1, sheet);
        SetupSheetScan(new List<JObject> { sheet });

        var callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls = [new LLMToolCall { Id = "c1", Name = "list_sheets", Arguments = "{}" }]
                    });
                if (callCount == 2)
                    return OperationResult<LLMResponse>.Success(new LLMResponse
                    {
                        Content = null,
                        FinishReason = LLMFinishReason.ToolCall,
                        ToolCalls = [new LLMToolCall { Id = "c2", Name = "get_sheet_summary", Arguments = @"{""sheet_id"": 1}" }]
                    });
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "You have 1 sheet called 'Combined' with 1 cell.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Give me an overview of my sheets",
                Context = new AgentContext { CurrentPage = "sheet-list" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade.Should().HaveCount(2);
        result.ToolCallsMade[0].ToolName.Should().Be("list_sheets");
        result.ToolCallsMade[1].ToolName.Should().Be("get_sheet_summary");
    }

    #endregion

    #region System prompt and tool selection

    [Fact]
    public async Task SystemPrompt_IncludesFormulaReference_WhenOnSheetPage()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Hello",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var systemMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.System);
        systemMessage.Content.Should().Contain("RF.FIELD");
        systemMessage.Content.Should().Contain("RF.SUM");
        systemMessage.Content.Should().Contain("RF.LIST");
        systemMessage.Content.Should().Contain("RF.FILTER");
    }

    [Fact]
    public async Task SystemPrompt_ExcludesFormulaReference_WhenNotOnSheetPage()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Hello",
                Context = new AgentContext { CurrentPage = "entity-list" }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var systemMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.System);
        systemMessage.Content.Should().NotContain("RF.FIELD");
    }

    [Fact]
    public async Task SheetTools_IncludedInToolList_WhenOnSheetPage()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Hello",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var toolNames = capturedRequest!.Tools!.Select(t => t.Name).ToList();
        toolNames.Should().Contain("list_sheets");
        toolNames.Should().Contain("get_sheet_summary");
        toolNames.Should().Contain("get_sheet_cell_value");
        toolNames.Should().Contain("suggest_formula");
        toolNames.Should().Contain("propose_sheet_formulas");
        toolNames.Should().Contain("propose_add_sheet_source");
    }

    [Fact]
    public async Task SheetTools_ExcludedFromToolList_WhenNotOnSheetPage()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Hello",
                Context = new AgentContext { CurrentPage = "entity-list" }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var toolNames = capturedRequest!.Tools!.Select(t => t.Name).ToList();
        toolNames.Should().NotContain("list_sheets");
        toolNames.Should().NotContain("get_sheet_summary");
        toolNames.Should().NotContain("propose_sheet_formulas");
    }

    #endregion

    #region Permission & sharing

    [Fact]
    public async Task ListSheets_RespectsIndividualSharing()
    {
        InitializeAll();

        // Sheet shared with user 50 at view level
        var sharedSheet = CreateSheetEntity(1, "Shared With Me", author: 99,
            sharedUsers: new[] { (UserId: 50, Permission: "view") });
        var privateSheet = CreateSheetEntity(2, "Not Shared", author: 99);
        SetupSheetScan(new List<JObject> { sharedSheet, privateSheet });

        SetupLlmToolCallThenStop("list_sheets", "{}", "Found sheets.");

        var user = CreateUserWithId(50, roleId: 10);
        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Show sheets",
                Context = new AgentContext { CurrentPage = "sheet-list" }
            }, user, CancellationToken.None);

        var toolResult = JObject.Parse(result.ToolCallsMade[0].Result);
        toolResult["total_count"]!.Value<int>().Should().Be(1);
        var sheetsArr = (JArray)toolResult["sheets"]!;
        sheetsArr[0]["title"]!.Value<string>().Should().Be("Shared With Me");
    }

    [Fact]
    public async Task GetSheetSummary_RespectsSharing_ViewLevel()
    {
        InitializeAll();

        var sheet = CreateSheetEntity(1, "View Only", author: 99,
            sharedUsers: new[] { (UserId: 50, Permission: "view") });
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_summary", @"{""sheet_id"": 1}", "Summary.");

        var user = CreateUserWithId(50, roleId: 10);
        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Describe sheet 1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, user, CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().NotContain("Error");
        var summary = JObject.Parse(result.ToolCallsMade[0].Result);
        summary["access_level"]!.Value<string>().Should().Be("view");
    }

    [Fact]
    public async Task GetSheetSummary_DeniedWhenNoSharing()
    {
        InitializeAll();

        var sheet = CreateSheetEntity(1, "Private", author: 99);
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_summary", @"{""sheet_id"": 1}", "No access.");

        var user = CreateUserWithId(50, roleId: 10);
        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Describe sheet 1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, user, CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("access");
    }

    #endregion

    #region Edge cases

    [Fact]
    public async Task GetSheetCellValue_LargeWorkbook_HandlesGracefully()
    {
        InitializeAll();

        var cellData = new Dictionary<(int Row, int Col), (object? Value, string? Formula)>();
        for (var r = 0; r < 100; r++)
            for (var c = 0; c < 10; c++)
                cellData[(r, c)] = ($"R{r}C{c}", null);

        var workbook = CreateWorkbookWithCells(cellData);
        var sheet = CreateSheetEntity(1, "Big", author: 1, workbookData: workbook);
        SetupSheetGetItem(1, sheet);

        // Request only a small range
        SetupLlmToolCallThenStop("get_sheet_cell_value", @"{""sheet_id"": 1, ""range"": ""A1:B2""}", "Got cells.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Show A1:B2",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        var cellResult = JObject.Parse(result.ToolCallsMade[0].Result);
        var cells = (JArray)cellResult["cells"]!;
        cells.Should().HaveCount(4); // Only 2x2 range, not all 1000 cells
    }

    [Fact]
    public async Task GetSheetSummary_CorruptWorkbookData_HandlesGracefully()
    {
        InitializeAll();

        var sheet = CreateSheetEntity(1, "Corrupt", author: 1, workbookData: "not valid json {{{");
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_summary", @"{""sheet_id"": 1}", "Partial summary.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Describe sheet 1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        // Should return partial summary without crashing
        result.ToolCallsMade[0].Result.Should().NotContain("Error");
        var summary = JObject.Parse(result.ToolCallsMade[0].Result);
        summary["title"]!.Value<string>().Should().Be("Corrupt");
        summary["cell_count"]!.Value<int>().Should().Be(0);
    }

    [Fact]
    public async Task GetSheetCellValue_MissingSheetId_ReturnsError()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("get_sheet_cell_value", @"{""range"": ""A1""}", "Missing sheet_id.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Show A1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("sheet_id");
    }

    [Fact]
    public async Task SuggestFormula_LlmFailure_ReturnsError()
    {
        InitializeAll();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Failure("LLM down", HttpStatusCode.InternalServerError));

        SetupLlmToolCallThenStop("suggest_formula",
            @"{""description"": ""some formula""}",
            "Error generating formula.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Suggest a formula",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error");
    }

    [Fact]
    public async Task ProposeSheetFormulas_MissingRowCol_ReturnsError()
    {
        InitializeAll();

        var operations = new JArray
        {
            new JObject { ["value"] = "test" } // Missing row and col
        };

        SetupLlmToolCallThenStop("propose_sheet_formulas",
            new JObject { ["sheet_id"] = 1, ["operations"] = operations }.ToString(),
            "Invalid operations.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Write without position",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("row");
        result.ProposedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeSheetFormulas_MissingValueAndFormula_ReturnsError()
    {
        InitializeAll();

        var operations = new JArray
        {
            new JObject { ["row"] = 0, ["col"] = 0 } // Neither value nor formula
        };

        SetupLlmToolCallThenStop("propose_sheet_formulas",
            new JObject { ["sheet_id"] = 1, ["operations"] = operations }.ToString(),
            "Missing content.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Write empty",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error");
        result.ToolCallsMade[0].Result.Should().Match(r => r.Contains("value") || r.Contains("formula"));
    }

    #endregion

    #region Sheet context — SelectedCell, SheetSources, and advanced prompt

    [Fact]
    public async Task Context_SelectedCell_IncludedInUserMessage()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Put a SUM formula on the cell I clicked",
                Context = new AgentContext
                {
                    CurrentPage = "sheet-edit",
                    SelectedCell = "R3C2"
                }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var userMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.User);
        userMessage.Content.Should().Contain("Selected cell: R3C2");
    }

    [Fact]
    public async Task Context_SelectedCell_OmittedWhenNull()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Hello",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var userMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.User);
        userMessage.Content.Should().NotContain("Selected cell");
    }

    [Fact]
    public async Task Context_SheetSources_IncludedInUserMessage()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Build a dashboard",
                Context = new AgentContext
                {
                    CurrentPage = "sheet-edit",
                    SheetSources = new List<string> { "product", "warehouse" }
                }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var userMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.User);
        userMessage.Content.Should().Contain("Sheet data sources: product, warehouse");
    }

    [Fact]
    public async Task Context_SheetSources_OmittedWhenEmpty()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Hello",
                Context = new AgentContext
                {
                    CurrentPage = "sheet-edit",
                    SheetSources = new List<string>()
                }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var userMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.User);
        userMessage.Content.Should().NotContain("Sheet data sources");
    }

    [Fact]
    public async Task Context_SheetSources_OmittedWhenNull()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Hello",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var userMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.User);
        userMessage.Content.Should().NotContain("Sheet data sources");
    }

    [Fact]
    public async Task Context_SelectedCellAndSheetSources_BothIncluded()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Put the total here",
                Context = new AgentContext
                {
                    CurrentPage = "sheet-edit",
                    EntityType = "rf-sheets",
                    EntityId = 5,
                    SheetSources = new List<string> { "test-entity" },
                    SelectedCell = "R10C4"
                }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var userMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.User);
        userMessage.Content.Should().Contain("Selected cell: R10C4");
        userMessage.Content.Should().Contain("Sheet data sources: test-entity");
        userMessage.Content.Should().Contain("Entity type: rf-sheets");
        userMessage.Content.Should().Contain("Entity ID: 5");
    }

    [Fact]
    public async Task SystemPrompt_IncludesAdvancedUsageGuidance_WhenOnSheetPage()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Hello",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var systemMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.System);
        systemMessage.Content.Should().Contain("ADVANCED USAGE");
        systemMessage.Content.Should().Contain("Selected cell");
        systemMessage.Content.Should().Contain("propose_add_sheet_source");
    }

    [Fact]
    public async Task SystemPrompt_ExcludesAdvancedUsage_WhenNotOnSheetPage()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Hello",
                Context = new AgentContext { CurrentPage = "entity-list" }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var systemMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.System);
        systemMessage.Content.Should().NotContain("ADVANCED USAGE");
    }

    [Fact]
    public async Task Context_SheetSources_SingleSource_FormattedCorrectly()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Show data",
                Context = new AgentContext
                {
                    CurrentPage = "sheet-edit",
                    SheetSources = new List<string> { "test-entity" }
                }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var userMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.User);
        userMessage.Content.Should().Contain("Sheet data sources: test-entity");
        // Should not have trailing comma
        userMessage.Content.Should().NotContain("test-entity,");
    }

    [Fact]
    public async Task Context_SelectedCell_OriginCell_R0C0()
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

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Put something here",
                Context = new AgentContext
                {
                    CurrentPage = "sheet-edit",
                    SelectedCell = "R0C0"
                }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var userMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.User);
        userMessage.Content.Should().Contain("Selected cell: R0C0");
    }

    #endregion

    #region Phase 1 — Sheet authorization gaps

    [Fact]
    public async Task ProposeSheetFormulas_ReadOnlyUser_DeniedByUpdateCheck()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("propose_sheet_formulas",
            @"{""sheet_id"":1,""operations"":[{""row"":0,""col"":0,""value"":""Hello""}]}",
            "No permission.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Write Hello to A1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateReadOnlyUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("edit permission");
    }

    [Fact]
    public async Task ProposeAddSheetSource_ReadOnlyUser_DeniedByUpdateCheck()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("propose_add_sheet_source",
            @"{""sheet_id"":1,""entity"":""test-entity""}",
            "No permission.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Add test-entity as source",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateReadOnlyUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("edit permission");
    }

    [Fact]
    public async Task SuggestFormula_UnauthorizedEntityType_Denied()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("suggest_formula",
            @"{""description"":""sum prices"",""entity_type"":""test-entity""}",
            "Denied.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Suggest a sum formula for test-entity prices",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateRestrictedUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("access");
    }

    [Fact]
    public async Task ProposeSheetFormulas_FormulaRefersToUnauthorizedEntity_Denied()
    {
        InitializeAll();

        // Use read-only user (has PEEK_ALL on test-entity but not on "secret-entity")
        // However "secret-entity" doesn't exist in config, so it would fail entity-existence check first.
        // Instead, test with a restricted user (no roles) who can't PEEK_ALL on test-entity.
        SetupLlmToolCallThenStop("propose_sheet_formulas",
            @"{""sheet_id"":1,""operations"":[{""row"":0,""col"":0,""formula"":""=RF.SUM(\""test-entity\"",\""body\"")""}]}",
            "Denied.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Write formula",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateRestrictedUser(), CancellationToken.None);

        // Restricted user has no roles → fails at PEEK_ALL check on sheets first
        result.ToolCallsMade[0].Result.Should().Contain("Error");
    }

    [Fact]
    public async Task ProposeSheetFormulas_FormulaWithMultipleEntityRefs_ValidatesAll()
    {
        InitializeAll();

        // Formula references both "test-entity" (valid) and "nonexistent-entity" (unknown)
        SetupLlmToolCallThenStop("propose_sheet_formulas",
            @"{""sheet_id"":1,""operations"":[{""row"":0,""col"":0,""formula"":""=RF.SUM(\""test-entity\"",\""body\"") + RF.COUNT(\""nonexistent-entity\"")""}]}",
            "Error.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Write combined formula",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("nonexistent-entity");
    }

    [Fact]
    public async Task ProposeSheetFormulas_PlainValueOperation_NoEntityCheckNeeded()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("propose_sheet_formulas",
            @"{""sheet_id"":1,""operations"":[{""row"":0,""col"":0,""value"":""Header""},{""row"":0,""col"":1,""value"":42}]}",
            "Proposed.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Write headers",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Proposed");
        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("sheet_edit");
    }

    [Fact]
    public async Task GetSheetCellValue_DeniedWhenNoSharing()
    {
        InitializeAll();

        var sheet = CreateSheetEntity(1, "Private Sheet", author: 99);
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_cell_value",
            @"{""sheet_id"":1,""range"":""A1""}",
            "Denied.");

        var user = CreateUserWithId(50, roleId: 10);
        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Read A1 from sheet 1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, user, CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("access");
    }

    [Fact]
    public async Task GetSheetCellValue_AllowedWithViewSharing()
    {
        InitializeAll();

        var workbook = CreateWorkbookWithCells(new Dictionary<(int Row, int Col), (object? Value, string? Formula)>
        {
            [(0, 0)] = ("Shared Data", null)
        });
        var sheet = CreateSheetEntity(1, "Shared Sheet", author: 99,
            workbookData: workbook, sharedUsers: new[] { (UserId: 50, Permission: "view") });
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_cell_value",
            @"{""sheet_id"":1,""range"":""A1""}",
            "Cell value.");

        var user = CreateUserWithId(50, roleId: 10);
        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Read A1 from sheet 1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, user, CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().NotContain("Error");
        var cellResult = JObject.Parse(result.ToolCallsMade[0].Result);
        var cells = (JArray)cellResult["cells"]!;
        cells[0]["value"]!.Value<string>().Should().Be("Shared Data");
    }

    [Fact]
    public async Task GetSheetSummary_EmptyWorkbook_ReturnsZeroCells()
    {
        InitializeAll();

        var sheet = CreateSheetEntity(1, "Empty Sheet", author: 1, workbookData: "{}");
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_summary", @"{""sheet_id"":1}", "Summary.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Describe sheet 1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        var summary = JObject.Parse(result.ToolCallsMade[0].Result);
        summary["cell_count"]!.Value<int>().Should().Be(0);
        summary["formula_inventory"].Should().BeNull();
    }

    [Fact]
    public async Task GetSheetSummary_ReturnsCorrectAccessLevel_ForOwner()
    {
        InitializeAll();

        var sheet = CreateSheetEntity(1, "My Sheet", author: 1);
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_summary", @"{""sheet_id"":1}", "Summary.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Describe sheet 1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        var summary = JObject.Parse(result.ToolCallsMade[0].Result);
        summary["access_level"]!.Value<string>().Should().Be("owner");
    }

    #endregion

    #region Phase 3 — Edge cases and tool interaction

    [Fact]
    public async Task ProposeSheetFormulas_DuplicateProposal_AllowsMultiple()
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
                            new LLMToolCall { Id = "call_1", Name = "propose_sheet_formulas",
                                Arguments = @"{""sheet_id"":1,""operations"":[{""row"":0,""col"":0,""value"":""A""}]}" }
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
                            new LLMToolCall { Id = "call_2", Name = "propose_sheet_formulas",
                                Arguments = @"{""sheet_id"":1,""operations"":[{""row"":1,""col"":0,""value"":""B""}]}" }
                        ]
                    });
                }
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Proposed two edits.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Write A then B",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(2);
        result.ProposedActions.All(a => a.ActionType == "sheet_edit").Should().BeTrue();
    }

    [Fact]
    public async Task ProposeAddSheetSource_WithFields_IncludesFieldsInPayload()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("propose_add_sheet_source",
            @"{""sheet_id"":1,""entity"":""test-entity"",""fields"":[""body"",""content""]}",
            "Proposed.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Add test-entity with body and content fields",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        var payload = result.ProposedActions[0].Payload!;
        payload["entity"]!.Value<string>().Should().Be("test-entity");
        var fields = (JArray)payload["fields"]!;
        fields.Select(f => f.Value<string>()).Should().BeEquivalentTo(new[] { "body", "content" });
    }

    [Fact]
    public async Task ProposeAddSheetSource_MissingSheetId_ReturnsError()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("propose_add_sheet_source",
            @"{""entity"":""test-entity""}",
            "Error.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Add source",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().Contain("Error").And.Contain("sheet_id");
    }

    [Fact]
    public async Task ListSheets_SheetWithNoSources_ReturnsEmptySourcesArray()
    {
        InitializeAll();

        var sheet = CreateSheetEntity(1, "No Sources", author: 1, sources: null);
        SetupSheetScan(new List<JObject> { sheet });

        SetupLlmToolCallThenStop("list_sheets", "{}", "Found sheets.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "List sheets",
                Context = new AgentContext { CurrentPage = "sheet-list" }
            }, CreateAdminUser(), CancellationToken.None);

        var toolResult = JObject.Parse(result.ToolCallsMade[0].Result);
        var sheetsArr = (JArray)toolResult["sheets"]!;
        var sources = (JArray)sheetsArr[0]["sources"]!;
        sources.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSheets_MixedPublicAndSharedSheets_ReturnsCorrectSubset()
    {
        InitializeAll();

        var ownedSheet = CreateSheetEntity(1, "My Sheet", author: 50);
        var publicSheet = CreateSheetEntity(2, "Public Report", author: 99, isPublic: true);
        var sharedSheet = CreateSheetEntity(3, "Shared With Me", author: 99,
            sharedUsers: new[] { (UserId: 50, Permission: "view") });
        var privateSheet = CreateSheetEntity(4, "Someone Else's Private", author: 99);
        SetupSheetScan(new List<JObject> { ownedSheet, publicSheet, sharedSheet, privateSheet });

        SetupLlmToolCallThenStop("list_sheets", "{}", "Sheets.");

        var user = CreateUserWithId(50, roleId: 10);
        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Show all sheets",
                Context = new AgentContext { CurrentPage = "sheet-list" }
            }, user, CancellationToken.None);

        var toolResult = JObject.Parse(result.ToolCallsMade[0].Result);
        toolResult["total_count"]!.Value<int>().Should().Be(3);
        var ids = ((JArray)toolResult["sheets"]!).Select(s => s["id"]!.Value<int>()).ToList();
        ids.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task GetSheetCellValue_LargeRange_HandlesGracefully()
    {
        InitializeAll();

        // Create a workbook with just a few cells, request a large range
        var workbook = CreateWorkbookWithCells(new Dictionary<(int Row, int Col), (object? Value, string? Formula)>
        {
            [(0, 0)] = ("A", null),
            [(1, 0)] = ("B", null)
        });
        var sheet = CreateSheetEntity(1, "Test", author: 1, workbookData: workbook);
        SetupSheetGetItem(1, sheet);

        SetupLlmToolCallThenStop("get_sheet_cell_value",
            @"{""sheet_id"":1,""range"":""A1:Z100""}",
            "Data returned.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Show A1:Z100",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        // Should not error — returns cells that exist within the range
        result.ToolCallsMade[0].Result.Should().NotContain("Error");
    }

    [Fact]
    public async Task SuggestFormula_NoEntityType_StillWorks()
    {
        InitializeAll();

        _mockLightLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "=RF.SUM(\"test-entity\",\"body\")",
                FinishReason = LLMFinishReason.Stop
            }));

        SetupLlmToolCallThenStop("suggest_formula",
            @"{""description"":""sum body values""}",
            "Here's the formula.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Suggest a formula to sum body values",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ToolCallsMade[0].Result.Should().NotContain("Error");
        result.ToolCallsMade[0].Result.Should().Contain("formula");
    }

    #endregion

    #region Phase 4 — Endpoint action dispatch

    [Fact]
    public async Task SheetEditAction_ExecutedAsClientSide()
    {
        InitializeAll();

        // Simulate: LLM proposes sheet_edit, user confirms, then new turn
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
                            new LLMToolCall { Id = "call_1", Name = "propose_sheet_formulas",
                                Arguments = @"{""sheet_id"":1,""operations"":[{""row"":0,""col"":0,""value"":""Test""}]}" }
                        ]
                    });
                }
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Applied.",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        // First turn: get the proposal
        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Write Test to A1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("sheet_edit");
        var actionId = result.ProposedActions[0].ActionId;

        // Second turn: confirm the action, pass execution result
        callCount = 0;
        var result2 = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Confirmed",
                Context = new AgentContext { CurrentPage = "sheet-edit" },
                ConfirmedActions =
                [
                    new ActionConfirmation { ActionId = actionId, Approved = true }
                ],
                ExecutedActionResults =
                [
                    new ActionExecutionResult { ActionId = actionId, Success = true, Message = "Action handled client-side." }
                ]
            }, CreateAdminUser(), CancellationToken.None);

        // The LLM should receive feedback about successful execution
        result2.Response.Should().NotBeNull();
    }

    [Fact]
    public async Task SheetAddSourceAction_ExecutedAsClientSide()
    {
        InitializeAll();

        SetupLlmToolCallThenStop("propose_add_sheet_source",
            @"{""sheet_id"":1,""entity"":""test-entity""}",
            "Proposed adding source.");

        var result = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Add test-entity source",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        result.ProposedActions.Should().HaveCount(1);
        result.ProposedActions[0].ActionType.Should().Be("sheet_add_source");
    }

    [Fact]
    public async Task ConfirmedSheetEdit_ResultFeedback_PassedToLlm()
    {
        InitializeAll();

        // First: propose
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
                            new LLMToolCall { Id = "call_1", Name = "propose_sheet_formulas",
                                Arguments = @"{""sheet_id"":1,""operations"":[{""row"":0,""col"":0,""value"":""Done""}]}" }
                        ]
                    });
                }
                return OperationResult<LLMResponse>.Success(new LLMResponse
                {
                    Content = "Great, the edit was applied!",
                    FinishReason = LLMFinishReason.Stop
                });
            });

        var result1 = await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "Write Done to A1",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        var actionId = result1.ProposedActions[0].ActionId;

        // Second turn: confirm + pass execution result
        LLMRequest? capturedRequest = null;
        callCount = 0;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "The edit was applied successfully.",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "I approved the edit",
                Context = new AgentContext { CurrentPage = "sheet-edit" },
                ConfirmedActions =
                [
                    new ActionConfirmation { ActionId = actionId, Approved = true }
                ],
                ExecutedActionResults =
                [
                    new ActionExecutionResult { ActionId = actionId, Success = true, Message = "Action handled client-side." }
                ]
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        var userMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.User);
        userMessage.Content.Should().Contain("SUCCESSFULLY EXECUTED");
    }

    #endregion

    #region SheetsEnabled Flag

    [Fact]
    public async Task SheetsDisabled_SheetToolsNotActivated()
    {
        InitializeAllWithSheetsDisabled();

        LLMRequest? capturedRequest = null;
        _mockHeavyLlm
            .Setup(l => l.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LLMRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(OperationResult<LLMResponse>.Success(new LLMResponse
            {
                Content = "Done.",
                FinishReason = LLMFinishReason.Stop
            }));

        await AiAgentChatHandler.ChatAsync(
            new AgentChatRequest
            {
                Message = "List sheets",
                Context = new AgentContext { CurrentPage = "sheet-edit" }
            }, CreateAdminUser(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        // System prompt should NOT contain sheet-related content
        var systemMessage = capturedRequest!.Messages.First(m => m.Role == LLMRole.System);
        systemMessage.Content.Should().NotContain("RF FORMULA REFERENCE");
        systemMessage.Content.Should().NotContain("RF.IDS");
        // Tools should NOT include any sheet tools
        capturedRequest.Tools.Should().NotBeNull();
        capturedRequest.Tools!.Should().NotContain(t => t.Name == "list_sheets");
        capturedRequest.Tools!.Should().NotContain(t => t.Name == "suggest_formula");
        capturedRequest.Tools!.Should().NotContain(t => t.Name == "propose_sheet_formulas");
    }

    [Fact]
    public void SheetsDisabled_SheetsEntityNotInConfiguration()
    {
        InitializeAllWithSheetsDisabled();
        RfConfiguration.EntityNameToConfiguration.Should().NotContainKey("rf-sheets");
    }

    [Fact]
    public void SheetsDisabled_FlagExposedCorrectly()
    {
        InitializeAllWithSheetsDisabled();
        RfConfiguration.SheetsEnabled.Should().BeFalse();
    }

    [Fact]
    public void SheetsEnabled_FlagExposedCorrectly()
    {
        InitializeAll();
        RfConfiguration.SheetsEnabled.Should().BeTrue();
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

    private void InitializeAllWithSheetsDisabled()
    {
        AiConfiguration.Initialize(
            _mockDatabaseService.Object,
            _mockMemoryService.Object,
            _mockVectorService.Object,
            _mockHeavyLlm.Object,
            _mockLightLlm.Object,
            embeddingDimensions: 384);

        SetupMockRfConfig(sheetsEnabled: false);
    }

    private void SetupMockRfConfig(bool sheetsEnabled = true)
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
            SheetsEnabled = sheetsEnabled,
            EntityTypes = new List<EntityConfigurationBuilderBase>
            {
                new EntityConfigurationBuilder<TestSheetEntityModel>
                {
                    EntityName = "test-entity",
                    EntityReadableNameSingular = "Test Entity",
                    EntityReadableNamePlural = "Test Entities",
                    EntityDescription = "A test entity for sheet integration.",
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
                }
            }
        };

        var configField = typeof(RfConfiguration).GetField("_configuration",
            BindingFlags.Static | BindingFlags.NonPublic);
        var initializedField = typeof(RfConfiguration).GetField("_initialized",
            BindingFlags.Static | BindingFlags.NonPublic);
        configField?.SetValue(null, builder);
        initializedField?.SetValue(null, true);

        var iamCache = (IamRoleEntitiesCache)FormatterServices.GetUninitializedObject(typeof(IamRoleEntitiesCache));

        var baseLockField = typeof(EntitiesCacheBase<IamRoleEntityFieldsModel>)
            .GetField("_entitiesLock", BindingFlags.Instance | BindingFlags.NonPublic)!;
        baseLockField.SetValue(iamCache, new object());

        var baseEntitiesField = typeof(EntitiesCacheBase<IamRoleEntityFieldsModel>)
            .GetField("_entities", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var entities = new Dictionary<int, EntityModel<IamRoleEntityFieldsModel>>();

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
                        EntityType = RfReservedEntities.SheetsEntityName,
                        AllowPeekAll = true, AllowRead = true,
                        AllowUpdate = true, AllowCreate = true, AllowDelete = true
                    }
                ]
            }
        };
        entities[10] = adminRole;

        // Read-only role (ID=20): PEEK_ALL on sheets + test-entity, but no UPDATE/CREATE/DELETE
        var readOnlyRole = new EntityModel<IamRoleEntityFieldsModel>
        {
            Id = 20,
            Title = new TitleRenderedModel(),
            Fields = new IamRoleEntityFieldsModel
            {
                Capabilities =
                [
                    new IamRoleCapabilitiesModel
                    {
                        EntityType = "test-entity",
                        AllowPeekAll = true, AllowRead = true,
                        AllowUpdate = false, AllowCreate = false, AllowDelete = false
                    },
                    new IamRoleCapabilitiesModel
                    {
                        EntityType = RfReservedEntities.SheetsEntityName,
                        AllowPeekAll = true, AllowRead = true,
                        AllowUpdate = false, AllowCreate = false, AllowDelete = false
                    }
                ]
            }
        };
        entities[20] = readOnlyRole;

        baseEntitiesField.SetValue(iamCache, entities);

        var iamCacheField = typeof(RfConfiguration).GetField("_iamRoleEntitiesCache",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        iamCacheField.SetValue(null, iamCache);
    }

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

    /// <summary>
    /// User with role 20: PEEK_ALL on sheets + test-entity but no UPDATE/CREATE/DELETE.
    /// </summary>
    private static EntityModel<UserEntityFieldsModel> CreateReadOnlyUser()
    {
        return new EntityModel<UserEntityFieldsModel>
        {
            Id = 80,
            Title = new TitleRenderedModel(),
            Fields = new UserEntityFieldsModel
            {
                EmailAddress = "readonly@test.com",
                Roles = [new UserRoleAssignmentModel { RoleId = 20 }]
            }
        };
    }

    private static EntityModel<UserEntityFieldsModel> CreateUserWithId(int userId, int roleId)
    {
        return new EntityModel<UserEntityFieldsModel>
        {
            Id = userId,
            Title = new TitleRenderedModel(),
            Fields = new UserEntityFieldsModel
            {
                EmailAddress = $"user{userId}@test.com",
                Roles = [new UserRoleAssignmentModel { RoleId = roleId }]
            }
        };
    }

    private static JObject CreateSheetEntity(int id, string title, int author = 1,
        string[]? sources = null, string? workbookData = null, bool isPublic = false,
        (int UserId, string Permission)[]? sharedUsers = null)
    {
        var sourcesArray = new JArray();
        if (sources != null)
            foreach (var s in sources)
                sourcesArray.Add(new JObject { ["entity"] = s });

        var fields = new JObject
        {
            ["sources"] = sourcesArray.ToString(),
            ["workbook_data"] = workbookData ?? "{}",
            ["bound_regions"] = "[]",
            ["refresh_interval_seconds"] = 30,
            ["is_public"] = isPublic
        };

        if (sharedUsers != null)
        {
            var sharedUsersArray = new JArray();
            foreach (var (userId, permission) in sharedUsers)
                sharedUsersArray.Add(new JObject { ["user"] = userId, ["permission"] = permission });
            fields["shared_users"] = sharedUsersArray;
        }

        return new JObject
        {
            [EntityModelAttributes.Id] = id,
            [EntityModelAttributes.Title] = new JObject { [EntityModelAttributes.TitleRendered] = title },
            [EntityModelAttributes.Author] = author,
            [EntityModelAttributes.ModifiedGmt] = DateTime.UtcNow.ToString("o"),
            [EntityModelAttributes.Fields] = fields
        };
    }

    private static string CreateWorkbookWithCells(Dictionary<(int Row, int Col), (object? Value, string? Formula)> cells)
    {
        var cellData = new JObject();
        foreach (var ((row, col), (value, formula)) in cells)
        {
            var rowStr = row.ToString();
            var colStr = col.ToString();
            if (cellData[rowStr] == null)
                cellData[rowStr] = new JObject();

            var cellObj = new JObject();
            if (value != null)
                cellObj["v"] = JToken.FromObject(value);
            if (formula != null)
                cellObj["f"] = formula;
            ((JObject)cellData[rowStr]!)[colStr] = cellObj;
        }

        var workbook = new JObject
        {
            ["sheets"] = new JObject
            {
                ["sheet1"] = new JObject { ["cellData"] = cellData }
            }
        };
        return workbook.ToString();
    }

    private void SetupSheetScan(List<JObject> sheets)
    {
        _mockDatabaseService
            .Setup(d => d.ScanTableAsync("rf-sheets", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<(IReadOnlyList<string> Keys, IReadOnlyList<JObject> Items)>.Success((
                sheets.Select(s => s[EntityModelAttributes.Id]!.Value<int>().ToString()).ToList() as IReadOnlyList<string>,
                sheets as IReadOnlyList<JObject>)));
    }

    private void SetupSheetGetItem(int sheetId, JObject sheet)
    {
        _mockDatabaseService
            .Setup(d => d.GetItemAsync("rf-sheets",
                It.Is<DbKey>(k => k.Value.AsInteger == (long)sheetId),
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<JObject>.Success(sheet));
    }

    private void SetupDatabaseScan(string entityName, List<JObject> entities)
    {
        _mockDatabaseService
            .Setup(d => d.ScanTableAsync(entityName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<(IReadOnlyList<string> Keys, IReadOnlyList<JObject> Items)>.Success((
                entities.Select(e => e[EntityModelAttributes.Id]!.Value<int>().ToString()).ToList() as IReadOnlyList<string>,
                entities as IReadOnlyList<JObject>)));
    }

    private static List<JObject> CreateTestEntities(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new JObject
            {
                [EntityModelAttributes.Id] = i,
                [EntityModelAttributes.Title] = new JObject { [EntityModelAttributes.TitleRendered] = $"Entity {i}" },
                [EntityModelAttributes.ModifiedGmt] = DateTime.UtcNow.ToString("o"),
                [EntityModelAttributes.Fields] = new JObject { ["body"] = $"Body {i}", ["content"] = $"Content {i}" }
            })
            .ToList();
    }

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

    private class TestSheetEntityModel : EntityFieldsModel
    {
        [Newtonsoft.Json.JsonProperty("body")]
        [AISanityCheck("Is this text professional?")]
        [AISuggestion("Suggest content")]
        [Attributes.Fields.TextArea(label: "Body", instructions: "Body text", mandatory: false, placeholderText: "")]
        public string _body = "";

        [Newtonsoft.Json.JsonProperty("content")]
        [Attributes.Fields.TextArea(label: "Content", instructions: "Content text", mandatory: false, placeholderText: "")]
        public string _content = "";
    }

    #endregion
}

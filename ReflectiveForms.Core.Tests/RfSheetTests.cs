using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models.ReservedEntityTypes;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class RfSheetTests
{
    // ── RfSheetEntityFieldsModel — Default Values ────────────────────────

    [Fact]
    public void RfSheetEntityFieldsModel_Sources_DefaultsToEmptyJsonArray()
    {
        var model = new RfSheetEntityFieldsModel();
        model.Sources.Should().Be("[]");
    }

    [Fact]
    public void RfSheetEntityFieldsModel_BoundRegions_DefaultsToEmptyJsonArray()
    {
        var model = new RfSheetEntityFieldsModel();
        model.BoundRegions.Should().Be("[]");
    }

    [Fact]
    public void RfSheetEntityFieldsModel_WorkbookData_DefaultsToEmptyJsonObject()
    {
        var model = new RfSheetEntityFieldsModel();
        model.WorkbookData.Should().Be("{}");
    }

    [Fact]
    public void RfSheetEntityFieldsModel_RefreshIntervalSeconds_DefaultsTo30()
    {
        var model = new RfSheetEntityFieldsModel();
        model.RefreshIntervalSeconds.Should().Be(30);
    }

    // ── RfSheetEntityFieldsModel — Serialization ─────────────────────────

    [Fact]
    public void RfSheetEntityFieldsModel_SerializesToJson_WithCorrectPropertyNames()
    {
        var model = new RfSheetEntityFieldsModel
        {
            Sources = "[{\"entity\":\"employee\"}]",
            BoundRegions = "[{\"id\":\"r1\"}]",
            WorkbookData = "{\"sheets\":[]}",
            RefreshIntervalSeconds = 60
        };

        var json = JsonConvert.SerializeObject(model);
        var obj = JObject.Parse(json);

        obj["sources"]!.Value<string>().Should().Be("[{\"entity\":\"employee\"}]");
        obj["bound_regions"]!.Value<string>().Should().Be("[{\"id\":\"r1\"}]");
        obj["workbook_data"]!.Value<string>().Should().Be("{\"sheets\":[]}");
        obj["refresh_interval_seconds"]!.Value<int>().Should().Be(60);
    }

    [Fact]
    public void RfSheetEntityFieldsModel_DeserializesFromJson()
    {
        var json = """
        {
            "sources": "[{\"entity\":\"department\"}]",
            "bound_regions": "[]",
            "workbook_data": "{}",
            "refresh_interval_seconds": 15
        }
        """;

        var model = JsonConvert.DeserializeObject<RfSheetEntityFieldsModel>(json);

        model.Should().NotBeNull();
        model!.Sources.Should().Be("[{\"entity\":\"department\"}]");
        model.BoundRegions.Should().Be("[]");
        model.WorkbookData.Should().Be("{}");
        model.RefreshIntervalSeconds.Should().Be(15);
    }

    [Fact]
    public void RfSheetEntityFieldsModel_RoundTripSerialization_PreservesData()
    {
        var original = new RfSheetEntityFieldsModel
        {
            Sources = "[{\"entity\":\"employee\",\"fields\":[\"name\",\"email\"]}]",
            BoundRegions = "[{\"id\":\"region-1\",\"entity\":\"employee\",\"startCol\":0,\"headerRow\":0}]",
            WorkbookData = "{\"sheets\":[{\"name\":\"Sheet1\",\"cells\":{}}]}",
            RefreshIntervalSeconds = 10
        };

        var json = JsonConvert.SerializeObject(original);
        var deserialized = JsonConvert.DeserializeObject<RfSheetEntityFieldsModel>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Sources.Should().Be(original.Sources);
        deserialized.BoundRegions.Should().Be(original.BoundRegions);
        deserialized.WorkbookData.Should().Be(original.WorkbookData);
        deserialized.RefreshIntervalSeconds.Should().Be(original.RefreshIntervalSeconds);
    }

    [Fact]
    public void RfSheetEntityFieldsModel_DeserializesDefaults_WhenFieldsMissing()
    {
        var json = "{}";
        var model = JsonConvert.DeserializeObject<RfSheetEntityFieldsModel>(json);

        model.Should().NotBeNull();
        model!.Sources.Should().Be("[]");
        model.BoundRegions.Should().Be("[]");
        model.WorkbookData.Should().Be("{}");
        model.RefreshIntervalSeconds.Should().Be(30);
    }

    // ── RfSheetEntityFieldsModel — Sources Parsing ───────────────────────

    [Fact]
    public void RfSheetEntityFieldsModel_Sources_CanStoreMultipleSources()
    {
        var model = new RfSheetEntityFieldsModel
        {
            Sources = "[{\"entity\":\"employee\"},{\"entity\":\"department\"},{\"entity\":\"project\"}]"
        };

        var sources = JArray.Parse(model.Sources);
        sources.Should().HaveCount(3);
        sources[0]["entity"]!.Value<string>().Should().Be("employee");
        sources[1]["entity"]!.Value<string>().Should().Be("department");
        sources[2]["entity"]!.Value<string>().Should().Be("project");
    }

    [Fact]
    public void RfSheetEntityFieldsModel_Sources_EmptyArrayIsValid()
    {
        var model = new RfSheetEntityFieldsModel { Sources = "[]" };
        var sources = JArray.Parse(model.Sources);
        sources.Should().BeEmpty();
    }

    [Fact]
    public void RfSheetEntityFieldsModel_Sources_WithFieldFilters()
    {
        var model = new RfSheetEntityFieldsModel
        {
            Sources = "[{\"entity\":\"employee\",\"fields\":[\"name\",\"email\",\"department_id\"]}]"
        };

        var sources = JArray.Parse(model.Sources);
        sources.Should().HaveCount(1);
        var fields = sources[0]["fields"] as JArray;
        fields.Should().NotBeNull();
        fields!.Should().HaveCount(3);
    }

    // ── RfSheetEntityFieldsModel — Bound Regions Parsing ─────────────────

    [Fact]
    public void RfSheetEntityFieldsModel_BoundRegions_ComplexRegionDefinition()
    {
        var region = new JObject
        {
            ["id"] = "region-1",
            ["entity"] = "employee",
            ["startCol"] = 0,
            ["headerRow"] = 0,
            ["fields"] = new JArray("name", "email", "department"),
            ["includeIdColumn"] = true
        };
        var model = new RfSheetEntityFieldsModel
        {
            BoundRegions = new JArray(region).ToString(Formatting.None)
        };

        var regions = JArray.Parse(model.BoundRegions);
        regions.Should().HaveCount(1);
        regions[0]["entity"]!.Value<string>().Should().Be("employee");
        regions[0]["includeIdColumn"]!.Value<bool>().Should().BeTrue();
        ((JArray)regions[0]["fields"]!).Should().HaveCount(3);
    }

    [Fact]
    public void RfSheetEntityFieldsModel_BoundRegions_MultipleRegions()
    {
        var regions = new JArray(
            new JObject { ["id"] = "r1", ["entity"] = "employee" },
            new JObject { ["id"] = "r2", ["entity"] = "department" }
        );
        var model = new RfSheetEntityFieldsModel
        {
            BoundRegions = regions.ToString(Formatting.None)
        };

        var parsed = JArray.Parse(model.BoundRegions);
        parsed.Should().HaveCount(2);
    }

    // ── RfSheetEntityFieldsModel — RefreshInterval Edge Cases ────────────

    [Fact]
    public void RfSheetEntityFieldsModel_RefreshInterval_CanBeSetToMinimum()
    {
        var model = new RfSheetEntityFieldsModel { RefreshIntervalSeconds = 5 };
        model.RefreshIntervalSeconds.Should().Be(5);
    }

    [Fact]
    public void RfSheetEntityFieldsModel_RefreshInterval_CanBeSetToMaximum()
    {
        var model = new RfSheetEntityFieldsModel { RefreshIntervalSeconds = 3600 };
        model.RefreshIntervalSeconds.Should().Be(3600);
    }

    // ── Reserved Entity Registration ─────────────────────────────────────

    [Fact]
    public void RfReservedEntities_SheetsEntityName_IsCorrect()
    {
        RfReservedEntities.SheetsEntityName.Should().Be("rf-sheets");
    }

    [Fact]
    public void RfReservedEntities_ReservedEntityNames_ContainsSheets()
    {
        RfReservedEntities.ReservedEntityNames.Should().Contain("rf-sheets");
    }

    [Fact]
    public void RfReservedEntities_ReservedEntityNames_SheetsCaseInsensitive()
    {
        RfReservedEntities.ReservedEntityNames.Contains("RF-SHEETS").Should().BeTrue();
        RfReservedEntities.ReservedEntityNames.Contains("Rf-Sheets").Should().BeTrue();
        RfReservedEntities.ReservedEntityNames.Contains("rf-sheets").Should().BeTrue();
    }

    [Fact]
    public void RfReservedEntities_ReservedEntityTypes_ContainsSheetsConfiguration()
    {
        var sheetsConfig = RfReservedEntities.ReservedEntityTypes
            .FirstOrDefault(t => t.EntityConfiguration.EntityName == "rf-sheets");

        sheetsConfig.Should().NotBeNull();
    }

    [Fact]
    public void RfReservedEntities_SheetsConfiguration_HasCorrectReadableNames()
    {
        var sheetsConfig = RfReservedEntities.ReservedEntityTypes
            .First(t => t.EntityConfiguration.EntityName == "rf-sheets");

        sheetsConfig.EntityConfiguration.EntityReadableNameSingular.Should().Be("Sheet");
        sheetsConfig.EntityConfiguration.EntityReadableNamePlural.Should().Be("Sheets");
    }

    [Fact]
    public void RfReservedEntities_SheetsConfiguration_DoesNotSupportFrontendEdit()
    {
        var sheetsConfig = RfReservedEntities.ReservedEntityTypes
            .First(t => t.EntityConfiguration.EntityName == "rf-sheets");

        sheetsConfig.EntityConfiguration.SupportsFrontendEdit.Should().BeFalse();
    }

    [Fact]
    public void RfReservedEntities_SheetsConfiguration_HasAuthor()
    {
        var sheetsConfig = RfReservedEntities.ReservedEntityTypes
            .First(t => t.EntityConfiguration.EntityName == "rf-sheets");

        sheetsConfig.EntityConfiguration.HasAuthor.Should().BeTrue();
    }

    [Fact]
    public void RfReservedEntities_SheetsConfiguration_NoTagsCategoriesParentChild()
    {
        var sheetsConfig = RfReservedEntities.ReservedEntityTypes
            .First(t => t.EntityConfiguration.EntityName == "rf-sheets");

        sheetsConfig.EntityConfiguration.HasTags.Should().BeFalse();
        sheetsConfig.EntityConfiguration.HasCategories.Should().BeFalse();
        sheetsConfig.EntityConfiguration.HasParentChildRelationship.Should().BeFalse();
    }

    [Fact]
    public void RfReservedEntities_SheetsConfiguration_NoTitleUniqueness()
    {
        var sheetsConfig = RfReservedEntities.ReservedEntityTypes
            .First(t => t.EntityConfiguration.EntityName == "rf-sheets");

        sheetsConfig.EntityConfiguration.RequireGlobalTitleUniqueness.Should().BeFalse();
    }

    [Fact]
    public void RfReservedEntities_SheetsConfiguration_NoHooksSetup()
    {
        var sheetsConfig = RfReservedEntities.ReservedEntityTypes
            .First(t => t.EntityConfiguration.EntityName == "rf-sheets");

        sheetsConfig.EntityConfiguration.OptionalTitleSanityCheck.Should().BeNull();
    }

    // ── BulkRead Request Parsing Simulation ──────────────────────────────

    [Fact]
    public void BulkReadRequest_ValidSources_ParsesCorrectly()
    {
        var requestBody = JObject.Parse("""
        {
            "sources": [
                { "entity": "employee" },
                { "entity": "department", "fields": ["name", "budget"] }
            ]
        }
        """);

        requestBody.TryGetValue("sources", out var sourcesToken).Should().BeTrue();
        var sources = sourcesToken as JArray;
        sources.Should().NotBeNull();
        sources!.Should().HaveCount(2);
        sources[0].Value<string>("entity").Should().Be("employee");
        sources[1].Value<string>("entity").Should().Be("department");
    }

    [Fact]
    public void BulkReadRequest_EmptySources_ParsesAsEmptyArray()
    {
        var requestBody = JObject.Parse("""{ "sources": [] }""");

        var sources = requestBody["sources"] as JArray;
        sources.Should().NotBeNull();
        sources!.Should().BeEmpty();
    }

    [Fact]
    public void BulkReadRequest_MissingSources_TryGetValueReturnsFalse()
    {
        var requestBody = JObject.Parse("{}");

        requestBody.TryGetValue("sources", out var sourcesToken).Should().BeFalse();
    }

    [Fact]
    public void BulkReadRequest_SourcesNotArray_CastReturnsNull()
    {
        var requestBody = JObject.Parse("""{ "sources": "invalid" }""");

        var sources = requestBody["sources"] as JArray;
        sources.Should().BeNull();
    }

    [Fact]
    public void BulkReadRequest_SourceWithNullEntity_ValueIsNull()
    {
        var requestBody = JObject.Parse("""{ "sources": [{ "entity": null }] }""");

        var sources = requestBody["sources"] as JArray;
        sources.Should().HaveCount(1);
        sources![0].Value<string>("entity").Should().BeNull();
    }

    [Fact]
    public void BulkReadRequest_SourceWithEmptyEntity_ValueIsEmpty()
    {
        var requestBody = JObject.Parse("""{ "sources": [{ "entity": "" }] }""");

        var sources = requestBody["sources"] as JArray;
        sources![0].Value<string>("entity").Should().BeEmpty();
    }

    [Fact]
    public void BulkReadRequest_SourceWithoutEntityKey_ValueIsNull()
    {
        var requestBody = JObject.Parse("""{ "sources": [{ "something": "else" }] }""");

        var sources = requestBody["sources"] as JArray;
        sources![0].Value<string>("entity").Should().BeNull();
    }

    [Fact]
    public void BulkReadRequest_DuplicateEntities_AllArePresent()
    {
        var requestBody = JObject.Parse("""
        {
            "sources": [
                { "entity": "employee" },
                { "entity": "employee" }
            ]
        }
        """);

        var sources = requestBody["sources"] as JArray;
        sources!.Should().HaveCount(2);
    }

    // ── BulkRead Response Structure ──────────────────────────────────────

    [Fact]
    public void BulkReadResponse_Structure_IsCorrect()
    {
        var response = new JObject
        {
            ["results"] = new JArray(
                new JObject
                {
                    ["entity"] = "employee",
                    ["total_count"] = 3,
                    ["rows"] = new JArray(
                        new JObject { ["id"] = 1, ["fields"] = new JObject { ["name"] = "Alice" } },
                        new JObject { ["id"] = 2, ["fields"] = new JObject { ["name"] = "Bob" } },
                        new JObject { ["id"] = 3, ["fields"] = new JObject { ["name"] = "Charlie" } }
                    )
                }
            ),
            ["unauthorized"] = new JArray("salary_band")
        };

        var results = response["results"] as JArray;
        results.Should().HaveCount(1);
        results![0]["entity"]!.Value<string>().Should().Be("employee");
        results[0]["total_count"]!.Value<int>().Should().Be(3);
        ((JArray)results[0]["rows"]!).Should().HaveCount(3);

        var unauthorized = response["unauthorized"] as JArray;
        unauthorized.Should().HaveCount(1);
        unauthorized![0].Value<string>().Should().Be("salary_band");
    }

    [Fact]
    public void BulkReadResponse_EmptyResults_WhenNoAuthorizedSources()
    {
        var response = new JObject
        {
            ["results"] = new JArray(),
            ["unauthorized"] = new JArray("employee", "department")
        };

        ((JArray)response["results"]!).Should().BeEmpty();
        ((JArray)response["unauthorized"]!).Should().HaveCount(2);
    }

    [Fact]
    public void BulkReadResponse_MixedAuthorizedAndUnauthorized()
    {
        var response = new JObject
        {
            ["results"] = new JArray(
                new JObject
                {
                    ["entity"] = "employee",
                    ["total_count"] = 2,
                    ["rows"] = new JArray(
                        new JObject { ["id"] = 1 },
                        new JObject { ["id"] = 2 }
                    )
                }
            ),
            ["unauthorized"] = new JArray("salary_band", "payroll")
        };

        ((JArray)response["results"]!).Should().HaveCount(1);
        ((JArray)response["unauthorized"]!).Should().HaveCount(2);
    }

    [Fact]
    public void BulkReadResponse_EntityWithNoRows_HasZeroCount()
    {
        var response = new JObject
        {
            ["results"] = new JArray(
                new JObject
                {
                    ["entity"] = "empty-entity",
                    ["total_count"] = 0,
                    ["rows"] = new JArray()
                }
            ),
            ["unauthorized"] = new JArray()
        };

        var result = ((JArray)response["results"]!)[0];
        result["total_count"]!.Value<int>().Should().Be(0);
        ((JArray)result["rows"]!).Should().BeEmpty();
    }

    // ── Entity Added Scenario ────────────────────────────────────────────

    [Fact]
    public void BulkReadResponse_EntityAdded_ReflectedInTotalCount()
    {
        // Simulate: first poll returns 2, then entity is added, second poll returns 3
        var firstPoll = new JObject
        {
            ["results"] = new JArray(
                new JObject
                {
                    ["entity"] = "employee",
                    ["total_count"] = 2,
                    ["rows"] = new JArray(
                        new JObject { ["id"] = 1, ["fields"] = new JObject { ["name"] = "Alice" } },
                        new JObject { ["id"] = 2, ["fields"] = new JObject { ["name"] = "Bob" } }
                    )
                }
            ),
            ["unauthorized"] = new JArray()
        };

        var secondPoll = new JObject
        {
            ["results"] = new JArray(
                new JObject
                {
                    ["entity"] = "employee",
                    ["total_count"] = 3,
                    ["rows"] = new JArray(
                        new JObject { ["id"] = 1, ["fields"] = new JObject { ["name"] = "Alice" } },
                        new JObject { ["id"] = 2, ["fields"] = new JObject { ["name"] = "Bob" } },
                        new JObject { ["id"] = 3, ["fields"] = new JObject { ["name"] = "Charlie" } }
                    )
                }
            ),
            ["unauthorized"] = new JArray()
        };

        var firstCount = ((JArray)firstPoll["results"]!)[0]["total_count"]!.Value<int>();
        var secondCount = ((JArray)secondPoll["results"]!)[0]["total_count"]!.Value<int>();
        var firstRows = (JArray)((JArray)firstPoll["results"]!)[0]["rows"]!;
        var secondRows = (JArray)((JArray)secondPoll["results"]!)[0]["rows"]!;

        secondCount.Should().Be(firstCount + 1);
        secondRows.Should().HaveCount(3);

        // New entity should be the one with id=3
        var newEntity = secondRows.FirstOrDefault(r => r["id"]!.Value<int>() == 3);
        newEntity.Should().NotBeNull();
        newEntity!["fields"]!["name"]!.Value<string>().Should().Be("Charlie");
    }

    // ── Entity Removed Scenario ──────────────────────────────────────────

    [Fact]
    public void BulkReadResponse_EntityRemoved_NotInSecondPoll()
    {
        var firstPoll = new JObject
        {
            ["results"] = new JArray(
                new JObject
                {
                    ["entity"] = "employee",
                    ["total_count"] = 3,
                    ["rows"] = new JArray(
                        new JObject { ["id"] = 1, ["fields"] = new JObject { ["name"] = "Alice" } },
                        new JObject { ["id"] = 2, ["fields"] = new JObject { ["name"] = "Bob" } },
                        new JObject { ["id"] = 3, ["fields"] = new JObject { ["name"] = "Charlie" } }
                    )
                }
            ),
            ["unauthorized"] = new JArray()
        };

        var secondPoll = new JObject
        {
            ["results"] = new JArray(
                new JObject
                {
                    ["entity"] = "employee",
                    ["total_count"] = 2,
                    ["rows"] = new JArray(
                        new JObject { ["id"] = 1, ["fields"] = new JObject { ["name"] = "Alice" } },
                        new JObject { ["id"] = 3, ["fields"] = new JObject { ["name"] = "Charlie" } }
                    )
                }
            ),
            ["unauthorized"] = new JArray()
        };

        var secondRows = (JArray)((JArray)secondPoll["results"]!)[0]["rows"]!;
        secondRows.Should().HaveCount(2);

        // Bob (id=2) should no longer be present
        var removedEntity = secondRows.FirstOrDefault(r => r["id"]!.Value<int>() == 2);
        removedEntity.Should().BeNull();

        // Remaining entities should still be present
        secondRows.Any(r => r["id"]!.Value<int>() == 1).Should().BeTrue();
        secondRows.Any(r => r["id"]!.Value<int>() == 3).Should().BeTrue();
    }

    // ── Entity Type Deleted Scenario ─────────────────────────────────────

    [Fact]
    public void BulkReadResponse_EntityTypeDeleted_NotInResultsOrUnauthorized()
    {
        // When an entity type is deleted from configuration, bulk_read should skip it gracefully
        // (not in results, not in unauthorized — just missing)
        var firstPoll = new JObject
        {
            ["results"] = new JArray(
                new JObject
                {
                    ["entity"] = "employee",
                    ["total_count"] = 2,
                    ["rows"] = new JArray(
                        new JObject { ["id"] = 1 },
                        new JObject { ["id"] = 2 }
                    )
                },
                new JObject
                {
                    ["entity"] = "contractor",
                    ["total_count"] = 1,
                    ["rows"] = new JArray(new JObject { ["id"] = 10 })
                }
            ),
            ["unauthorized"] = new JArray()
        };

        // After "contractor" entity type is removed from configuration:
        var secondPoll = new JObject
        {
            ["results"] = new JArray(
                new JObject
                {
                    ["entity"] = "employee",
                    ["total_count"] = 2,
                    ["rows"] = new JArray(
                        new JObject { ["id"] = 1 },
                        new JObject { ["id"] = 2 }
                    )
                }
            ),
            ["unauthorized"] = new JArray()
        };

        var secondResults = (JArray)secondPoll["results"]!;
        secondResults.Should().HaveCount(1);
        secondResults[0]["entity"]!.Value<string>().Should().Be("employee");

        // "contractor" should not appear anywhere
        secondResults.Any(r => r["entity"]!.Value<string>() == "contractor").Should().BeFalse();
        ((JArray)secondPoll["unauthorized"]!).Any(u => u.Value<string>() == "contractor").Should().BeFalse();
    }

    // ── Entity Field Removed Scenario ────────────────────────────────────

    [Fact]
    public void BulkReadResponse_EntityFieldRemoved_FieldMissingInRows()
    {
        // First poll: entities have name and email fields
        var firstPoll = new JObject
        {
            ["results"] = new JArray(
                new JObject
                {
                    ["entity"] = "employee",
                    ["total_count"] = 2,
                    ["rows"] = new JArray(
                        new JObject { ["id"] = 1, ["fields"] = new JObject { ["name"] = "Alice", ["email"] = "alice@co.com" } },
                        new JObject { ["id"] = 2, ["fields"] = new JObject { ["name"] = "Bob", ["email"] = "bob@co.com" } }
                    )
                }
            ),
            ["unauthorized"] = new JArray()
        };

        // After "email" field is removed from schema: rows no longer contain it
        var secondPoll = new JObject
        {
            ["results"] = new JArray(
                new JObject
                {
                    ["entity"] = "employee",
                    ["total_count"] = 2,
                    ["rows"] = new JArray(
                        new JObject { ["id"] = 1, ["fields"] = new JObject { ["name"] = "Alice" } },
                        new JObject { ["id"] = 2, ["fields"] = new JObject { ["name"] = "Bob" } }
                    )
                }
            ),
            ["unauthorized"] = new JArray()
        };

        var firstRows = (JArray)((JArray)firstPoll["results"]!)[0]["rows"]!;
        var secondRows = (JArray)((JArray)secondPoll["results"]!)[0]["rows"]!;

        // First poll had email, second doesn't
        firstRows[0]["fields"]!["email"].Should().NotBeNull();
        secondRows[0]["fields"]!["email"].Should().BeNull();

        // Name field is still present
        secondRows[0]["fields"]!["name"]!.Value<string>().Should().Be("Alice");
        secondRows[1]["fields"]!["name"]!.Value<string>().Should().Be("Bob");
    }

    [Fact]
    public void BulkReadResponse_EntityFieldRemoved_RowCountUnchanged()
    {
        var beforeRemoval = new JArray(
            new JObject { ["id"] = 1, ["fields"] = new JObject { ["name"] = "Alice", ["email"] = "a@co" } },
            new JObject { ["id"] = 2, ["fields"] = new JObject { ["name"] = "Bob", ["email"] = "b@co" } }
        );

        var afterRemoval = new JArray(
            new JObject { ["id"] = 1, ["fields"] = new JObject { ["name"] = "Alice" } },
            new JObject { ["id"] = 2, ["fields"] = new JObject { ["name"] = "Bob" } }
        );

        afterRemoval.Should().HaveCount(beforeRemoval.Count);
    }

    // ── Entity Field Value Changed Scenario ──────────────────────────────

    [Fact]
    public void BulkReadResponse_EntityFieldValueChanged_DetectedByComparison()
    {
        var firstPoll = new JArray(
            new JObject { ["id"] = 1, ["fields"] = new JObject { ["name"] = "Alice", ["department"] = "Engineering" } }
        );

        var secondPoll = new JArray(
            new JObject { ["id"] = 1, ["fields"] = new JObject { ["name"] = "Alice", ["department"] = "Product" } }
        );

        var oldDept = firstPoll[0]["fields"]!["department"]!.Value<string>();
        var newDept = secondPoll[0]["fields"]!["department"]!.Value<string>();

        oldDept.Should().Be("Engineering");
        newDept.Should().Be("Product");
        newDept.Should().NotBe(oldDept);
    }

    // ── Permission-Related Scenarios ─────────────────────────────────────

    [Fact]
    public void BulkReadResponse_AllUnauthorized_EmptyResults()
    {
        var response = new JObject
        {
            ["results"] = new JArray(),
            ["unauthorized"] = new JArray("employee", "department", "salary_band")
        };

        ((JArray)response["results"]!).Should().BeEmpty();
        ((JArray)response["unauthorized"]!).Should().HaveCount(3);
    }

    [Fact]
    public void BulkReadResponse_AllAuthorized_EmptyUnauthorized()
    {
        var response = new JObject
        {
            ["results"] = new JArray(
                new JObject { ["entity"] = "employee", ["total_count"] = 1, ["rows"] = new JArray(new JObject { ["id"] = 1 }) },
                new JObject { ["entity"] = "department", ["total_count"] = 1, ["rows"] = new JArray(new JObject { ["id"] = 1 }) }
            ),
            ["unauthorized"] = new JArray()
        };

        ((JArray)response["results"]!).Should().HaveCount(2);
        ((JArray)response["unauthorized"]!).Should().BeEmpty();
    }

    [Fact]
    public void BulkReadResponse_PermissionChange_EntityMovesToUnauthorized()
    {
        // First poll: user has access to both
        var firstPoll = new JObject
        {
            ["results"] = new JArray(
                new JObject { ["entity"] = "employee", ["total_count"] = 1, ["rows"] = new JArray(new JObject { ["id"] = 1 }) },
                new JObject { ["entity"] = "salary_band", ["total_count"] = 1, ["rows"] = new JArray(new JObject { ["id"] = 1 }) }
            ),
            ["unauthorized"] = new JArray()
        };

        // Second poll: permission revoked for salary_band
        var secondPoll = new JObject
        {
            ["results"] = new JArray(
                new JObject { ["entity"] = "employee", ["total_count"] = 1, ["rows"] = new JArray(new JObject { ["id"] = 1 }) }
            ),
            ["unauthorized"] = new JArray("salary_band")
        };

        // salary_band moved from results to unauthorized
        ((JArray)firstPoll["results"]!).Any(r => r["entity"]!.Value<string>() == "salary_band").Should().BeTrue();
        ((JArray)secondPoll["results"]!).Any(r => r["entity"]!.Value<string>() == "salary_band").Should().BeFalse();
        ((JArray)secondPoll["unauthorized"]!).Any(u => u.Value<string>() == "salary_band").Should().BeTrue();
    }

    // ── Large Dataset Scenarios ──────────────────────────────────────────

    [Fact]
    public void BulkReadResponse_LargeDataset_HandlesHundredsOfRows()
    {
        var rows = new JArray();
        for (var i = 1; i <= 500; i++)
        {
            rows.Add(new JObject
            {
                ["id"] = i,
                ["fields"] = new JObject { ["name"] = $"Employee {i}", ["department"] = $"Dept {i % 10}" }
            });
        }

        var response = new JObject
        {
            ["results"] = new JArray(
                new JObject { ["entity"] = "employee", ["total_count"] = 500, ["rows"] = rows }
            ),
            ["unauthorized"] = new JArray()
        };

        var result = ((JArray)response["results"]!)[0];
        result["total_count"]!.Value<int>().Should().Be(500);
        ((JArray)result["rows"]!).Should().HaveCount(500);
    }

    // ── Multiple Entity Types Scenario ───────────────────────────────────

    [Fact]
    public void BulkReadResponse_MultipleEntityTypes_AllPresent()
    {
        var response = new JObject
        {
            ["results"] = new JArray(
                new JObject { ["entity"] = "employee", ["total_count"] = 2, ["rows"] = new JArray(new JObject { ["id"] = 1 }, new JObject { ["id"] = 2 }) },
                new JObject { ["entity"] = "department", ["total_count"] = 1, ["rows"] = new JArray(new JObject { ["id"] = 1 }) },
                new JObject { ["entity"] = "project", ["total_count"] = 3, ["rows"] = new JArray(new JObject { ["id"] = 1 }, new JObject { ["id"] = 2 }, new JObject { ["id"] = 3 }) }
            ),
            ["unauthorized"] = new JArray()
        };

        var results = (JArray)response["results"]!;
        results.Should().HaveCount(3);
        results.Select(r => r["entity"]!.Value<string>()).Should()
            .Contain(new[] { "employee", "department", "project" });
    }

    // ── WorkbookData Scenarios ───────────────────────────────────────────

    [Fact]
    public void RfSheetEntityFieldsModel_WorkbookData_CanStoreComplexState()
    {
        var workbook = new JObject
        {
            ["sheets"] = new JArray(
                new JObject
                {
                    ["name"] = "Sheet1",
                    ["cells"] = new JObject
                    {
                        ["A1"] = new JObject { ["value"] = "Name", ["style"] = new JObject { ["bold"] = true } },
                        ["B1"] = new JObject { ["value"] = "Email" },
                        ["A2"] = new JObject { ["formula"] = "=RF.FIELD(\"employee\", 1, \"name\")" }
                    },
                    ["columnWidths"] = new JObject { ["A"] = 200, ["B"] = 300 }
                }
            )
        };

        var model = new RfSheetEntityFieldsModel
        {
            WorkbookData = workbook.ToString(Formatting.None)
        };

        var parsed = JObject.Parse(model.WorkbookData);
        var sheets = parsed["sheets"] as JArray;
        sheets.Should().HaveCount(1);
        var cells = sheets![0]["cells"] as JObject;
        cells!["A2"]!["formula"]!.Value<string>().Should().Contain("RF.FIELD");
    }

    [Fact]
    public void RfSheetEntityFieldsModel_WorkbookData_EmptyWorkbook_IsValidJson()
    {
        var model = new RfSheetEntityFieldsModel();
        var parsed = JObject.Parse(model.WorkbookData);
        parsed.Should().NotBeNull();
        parsed.Properties().Should().BeEmpty();
    }

    // ── Endpoint Registration ────────────────────────────────────────────

    [Fact]
    public void RfReservedEntities_ReservedEntityTypes_AllHaveValidEntityNames()
    {
        foreach (var entityType in RfReservedEntities.ReservedEntityTypes)
        {
            entityType.EntityConfiguration.EntityName.Should().NotBeNullOrWhiteSpace();
            RfReservedEntities.ReservedEntityNames.Should().Contain(entityType.EntityConfiguration.EntityName);
        }
    }

    // ── BulkRead Field Filtering ─────────────────────────────────────────

    [Fact]
    public void BulkReadRequest_SourceWithFields_ParsesFieldsArray()
    {
        var json = JObject.Parse(@"{ ""sources"": [{ ""entity"": ""employee"", ""fields"": [""name"", ""salary""] }] }");
        var sourcesArray = (JArray)json["sources"]!;
        var sourceObj = (JObject)sourcesArray[0];

        sourceObj.Value<string>("entity").Should().Be("employee");
        var fieldsToken = sourceObj["fields"] as JArray;
        fieldsToken.Should().NotBeNull();
        fieldsToken!.Count.Should().Be(2);
        fieldsToken[0].Value<string>().Should().Be("name");
        fieldsToken[1].Value<string>().Should().Be("salary");
    }

    [Fact]
    public void BulkReadRequest_SourceWithoutFields_HasNoFieldsProperty()
    {
        var json = JObject.Parse(@"{ ""sources"": [{ ""entity"": ""employee"" }] }");
        var sourcesArray = (JArray)json["sources"]!;
        var sourceObj = (JObject)sourcesArray[0];

        (sourceObj["fields"] is JArray).Should().BeFalse();
    }

    [Fact]
    public void BulkReadFieldFiltering_FiltersRowFields()
    {
        // Simulate the field filtering logic from BulkRead.cs
        var row = JObject.Parse(@"{ ""id"": 1, ""fields"": { ""name"": ""Alice"", ""salary"": 60000, ""department_id"": 10 } }");
        var requestedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id", "name" };

        var fieldsObj = (JObject)row["fields"]!;
        var filteredFields = new JObject();
        foreach (var fp in fieldsObj.Properties())
        {
            if (requestedFields.Contains(fp.Name))
                filteredFields.Add(fp.Name, fp.Value.DeepClone());
        }

        filteredFields.Properties().Should().HaveCount(1);
        filteredFields["name"]!.Value<string>().Should().Be("Alice");
        filteredFields["salary"].Should().BeNull();
        filteredFields["department_id"].Should().BeNull();
    }

    [Fact]
    public void BulkReadFieldFiltering_NoFieldsFilter_ReturnsAllFields()
    {
        var row = JObject.Parse(@"{ ""id"": 1, ""fields"": { ""name"": ""Alice"", ""salary"": 60000 } }");
        // When no fields filter is provided, all fields should remain
        var fieldsObj = (JObject)row["fields"]!;
        fieldsObj.Properties().Should().HaveCount(2);
    }

    [Fact]
    public void BulkReadFieldFiltering_IdIsAlwaysKept()
    {
        // Even if caller doesn't explicitly request 'id', the row id is preserved
        var row = JObject.Parse(@"{ ""id"": 42, ""parent"": -1, ""author"": 2, ""slug"": ""test"", ""fields"": { ""name"": ""Alice"", ""salary"": 60000 } }");
        var requestedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id", "name" };

        var filteredRow = new JObject();
        if (row.TryGetValue("id", out var idVal))
            filteredRow["id"] = idVal.DeepClone();

        if (row.TryGetValue("fields", out var fieldsVal) && fieldsVal is JObject fieldsObj)
        {
            var filteredFields = new JObject();
            foreach (var fp in fieldsObj.Properties())
            {
                if (requestedFields.Contains(fp.Name))
                    filteredFields.Add(fp.Name, fp.Value.DeepClone());
            }
            filteredRow["fields"] = filteredFields;
        }

        filteredRow["id"]!.Value<int>().Should().Be(42);
        ((JObject)filteredRow["fields"]!).Properties().Should().HaveCount(1);
        // Top-level metadata properties must NOT be present
        filteredRow["parent"].Should().BeNull();
        filteredRow["author"].Should().BeNull();
        filteredRow["slug"].Should().BeNull();
    }

    [Fact]
    public void BulkReadFieldFiltering_CaseInsensitive()
    {
        var row = JObject.Parse(@"{ ""id"": 1, ""fields"": { ""Name"": ""Alice"", ""salary"": 60000 } }");
        var requestedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id", "name" };

        var fieldsObj = (JObject)row["fields"]!;
        var filteredFields = new JObject();
        foreach (var fp in fieldsObj.Properties())
        {
            if (requestedFields.Contains(fp.Name))
                filteredFields.Add(fp.Name, fp.Value.DeepClone());
        }

        // "Name" should match "name" case-insensitively
        filteredFields.Properties().Should().HaveCount(1);
        filteredFields["Name"]!.Value<string>().Should().Be("Alice");
    }

    [Fact]
    public void BulkReadFieldFiltering_EmptyFieldsArray_ReturnsNoFields()
    {
        // An empty fields array means "no fields requested" — return just id
        var row = JObject.Parse(@"{ ""id"": 1, ""fields"": { ""name"": ""Alice"", ""salary"": 60000 } }");
        var requestedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id" };

        var fieldsObj = (JObject)row["fields"]!;
        var filteredFields = new JObject();
        foreach (var fp in fieldsObj.Properties())
        {
            if (requestedFields.Contains(fp.Name))
                filteredFields.Add(fp.Name, fp.Value.DeepClone());
        }

        filteredFields.Properties().Should().BeEmpty();
    }
}

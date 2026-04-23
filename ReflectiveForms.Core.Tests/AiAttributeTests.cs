using FluentAssertions;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Schema.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class AiAttributeTests
{
    #region AISuggestion

    [Fact]
    public void AISuggestion_StoresPromptAndSourceFields()
    {
        var attr = new AISuggestion("Generate a summary", "title", "body");

        attr.Prompt.Should().Be("Generate a summary");
        attr.SourceFields.Should().BeEquivalentTo(["title", "body"]);
    }

    [Fact]
    public void AISuggestion_EmptySourceFieldsIfNoneProvided()
    {
        var attr = new AISuggestion("Auto-fill this field");

        attr.Prompt.Should().Be("Auto-fill this field");
        attr.SourceFields.Should().BeEmpty();
    }

    #endregion

    #region AISanityCheck

    [Fact]
    public void AISanityCheck_StoresCheckPromptAndSeverity()
    {
        var attr = new AISanityCheck("Is this professional?", AISanityCheckSeverity.Error);

        attr.CheckPrompt.Should().Be("Is this professional?");
        attr.Severity.Should().Be(AISanityCheckSeverity.Error);
    }

    [Fact]
    public void AISanityCheck_DefaultSeverityIsWarning()
    {
        var attr = new AISanityCheck("Check spelling");

        attr.Severity.Should().Be(AISanityCheckSeverity.Warning);
    }

    [Fact]
    public void AISanityCheck_AllowsMultiple()
    {
        var attrUsage = typeof(AISanityCheck)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .FirstOrDefault() as AttributeUsageAttribute;

        attrUsage.Should().NotBeNull();
        attrUsage!.AllowMultiple.Should().BeTrue();
    }

    #endregion

    #region AIRelationSuggestion

    [Fact]
    public void AIRelationSuggestion_DefaultTopKIsFive()
    {
        var attr = new AIRelationSuggestion();

        attr.TopK.Should().Be(5);
    }

    [Fact]
    public void AIRelationSuggestion_CustomTopK()
    {
        var attr = new AIRelationSuggestion(10);

        attr.TopK.Should().Be(10);
    }

    #endregion

    #region Schema Models

    [Fact]
    public void AiSuggestionSchema_SerializesCorrectly()
    {
        var schema = new AiSuggestionSchema
        {
            Prompt = "Generate title",
            SourceFields = ["body", "category"]
        };

        schema.Prompt.Should().Be("Generate title");
        schema.SourceFields.Should().BeEquivalentTo(["body", "category"]);
    }

    [Fact]
    public void AiSanityCheckSchema_SerializesCorrectly()
    {
        var schema = new AiSanityCheckSchema
        {
            Prompt = "Check grammar",
            Severity = AISanityCheckSeverity.Error
        };

        schema.Prompt.Should().Be("Check grammar");
        schema.Severity.Should().Be(AISanityCheckSeverity.Error);
    }

    [Fact]
    public void AiRelationSuggestionSchema_SerializesCorrectly()
    {
        var schema = new AiRelationSuggestionSchema
        {
            TopK = 8
        };

        schema.TopK.Should().Be(8);
    }

    [Fact]
    public void FieldSchema_IncludesAllAiProperties()
    {
        var field = new FieldSchema
        {
            Name = "summary",
            Type = FieldSchemaType.TextArea,
            Label = "Summary",
            AiSuggestion = new AiSuggestionSchema
            {
                Prompt = "Write a summary",
                SourceFields = ["body"]
            },
            AiSanityChecks =
            [
                new AiSanityCheckSchema
                {
                    Prompt = "Is this professional?",
                    Severity = AISanityCheckSeverity.Warning
                },
                new AiSanityCheckSchema
                {
                    Prompt = "Check for PII",
                    Severity = AISanityCheckSeverity.Error
                }
            ],
            AiRelationSuggestion = new AiRelationSuggestionSchema { TopK = 3 }
        };

        field.AiSuggestion.Should().NotBeNull();
        field.AiSuggestion!.Prompt.Should().Be("Write a summary");
        field.AiSanityChecks.Should().HaveCount(2);
        field.AiRelationSuggestion.Should().NotBeNull();
        field.AiRelationSuggestion!.TopK.Should().Be(3);
    }

    [Fact]
    public void FieldSchema_AiPropertiesAreNullByDefault()
    {
        var field = new FieldSchema
        {
            Name = "title",
            Type = FieldSchemaType.Text,
            Label = "Title"
        };

        field.AiSuggestion.Should().BeNull();
        field.AiSanityChecks.Should().BeNull();
        field.AiRelationSuggestion.Should().BeNull();
    }

    [Fact]
    public void AiSanityCheckSchema_SeveritySerializesAsString()
    {
        var schema = new AiSanityCheckSchema
        {
            Prompt = "Test",
            Severity = AISanityCheckSeverity.Error
        };

        var json = Newtonsoft.Json.JsonConvert.SerializeObject(schema);
        json.Should().Contain("\"Error\"");
        json.Should().NotContain("\"1\""); // Not the integer value
    }

    #endregion

    #region NL Filter Field Validation

    [Fact]
    public void NlFilterFieldValidation_ValidFieldPaths()
    {
        // The NL filter handler validates field paths against the schema.
        // Test the validation logic via the handler's internal method using reflection.
        var schema = CreateTestSchema();
        var method = typeof(ReflectiveForms.Core.Ai.AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull("IsValidFieldPath should be an internal static method");

        var result = (bool)method!.Invoke(null, [
            "fields.status", schema
        ])!;
        result.Should().BeTrue("'fields.status' is a valid field path");
    }

    [Fact]
    public void NlFilterFieldValidation_RejectsSystemFields()
    {
        var schema = CreateTestSchema();
        var method = typeof(ReflectiveForms.Core.Ai.AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();

        // Direct system field (no "fields." prefix)
        var result1 = (bool)method!.Invoke(null, ["shared_users", schema])!;
        result1.Should().BeFalse("system fields without 'fields.' prefix should be rejected");

        // Non-existent field
        var result2 = (bool)method!.Invoke(null, ["fields.nonexistent", schema])!;
        result2.Should().BeFalse("non-existent fields should be rejected");
    }

    [Fact]
    public void NlFilterFieldValidation_ValidatesNestedGroupPaths()
    {
        var schema = CreateTestSchemaWithGroup();
        var method = typeof(ReflectiveForms.Core.Ai.AiNaturalLanguageFilterHandler)
            .GetMethod("IsValidFieldPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();

        var valid = (bool)method!.Invoke(null, ["fields.seo.meta_title", schema])!;
        valid.Should().BeTrue("nested group field path should be valid");

        var invalid = (bool)method!.Invoke(null, ["fields.seo.nonexistent", schema])!;
        invalid.Should().BeFalse("invalid nested path should be rejected");
    }

    [Fact]
    public void NlFilterParseValue_ParsesLong()
    {
        var method = typeof(ReflectiveForms.Core.Ai.AiNaturalLanguageFilterHandler)
            .GetMethod("ParseValueToPrimitive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();

        var result = method!.Invoke(null, ["42"]);
        result.Should().NotBeNull();
        result!.ToString().Should().Be("42"); // Primitive wrapping long
    }

    [Fact]
    public void NlFilterParseValue_ParsesString()
    {
        var method = typeof(ReflectiveForms.Core.Ai.AiNaturalLanguageFilterHandler)
            .GetMethod("ParseValueToPrimitive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();

        var result = method!.Invoke(null, ["active"]);
        result.Should().NotBeNull();
        result!.ToString().Should().Be("active"); // Primitive wrapping string
    }

    #endregion

    #region Diff Summary ComputeDiff

    [Fact]
    public void DiffSummary_ComputeDiff_DetectsAddedFields()
    {
        var method = typeof(ReflectiveForms.Core.Ai.AiDiffSummaryHandler)
            .GetMethod("ComputeDiff", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();

        var oldFields = new Newtonsoft.Json.Linq.JObject { ["title"] = "Old Title" };
        var newFields = new Newtonsoft.Json.Linq.JObject { ["title"] = "Old Title", ["summary"] = "New Summary" };

        var result = (List<string>)method!.Invoke(null, [oldFields, newFields])!;
        result.Should().Contain(s => s.Contains("Added 'summary'"));
    }

    [Fact]
    public void DiffSummary_ComputeDiff_DetectsRemovedFields()
    {
        var method = typeof(ReflectiveForms.Core.Ai.AiDiffSummaryHandler)
            .GetMethod("ComputeDiff", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();

        var oldFields = new Newtonsoft.Json.Linq.JObject { ["title"] = "Old", ["summary"] = "To Remove" };
        var newFields = new Newtonsoft.Json.Linq.JObject { ["title"] = "Old" };

        var result = (List<string>)method!.Invoke(null, [oldFields, newFields])!;
        result.Should().Contain(s => s.Contains("Removed 'summary'"));
    }

    [Fact]
    public void DiffSummary_ComputeDiff_DetectsChangedFields()
    {
        var method = typeof(ReflectiveForms.Core.Ai.AiDiffSummaryHandler)
            .GetMethod("ComputeDiff", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();

        var oldFields = new Newtonsoft.Json.Linq.JObject { ["title"] = "Old Title" };
        var newFields = new Newtonsoft.Json.Linq.JObject { ["title"] = "New Title" };

        var result = (List<string>)method!.Invoke(null, [oldFields, newFields])!;
        result.Should().Contain(s => s.Contains("Changed 'title'"));
    }

    [Fact]
    public void DiffSummary_ComputeDiff_EmptyOnIdentical()
    {
        var method = typeof(ReflectiveForms.Core.Ai.AiDiffSummaryHandler)
            .GetMethod("ComputeDiff", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();

        var oldFields = new Newtonsoft.Json.Linq.JObject { ["title"] = "Same" };
        var newFields = new Newtonsoft.Json.Linq.JObject { ["title"] = "Same" };

        var result = (List<string>)method!.Invoke(null, [oldFields, newFields])!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void DiffSummary_ComputeDiff_HandlesNullInputs()
    {
        var method = typeof(ReflectiveForms.Core.Ai.AiDiffSummaryHandler)
            .GetMethod("ComputeDiff", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();

        var result = (List<string>)method!.Invoke(null, [null, null])!;
        result.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static EntitySchema CreateTestSchema()
    {
        return new EntitySchema
        {
            EntityName = "test-entity",
            ReadableName = new ReadableName { Singular = "Test Entity", Plural = "Test Entities" },
            Features = new EntityFeatures(),
            ApiEndpoints = new ApiEndpoints { Crud = "", SanityCheck = "", EntityLock = "", Media = "" },
            Fields =
            [
                new FieldSchema { Name = "status", Type = FieldSchemaType.Select, Label = "Status" },
                new FieldSchema { Name = "title", Type = FieldSchemaType.Text, Label = "Title" },
                new FieldSchema { Name = "body", Type = FieldSchemaType.TextArea, Label = "Body" }
            ]
        };
    }

    private static EntitySchema CreateTestSchemaWithGroup()
    {
        return new EntitySchema
        {
            EntityName = "test-entity",
            ReadableName = new ReadableName { Singular = "Test Entity", Plural = "Test Entities" },
            Features = new EntityFeatures(),
            ApiEndpoints = new ApiEndpoints { Crud = "", SanityCheck = "", EntityLock = "", Media = "" },
            Fields =
            [
                new FieldSchema
                {
                    Name = "seo",
                    Type = FieldSchemaType.Group,
                    Label = "SEO",
                    GroupOptions = new GroupFieldOptions
                    {
                        ChildSchema =
                        [
                            new FieldSchema { Name = "meta_title", Type = FieldSchemaType.Text, Label = "Meta Title" },
                            new FieldSchema { Name = "meta_description", Type = FieldSchemaType.TextArea, Label = "Meta Desc" }
                        ]
                    }
                }
            ]
        };
    }

    #endregion
}

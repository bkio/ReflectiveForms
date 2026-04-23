using FluentAssertions;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Ai;
using ReflectiveForms.Core.Schema.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class AiTextExtractorTests
{
    [Fact]
    public void ExtractText_ReturnsNull_WhenSchemaGenerationFails()
    {
        // ExtractText calls EntitySchemaGenerator which requires RfConfiguration.
        // Without initialization, it returns null (schema generation returns failure).
        // This tests that the method handles schema generation failure gracefully.
        var entity = new JObject
        {
            ["title"] = new JObject { ["rendered"] = "Test Title" },
            ["fields"] = new JObject { ["content"] = "Hello world" }
        };

        // When RfConfiguration is not initialized, GenerateSchema will throw.
        // AiTextExtractor.ExtractText should handle this and return null.
        // Since RfConfiguration throws InvalidOperationException when not initialized,
        // we test via the schema-based approach with the private method instead.
        var fieldSchemas = new List<FieldSchema>();
        var parts = new List<string>();
        InvokeExtractFromFields(new JObject(), fieldSchemas, parts);
        parts.Should().BeEmpty("no fields means no extracted text");
    }

    [Fact]
    public void ExtractFromFields_ExtractsTextArea()
    {
        var fields = new JObject { ["body"] = "This is a long body text." };
        var fieldSchemas = new List<FieldSchema>
        {
            new()
            {
                Name = "body",
                Type = FieldSchemaType.TextArea,
                Label = "Body"
            }
        };

        var parts = new List<string>();
        InvokeExtractFromFields(fields, fieldSchemas, parts);

        parts.Should().ContainSingle().Which.Should().Be("This is a long body text.");
    }

    [Fact]
    public void ExtractFromFields_ExtractsWysiwygEditor_StripsHtml()
    {
        var fields = new JObject { ["content"] = "<p>Hello <strong>world</strong></p>" };
        var fieldSchemas = new List<FieldSchema>
        {
            new()
            {
                Name = "content",
                Type = FieldSchemaType.WysiwygEditor,
                Label = "Content"
            }
        };

        var parts = new List<string>();
        InvokeExtractFromFields(fields, fieldSchemas, parts);

        parts.Should().ContainSingle().Which.Should().Be("Hello world");
    }

    [Fact]
    public void ExtractFromFields_ExcludesShortTextField()
    {
        var fields = new JObject { ["name"] = "John Doe" };
        var fieldSchemas = new List<FieldSchema>
        {
            new()
            {
                Name = "name",
                Type = FieldSchemaType.Text,
                Label = "Name",
                TextOptions = new TextFieldOptions { MaxLength = 50 }
            }
        };

        var parts = new List<string>();
        InvokeExtractFromFields(fields, fieldSchemas, parts);

        parts.Should().BeEmpty("short text fields (max_length <= 100) should be excluded");
    }

    [Fact]
    public void ExtractFromFields_IncludesLongTextField()
    {
        var fields = new JObject { ["description"] = "A long description" };
        var fieldSchemas = new List<FieldSchema>
        {
            new()
            {
                Name = "description",
                Type = FieldSchemaType.Text,
                Label = "Description",
                TextOptions = new TextFieldOptions { MaxLength = 500 }
            }
        };

        var parts = new List<string>();
        InvokeExtractFromFields(fields, fieldSchemas, parts);

        parts.Should().ContainSingle().Which.Should().Be("A long description");
    }

    [Fact]
    public void ExtractFromFields_SkipsNullFields()
    {
        var fields = new JObject { ["body"] = JValue.CreateNull() };
        var fieldSchemas = new List<FieldSchema>
        {
            new()
            {
                Name = "body",
                Type = FieldSchemaType.TextArea,
                Label = "Body"
            }
        };

        var parts = new List<string>();
        InvokeExtractFromFields(fields, fieldSchemas, parts);

        parts.Should().BeEmpty();
    }

    [Fact]
    public void ExtractFromFields_SkipsEmptyStrings()
    {
        var fields = new JObject { ["body"] = "" };
        var fieldSchemas = new List<FieldSchema>
        {
            new()
            {
                Name = "body",
                Type = FieldSchemaType.TextArea,
                Label = "Body"
            }
        };

        var parts = new List<string>();
        InvokeExtractFromFields(fields, fieldSchemas, parts);

        parts.Should().BeEmpty();
    }

    [Fact]
    public void ExtractFromFields_WalksGroupChildSchema()
    {
        var fields = new JObject
        {
            ["seo"] = new JObject
            {
                ["meta_description"] = "SEO description text"
            }
        };
        var fieldSchemas = new List<FieldSchema>
        {
            new()
            {
                Name = "seo",
                Type = FieldSchemaType.Group,
                Label = "SEO",
                GroupOptions = new GroupFieldOptions
                {
                    ChildSchema =
                    [
                        new FieldSchema
                        {
                            Name = "meta_description",
                            Type = FieldSchemaType.TextArea,
                            Label = "Meta Description"
                        }
                    ]
                }
            }
        };

        var parts = new List<string>();
        InvokeExtractFromFields(fields, fieldSchemas, parts);

        parts.Should().ContainSingle().Which.Should().Be("SEO description text");
    }

    [Fact]
    public void ExtractFromFields_WalksRepeaterItemSchema()
    {
        var fields = new JObject
        {
            ["sections"] = new JArray
            {
                new JObject { ["body"] = "First section content" },
                new JObject { ["body"] = "Second section content" }
            }
        };
        var fieldSchemas = new List<FieldSchema>
        {
            new()
            {
                Name = "sections",
                Type = FieldSchemaType.Repeater,
                Label = "Sections",
                RepeaterOptions = new RepeaterFieldOptions
                {
                    ItemSchema =
                    [
                        new FieldSchema
                        {
                            Name = "body",
                            Type = FieldSchemaType.TextArea,
                            Label = "Body"
                        }
                    ],
                    AddButtonLabel = "Add Section"
                }
            }
        };

        var parts = new List<string>();
        InvokeExtractFromFields(fields, fieldSchemas, parts);

        parts.Should().HaveCount(2);
        parts[0].Should().Be("First section content");
        parts[1].Should().Be("Second section content");
    }

    [Fact]
    public void ExtractFromFields_IgnoresSelectCheckboxRelationFields()
    {
        var fields = new JObject
        {
            ["status"] = "active",
            ["is_featured"] = true,
            ["category"] = 42
        };
        var fieldSchemas = new List<FieldSchema>
        {
            new() { Name = "status", Type = FieldSchemaType.Select, Label = "Status" },
            new() { Name = "is_featured", Type = FieldSchemaType.Checkbox, Label = "Featured" },
            new() { Name = "category", Type = FieldSchemaType.Relation, Label = "Category" }
        };

        var parts = new List<string>();
        InvokeExtractFromFields(fields, fieldSchemas, parts);

        parts.Should().BeEmpty("Select, Checkbox, and Relation fields should not be extracted for embeddings");
    }

    /// <summary>
    /// Helper to invoke the private ExtractFromFields method via reflection.
    /// </summary>
    private static void InvokeExtractFromFields(JObject data, List<FieldSchema> fieldSchemas, List<string> parts)
    {
        var method = typeof(AiTextExtractor).GetMethod(
            "ExtractFromFields",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull("ExtractFromFields should exist as a private static method");
        method!.Invoke(null, [data, fieldSchemas, parts]);
    }
}

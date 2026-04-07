using FluentAssertions;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Attributes.Fields;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class RepeaterFieldSanityCheckTests
{
    [Fact]
    public async Task Repeater_MissingField_Succeeds()
    {
        var field = new Repeater("Items", "Add items", typeof(Models.EntityFieldsModel), "Add");
        var obj = new JObject();

        var result = await field.SanityCheckAsync(1, obj, "items", "fields.items", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Repeater_NullField_ReplacedWithEmptyArray()
    {
        var field = new Repeater("Items", "Add items", typeof(Models.EntityFieldsModel), "Add");
        var obj = new JObject { ["items"] = JValue.CreateNull() };

        var result = await field.SanityCheckAsync(1, obj, "items", "fields.items", null!, default);

        result.IsSuccessful.Should().BeTrue();
        obj["items"]!.Type.Should().Be(JTokenType.Array);
    }

    [Fact]
    public async Task Repeater_NonArrayType_Fails()
    {
        var field = new Repeater("Items", "Add items", typeof(Models.EntityFieldsModel), "Add");
        var obj = new JObject { ["items"] = "not an array" };

        var result = await field.SanityCheckAsync(1, obj, "items", "fields.items", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not array");
    }

    [Fact]
    public async Task Repeater_BelowMinimum_Fails()
    {
        var field = new Repeater("Items", "Add items", typeof(Models.EntityFieldsModel), "Add",
            minimumRows: 2, maximumRows: 5);
        var obj = new JObject { ["items"] = new JArray { new JObject() } };

        var result = await field.SanityCheckAsync(1, obj, "items", "fields.items", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("at least 2");
    }

    [Fact]
    public async Task Repeater_AboveMaximum_Fails()
    {
        var field = new Repeater("Items", "Add items", typeof(Models.EntityFieldsModel), "Add",
            minimumRows: 1, maximumRows: 2);
        var obj = new JObject
        {
            ["items"] = new JArray { new JObject(), new JObject(), new JObject() }
        };

        var result = await field.SanityCheckAsync(1, obj, "items", "fields.items", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("maximum 2");
    }

    [Fact]
    public async Task Repeater_NonObjectArrayElement_Fails()
    {
        var field = new Repeater("Items", "Add items", typeof(Models.EntityFieldsModel), "Add");
        var obj = new JObject
        {
            ["items"] = new JArray { "not an object" }
        };

        var result = await field.SanityCheckAsync(1, obj, "items", "fields.items", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("non-object");
    }

    [Fact]
    public async Task Repeater_EmptyArray_Succeeds()
    {
        var field = new Repeater("Items", "Add items", typeof(Models.EntityFieldsModel), "Add");
        var obj = new JObject { ["items"] = new JArray() };

        var result = await field.SanityCheckAsync(1, obj, "items", "fields.items", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Repeater_ValidArrayWithObjects_Succeeds()
    {
        var field = new Repeater("Items", "Add items", typeof(Models.EntityFieldsModel), "Add");
        var obj = new JObject
        {
            ["items"] = new JArray { new JObject(), new JObject() }
        };

        var result = await field.SanityCheckAsync(1, obj, "items", "fields.items", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Repeater_ExactMinimum_Succeeds()
    {
        var field = new Repeater("Items", "Add items", typeof(Models.EntityFieldsModel), "Add",
            minimumRows: 2, maximumRows: 5);
        var obj = new JObject
        {
            ["items"] = new JArray { new JObject(), new JObject() }
        };

        var result = await field.SanityCheckAsync(1, obj, "items", "fields.items", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Repeater_ExactMaximum_Succeeds()
    {
        var field = new Repeater("Items", "Add items", typeof(Models.EntityFieldsModel), "Add",
            minimumRows: 1, maximumRows: 3);
        var obj = new JObject
        {
            ["items"] = new JArray { new JObject(), new JObject(), new JObject() }
        };

        var result = await field.SanityCheckAsync(1, obj, "items", "fields.items", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }
}

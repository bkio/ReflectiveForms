using FluentAssertions;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class FieldAttributeSanityCheckTests
{
    // ── Text field ──────────────────────────────────────────────────────

    [Fact]
    public async Task Text_Mandatory_MissingField_Fails()
    {
        var field = new Text("Name", "Enter name", mandatory: true, placeholderText: "name");
        var obj = new JObject();

        var result = await field.SanityCheckAsync(1, obj, "name", "fields.name", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("mandatory");
    }

    [Fact]
    public async Task Text_Mandatory_EmptyString_Fails()
    {
        var field = new Text("Name", "Enter name", mandatory: true, placeholderText: "name");
        var obj = new JObject { ["name"] = "" };

        var result = await field.SanityCheckAsync(1, obj, "name", "fields.name", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("at least one character");
    }

    [Fact]
    public async Task Text_Mandatory_ValidString_Succeeds()
    {
        var field = new Text("Name", "Enter name", mandatory: true, placeholderText: "name");
        var obj = new JObject { ["name"] = "Hello" };

        var result = await field.SanityCheckAsync(1, obj, "name", "fields.name", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Text_Optional_MissingField_Succeeds()
    {
        var field = new Text("Name", "Enter name", mandatory: false, placeholderText: "name");
        var obj = new JObject();

        var result = await field.SanityCheckAsync(1, obj, "name", "fields.name", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Text_Optional_NullValue_Succeeds()
    {
        var field = new Text("Name", "Enter name", mandatory: false, placeholderText: "name");
        var obj = new JObject { ["name"] = JValue.CreateNull() };

        var result = await field.SanityCheckAsync(1, obj, "name", "fields.name", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Text_WrongType_Fails()
    {
        var field = new Text("Name", "Enter name", mandatory: true, placeholderText: "name");
        var obj = new JObject { ["name"] = 42 };

        var result = await field.SanityCheckAsync(1, obj, "name", "fields.name", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Type is incorrect");
    }

    // ── Checkbox field ──────────────────────────────────────────────────

    [Fact]
    public async Task Checkbox_ValidBoolean_Succeeds()
    {
        var field = new Checkbox("Active", "Is active", defaultValue: false);
        var obj = new JObject { ["active"] = true };

        var result = await field.SanityCheckAsync(1, obj, "active", "fields.active", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Checkbox_WrongType_SetsDefault()
    {
        var field = new Checkbox("Active", "Is active", defaultValue: true);
        var obj = new JObject { ["active"] = "not a bool" };

        var result = await field.SanityCheckAsync(1, obj, "active", "fields.active", null!, default);

        result.IsSuccessful.Should().BeTrue();
        obj["active"]!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Checkbox_MissingField_SetsDefault()
    {
        var field = new Checkbox("Active", "Is active", defaultValue: false);
        var obj = new JObject();

        var result = await field.SanityCheckAsync(1, obj, "active", "fields.active", null!, default);

        result.IsSuccessful.Should().BeTrue();
        obj["active"]!.Value<bool>().Should().BeFalse();
    }

    // ── Select field ────────────────────────────────────────────────────

    [Fact]
    public async Task Select_ValidChoice_Succeeds()
    {
        var field = new Select("Color", "Choose color", "red", ["red : Red", "blue : Blue"]);
        var obj = new JObject { ["color"] = "red" };

        var result = await field.SanityCheckAsync(1, obj, "color", "fields.color", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Select_InvalidChoice_Fails()
    {
        var field = new Select("Color", "Choose color", "red", ["red : Red", "blue : Blue"]);
        var obj = new JObject { ["color"] = "green" };

        var result = await field.SanityCheckAsync(1, obj, "color", "fields.color", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unexpected choice");
    }

    [Fact]
    public async Task Select_EmptyStringChoice_Fails()
    {
        var field = new Select("Color", "Choose color", "red", ["red : Red", "blue : Blue"]);
        var obj = new JObject { ["color"] = "" };

        var result = await field.SanityCheckAsync(1, obj, "color", "fields.color", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Mandatory to choose");
    }

    [Fact]
    public async Task Select_WrongType_Fails()
    {
        var field = new Select("Color", "Choose color", "red", ["red : Red", "blue : Blue"]);
        var obj = new JObject { ["color"] = 123 };

        var result = await field.SanityCheckAsync(1, obj, "color", "fields.color", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Type is incorrect");
    }

    [Fact]
    public async Task Select_MissingField_Succeeds()
    {
        var field = new Select("Color", "Choose color", "red", ["red : Red"]);
        var obj = new JObject();

        var result = await field.SanityCheckAsync(1, obj, "color", "fields.color", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    // ── Number field ────────────────────────────────────────────────────

    [Fact]
    public async Task Number_ValidInteger_Succeeds()
    {
        var field = new Number("Count", "Enter count", mandatory: true, placeholderText: "0");
        var obj = new JObject { ["count"] = 5 };

        var result = await field.SanityCheckAsync(1, obj, "count", "fields.count", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Number_ValidFloat_Succeeds()
    {
        var field = new Number("Price", "Enter price", mandatory: true, placeholderText: "0");
        var obj = new JObject { ["price"] = 19.99 };

        var result = await field.SanityCheckAsync(1, obj, "price", "fields.price", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Number_Mandatory_MissingField_Fails()
    {
        var field = new Number("Count", "Enter count", mandatory: true, placeholderText: "0");
        var obj = new JObject();

        var result = await field.SanityCheckAsync(1, obj, "count", "fields.count", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("mandatory");
    }

    [Fact]
    public async Task Number_Optional_MissingField_Succeeds()
    {
        var field = new Number("Count", "Enter count", mandatory: false, placeholderText: "0");
        var obj = new JObject();

        var result = await field.SanityCheckAsync(1, obj, "count", "fields.count", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Number_BelowMinimum_Fails()
    {
        var field = new Number("Rating", "Rate 1-10", mandatory: true, placeholderText: "1", [1.0, 10.0]);
        var obj = new JObject { ["rating"] = 0 };

        var result = await field.SanityCheckAsync(1, obj, "rating", "fields.rating", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Must be >=");
    }

    [Fact]
    public async Task Number_AboveMaximum_Fails()
    {
        var field = new Number("Rating", "Rate 1-10", mandatory: true, placeholderText: "1", [1.0, 10.0]);
        var obj = new JObject { ["rating"] = 11 };

        var result = await field.SanityCheckAsync(1, obj, "rating", "fields.rating", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Must be <=");
    }

    [Fact]
    public async Task Number_AtMinimum_Succeeds()
    {
        var field = new Number("Rating", "Rate 1-10", mandatory: true, placeholderText: "1", [1.0, 10.0]);
        var obj = new JObject { ["rating"] = 1 };

        var result = await field.SanityCheckAsync(1, obj, "rating", "fields.rating", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Number_AtMaximum_Succeeds()
    {
        var field = new Number("Rating", "Rate 1-10", mandatory: true, placeholderText: "1", [1.0, 10.0]);
        var obj = new JObject { ["rating"] = 10 };

        var result = await field.SanityCheckAsync(1, obj, "rating", "fields.rating", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Number_WrongType_Fails()
    {
        var field = new Number("Count", "Enter count", mandatory: true, placeholderText: "0");
        var obj = new JObject { ["count"] = "not a number" };

        var result = await field.SanityCheckAsync(1, obj, "count", "fields.count", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Type is incorrect");
    }

    [Fact]
    public async Task Number_Optional_NullValue_Succeeds()
    {
        var field = new Number("Count", "Enter count", mandatory: false, placeholderText: "0");
        var obj = new JObject { ["count"] = JValue.CreateNull() };

        var result = await field.SanityCheckAsync(1, obj, "count", "fields.count", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    // ── DatePicker field ────────────────────────────────────────────────

    [Fact]
    public async Task DatePicker_ValidDate_Succeeds()
    {
        var field = new DatePicker("Start", "Pick start date", mandatory: true);
        var obj = new JObject { ["start"] = "20260315" };

        var result = await field.SanityCheckAsync(1, obj, "start", "fields.start", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task DatePicker_InvalidDate_Fails()
    {
        var field = new DatePicker("Start", "Pick start date", mandatory: true);
        var obj = new JObject { ["start"] = "not-a-date" };

        var result = await field.SanityCheckAsync(1, obj, "start", "fields.start", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("format");
    }

    [Fact]
    public async Task DatePicker_Mandatory_MissingField_Fails()
    {
        var field = new DatePicker("Start", "Pick start date", mandatory: true);
        var obj = new JObject();

        var result = await field.SanityCheckAsync(1, obj, "start", "fields.start", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("mandatory");
    }

    [Fact]
    public async Task DatePicker_Optional_MissingField_Succeeds()
    {
        var field = new DatePicker("Start", "Pick start date", mandatory: false);
        var obj = new JObject();

        var result = await field.SanityCheckAsync(1, obj, "start", "fields.start", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task DatePicker_CustomFormat_ValidDate_Succeeds()
    {
        var field = new DatePicker("Start", "Pick start date", mandatory: true, dateFormat: "yyyy-MM-dd");
        var obj = new JObject { ["start"] = "2026-03-15" };

        var result = await field.SanityCheckAsync(1, obj, "start", "fields.start", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task DatePicker_CustomFormat_WrongFormat_Fails()
    {
        var field = new DatePicker("Start", "Pick start date", mandatory: true, dateFormat: "yyyy-MM-dd");
        var obj = new JObject { ["start"] = "20260315" };

        var result = await field.SanityCheckAsync(1, obj, "start", "fields.start", null!, default);

        result.IsSuccessful.Should().BeFalse();
    }

    // ── Email field ─────────────────────────────────────────────────────

    [Fact]
    public async Task Email_Mandatory_MissingField_Fails()
    {
        var field = new Email("Email", "Your email", mandatory: true, placeholderText: "email@example.com");
        var obj = new JObject();

        var result = await field.SanityCheckAsync(1, obj, "email", "fields.email", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("mandatory");
    }

    [Fact]
    public async Task Email_Optional_MissingField_Succeeds()
    {
        var field = new Email("Email", "Your email", mandatory: false, placeholderText: "email@example.com");
        var obj = new JObject();

        var result = await field.SanityCheckAsync(1, obj, "email", "fields.email", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Email_WrongType_Fails()
    {
        var field = new Email("Email", "Your email", mandatory: true, placeholderText: "email@example.com");
        var obj = new JObject { ["email"] = 42 };

        var result = await field.SanityCheckAsync(1, obj, "email", "fields.email", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Type is incorrect");
    }
}

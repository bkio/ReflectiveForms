using FluentAssertions;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class FieldAttributeConstructorTests
{
    // ── Text ────────────────────────────────────────────────────────────

    [Fact]
    public void Text_SetsFieldType()
    {
        var field = new Text("Label", "Instructions", mandatory: true, placeholderText: "hint");
        field.Type.Should().Be(FieldType.Text);
        field.Label.Should().Be("Label");
        field.Instructions.Should().Be("Instructions");
    }

    [Fact]
    public void Text_WithDefaultValue_SetsFieldType()
    {
        var field = new Text("Label", "Instructions", mandatory: false, defaultValue: "Hello", placeholderText: "hint");
        field.Type.Should().Be(FieldType.Text);
    }

    // ── TextArea ────────────────────────────────────────────────────────

    [Fact]
    public void TextArea_SetsFieldType()
    {
        var field = new TextArea("Body", "Enter body text", mandatory: false, placeholderText: "Type here");
        field.Type.Should().Be(FieldType.TextArea);
        field.Label.Should().Be("Body");
    }

    // ── Email ───────────────────────────────────────────────────────────

    [Fact]
    public void Email_SetsFieldType()
    {
        var field = new Email("Email", "Enter email", mandatory: true, placeholderText: "you@example.com");
        field.Type.Should().Be(FieldType.Email);
    }

    // ── Checkbox ────────────────────────────────────────────────────────

    [Fact]
    public void Checkbox_SetsFieldType()
    {
        var field = new Checkbox("Active", "Is active", defaultValue: true);
        field.Type.Should().Be(FieldType.Checkbox);
        field.Label.Should().Be("Active");
    }

    // ── Select ──────────────────────────────────────────────────────────

    [Fact]
    public void Select_SetsFieldTypeAndChoices()
    {
        var field = new Select("Status", "Choose status", "draft", ["draft : Draft", "published : Published"]);
        field.Type.Should().Be(FieldType.Select);
        field.Choices.Should().HaveCount(2);
    }

    [Fact]
    public void Select_NullChoices_IsAllowed()
    {
        var field = new Select("Dynamic", "Dynamic choices", "", null);
        field.Type.Should().Be(FieldType.Select);
        field.Choices.Should().BeNull();
    }

    // ── Number ──────────────────────────────────────────────────────────

    [Fact]
    public void Number_BasicConstructor_SetsFieldType()
    {
        var field = new Number("Count", "Enter count", mandatory: true, placeholderText: "0");
        field.Type.Should().Be(FieldType.Number);
    }

    [Fact]
    public void Number_WithMinMax_SetsFieldType()
    {
        var field = new Number("Rating", "Rate", mandatory: true, placeholderText: "1", [1.0, 10.0]);
        field.Type.Should().Be(FieldType.Number);
    }

    [Fact]
    public void Number_WithDefaultAndMinMax_SetsFieldType()
    {
        var field = new Number("Rating", "Rate", mandatory: true, placeholderText: "5", defaultValue: 5.0, [1.0, 10.0]);
        field.Type.Should().Be(FieldType.Number);
    }

    [Fact]
    public void Number_WithMinMaxAndStep_SetsFieldType()
    {
        var field = new Number("Qty", "Quantity", mandatory: false, placeholderText: "0", [0.0, 100.0], 1.0);
        field.Type.Should().Be(FieldType.Number);
    }

    // ── DatePicker ──────────────────────────────────────────────────────

    [Fact]
    public void DatePicker_SetsFieldType()
    {
        var field = new DatePicker("Start Date", "Pick start", mandatory: true);
        field.Type.Should().Be(FieldType.DatePicker);
    }

    [Fact]
    public void DatePicker_CustomFormat_SetsFieldType()
    {
        var field = new DatePicker("Date", "Pick", mandatory: false, dateFormat: "yyyy-MM-dd");
        field.Type.Should().Be(FieldType.DatePicker);
    }

    // ── Group ───────────────────────────────────────────────────────────

    [Fact]
    public void Group_SetsFieldType()
    {
        var field = new Group("Address", "Home address", typeof(Models.EntityFieldsModel));
        field.Type.Should().Be(FieldType.Group);
        field.Label.Should().Be("Address");
    }

    [Fact]
    public void Group_WithRenderStyle_SetsFieldType()
    {
        var field = new Group("Address", "Home address", typeof(Models.EntityFieldsModel), GroupRenderStyle.Grid2ElementsInRow);
        field.Type.Should().Be(FieldType.Group);
    }

    // ── Repeater ────────────────────────────────────────────────────────

    [Fact]
    public void Repeater_SetsFieldType()
    {
        var field = new Repeater("Items", "Add items", typeof(Models.EntityFieldsModel), "Add Item");
        field.Type.Should().Be(FieldType.Repeater);
        field.Label.Should().Be("Items");
    }

    [Fact]
    public void Repeater_WithMinMax_SetsFieldType()
    {
        var field = new Repeater("Items", "Add items", typeof(Models.EntityFieldsModel), "Add Item",
            minimumRows: 1, maximumRows: 5);
        field.Type.Should().Be(FieldType.Repeater);
        field.MinimumRows.Should().Be(1);
        field.MaximumRows.Should().Be(5);
    }

    [Fact]
    public void Repeater_WithAccordion_SetsFieldType()
    {
        var field = new Repeater("Items", "Add items", typeof(Models.EntityFieldsModel), "Add Item",
            groupRenderStyle: GroupRenderStyle.Grid3ElementsInRow,
            useAccordion: RepeatUseAccordion.Yes);
        field.Type.Should().Be(FieldType.Repeater);
    }

    // ── WysiwygEditor ───────────────────────────────────────────────────

    [Fact]
    public void WysiwygEditor_SetsFieldType()
    {
        var field = new WysiwygEditor("Content", "Rich text content", mandatory: false);
        field.Type.Should().Be(FieldType.WysiwygEditor);
        field.Label.Should().Be("Content");
    }

    // ── DisplayCondition ────────────────────────────────────────────────

    [Fact]
    public void DisplayCondition_StoresConditionString()
    {
        var condition = new ReflectiveForms.Core.Attributes.DisplayCondition("status == 'active'");
        condition.Condition.Should().Be("status == 'active'");
    }
}

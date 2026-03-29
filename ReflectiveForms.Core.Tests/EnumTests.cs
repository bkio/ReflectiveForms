using FluentAssertions;
using ReflectiveForms.Core.Enums;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class EnumTests
{
    [Fact]
    public void FieldType_Contains14Values()
    {
        Enum.GetValues<FieldType>().Should().HaveCount(14);
    }

    [Theory]
    [InlineData(FieldType.Text)]
    [InlineData(FieldType.TextArea)]
    [InlineData(FieldType.WysiwygEditor)]
    [InlineData(FieldType.Number)]
    [InlineData(FieldType.Range)]
    [InlineData(FieldType.Email)]
    [InlineData(FieldType.Url)]
    [InlineData(FieldType.Select)]
    [InlineData(FieldType.Checkbox)]
    [InlineData(FieldType.Relation)]
    [InlineData(FieldType.DatePicker)]
    [InlineData(FieldType.Group)]
    [InlineData(FieldType.Repeater)]
    [InlineData(FieldType.MediaSourceBase64)]
    public void FieldType_AllExpectedMembersExist(FieldType type)
    {
        Enum.IsDefined(type).Should().BeTrue();
    }

    [Theory]
    [InlineData(GroupRenderStyle.Full)]
    [InlineData(GroupRenderStyle.Grid2ElementsInRow)]
    [InlineData(GroupRenderStyle.Grid3ElementsInRow)]
    [InlineData(GroupRenderStyle.Grid4ElementsInRow)]
    [InlineData(GroupRenderStyle.Grid6ElementsInRow)]
    public void GroupRenderStyle_AllMembersExist(GroupRenderStyle style)
    {
        Enum.IsDefined(style).Should().BeTrue();
    }

    [Fact]
    public void RepeatUseAccordion_HasNoAndYes()
    {
        var values = Enum.GetValues<RepeatUseAccordion>();
        values.Should().HaveCount(2);
        values.Should().Contain(RepeatUseAccordion.No);
        values.Should().Contain(RepeatUseAccordion.Yes);
    }

    [Fact]
    public void SupportsFrontendEdit_HasThreeValues()
    {
        var values = Enum.GetValues<SupportsFrontendEdit>();
        values.Should().HaveCount(3);
        values.Should().Contain(SupportsFrontendEdit.No);
        values.Should().Contain(SupportsFrontendEdit.ForAllAuthorized);
        values.Should().Contain(SupportsFrontendEdit.ForSuperAdminOnly);
    }
}

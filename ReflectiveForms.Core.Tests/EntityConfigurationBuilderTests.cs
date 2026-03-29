using FluentAssertions;
using ReflectiveForms.Core.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class EntityConfigurationBuilderTests
{
    [Fact]
    public void EntityConfigurationBuilder_SetsAllProperties()
    {
        var builder = new EntityConfigurationBuilder<EntityFieldsModel>
        {
            EntityName = "test-entity",
            EntityReadableNameSingular = "Test Entity",
            EntityReadableNamePlural = "Test Entities",
            ShallSupportFrontendEdit = SupportsFrontendEdit.ForAllAuthorized,
            HasParentChildRelationship = true,
            HasAuthor = true,
            HasTags = true,
            HasCategories = false,
            RequireGlobalTitleUniqueness = true,
            OptionalTitleSanityCheck = null
        };

        builder.EntityName.Should().Be("test-entity");
        builder.EntityReadableNameSingular.Should().Be("Test Entity");
        builder.EntityReadableNamePlural.Should().Be("Test Entities");
        builder.ShallSupportFrontendEdit.Should().Be(SupportsFrontendEdit.ForAllAuthorized);
        builder.HasParentChildRelationship.Should().BeTrue();
        builder.HasAuthor.Should().BeTrue();
        builder.HasTags.Should().BeTrue();
        builder.HasCategories.Should().BeFalse();
        builder.RequireGlobalTitleUniqueness.Should().BeTrue();
        builder.OptionalTitleSanityCheck.Should().BeNull();
    }

    [Fact]
    public void EntityConfigurationBuilder_WithHooksSetup()
    {
        var hookCalled = false;
        var builder = new EntityConfigurationBuilder<EntityFieldsModel>
        {
            EntityName = "hooked-entity",
            EntityReadableNameSingular = "Hook",
            EntityReadableNamePlural = "Hooks",
            ShallSupportFrontendEdit = SupportsFrontendEdit.No,
            HasParentChildRelationship = false,
            HasAuthor = false,
            HasTags = false,
            HasCategories = false,
            RequireGlobalTitleUniqueness = false,
            OptionalTitleSanityCheck = null,
            HooksSetup = new EntityOnChangedHooksSetup<EntityFieldsModel>
            {
                PostCreateHook = (_, _) => { hookCalled = true; return Task.CompletedTask; }
            }
        };

        builder.HooksSetup.Should().NotBeNull();
        builder.HooksSetup!.PostCreateHook.Should().NotBeNull();
        builder.HooksSetup.PostUpdateHook.Should().BeNull();
        builder.HooksSetup.PostDeleteHook.Should().BeNull();
    }

    [Fact]
    public void EntityConfigurationBuilder_OptionalTitleSanityCheck_CanBeProvided()
    {
        var builder = new EntityConfigurationBuilder<EntityFieldsModel>
        {
            EntityName = "checked-entity",
            EntityReadableNameSingular = "Checked",
            EntityReadableNamePlural = "Checked",
            ShallSupportFrontendEdit = SupportsFrontendEdit.No,
            HasParentChildRelationship = false,
            HasAuthor = false,
            HasTags = false,
            HasCategories = false,
            RequireGlobalTitleUniqueness = false,
            OptionalTitleSanityCheck = title => Task.FromResult(title.Text != "Forbidden")
        };

        builder.OptionalTitleSanityCheck.Should().NotBeNull();
    }
}

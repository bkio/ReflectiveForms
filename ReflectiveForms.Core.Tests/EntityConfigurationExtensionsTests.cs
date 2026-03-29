using FluentAssertions;
using ReflectiveForms.Core.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class EntityConfigurationExtensionsTests
{
    [Fact]
    public void ToEntityModelType_NoParentNoAuthorNoTagsNoCategories()
    {
        var config = CreateConfig(
            hasParent: false, hasAuthor: false, hasTags: false, hasCategories: false);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithoutParentWithoutAuthorWithoutTagsWithoutCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_WithParentNoAuthorNoTagsNoCategories()
    {
        var config = CreateConfig(
            hasParent: true, hasAuthor: false, hasTags: false, hasCategories: false);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithParentWithoutAuthorWithoutTagsWithoutCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_NoParentWithAuthorNoTagsNoCategories()
    {
        var config = CreateConfig(
            hasParent: false, hasAuthor: true, hasTags: false, hasCategories: false);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithoutParentWithAuthorWithoutTagsWithoutCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_NoParentNoAuthorWithTagsNoCategories()
    {
        var config = CreateConfig(
            hasParent: false, hasAuthor: false, hasTags: true, hasCategories: false);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithoutParentWithoutAuthorWithTagsWithoutCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_NoParentNoAuthorNoTagsWithCategories()
    {
        var config = CreateConfig(
            hasParent: false, hasAuthor: false, hasTags: false, hasCategories: true);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithoutParentWithoutAuthorWithoutTagsWithCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_AllTrue()
    {
        var config = CreateConfig(
            hasParent: true, hasAuthor: true, hasTags: true, hasCategories: true);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithParentWithAuthorWithTagsWithCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_WithParentWithAuthorNoTagsNoCategories()
    {
        var config = CreateConfig(
            hasParent: true, hasAuthor: true, hasTags: false, hasCategories: false);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithParentWithAuthorWithoutTagsWithoutCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_WithParentNoAuthorWithTagsNoCategories()
    {
        var config = CreateConfig(
            hasParent: true, hasAuthor: false, hasTags: true, hasCategories: false);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithParentWithoutAuthorWithTagsWithoutCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_WithParentNoAuthorNoTagsWithCategories()
    {
        var config = CreateConfig(
            hasParent: true, hasAuthor: false, hasTags: false, hasCategories: true);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithParentWithoutAuthorWithoutTagsWithCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_NoParentWithAuthorWithTagsNoCategories()
    {
        var config = CreateConfig(
            hasParent: false, hasAuthor: true, hasTags: true, hasCategories: false);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithoutParentWithAuthorWithTagsWithoutCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_NoParentWithAuthorNoTagsWithCategories()
    {
        var config = CreateConfig(
            hasParent: false, hasAuthor: true, hasTags: false, hasCategories: true);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithoutParentWithAuthorWithoutTagsWithCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_NoParentNoAuthorWithTagsWithCategories()
    {
        var config = CreateConfig(
            hasParent: false, hasAuthor: false, hasTags: true, hasCategories: true);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithoutParentWithoutAuthorWithTagsWithCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_WithParentWithAuthorWithTagsNoCategories()
    {
        var config = CreateConfig(
            hasParent: true, hasAuthor: true, hasTags: true, hasCategories: false);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithParentWithAuthorWithTagsWithoutCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_WithParentWithAuthorNoTagsWithCategories()
    {
        var config = CreateConfig(
            hasParent: true, hasAuthor: true, hasTags: false, hasCategories: true);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithParentWithAuthorWithoutTagsWithCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_WithParentNoAuthorWithTagsWithCategories()
    {
        var config = CreateConfig(
            hasParent: true, hasAuthor: false, hasTags: true, hasCategories: true);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithParentWithoutAuthorWithTagsWithCategories<EntityFieldsModel>));
    }

    [Fact]
    public void ToEntityModelType_NoParentWithAuthorWithTagsWithCategories()
    {
        var config = CreateConfig(
            hasParent: false, hasAuthor: true, hasTags: true, hasCategories: true);

        var type = config.ToEntityModelType();

        type.Should().Be(typeof(WithoutParentWithAuthorWithTagsWithCategories<EntityFieldsModel>));
    }

    private static EntityConfigurationBuilder<EntityFieldsModel> CreateConfig(
        bool hasParent, bool hasAuthor, bool hasTags, bool hasCategories)
    {
        return new EntityConfigurationBuilder<EntityFieldsModel>
        {
            EntityName = "test",
            EntityReadableNameSingular = "Test",
            EntityReadableNamePlural = "Tests",
            ShallSupportFrontendEdit = SupportsFrontendEdit.No,
            HasParentChildRelationship = hasParent,
            HasAuthor = hasAuthor,
            HasTags = hasTags,
            HasCategories = hasCategories,
            RequireGlobalTitleUniqueness = false,
            OptionalTitleSanityCheck = null
        };
    }
}

using FluentAssertions;
using ReflectiveForms.Core.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class EntityModelTests
{
    [Fact]
    public void EntityModelAttributes_HasExpectedConstants()
    {
        EntityModelAttributes.Id.Should().Be("id");
        EntityModelAttributes.Slug.Should().Be("slug");
        EntityModelAttributes.Link.Should().Be("link");
        EntityModelAttributes.Fields.Should().Be("fields");
        EntityModelAttributes.Parent.Should().Be("parent");
        EntityModelAttributes.Author.Should().Be("author");
        EntityModelAttributes.Title.Should().Be("title");
        EntityModelAttributes.TitleRendered.Should().Be("rendered");
        EntityModelAttributes.Date.Should().Be("date");
        EntityModelAttributes.DateGmt.Should().Be("date_gmt");
        EntityModelAttributes.Modified.Should().Be("modified");
        EntityModelAttributes.ModifiedGmt.Should().Be("modified_gmt");
        EntityModelAttributes.Tags.Should().Be("tags");
        EntityModelAttributes.Categories.Should().Be("categories");
    }

    [Fact]
    public void BaseModel_UniqueFieldId_DefaultsToEmpty()
    {
        var model = new EntityFieldsModel();
        model.UniqueFieldId.Should().BeEmpty();
    }

    [Fact]
    public void BaseModel_ShouldSerializeUniqueFieldId_FalseWhenEmpty()
    {
        var model = new EntityFieldsModel();
        model.ShouldSerializeUniqueFieldId().Should().BeFalse();
    }

    [Fact]
    public void BaseModel_ShouldSerializeUniqueFieldId_TrueWhenNotEmpty()
    {
        var model = new EntityFieldsModel();
        model.UniqueFieldId = "abc-123";
        model.ShouldSerializeUniqueFieldId().Should().BeTrue();
    }

    [Fact]
    public void BaseModel_ShouldSerializeUniqueFieldId_TrueWhenForcedByFlag()
    {
        var model = new EntityFieldsModel();
        model.MustSerializeUniqueFieldId = true;
        model.ShouldSerializeUniqueFieldId().Should().BeTrue();
    }

    [Fact]
    public void RfReservedEntities_ReservedNames_ContainsExpected()
    {
        RfReservedEntities.ReservedEntityNames.Should().Contain("users");
        RfReservedEntities.ReservedEntityNames.Should().Contain("iam-role");
        RfReservedEntities.ReservedEntityNames.Should().Contain("tags");
        RfReservedEntities.ReservedEntityNames.Should().Contain("categories");
        RfReservedEntities.ReservedEntityNames.Should().Contain("media");
        RfReservedEntities.ReservedEntityNames.Should().Contain("rf-sheets");
    }

    [Fact]
    public void RfReservedEntities_ReservedNames_HasExactly6()
    {
        RfReservedEntities.ReservedEntityNames.Should().HaveCount(6);
    }

    [Fact]
    public void RfReservedEntities_CaseInsensitiveContains()
    {
        RfReservedEntities.ReservedEntityNames.Contains("USERS").Should().BeTrue();
        RfReservedEntities.ReservedEntityNames.Contains("Tags").Should().BeTrue();
        RfReservedEntities.ReservedEntityNames.Contains("IAM-ROLE").Should().BeTrue();
    }

    [Fact]
    public void RfReservedEntities_Constants_MatchSet()
    {
        RfReservedEntities.UsersEntityName.Should().Be("users");
        RfReservedEntities.IamRoleEntityName.Should().Be("iam-role");
        RfReservedEntities.TagsEntityName.Should().Be("tags");
        RfReservedEntities.CategoriesEntityName.Should().Be("categories");
        RfReservedEntities.MediaEntityName.Should().Be("media");
        RfReservedEntities.SheetsEntityName.Should().Be("rf-sheets");
    }

    [Fact]
    public void RfReservedEntities_ReservedEntityTypes_Has6Types()
    {
        RfReservedEntities.ReservedEntityTypes.Should().HaveCount(6);
    }

    [Fact]
    public void RfReservedEntities_ReservedEntityTypes_HaveConfigurations()
    {
        foreach (var entityType in RfReservedEntities.ReservedEntityTypes)
        {
            entityType.EntityConfiguration.Should().NotBeNull();
            entityType.EntityConfiguration.EntityName.Should().NotBeNullOrWhiteSpace();
            entityType.EntityConfiguration.EntityReadableNameSingular.Should().NotBeNullOrWhiteSpace();
            entityType.EntityConfiguration.EntityReadableNamePlural.Should().NotBeNullOrWhiteSpace();
        }
    }
}

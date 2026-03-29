using FluentAssertions;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Schema.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class SchemaModelTests
{
    [Fact]
    public void EntitySchema_RequiredProperties_CanBeSet()
    {
        var schema = new EntitySchema
        {
            EntityName = "post",
            ReadableName = new ReadableName { Singular = "Post", Plural = "Posts" },
            Features = new EntityFeatures
            {
                HasAuthor = true,
                HasTags = true,
                HasCategories = false,
                HasParentChild = false,
                RequireTitleUniqueness = true,
                SupportsFrontendEdit = SupportsFrontendEdit.ForAllAuthorized
            },
            Fields = [],
            ApiEndpoints = new ApiEndpoints
            {
                Crud = "/rf/api/crud",
                SanityCheck = "/rf/api/sanity_check",
                EntityLock = "/rf/api/entity_lock_control",
                Media = "/rf/api/media"
            }
        };

        schema.EntityName.Should().Be("post");
        schema.ReadableName.Singular.Should().Be("Post");
        schema.ReadableName.Plural.Should().Be("Posts");
        schema.Features.HasAuthor.Should().BeTrue();
        schema.Features.HasTags.Should().BeTrue();
        schema.Features.HasCategories.Should().BeFalse();
        schema.Features.RequireTitleUniqueness.Should().BeTrue();
        schema.Fields.Should().BeEmpty();
        schema.SchemaVersion.Should().Be("1.0");
    }

    [Fact]
    public void FieldSchema_TextType_SetsOptionsCorrectly()
    {
        var field = new FieldSchema
        {
            Name = "title",
            Type = FieldSchemaType.Text,
            Label = "Title",
            Required = true,
            TextOptions = new TextFieldOptions
            {
                Placeholder = "Enter title",
                IsMultiline = false,
                MaxLength = 256
            }
        };

        field.Name.Should().Be("title");
        field.Type.Should().Be(FieldSchemaType.Text);
        field.Required.Should().BeTrue();
        field.TextOptions.Should().NotBeNull();
        field.TextOptions!.Placeholder.Should().Be("Enter title");
        field.TextOptions.IsMultiline.Should().BeFalse();
        field.TextOptions.MaxLength.Should().Be(256);
        // Other options should be null
        field.SelectOptions.Should().BeNull();
        field.NumberOptions.Should().BeNull();
    }

    [Fact]
    public void FieldSchema_SelectType_ParsesChoices()
    {
        var field = new FieldSchema
        {
            Name = "status",
            Type = FieldSchemaType.Select,
            Label = "Status",
            SelectOptions = new SelectFieldOptions
            {
                Choices =
                [
                    new SelectChoice { Value = "draft", Label = "Draft" },
                    new SelectChoice { Value = "published", Label = "Published" }
                ]
            }
        };

        field.SelectOptions.Should().NotBeNull();
        field.SelectOptions!.Choices.Should().HaveCount(2);
        field.SelectOptions.Choices![0].Value.Should().Be("draft");
        field.SelectOptions.Choices[0].Label.Should().Be("Draft");
        field.SelectOptions.Choices[1].Value.Should().Be("published");
    }

    [Fact]
    public void FieldSchema_NumberType_HasMinMaxStep()
    {
        var field = new FieldSchema
        {
            Name = "rating",
            Type = FieldSchemaType.Number,
            Label = "Rating",
            NumberOptions = new NumberFieldOptions
            {
                Min = 1,
                Max = 10,
                Step = 0.5,
                IsRange = false
            }
        };

        field.NumberOptions.Should().NotBeNull();
        field.NumberOptions!.Min.Should().Be(1);
        field.NumberOptions.Max.Should().Be(10);
        field.NumberOptions.Step.Should().Be(0.5);
        field.NumberOptions.IsRange.Should().BeFalse();
    }

    [Fact]
    public void FieldSchema_DateType_HasFormat()
    {
        var field = new FieldSchema
        {
            Name = "start_date",
            Type = FieldSchemaType.DatePicker,
            Label = "Start Date",
            DateOptions = new DateFieldOptions { Format = "yyyy-MM-dd" }
        };

        field.DateOptions.Should().NotBeNull();
        field.DateOptions!.Format.Should().Be("yyyy-MM-dd");
    }

    [Fact]
    public void FieldSchema_RepeaterType_HasItemSchema()
    {
        var childField = new FieldSchema
        {
            Name = "item_name",
            Type = FieldSchemaType.Text,
            Label = "Item Name"
        };

        var field = new FieldSchema
        {
            Name = "items",
            Type = FieldSchemaType.Repeater,
            Label = "Items",
            RepeaterOptions = new RepeaterFieldOptions
            {
                ItemSchema = [childField],
                MinItems = 1,
                MaxItems = 10,
                AddButtonLabel = "Add Item",
                UseAccordion = true,
                RenderStyle = GroupRenderStyleSchema.Full
            }
        };

        field.RepeaterOptions.Should().NotBeNull();
        field.RepeaterOptions!.ItemSchema.Should().HaveCount(1);
        field.RepeaterOptions.ItemSchema[0].Name.Should().Be("item_name");
        field.RepeaterOptions.MinItems.Should().Be(1);
        field.RepeaterOptions.MaxItems.Should().Be(10);
        field.RepeaterOptions.AddButtonLabel.Should().Be("Add Item");
        field.RepeaterOptions.UseAccordion.Should().BeTrue();
    }

    [Fact]
    public void FieldSchema_GroupType_HasChildSchema()
    {
        var childField = new FieldSchema
        {
            Name = "street",
            Type = FieldSchemaType.Text,
            Label = "Street"
        };

        var field = new FieldSchema
        {
            Name = "address",
            Type = FieldSchemaType.Group,
            Label = "Address",
            GroupOptions = new GroupFieldOptions
            {
                ChildSchema = [childField],
                RenderStyle = GroupRenderStyleSchema.Grid2
            }
        };

        field.GroupOptions.Should().NotBeNull();
        field.GroupOptions!.ChildSchema.Should().HaveCount(1);
        field.GroupOptions.RenderStyle.Should().Be(GroupRenderStyleSchema.Grid2);
    }

    [Fact]
    public void FieldSchema_MediaType_HasOptions()
    {
        var field = new FieldSchema
        {
            Name = "photo",
            Type = FieldSchemaType.MediaSourceBase64,
            Label = "Photo",
            MediaOptions = new MediaFieldOptions
            {
                MaxFileSizeMb = 8,
                AcceptedTypes = ["image/*"],
                PreviewEnabled = true
            }
        };

        field.MediaOptions.Should().NotBeNull();
        field.MediaOptions!.MaxFileSizeMb.Should().Be(8);
        field.MediaOptions.AcceptedTypes.Should().Contain("image/*");
        field.MediaOptions.PreviewEnabled.Should().BeTrue();
    }

    [Fact]
    public void FieldSchema_RelationOptions_HasEntityName()
    {
        var field = new FieldSchema
        {
            Name = "author",
            Type = FieldSchemaType.Relation,
            Label = "Author",
            RelationOptions = new RelationFieldOptions
            {
                RelationEntityName = "users",
                IsRelationEntityNotExistsOk = false
            }
        };

        field.RelationOptions.Should().NotBeNull();
        field.RelationOptions!.RelationEntityName.Should().Be("users");
        field.RelationOptions.IsRelationEntityNotExistsOk.Should().BeFalse();
    }

    [Fact]
    public void FieldSchemaType_HasAll14Types()
    {
        var values = Enum.GetValues<FieldSchemaType>();
        values.Should().HaveCount(14);
    }

    [Fact]
    public void GroupRenderStyleSchema_HasAllStyles()
    {
        var values = Enum.GetValues<GroupRenderStyleSchema>();
        values.Should().HaveCount(5);
        values.Should().Contain(GroupRenderStyleSchema.Full);
        values.Should().Contain(GroupRenderStyleSchema.Grid2);
        values.Should().Contain(GroupRenderStyleSchema.Grid3);
        values.Should().Contain(GroupRenderStyleSchema.Grid4);
        values.Should().Contain(GroupRenderStyleSchema.Grid6);
    }

    [Fact]
    public void FieldSchema_DisplayCondition_CanBeSet()
    {
        var field = new FieldSchema
        {
            Name = "details",
            Type = FieldSchemaType.TextArea,
            Label = "Details",
            DisplayCondition = "status == 'active'"
        };

        field.DisplayCondition.Should().Be("status == 'active'");
    }

    [Fact]
    public void FieldSchema_DynamicChoicesFlags_DefaultToFalse()
    {
        var field = new FieldSchema
        {
            Name = "test",
            Type = FieldSchemaType.Text,
            Label = "Test"
        };

        field.HasDynamicChoicesRuntime.Should().BeFalse();
        field.HasDynamicChoicesCompileTime.Should().BeFalse();
        field.HasLogicSanityCheck.Should().BeFalse();
    }
}

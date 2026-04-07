using FluentAssertions;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class GroupFieldSanityCheckTests
{
    [Fact]
    public async Task Group_MissingField_Succeeds()
    {
        var field = new Group("Address", "Enter address", typeof(Models.EntityFieldsModel));
        var obj = new JObject();

        var result = await field.SanityCheckAsync(1, obj, "address", "fields.address", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task Group_NullField_ReplacedWithEmptyObject()
    {
        var field = new Group("Address", "Enter address", typeof(Models.EntityFieldsModel));
        var obj = new JObject { ["address"] = JValue.CreateNull() };

        var result = await field.SanityCheckAsync(1, obj, "address", "fields.address", null!, default);

        result.IsSuccessful.Should().BeTrue();
        obj["address"]!.Type.Should().Be(JTokenType.Object);
    }

    [Fact]
    public async Task Group_NonObjectType_Fails()
    {
        var field = new Group("Address", "Enter address", typeof(Models.EntityFieldsModel));
        var obj = new JObject { ["address"] = "not an object" };

        var result = await field.SanityCheckAsync(1, obj, "address", "fields.address", null!, default);

        result.IsSuccessful.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Type is incorrect");
    }

    [Fact]
    public async Task Group_ValidObject_Succeeds()
    {
        var field = new Group("Address", "Enter address", typeof(Models.EntityFieldsModel));
        var obj = new JObject { ["address"] = new JObject() };

        var result = await field.SanityCheckAsync(1, obj, "address", "fields.address", null!, default);

        result.IsSuccessful.Should().BeTrue();
    }
}

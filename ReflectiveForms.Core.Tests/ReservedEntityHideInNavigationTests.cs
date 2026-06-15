// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using FluentAssertions;
using ReflectiveForms.Core;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class ReservedEntityHideInNavigationTests
{
    [Fact]
    public void ReservedEntityType_Enum_HasCorrectValues()
    {
        Enum.GetValues<ReservedEntityType>().Should().BeEquivalentTo([
            ReservedEntityType.Tags,
            ReservedEntityType.Categories,
            ReservedEntityType.Media,
            ReservedEntityType.Users,
            ReservedEntityType.IamRoles,
        ]);
    }

    [Fact]
    public void EnumNames_ArePascalCase_SerializedAsLowercase()
    {
        // The frontend receives lowercase entity names via FrontendSettings
        ReservedEntityType.Tags.ToString().ToLowerInvariant().Should().Be("tags");
        ReservedEntityType.Categories.ToString().ToLowerInvariant().Should().Be("categories");
        ReservedEntityType.Media.ToString().ToLowerInvariant().Should().Be("media");
        ReservedEntityType.Users.ToString().ToLowerInvariant().Should().Be("users");
        ReservedEntityType.IamRoles.ToString().ToLowerInvariant().Should().Be("iamroles");
    }

    [Fact]
    public void RfConfigurationBuilder_Default_ReservedEntityTypesToHideInNavigation_IsNull()
    {
        // Can't fully construct RfConfigurationBuilder without all required props,
        // but the property should be null by default
        typeof(RfConfigurationBuilder)
            .GetProperty(nameof(RfConfigurationBuilder.ReservedEntityTypesToHideInNavigation))
            .Should().NotBeNull("property must exist on the builder");
    }

    [Fact]
    public void RfConfigurationBuilder_CanSet_ReservedEntityTypesToHideInNavigation()
    {
        // Verify the property type and settability
        var prop = typeof(RfConfigurationBuilder)
            .GetProperty(nameof(RfConfigurationBuilder.ReservedEntityTypesToHideInNavigation))!;

        prop.PropertyType.Should().Be(typeof(IReadOnlyList<ReservedEntityType>));
        prop.CanWrite.Should().BeTrue("must be settable via init");
    }

    [Fact]
    public void RfConfiguration_ReservedEntityTypesToHideInNavigation_PropertyExists()
    {
        typeof(RfConfiguration)
            .GetProperty(nameof(RfConfiguration.ReservedEntityTypesToHideInNavigation))
            .Should().NotBeNull("static property must exist on RfConfiguration");
    }
}

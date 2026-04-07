// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using FluentAssertions;
using ReflectiveForms.Core.Endpoints;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class SsoConfigurationTests
{
    [Fact]
    public void SsoConfiguration_RequiredProperties_CanBeSet()
    {
        var config = new SsoConfiguration
        {
            Provider = SsoProvider.OpenIdConnect,
            Authority = "https://auth.example.com",
            ClientId = "my-client-id",
            ClientSecret = "my-client-secret"
        };

        config.Provider.Should().Be(SsoProvider.OpenIdConnect);
        config.Authority.Should().Be("https://auth.example.com");
        config.ClientId.Should().Be("my-client-id");
        config.ClientSecret.Should().Be("my-client-secret");
    }

    [Fact]
    public void SsoConfiguration_DefaultCallbackPath()
    {
        var config = new SsoConfiguration
        {
            Provider = SsoProvider.OpenIdConnect,
            Authority = "https://auth.example.com",
            ClientId = "id",
            ClientSecret = "secret"
        };

        config.CallbackPath.Should().Be("/auth/sso/callback");
    }

    [Fact]
    public void SsoConfiguration_CustomCallbackPath()
    {
        var config = new SsoConfiguration
        {
            Provider = SsoProvider.AzureAd,
            Authority = "https://login.microsoftonline.com/tenant/v2.0",
            ClientId = "id",
            ClientSecret = "secret",
            CallbackPath = "/custom/callback"
        };

        config.CallbackPath.Should().Be("/custom/callback");
    }

    [Fact]
    public void SsoConfiguration_DefaultClaimsMappings()
    {
        var config = new SsoConfiguration
        {
            Provider = SsoProvider.Google,
            Authority = "https://accounts.google.com",
            ClientId = "id",
            ClientSecret = "secret"
        };

        config.ClaimsMappings.Email.Should().Be("email");
        config.ClaimsMappings.Name.Should().Be("name");
    }

    [Fact]
    public void SsoConfiguration_CustomClaimsMappings()
    {
        var config = new SsoConfiguration
        {
            Provider = SsoProvider.AzureAd,
            Authority = "https://login.microsoftonline.com/tenant/v2.0",
            ClientId = "id",
            ClientSecret = "secret",
            ClaimsMappings = new ClaimsMappings
            {
                Email = "preferred_username",
                Name = "display_name"
            }
        };

        config.ClaimsMappings.Email.Should().Be("preferred_username");
        config.ClaimsMappings.Name.Should().Be("display_name");
    }

    [Fact]
    public void SsoConfiguration_DefaultAutoProvisionUsers_IsTrue()
    {
        var config = new SsoConfiguration
        {
            Provider = SsoProvider.OpenIdConnect,
            Authority = "https://auth.example.com",
            ClientId = "id",
            ClientSecret = "secret"
        };

        config.AutoProvisionUsers.Should().BeTrue();
    }

    [Fact]
    public void SsoConfiguration_DefaultRole_IsEditor()
    {
        var config = new SsoConfiguration
        {
            Provider = SsoProvider.OpenIdConnect,
            Authority = "https://auth.example.com",
            ClientId = "id",
            ClientSecret = "secret"
        };

        config.DefaultRole.Should().Be("editor");
    }

    [Fact]
    public void SsoConfiguration_DefaultAllowedDomains_IsEmpty()
    {
        var config = new SsoConfiguration
        {
            Provider = SsoProvider.OpenIdConnect,
            Authority = "https://auth.example.com",
            ClientId = "id",
            ClientSecret = "secret"
        };

        config.AllowedDomains.Should().BeEmpty();
    }

    [Fact]
    public void SsoConfiguration_AllowedDomains_CanBeSet()
    {
        var config = new SsoConfiguration
        {
            Provider = SsoProvider.AzureAd,
            Authority = "https://login.microsoftonline.com/tenant/v2.0",
            ClientId = "id",
            ClientSecret = "secret",
            AllowedDomains = ["school.edu", "university.org"]
        };

        config.AllowedDomains.Should().HaveCount(2);
        config.AllowedDomains.Should().Contain("school.edu");
        config.AllowedDomains.Should().Contain("university.org");
    }

    [Theory]
    [InlineData(SsoProvider.OpenIdConnect)]
    [InlineData(SsoProvider.AzureAd)]
    [InlineData(SsoProvider.Google)]
    public void SsoProvider_Enum_AllValuesValid(SsoProvider provider)
    {
        var config = new SsoConfiguration
        {
            Provider = provider,
            Authority = "https://auth.example.com",
            ClientId = "id",
            ClientSecret = "secret"
        };

        config.Provider.Should().Be(provider);
    }
}

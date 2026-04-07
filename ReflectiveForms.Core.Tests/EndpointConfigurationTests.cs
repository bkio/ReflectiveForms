using FluentAssertions;
using ReflectiveForms.Core.Endpoints;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class EndpointConfigurationTests
{
    [Fact]
    public void EndpointConfiguration_PublicProperties_CanBeSet()
    {
        var config = new EndpointConfiguration
        {
            RootPath = "/rf",
            PublicUrlRootForApi = "http://localhost:9000/rf/api/",
            PublicFrontendBaseUrl = "http://localhost:3000",
            JwtSecret = "test-secret-key-1234567890-test"
        };

        config.RootPath.Should().Be("/rf");
        config.PublicUrlRootForApi.Should().Be("http://localhost:9000/rf/api/");
        config.PublicFrontendBaseUrl.Should().Be("http://localhost:3000");
    }

    [Fact]
    public void EndpointConfiguration_RequiredProperties_MustBeProvided()
    {
        // All three required properties (RootPath, PublicUrlRootForApi, PublicFrontendBaseUrl, JwtSecret)
        // must be provided — no defaults for URL properties
        var config = new EndpointConfiguration
        {
            RootPath = "/rf",
            PublicUrlRootForApi = "https://api.school.edu/rf/api/",
            PublicFrontendBaseUrl = "https://school.edu",
            JwtSecret = "secret-key-for-testing-12345678"
        };

        config.PublicUrlRootForApi.Should().Be("https://api.school.edu/rf/api/");
        config.PublicFrontendBaseUrl.Should().Be("https://school.edu");
    }

    [Fact]
    public void EndpointConfiguration_CustomFrontendBaseUrl()
    {
        var config = new EndpointConfiguration
        {
            RootPath = "/app",
            PublicUrlRootForApi = "https://example.com/app/api/",
            PublicFrontendBaseUrl = "https://app.example.com",
            JwtSecret = "secret-key-for-testing-12345678"
        };

        config.PublicFrontendBaseUrl.Should().Be("https://app.example.com");
        config.RootPath.Should().Be("/app");
    }

    [Fact]
    public void EndpointConfiguration_SsoConfiguration_DefaultsToNull()
    {
        var config = new EndpointConfiguration
        {
            RootPath = "/rf",
            PublicUrlRootForApi = "http://localhost:9000/rf/api/",
            PublicFrontendBaseUrl = "http://localhost:3000",
            JwtSecret = "secret-key-for-testing-12345678"
        };

        config.SsoConfiguration.Should().BeNull();
    }

    [Fact]
    public void EndpointConfiguration_WithSsoConfiguration()
    {
        var config = new EndpointConfiguration
        {
            RootPath = "/rf",
            PublicUrlRootForApi = "https://api.school.edu/rf/api/",
            PublicFrontendBaseUrl = "https://school.edu",
            JwtSecret = "secret-key-for-testing-12345678",
            SsoConfiguration = new SsoConfiguration
            {
                Provider = SsoProvider.AzureAd,
                Authority = "https://login.microsoftonline.com/tenant-id/v2.0",
                ClientId = "client-id-123",
                ClientSecret = "client-secret-456"
            }
        };

        config.SsoConfiguration.Should().NotBeNull();
        config.SsoConfiguration!.Provider.Should().Be(SsoProvider.AzureAd);
        config.SsoConfiguration.Authority.Should().Be("https://login.microsoftonline.com/tenant-id/v2.0");
        config.SsoConfiguration.ClientId.Should().Be("client-id-123");
    }
}

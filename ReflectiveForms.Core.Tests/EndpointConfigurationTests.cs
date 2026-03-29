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
            JwtSecret = "test-secret-key-1234567890-test"
        };

        config.RootPath.Should().Be("/rf");
        config.PublicUrlRootForApi.Should().Be("http://localhost:9000/rf/api/");
    }

    [Fact]
    public void EndpointConfiguration_DefaultFrontendBaseUrl()
    {
        var config = new EndpointConfiguration
        {
            RootPath = "/rf",
            PublicUrlRootForApi = "http://localhost:9000/rf/api/",
            JwtSecret = "secret-key-for-testing-12345678"
        };

        config.PublicFrontendBaseUrl.Should().Be("http://localhost:5173");
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
}

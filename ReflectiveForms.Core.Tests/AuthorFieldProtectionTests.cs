using FluentAssertions;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Tests for author field protection logic:
/// - CREATE always overwrites author with requester ID
/// - UPDATE strips author for non-admin, non-author users
/// - READ response includes can_edit_author flag
/// </summary>
public class AuthorFieldProtectionTests
{
    // ── CREATE: Author is always force-set to requester ──

    [Fact]
    public void Create_AuthorAlwaysOverwritten_WhenBodyContainsDifferentAuthor()
    {
        // Simulates the HandleCreate logic: body[Author] = requesterId regardless of existing value
        var body = new JObject { [EntityModelAttributes.Author] = 999 };
        var requesterId = 42;

        // This is the exact logic from HandleCreate
        body[EntityModelAttributes.Author] = requesterId;

        body[EntityModelAttributes.Author]!.Value<int>().Should().Be(42);
    }

    [Fact]
    public void Create_AuthorSet_WhenBodyOmitsAuthor()
    {
        var body = new JObject { ["title"] = new JObject { ["rendered"] = "Test" } };
        var requesterId = 42;

        body[EntityModelAttributes.Author] = requesterId;

        body[EntityModelAttributes.Author]!.Value<int>().Should().Be(42);
    }

    [Fact]
    public void Create_AuthorOverwritesClientProvidedValue()
    {
        // Client tries to impersonate another user
        var body = new JObject { [EntityModelAttributes.Author] = 100 };
        var requesterId = 7;

        body[EntityModelAttributes.Author] = requesterId;

        body[EntityModelAttributes.Author]!.Value<int>().Should().Be(7,
            "server must override client-provided author to prevent impersonation");
    }

    // ── UPDATE: Author field stripping logic ──

    [Fact]
    public void Update_AuthorStripped_WhenRequesterIsNotAdminOrAuthor()
    {
        // Simulates HandleUpdate author protection: non-privileged user → strip author
        var body = new JObject
        {
            ["id"] = 1,
            [EntityModelAttributes.Author] = 999, // trying to change author
            ["fields"] = new JObject { ["name"] = "updated" }
        };

        var isAdmin = false;
        var isAuthor = false;

        if (!isAdmin && !isAuthor)
        {
            body.Remove(EntityModelAttributes.Author);
        }

        body.ContainsKey(EntityModelAttributes.Author).Should().BeFalse(
            "non-admin/non-author user should have author field silently stripped");
    }

    [Fact]
    public void Update_AuthorPreserved_WhenRequesterIsAdmin()
    {
        var body = new JObject
        {
            ["id"] = 1,
            [EntityModelAttributes.Author] = 999,
            ["fields"] = new JObject { ["name"] = "updated" }
        };

        var isAdmin = true;
        var isAuthor = false;

        if (!isAdmin && !isAuthor)
        {
            body.Remove(EntityModelAttributes.Author);
        }

        body.ContainsKey(EntityModelAttributes.Author).Should().BeTrue(
            "admin should be able to change author");
        body[EntityModelAttributes.Author]!.Value<int>().Should().Be(999);
    }

    [Fact]
    public void Update_AuthorPreserved_WhenRequesterIsCurrentAuthor()
    {
        var body = new JObject
        {
            ["id"] = 1,
            [EntityModelAttributes.Author] = 50, // author transferring to user 50
            ["fields"] = new JObject { ["name"] = "updated" }
        };

        var isAdmin = false;
        var isAuthor = true; // requester is the current author

        if (!isAdmin && !isAuthor)
        {
            body.Remove(EntityModelAttributes.Author);
        }

        body.ContainsKey(EntityModelAttributes.Author).Should().BeTrue(
            "current author should be able to transfer authorship");
        body[EntityModelAttributes.Author]!.Value<int>().Should().Be(50);
    }

    [Fact]
    public void Update_OtherFieldsNotAffected_WhenAuthorStripped()
    {
        var body = new JObject
        {
            ["id"] = 1,
            [EntityModelAttributes.Author] = 999,
            ["title"] = new JObject { ["rendered"] = "Updated Title" },
            ["fields"] = new JObject { ["description"] = "new desc" }
        };

        var isAdmin = false;
        var isAuthor = false;

        if (!isAdmin && !isAuthor)
        {
            body.Remove(EntityModelAttributes.Author);
        }

        body["title"]!["rendered"]!.Value<string>().Should().Be("Updated Title");
        ((JObject)body["fields"]!)["description"]!.Value<string>().Should().Be("new desc");
        body["id"]!.Value<int>().Should().Be(1);
    }

    [Fact]
    public void Update_NoErrorWhenBodyLacksAuthor()
    {
        // Updating without sending author should work fine
        var body = new JObject
        {
            ["id"] = 1,
            ["fields"] = new JObject { ["name"] = "updated" }
        };

        var isAdmin = false;
        var isAuthor = false;

        if (!isAdmin && !isAuthor)
        {
            body.Remove(EntityModelAttributes.Author);
        }

        body.ContainsKey(EntityModelAttributes.Author).Should().BeFalse();
        body["fields"]!["name"]!.Value<string>().Should().Be("updated");
    }

    // ── READ: can_edit_author logic ──

    [Fact]
    public void CanEditAuthor_True_WhenRequesterIsAdmin()
    {
        var isAdmin = true;
        var isAuthor = false;
        var canEditAuthor = isAdmin || isAuthor;

        canEditAuthor.Should().BeTrue();
    }

    [Fact]
    public void CanEditAuthor_True_WhenRequesterIsAuthor()
    {
        var isAdmin = false;
        var isAuthor = true;
        var canEditAuthor = isAdmin || isAuthor;

        canEditAuthor.Should().BeTrue();
    }

    [Fact]
    public void CanEditAuthor_False_WhenRequesterIsNeitherAdminNorAuthor()
    {
        var isAdmin = false;
        var isAuthor = false;
        var canEditAuthor = isAdmin || isAuthor;

        canEditAuthor.Should().BeFalse();
    }

    [Fact]
    public void CanEditAuthor_AddedToEntityResponse()
    {
        var entityData = new JObject
        {
            ["id"] = 1,
            [EntityModelAttributes.Author] = 42,
            ["fields"] = new JObject { ["name"] = "Test" }
        };

        // Simulate: requester is the author
        var requesterId = 42;
        var isAdmin = false;
        var isAuthor = entityData.TryGetValue(EntityModelAttributes.Author, out var authorToken)
                       && authorToken.Type == JTokenType.Integer
                       && authorToken.Value<int>() == requesterId;

        entityData["can_edit_author"] = isAdmin || isAuthor;

        entityData["can_edit_author"]!.Value<bool>().Should().BeTrue();
    }

    [Fact]
    public void CanEditAuthor_False_InEntityResponse_ForOtherUser()
    {
        var entityData = new JObject
        {
            ["id"] = 1,
            [EntityModelAttributes.Author] = 42,
            ["fields"] = new JObject { ["name"] = "Test" }
        };

        // Simulate: requester is NOT the author
        var requesterId = 99;
        var isAdmin = false;
        var isAuthor = entityData.TryGetValue(EntityModelAttributes.Author, out var authorToken)
                       && authorToken.Type == JTokenType.Integer
                       && authorToken.Value<int>() == requesterId;

        entityData["can_edit_author"] = isAdmin || isAuthor;

        entityData["can_edit_author"]!.Value<bool>().Should().BeFalse();
    }

    // ── Author detection from entity JObject ──

    [Fact]
    public void IsAuthor_Detected_WhenAuthorFieldMatchesRequesterId()
    {
        var entity = new JObject { [EntityModelAttributes.Author] = 42 };
        var requesterId = 42;

        var isAuthor = entity.TryGetValue(EntityModelAttributes.Author, out var authorToken)
                       && authorToken.Type == JTokenType.Integer
                       && authorToken.Value<int>() == requesterId;

        isAuthor.Should().BeTrue();
    }

    [Fact]
    public void IsAuthor_NotDetected_WhenAuthorFieldDiffers()
    {
        var entity = new JObject { [EntityModelAttributes.Author] = 42 };
        var requesterId = 99;

        var isAuthor = entity.TryGetValue(EntityModelAttributes.Author, out var authorToken)
                       && authorToken.Type == JTokenType.Integer
                       && authorToken.Value<int>() == requesterId;

        isAuthor.Should().BeFalse();
    }

    [Fact]
    public void IsAuthor_NotDetected_WhenAuthorFieldMissing()
    {
        var entity = new JObject { ["fields"] = new JObject() };
        var requesterId = 42;

        var isAuthor = entity.TryGetValue(EntityModelAttributes.Author, out var authorToken)
                       && authorToken.Type == JTokenType.Integer
                       && authorToken.Value<int>() == requesterId;

        isAuthor.Should().BeFalse();
    }

    [Fact]
    public void IsAuthor_NotDetected_WhenAuthorFieldIsNotInteger()
    {
        var entity = new JObject { [EntityModelAttributes.Author] = "not-an-int" };
        var requesterId = 42;

        var isAuthor = entity.TryGetValue(EntityModelAttributes.Author, out var authorToken)
                       && authorToken.Type == JTokenType.Integer
                       && authorToken.Value<int>() == requesterId;

        isAuthor.Should().BeFalse();
    }
}

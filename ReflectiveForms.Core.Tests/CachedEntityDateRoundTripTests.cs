// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using FluentAssertions;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Models.ReservedEntityTypes;
using ReflectiveForms.Core.Operation;
using ReflectiveForms.Core.Utilities;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Rows come back from the database as JObjects whose date/date_gmt/modified/modified_gmt values
/// have already been parsed into JTokenType.Date. Converting such a row into an EntityModel (as the
/// entity caches do) must keep the canonical date strings, otherwise writing a cached copy back
/// fails the date sanity check. That broke startup on every existing database whose set of
/// registered entity types changed: EnsureOwnerRoleExistAsync updates the owner role from its
/// cached copy and threw "Fields -date- ... are mandatory and should be strings".
/// </summary>
public class CachedEntityDateRoundTripTests
{
    private const string StoredRow = """
    {
      "id": 1,
      "slug": "owner",
      "title": { "rendered": "Owner" },
      "date": "2026-10-01T12:48:21.749Z",
      "date_gmt": "2026-10-01T10:48:21.749Z",
      "modified": "2026-10-01T12:48:21.755Z",
      "modified_gmt": "2026-10-01T10:48:21.755Z",
      "fields": { "capabilities": [] }
    }
    """;

    [Fact]
    public void DatabaseRow_WithParsedDateTokens_KeepsCanonicalDateStrings()
    {
        var row = JObject.Parse(StoredRow); // default DateParseHandling: dates become JTokenType.Date
        row["date"]!.Type.Should().Be(JTokenType.Date);

        var entity = row.ToObjectWithPolymorphism<EntityModel<IamRoleEntityFieldsModel>>()!;

        entity.Date.Should().Be("2026-10-01T12:48:21.749Z");
        entity.DateGmt.Should().Be("2026-10-01T10:48:21.749Z");
        entity.LastUpdated.Should().Be("2026-10-01T12:48:21.755Z");
        entity.LastUpdatedGmt.Should().Be("2026-10-01T10:48:21.755Z");
    }

    [Fact]
    public void CachedCopy_WrittenBack_PassesDateSanityCheck()
    {
        var cached = JObject.Parse(StoredRow).ToObjectWithPolymorphism<EntityModel<IamRoleEntityFieldsModel>>()!;
        // EntitiesCacheBase.FindEntityByFilterAndGetCopy round-trip, then the update payload.
        var copy = cached.FromObjectWithPolymorphism().ToObjectWithPolymorphism<EntityModel<IamRoleEntityFieldsModel>>()!;

        EntitySanityChecker.DateFieldsSanityCheck(copy.FromObjectWithPolymorphism(), out var failure)
            .Should().BeTrue(failure);
    }
}

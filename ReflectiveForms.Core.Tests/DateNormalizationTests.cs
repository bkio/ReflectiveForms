using System.Reflection;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Operation;
using ReflectiveForms.Core.Repositories;
using ReflectiveForms.Core.Utilities;
using Xunit;

namespace ReflectiveForms.Core.Tests;

/// <summary>
/// Tests for the Newtonsoft.Json date-format round-trip normalization fix.
///
/// Root cause: Newtonsoft.Json's default DateParseHandling=DateTime causes date-like strings
/// in JSON files to be auto-parsed to JTokenType.Date. When these are later serialized back
/// to string (e.g. via EntityModel&lt;T&gt; string fields), Newtonsoft produces the "O" round-trip
/// format "yyyy-MM-ddTHH:mm:ss.fffffffZ" (7 decimal places) instead of the canonical
/// "yyyy-MM-ddTHH:mm:ss.fffZ" (3 decimal places) that DateUtility.FromDesiredStringToDateTime
/// requires. Both EntitySanityChecker.DateFieldsSanityCheck and
/// EntityRepositoryService.FixBodyForMustHaveFields were updated to normalize these strings.
/// </summary>
public class DateNormalizationTests
{
    // ── Canonical (3-decimal-place) test fixtures ───────────────────────
    //   date < modified so that the ordering check passes
    private const string CanonicalDate = "2026-01-15T08:00:00.000Z";
    private const string CanonicalDateGmt = "2026-01-15T10:00:00.000Z";
    private const string CanonicalModified = "2026-01-15T09:00:00.000Z";
    private const string CanonicalModifiedGmt = "2026-01-15T11:00:00.000Z";

    // ── 7-decimal-place format produced by Newtonsoft.Json round-trip ───
    //   These are rejected by DateUtility.FromDesiredStringToDateTime (strict TryParseExact)
    //   but are valid ISO 8601 strings that DateTime.TryParse can handle.
    private const string SevenDecimalDate = "2026-01-15T08:00:00.0000000Z";
    private const string SevenDecimalDateGmt = "2026-01-15T10:00:00.0000000Z";
    private const string SevenDecimalModified = "2026-01-15T09:00:00.0000000Z";
    private const string SevenDecimalModifiedGmt = "2026-01-15T11:00:00.0000000Z";

    private static JObject BuildCanonicalObj() => new()
    {
        [EntityModelAttributes.Date] = CanonicalDate,
        [EntityModelAttributes.DateGmt] = CanonicalDateGmt,
        [EntityModelAttributes.Modified] = CanonicalModified,
        [EntityModelAttributes.ModifiedGmt] = CanonicalModifiedGmt,
    };

    // ════════════════════════════════════════════════════════════════════
    // EntitySanityChecker.DateFieldsSanityCheck
    // ════════════════════════════════════════════════════════════════════

    // ── Canonical strings — must pass without modification ───────────────

    [Fact]
    public void DateFieldsSanityCheck_CanonicalStrings_Passes()
    {
        var obj = BuildCanonicalObj();

        var result = EntitySanityChecker.DateFieldsSanityCheck(obj, out var msg);

        result.Should().BeTrue(because: msg);
        msg.Should().BeEmpty();
    }

    [Fact]
    public void DateFieldsSanityCheck_CanonicalStrings_ValuesUnchanged()
    {
        var obj = BuildCanonicalObj();

        EntitySanityChecker.DateFieldsSanityCheck(obj, out _);

        obj[EntityModelAttributes.Date]!.Value<string>().Should().Be(CanonicalDate);
        obj[EntityModelAttributes.DateGmt]!.Value<string>().Should().Be(CanonicalDateGmt);
        obj[EntityModelAttributes.Modified]!.Value<string>().Should().Be(CanonicalModified);
        obj[EntityModelAttributes.ModifiedGmt]!.Value<string>().Should().Be(CanonicalModifiedGmt);
    }

    // ── JTokenType.Date normalization ────────────────────────────────────

    [Fact]
    public void DateFieldsSanityCheck_JTokenTypeDate_NormalizedToCanonicalStringAndPasses()
    {
        // Simulates what Newtonsoft.Json produces when it reads a JSON file:
        // date strings are auto-parsed to JTokenType.Date tokens.
        var obj = new JObject
        {
            [EntityModelAttributes.Date] = new JValue(new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc)),
            [EntityModelAttributes.DateGmt] = new JValue(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)),
            [EntityModelAttributes.Modified] = new JValue(new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc)),
            [EntityModelAttributes.ModifiedGmt] = new JValue(new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc)),
        };
        obj[EntityModelAttributes.Date]!.Type.Should().Be(JTokenType.Date, "pre-condition: input must be JTokenType.Date");

        var result = EntitySanityChecker.DateFieldsSanityCheck(obj, out var msg);

        result.Should().BeTrue(because: msg);
        // After normalization the tokens must be canonical strings
        obj[EntityModelAttributes.Date]!.Type.Should().Be(JTokenType.String);
        obj[EntityModelAttributes.DateGmt]!.Type.Should().Be(JTokenType.String);
        obj[EntityModelAttributes.Modified]!.Type.Should().Be(JTokenType.String);
        obj[EntityModelAttributes.ModifiedGmt]!.Type.Should().Be(JTokenType.String);
        DateUtility.FromDesiredStringToDateTime(obj[EntityModelAttributes.Date]!.Value<string>(), out _)
            .Should().BeTrue("normalized Date must be parseable by DateUtility");
        DateUtility.FromDesiredStringToDateTime(obj[EntityModelAttributes.DateGmt]!.Value<string>(), out _)
            .Should().BeTrue("normalized DateGmt must be parseable by DateUtility");
    }

    // ── 7-decimal-place string normalization ─────────────────────────────

    [Fact]
    public void DateFieldsSanityCheck_SevenDecimalPlaceStrings_RejectedByStrictParser()
    {
        // Verify the pre-condition: these strings must NOT be accepted by the strict parser
        DateUtility.FromDesiredStringToDateTime(SevenDecimalDate, out _)
            .Should().BeFalse("7-decimal-place format must not pass the strict validator");
        DateUtility.FromDesiredStringToDateTime(SevenDecimalDateGmt, out _)
            .Should().BeFalse();
        DateUtility.FromDesiredStringToDateTime(SevenDecimalModified, out _)
            .Should().BeFalse();
        DateUtility.FromDesiredStringToDateTime(SevenDecimalModifiedGmt, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void DateFieldsSanityCheck_SevenDecimalPlaceStrings_NormalizedAndPasses()
    {
        // Exact round-trip scenario: JTokenType.Date → EntityModel<T> string field
        // → back to JObject with the 7-decimal-place Newtonsoft round-trip format.
        var obj = new JObject
        {
            [EntityModelAttributes.Date] = SevenDecimalDate,
            [EntityModelAttributes.DateGmt] = SevenDecimalDateGmt,
            [EntityModelAttributes.Modified] = SevenDecimalModified,
            [EntityModelAttributes.ModifiedGmt] = SevenDecimalModifiedGmt,
        };

        var result = EntitySanityChecker.DateFieldsSanityCheck(obj, out var msg);

        result.Should().BeTrue(because: msg);
        DateUtility.FromDesiredStringToDateTime(obj[EntityModelAttributes.Date]!.Value<string>(), out _)
            .Should().BeTrue("Date must be normalized to canonical format");
        DateUtility.FromDesiredStringToDateTime(obj[EntityModelAttributes.DateGmt]!.Value<string>(), out _)
            .Should().BeTrue("DateGmt must be normalized to canonical format");
        DateUtility.FromDesiredStringToDateTime(obj[EntityModelAttributes.Modified]!.Value<string>(), out _)
            .Should().BeTrue("Modified must be normalized to canonical format");
        DateUtility.FromDesiredStringToDateTime(obj[EntityModelAttributes.ModifiedGmt]!.Value<string>(), out _)
            .Should().BeTrue("ModifiedGmt must be normalized to canonical format");
    }

    [Fact]
    public void DateFieldsSanityCheck_SevenDecimalPlaceStrings_DateComponentsPreserved()
    {
        // After normalization the milliseconds must be preserved correctly.
        var obj = new JObject
        {
            [EntityModelAttributes.Date] = "2026-03-25T14:45:59.1230000Z",
            [EntityModelAttributes.DateGmt] = "2026-03-25T16:45:59.4560000Z",
            [EntityModelAttributes.Modified] = "2026-03-26T14:45:59.7890000Z",
            [EntityModelAttributes.ModifiedGmt] = "2026-03-26T16:45:59.0010000Z",
        };

        EntitySanityChecker.DateFieldsSanityCheck(obj, out _);

        // Check raw normalized strings from JObject to avoid DateTime.TryParseExact timezone
        // conversion that occurs when re-parsing canonical strings ending in "Z".
        obj[EntityModelAttributes.Date]!.Value<string>()!
            .Should().Be("2026-03-25T14:45:59.123Z", "normalization must preserve all time components");
        obj[EntityModelAttributes.DateGmt]!.Value<string>()!
            .Should().Be("2026-03-25T16:45:59.456Z", "normalization must preserve all time components");
        obj[EntityModelAttributes.Modified]!.Value<string>()!
            .Should().Be("2026-03-26T14:45:59.789Z", "normalization must preserve all time components");
        obj[EntityModelAttributes.ModifiedGmt]!.Value<string>()!
            .Should().Be("2026-03-26T16:45:59.001Z", "normalization must preserve all time components");
    }

    // ── Failure cases ─────────────────────────────────────────────────────

    [Fact]
    public void DateFieldsSanityCheck_MissingDateField_Fails()
    {
        var obj = new JObject
        {
            [EntityModelAttributes.DateGmt] = CanonicalDateGmt,
            [EntityModelAttributes.Modified] = CanonicalModified,
            [EntityModelAttributes.ModifiedGmt] = CanonicalModifiedGmt,
        };

        var result = EntitySanityChecker.DateFieldsSanityCheck(obj, out var msg);

        result.Should().BeFalse();
        msg.Should().NotBeEmpty();
    }

    [Fact]
    public void DateFieldsSanityCheck_AllMissingDateFields_Fails()
    {
        var result = EntitySanityChecker.DateFieldsSanityCheck(new JObject(), out var msg);

        result.Should().BeFalse();
        msg.Should().NotBeEmpty();
    }

    [Fact]
    public void DateFieldsSanityCheck_CompletelyUnparseableString_Fails()
    {
        var obj = new JObject
        {
            [EntityModelAttributes.Date] = "not-a-date",
            [EntityModelAttributes.DateGmt] = CanonicalDateGmt,
            [EntityModelAttributes.Modified] = CanonicalModified,
            [EntityModelAttributes.ModifiedGmt] = CanonicalModifiedGmt,
        };

        var result = EntitySanityChecker.DateFieldsSanityCheck(obj, out var msg);

        result.Should().BeFalse();
        msg.Should().Contain(EntityModelAttributes.Date);
    }

    [Fact]
    public void DateFieldsSanityCheck_ModifiedBeforeDate_Fails()
    {
        // modified < date — the ordering check must reject this
        var obj = new JObject
        {
            [EntityModelAttributes.Date] = "2026-06-01T12:00:00.000Z",
            [EntityModelAttributes.DateGmt] = "2026-06-01T10:00:00.000Z",
            [EntityModelAttributes.Modified] = "2026-05-01T12:00:00.000Z",     // before Date
            [EntityModelAttributes.ModifiedGmt] = "2026-05-01T10:00:00.000Z",  // before DateGmt
        };

        var result = EntitySanityChecker.DateFieldsSanityCheck(obj, out var msg);

        result.Should().BeFalse();
        msg.Should().Contain(EntityModelAttributes.Date);
    }

    // ════════════════════════════════════════════════════════════════════
    // EntityRepositoryService.FixBodyForMustHaveFields (via reflection)
    //
    // The method is private static; bodies must NOT include an "id" field
    // or the method will call RfConfiguration.EndpointConfiguration which
    // requires a fully initialized RfConfiguration.
    // ════════════════════════════════════════════════════════════════════

    private static void InvokeFixBody(JObject body)
    {
        var method = typeof(EntityRepositoryService)
            .GetMethod("FixBodyForMustHaveFields", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("FixBodyForMustHaveFields must exist as a private static method");
        method!.Invoke(null, ["test-entity", body]);
    }

    // ── DateGmt / Date present as JTokenType.Date ─────────────────────

    [Fact]
    public void FixBodyForMustHaveFields_DateGmtAsJTokenTypeDate_NormalizedToCanonicalString()
    {
        var body = new JObject
        {
            // no "id" — avoids RfConfiguration.EndpointConfiguration call
            [EntityModelAttributes.DateGmt] = new JValue(new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc)),
            [EntityModelAttributes.Date] = new JValue(new DateTime(2026, 4, 10, 14, 0, 0, DateTimeKind.Utc)),
        };
        body[EntityModelAttributes.DateGmt]!.Type.Should().Be(JTokenType.Date, "pre-condition");

        InvokeFixBody(body);

        var dateGmtStr = body[EntityModelAttributes.DateGmt]!.Value<string>();
        DateUtility.FromDesiredStringToDateTime(dateGmtStr, out _)
            .Should().BeTrue("JTokenType.Date DateGmt must be normalized to canonical string");
        var dateStr = body[EntityModelAttributes.Date]!.Value<string>();
        DateUtility.FromDesiredStringToDateTime(dateStr, out _)
            .Should().BeTrue("JTokenType.Date Date must be normalized to canonical string");
    }

    // ── DateGmt / Date present as 7-decimal-place strings ────────────────

    [Fact]
    public void FixBodyForMustHaveFields_DateGmtAsSevenDecimalString_NormalizedToCanonicalString()
    {
        var body = new JObject
        {
            [EntityModelAttributes.DateGmt] = SevenDecimalDateGmt,
            [EntityModelAttributes.Date] = SevenDecimalDate,
        };

        InvokeFixBody(body);

        var dateGmtStr = body[EntityModelAttributes.DateGmt]!.Value<string>();
        DateUtility.FromDesiredStringToDateTime(dateGmtStr, out _)
            .Should().BeTrue("7-decimal DateGmt must be normalized to canonical format");
        var dateStr = body[EntityModelAttributes.Date]!.Value<string>();
        DateUtility.FromDesiredStringToDateTime(dateStr, out _)
            .Should().BeTrue("7-decimal Date must be normalized to canonical format");
    }

    [Fact]
    public void FixBodyForMustHaveFields_DateGmtAsSevenDecimalString_DateComponentsPreserved()
    {
        var body = new JObject
        {
            [EntityModelAttributes.DateGmt] = "2026-04-10T12:30:45.1230000Z",
            [EntityModelAttributes.Date] = "2026-04-10T14:30:45.4560000Z",
        };

        InvokeFixBody(body);

        // Check raw normalized strings from JObject to avoid DateTime.TryParseExact timezone
        // conversion that occurs when re-parsing canonical strings ending in "Z".
        body[EntityModelAttributes.DateGmt]!.Value<string>()!
            .Should().Be("2026-04-10T12:30:45.123Z", "normalization must preserve all time components");
        body[EntityModelAttributes.Date]!.Value<string>()!
            .Should().Be("2026-04-10T14:30:45.456Z", "normalization must preserve all time components");
    }

    // ── No DateGmt, ModifiedGmt present (canonical) ────────────────────

    [Fact]
    public void FixBodyForMustHaveFields_NoDateGmt_WithCanonicalModifiedGmt_DerivesDateGmtAndDate()
    {
        var body = new JObject
        {
            [EntityModelAttributes.ModifiedGmt] = CanonicalModifiedGmt,
        };

        InvokeFixBody(body);

        var dateGmtStr = body[EntityModelAttributes.DateGmt]!.Value<string>();
        DateUtility.FromDesiredStringToDateTime(dateGmtStr, out _)
            .Should().BeTrue("DateGmt must be derived from ModifiedGmt in canonical format");
        var dateStr = body[EntityModelAttributes.Date]!.Value<string>();
        DateUtility.FromDesiredStringToDateTime(dateStr, out _)
            .Should().BeTrue("Date must be derived from ModifiedGmt in canonical format");
    }

    // ── No DateGmt, ModifiedGmt present as 7-decimal (the startup crash scenario) ──

    [Fact]
    public void FixBodyForMustHaveFields_NoDateGmt_WithSevenDecimalModifiedGmt_StillDerivesDateGmtAndDate()
    {
        // This reproduces the exact startup crash scenario:
        // The owner role is loaded from DB, round-tripped through EntityModel<T> string fields,
        // producing 7-decimal-place ModifiedGmt. FixBodyForMustHaveFields must derive
        // DateGmt/Date from it rather than silently falling back to "now".
        var body = new JObject
        {
            [EntityModelAttributes.ModifiedGmt] = SevenDecimalModifiedGmt,
        };

        InvokeFixBody(body);

        var dateGmtStr = body[EntityModelAttributes.DateGmt]!.Value<string>();
        DateUtility.FromDesiredStringToDateTime(dateGmtStr, out _)
            .Should().BeTrue("7-decimal ModifiedGmt must still yield a valid canonical DateGmt");

        // Check the raw string to verify the original time components (11:00:00 from SevenDecimalModifiedGmt)
        // are preserved without timezone conversion.
        dateGmtStr.Should().Be("2026-01-15T11:00:00.000Z",
            "DateGmt must be derived from ModifiedGmt with all time components preserved");
    }

    [Fact]
    public void FixBodyForMustHaveFields_NoDateGmt_NoModifiedGmt_FallsBackToCurrentTime()
    {
        var before = DateTime.UtcNow.AddSeconds(-2);
        var body = new JObject();

        InvokeFixBody(body);

        var dateGmtStr = body[EntityModelAttributes.DateGmt]!.Value<string>();
        DateUtility.FromDesiredStringToDateTime(dateGmtStr, out var dateGmt)
            .Should().BeTrue("fallback DateGmt must be in canonical format");
        // Verify it's a recent timestamp (loose bound)
        dateGmt.Should().BeOnOrAfter(before);
    }

    // ── Modified / ModifiedGmt are always overwritten by FixBody ─────────

    [Fact]
    public void FixBodyForMustHaveFields_AlwaysOverwritesModifiedAndModifiedGmt()
    {
        // Even if Modified/ModifiedGmt are present, FixBody overwrites them with "now"
        var body = new JObject
        {
            [EntityModelAttributes.DateGmt] = CanonicalDateGmt,
            [EntityModelAttributes.Date] = CanonicalDate,
            [EntityModelAttributes.ModifiedGmt] = "2020-01-01T00:00:00.000Z",
            [EntityModelAttributes.Modified] = "2020-01-01T00:00:00.000Z",
        };
        var before = DateTime.UtcNow.AddSeconds(-2);

        InvokeFixBody(body);

        DateUtility.FromDesiredStringToDateTime(body[EntityModelAttributes.ModifiedGmt]!.Value<string>()!, out var modifiedGmt)
            .Should().BeTrue("ModifiedGmt must be overwritten to a canonical string");
        modifiedGmt.Should().BeOnOrAfter(before, "ModifiedGmt must be overwritten to ~now");

        DateUtility.FromDesiredStringToDateTime(body[EntityModelAttributes.Modified]!.Value<string>()!, out var modified)
            .Should().BeTrue("Modified must be overwritten to a canonical string");
    }

    // ── Integration: FixBody → SanityCheck end-to-end ────────────────────

    [Fact]
    public void FixBodyThenSanityCheck_SevenDecimalDates_PassesSanityCheck()
    {
        // After FixBodyForMustHaveFields normalizes 7-decimal dates, the subsequent
        // DateFieldsSanityCheck must also succeed — the full pipeline must be clean.
        var body = new JObject
        {
            [EntityModelAttributes.DateGmt] = SevenDecimalDateGmt,
            [EntityModelAttributes.Date] = SevenDecimalDate,
        };

        InvokeFixBody(body);
        // FixBody always writes canonical Modified/ModifiedGmt, so the ordering check passes.
        var result = EntitySanityChecker.DateFieldsSanityCheck(body, out var msg);

        result.Should().BeTrue(because: msg);
    }

    [Fact]
    public void FixBodyThenSanityCheck_JTokenTypeDateFields_PassesSanityCheck()
    {
        var body = new JObject
        {
            [EntityModelAttributes.DateGmt] = new JValue(new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)),
            [EntityModelAttributes.Date] = new JValue(new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc)),
        };

        InvokeFixBody(body);
        var result = EntitySanityChecker.DateFieldsSanityCheck(body, out var msg);

        result.Should().BeTrue(because: msg);
    }
}

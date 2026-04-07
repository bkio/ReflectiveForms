using FluentAssertions;
using ReflectiveForms.Core.Utilities;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class DateUtilityTests
{
    [Fact]
    public void DateTimeToDesiredString_FormatsCorrectly()
    {
        var dt = new DateTime(2026, 3, 15, 10, 30, 45, 123, DateTimeKind.Utc);
        var result = DateUtility.DateTimeToDesiredString(dt);
        result.Should().Be("2026-03-15T10:30:45.123Z");
    }

    [Fact]
    public void DateTimeToDesiredString_HandlesMinValue()
    {
        var result = DateUtility.DateTimeToDesiredString(DateTime.MinValue);
        result.Should().Be("0001-01-01T00:00:00.000Z");
    }

    [Fact]
    public void DateTimeToDesiredString_HandlesMidnight()
    {
        var dt = new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        var result = DateUtility.DateTimeToDesiredString(dt);
        result.Should().Be("2026-01-01T00:00:00.000Z");
    }

    [Fact]
    public void FromDesiredStringToDateTime_ParsesValidString()
    {
        var success = DateUtility.FromDesiredStringToDateTime("2026-03-15T10:30:45.123Z", out var result);
        success.Should().BeTrue();
        result.Year.Should().Be(2026);
        result.Month.Should().Be(3);
        result.Day.Should().Be(15);
        result.Minute.Should().Be(30);
        result.Second.Should().Be(45);
        result.Millisecond.Should().Be(123);
    }

    [Fact]
    public void FromDesiredStringToDateTime_RejectsInvalidString()
    {
        var success = DateUtility.FromDesiredStringToDateTime("not-a-date", out _);
        success.Should().BeFalse();
    }

    [Fact]
    public void FromDesiredStringToDateTime_RejectsNull()
    {
        var success = DateUtility.FromDesiredStringToDateTime(null, out _);
        success.Should().BeFalse();
    }

    [Fact]
    public void FromDesiredStringToDateTime_RejectsEmptyString()
    {
        var success = DateUtility.FromDesiredStringToDateTime("", out _);
        success.Should().BeFalse();
    }

    [Fact]
    public void FromDesiredStringToDateTime_RejectsWrongFormat()
    {
        // ISO format without milliseconds
        var success = DateUtility.FromDesiredStringToDateTime("2026-03-15T10:30:45Z", out _);
        success.Should().BeFalse();
    }

    [Fact]
    public void RoundTrip_PreservesDateComponents()
    {
        var original = new DateTime(2026, 6, 20, 14, 25, 33, 456, DateTimeKind.Utc);
        var str = DateUtility.DateTimeToDesiredString(original);
        var success = DateUtility.FromDesiredStringToDateTime(str, out var parsed);

        success.Should().BeTrue();
        parsed.Year.Should().Be(original.Year);
        parsed.Month.Should().Be(original.Month);
        parsed.Day.Should().Be(original.Day);
        parsed.Minute.Should().Be(original.Minute);
        parsed.Second.Should().Be(original.Second);
        parsed.Millisecond.Should().Be(original.Millisecond);
    }
}

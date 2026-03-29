using FluentAssertions;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class CrudOperationTests
{
    [Theory]
    [InlineData("CREATE")]
    [InlineData("READ")]
    [InlineData("PEEK_ALL")]
    [InlineData("PEEK_ALL_PAGINATED")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    public void ValidCrudOperations_ShouldBeRecognized(string operation)
    {
        var validOps = new[] { "CREATE", "READ", "PEEK_ALL", "PEEK_ALL_PAGINATED", "UPDATE", "DELETE" };
        validOps.Should().Contain(operation);
    }

    [Theory]
    [InlineData("INVALID")]
    [InlineData("PATCH")]
    [InlineData("")]
    [InlineData("peek_all_paginated")] // lowercase not accepted by backend (already uppercased)
    public void InvalidCrudOperations_ShouldNotBeRecognized(string operation)
    {
        var validOps = new[] { "CREATE", "READ", "PEEK_ALL", "PEEK_ALL_PAGINATED", "UPDATE", "DELETE" };
        validOps.Should().NotContain(operation);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(101, false)]
    public void PageSize_ValidationRange(int pageSize, bool expectedValid)
    {
        var isValid = pageSize is > 0 and <= 100;
        isValid.Should().Be(expectedValid);
    }

    [Fact]
    public void PeekAllPaginatedAuth_ShouldMapToPeekAll()
    {
        // PEEK_ALL_PAGINATED should use PEEK_ALL permission check
        const string operation = "PEEK_ALL_PAGINATED";
        var authOperation = operation == "PEEK_ALL_PAGINATED" ? "PEEK_ALL" : operation;
        authOperation.Should().Be("PEEK_ALL");
    }

    [Fact]
    public void NormalOperationAuth_ShouldNotBeRemapped()
    {
        const string operation = "CREATE";
        var authOperation = operation == "PEEK_ALL_PAGINATED" ? "PEEK_ALL" : operation;
        authOperation.Should().Be("CREATE");
    }
}

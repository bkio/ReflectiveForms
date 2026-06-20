// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Utilities;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class ByteSerializationTests
{
    /// <summary>
    /// Test-only model with a byte field to verify round-trip behavior.
    /// </summary>
    private class TestByteModel : EntityFieldsModel
    {
        [JsonProperty("byte_value")]
        public byte ByteValue = 255;

        [JsonProperty("name")]
        public string Name = "";
    }

    [Fact]
    public void NewtonsoftRoundTrip_ByteValue255_PreservesValue()
    {
        var model = new TestByteModel { ByteValue = 255, Name = "test" };
        var jObject = model.FromObjectWithPolymorphism();

        // Round-trip through Newtonsoft — should preserve 255
        var deserialized = jObject.ToObjectWithPolymorphism<TestByteModel>();

        deserialized.Should().NotBeNull();
        deserialized!.ByteValue.Should().Be(255);
        deserialized.Name.Should().Be("test");
    }

    [Fact]
    public void NewtonsoftRoundTrip_ByteValue0_PreservesValue()
    {
        var model = new TestByteModel { ByteValue = 0, Name = "zero" };
        var jObject = model.FromObjectWithPolymorphism();

        var deserialized = jObject.ToObjectWithPolymorphism<TestByteModel>();

        deserialized.Should().NotBeNull();
        deserialized!.ByteValue.Should().Be(0);
    }

    [Fact]
    public void NewtonsoftRoundTrip_ByteValue127_PreservesValue()
    {
        var model = new TestByteModel { ByteValue = 127, Name = "edge" };
        var jObject = model.FromObjectWithPolymorphism();

        var deserialized = jObject.ToObjectWithPolymorphism<TestByteModel>();

        deserialized.Should().NotBeNull();
        deserialized!.ByteValue.Should().Be(127);
    }

    [Fact]
    public void SerializedJson_ByteValue255_IsNumber()
    {
        var model = new TestByteModel { ByteValue = 255, Name = "test" };
        var json = model.SerializeObjectWithPolymorphism();

        // The wire format is a JSON number — consumers using STJ receive this
        var parsed = JObject.Parse(json);
        parsed["byte_value"]!.Value<int>().Should().Be(255);
    }
}

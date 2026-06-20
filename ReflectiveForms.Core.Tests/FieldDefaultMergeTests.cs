// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Reflection;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Models;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class FieldDefaultMergeTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    private static JObject O(string json) => JObject.Parse(json);
    private static JArray A(string json) => JArray.Parse(json);

    /// <summary>
    /// Create a minimal EntityFinalConfigurationBase with the given DefaultJObject
    /// and an empty repeater template map.
    /// </summary>
    private static EntityFinalConfigurationBase MakeConfig(
        JObject defaultJObject,
        Dictionary<string, JObject>? templates = null)
    {
        // Use a dummy subclass — we can't instantiate the abstract base directly,
        // but we can set the properties via reflection in tests.
        // Instead, use EntityFinalConfiguration<MinimalFieldsModel> with a custom
        // DefaultJObject set via a test-only subclass.
        return new TestEntityConfig(defaultJObject, templates ?? new());
    }

    /// <summary>
    /// Test-only entity config that lets us inject DefaultJObject and templates.
    /// </summary>
    private sealed class TestEntityConfig : EntityFinalConfigurationBase
    {
        public TestEntityConfig(JObject defaultJObject, Dictionary<string, JObject> templates)
            : base(typeof(MinimalFieldsModel))
        {
            // Set via reflection since the properties are init-only
            var baseType = typeof(EntityFinalConfigurationBase);
            var defaultJObjectProp = baseType.GetProperty("DefaultJObject",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            defaultJObjectProp.SetValue(this, defaultJObject);

            var templateMapProp = baseType.GetProperty("RepeaterTemplateMap",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            templateMapProp.SetValue(this, templates);
        }
    }

    private class MinimalFieldsModel : EntityFieldsModel { }

    // ── Test 1: Top-level field missing ──────────────────────────────

    [Fact]
    public void TopLevelFieldMissing_InjectsDefault()
    {
        var defaults = O(@"{ fields: { name: '', is_active: false, count: 0 } }");
        var db = O(@"{ fields: { name: 'Bob', count: 5 } }");
        var config = MakeConfig(defaults);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        result["fields"]!["is_active"]!.Value<bool>().Should().BeFalse();
        result["fields"]!["name"]!.Value<string>().Should().Be("Bob");
        result["fields"]!["count"]!.Value<int>().Should().Be(5);
    }

    // ── Test 2: Existing value never overwritten ─────────────────────

    [Fact]
    public void ExistingValue_NeverOverwritten()
    {
        var defaults = O(@"{ fields: { name: '' } }");
        var db = O(@"{ fields: { name: 'Bob' } }");
        var config = MakeConfig(defaults);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        result["fields"]!["name"]!.Value<string>().Should().Be("Bob");
    }

    // ── Test 3: Extra DB keys preserved (field removed from model) ───

    [Fact]
    public void ExtraDbKey_Preserved()
    {
        var defaults = O(@"{ fields: { name: '' } }");
        var db = O(@"{ fields: { name: 'Bob', old_field: 'legacy' } }");
        var config = MakeConfig(defaults);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        result["fields"]!["old_field"]!.Value<string>().Should().Be("legacy");
        result["fields"]!["name"]!.Value<string>().Should().Be("Bob");
    }

    // ── Test 4: Group — nested object missing field ─────────────────

    [Fact]
    public void Group_NestedObjectMissingField_InjectsDefault()
    {
        var defaults = O(@"{ fields: { dims: { w: 0, h: 0, d: 0 } } }");
        var db = O(@"{ fields: { dims: { w: 10, h: 5 } } }");
        var config = MakeConfig(defaults);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        result["fields"]!["dims"]!["w"]!.Value<int>().Should().Be(10);
        result["fields"]!["dims"]!["h"]!.Value<int>().Should().Be(5);
        result["fields"]!["dims"]!["d"]!.Value<int>().Should().Be(0);
    }

    // ── Test 5: Repeater — array elements missing field ─────────────

    [Fact]
    public void Repeater_ElementsMissingField_InjectsDefault()
    {
        var itemTmpl = O(@"{ name: '', active: true, count: 0 }");
        var templates = new Dictionary<string, JObject> { ["fields.items"] = itemTmpl };
        var defaults = O(@"{ fields: { items: [] } }");
        var db = O(@"{ fields: { items: [{ name: 'A' }, { name: 'B', active: false }] } }");
        var config = MakeConfig(defaults, templates);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        var items = result["fields"]!["items"] as JArray;
        items.Should().NotBeNull();
        items!.Count.Should().Be(2);

        items[0]!["name"]!.Value<string>().Should().Be("A");
        items[0]!["active"]!.Value<bool>().Should().BeTrue();  // injected
        items[0]!["count"]!.Value<int>().Should().Be(0);        // injected

        items[1]!["name"]!.Value<string>().Should().Be("B");
        items[1]!["active"]!.Value<bool>().Should().BeFalse(); // preserved
        items[1]!["count"]!.Value<int>().Should().Be(0);        // injected
    }

    // ── Test 6: Repeater→Group — nested object inside repeater ──────

    [Fact]
    public void RepeaterWithGroup_NestedObjectFilled()
    {
        var variantTmpl = O(@"{ sku: '', price: 0, dims: { w: 0, h: 0 } }");
        var templates = new Dictionary<string, JObject> { ["fields.variants"] = variantTmpl };
        var defaults = O(@"{ fields: { variants: [] } }");
        var db = O(@"{ fields: { variants: [{ sku: 'A', price: 10, dims: { w: 5 } }] } }");
        var config = MakeConfig(defaults, templates);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        var variants = result["fields"]!["variants"] as JArray;
        variants.Should().NotBeNull();
        variants![0]!["dims"]!["w"]!.Value<int>().Should().Be(5);
        variants[0]!["dims"]!["h"]!.Value<int>().Should().Be(0); // injected
    }

    // ── Test 7: Repeater→Repeater — Array→Array, 2 levels ──────────

    [Fact]
    public void NestedRepeater_ArrayInArray_DefaultsInjected()
    {
        var variantTmpl = O(@"{ sku: '', specs: [] }");
        var specTmpl = O(@"{ name: '', unit: '' }");
        var templates = new Dictionary<string, JObject>
        {
            ["fields.variants"] = variantTmpl,
            ["fields.variants.specs"] = specTmpl
        };
        var defaults = O(@"{ fields: { variants: [] } }");
        var db = O(@"{ fields: { variants: [{ sku: 'A', specs: [{ name: 'Weight' }] }] } }");
        var config = MakeConfig(defaults, templates);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        var specs = result["fields"]!["variants"]![0]!["specs"] as JArray;
        specs.Should().NotBeNull();
        specs![0]!["name"]!.Value<string>().Should().Be("Weight");
        specs[0]!["unit"]!.Value<string>().Should().Be(""); // injected
    }

    // ── Test 8: Repeater→Group→Repeater — Array→Object→Array ────────

    [Fact]
    public void RepeaterGroupRepeater_ArrayObjectArray_DefaultsInjected()
    {
        var outerTmpl = O(@"{ sku: '', container: { inner: [] } }");
        var innerTmpl = O(@"{ key: '', value: '' }");
        var templates = new Dictionary<string, JObject>
        {
            ["fields.outer"] = outerTmpl,
            ["fields.outer.container.inner"] = innerTmpl
        };
        var defaults = O(@"{ fields: { outer: [] } }");
        var db = O(@"{ fields: { outer: [{ sku: 'A', container: { inner: [{ key: 'x' }] } }] } }");
        var config = MakeConfig(defaults, templates);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        var inner = result["fields"]!["outer"]![0]!["container"]!["inner"] as JArray;
        inner.Should().NotBeNull();
        inner![0]!["key"]!.Value<string>().Should().Be("x");
        inner[0]!["value"]!.Value<string>().Should().Be(""); // injected
    }

    // ── Test 9: Empty repeater array — no crash ─────────────────────

    [Fact]
    public void EmptyRepeater_NoCrash_StaysEmpty()
    {
        var itemTmpl = O(@"{ name: '', flag: false }");
        var templates = new Dictionary<string, JObject> { ["fields.items"] = itemTmpl };
        var defaults = O(@"{ fields: { items: [] } }");
        var db = O(@"{ fields: { items: [] } }");
        var config = MakeConfig(defaults, templates);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        var items = result["fields"]!["items"] as JArray;
        items.Should().NotBeNull();
        items!.Count.Should().Be(0);
    }

    // ── Test 10: Primitive array NOT treated as repeater ─────────────

    [Fact]
    public void PrimitiveArray_NotTreatedAsRepeater_ValuesUnchanged()
    {
        var variantTmpl = O(@"{ sku: '' }");
        var templates = new Dictionary<string, JObject> { ["fields.variants"] = variantTmpl };
        var defaults = O(@"{ fields: { tags: [], variants: [] } }");
        var db = O(@"{ fields: { tags: [1, 2, 3], variants: [] } }");
        var config = MakeConfig(defaults, templates);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        var tags = result["fields"]!["tags"] as JArray;
        tags.Should().NotBeNull();
        tags!.Select(t => t!.Value<int>()).Should().Equal(1, 2, 3);
    }

    // ── Test 11: Partial — some items have field, some don't ────────

    [Fact]
    public void Partial_SomeItemsHaveField_OnlyMissingGetDefault()
    {
        var itemTmpl = O(@"{ name: '', flag: false }");
        var templates = new Dictionary<string, JObject> { ["fields.items"] = itemTmpl };
        var defaults = O(@"{ fields: { items: [] } }");
        var db = O(@"{ fields: { items: [{ name: 'A' }, { name: 'B', flag: true }] } }");
        var config = MakeConfig(defaults, templates);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        var items = result["fields"]!["items"] as JArray;
        items![0]!["flag"]!.Value<bool>().Should().BeFalse(); // injected
        items[1]!["flag"]!.Value<bool>().Should().BeTrue();   // preserved
    }

    // ── Test 12: Entity missing "fields" key entirely ───────────────

    [Fact]
    public void EntityMissingFieldsKey_FieldsInjectedFromDefaults()
    {
        var defaults = O(@"{ id: -1, fields: { name: '', is_active: false } }");
        var db = O(@"{ id: 1, title: { title_rendered: 'Bob' } }");
        var config = MakeConfig(defaults);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        result["id"]!.Value<int>().Should().Be(1);
        result["title"]!["title_rendered"]!.Value<string>().Should().Be("Bob");
        result["fields"].Should().NotBeNull();
        result["fields"]!["name"]!.Value<string>().Should().Be("");
        result["fields"]!["is_active"]!.Value<bool>().Should().BeFalse();
    }

    // ── Test 13: null value vs. absent key ──────────────────────────

    [Fact]
    public void NullValue_KeptAsNull_NotReplacedWithDefault()
    {
        var defaults = O(@"{ fields: { flag: false, count: 0 } }");
        var db = O(@"{ fields: { flag: null, count: 5 } }");
        var config = MakeConfig(defaults);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        result["fields"]!["flag"]!.Type.Should().Be(JTokenType.Null);
        result["fields"]!["count"]!.Value<int>().Should().Be(5);
    }

    // ── Test 14: null group inside repeater item ────────────────────

    [Fact]
    public void NullGroupInsideRepeater_KeptAsNull()
    {
        var itemTmpl = O(@"{ sku: '', dims: { w: 0, h: 0 } }");
        var templates = new Dictionary<string, JObject> { ["fields.items"] = itemTmpl };
        var defaults = O(@"{ fields: { items: [] } }");
        var db = O(@"{ fields: { items: [{ sku: 'A', dims: null }] } }");
        var config = MakeConfig(defaults, templates);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        var items = result["fields"]!["items"] as JArray;
        items![0]!["dims"]!.Type.Should().Be(JTokenType.Null);
    }

    // ── Test 15: Multiple different repeater fields ─────────────────

    [Fact]
    public void MultipleRepeaters_DifferentTemplates_NoCrossContamination()
    {
        var variantTmpl = O(@"{ sku: '', is_available: true }");
        var galleryTmpl = O(@"{ image: '', caption: '' }");
        var templates = new Dictionary<string, JObject>
        {
            ["fields.variants"] = variantTmpl,
            ["fields.gallery"] = galleryTmpl
        };
        var defaults = O(@"{ fields: { variants: [], gallery: [] } }");
        var db = O(@"{
            fields: {
                variants: [{ sku: 'A' }],
                gallery: [{ image: 'pic.png' }]
            }
        }");
        var config = MakeConfig(defaults, templates);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        result["fields"]!["variants"]![0]!["is_available"]!.Value<bool>().Should().BeTrue();
        result["fields"]!["variants"]![0]!["sku"]!.Value<string>().Should().Be("A");
        result["fields"]!["gallery"]![0]!["image"]!.Value<string>().Should().Be("pic.png");
        result["fields"]!["gallery"]![0]!["caption"]!.Value<string>().Should().Be("");
        // Gallery should NOT have is_available, variants should NOT have caption
        result["fields"]!["gallery"]![0]["is_available"].Should().BeNull();
        result["fields"]!["variants"]![0]["caption"].Should().BeNull();
    }

    // ── Test 16: Large repeater (100 elements) ──────────────────────

    [Fact]
    public void LargeRepeater_AllElementsGetDefaults()
    {
        var itemTmpl = O(@"{ name: '', flag: false }");
        var templates = new Dictionary<string, JObject> { ["fields.items"] = itemTmpl };
        var defaults = O(@"{ fields: { items: [] } }");

        var items = new JArray();
        for (var i = 0; i < 100; i++)
            items.Add(O($@"{{ name: 'item-{i}' }}"));
        var db = O("{}");
        db["fields"] = new JObject { ["items"] = items };

        var config = MakeConfig(defaults, templates);
        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        var resultItems = result["fields"]!["items"] as JArray;
        resultItems.Should().NotBeNull();
        resultItems!.Count.Should().Be(100);
        for (var i = 0; i < 100; i++)
        {
            resultItems[i]!["name"]!.Value<string>().Should().Be($"item-{i}");
            resultItems[i]!["flag"]!.Value<bool>().Should().BeFalse();
        }
    }

    // ── Test 17: Triple-nested Repeater (3 levels deep) ─────────────

    [Fact]
    public void TripleNestedRepeater_DefaultsAtAllLevels()
    {
        var l1Tmpl = O(@"{ b: [] }");
        var l2Tmpl = O(@"{ x: '', c: [] }");
        var l3Tmpl = O(@"{ y: '' }");
        var templates = new Dictionary<string, JObject>
        {
            ["fields.a"] = l1Tmpl,
            ["fields.a.b"] = l2Tmpl,
            ["fields.a.b.c"] = l3Tmpl
        };
        var defaults = O(@"{ fields: { a: [] } }");
        var db = O(@"{ fields: { a: [{ b: [{ c: [{ y: 'ok' }] }] }] } }");
        var config = MakeConfig(defaults, templates);

        var result = EntityDefaultsMerger.MergeDefaults(db, config);

        // Level 3: y preserved, y in l3 template
        var cArr = result["fields"]!["a"]![0]!["b"]![0]!["c"] as JArray;
        cArr.Should().NotBeNull();
        cArr![0]!["y"]!.Value<string>().Should().Be("ok");

        // Level 2: x should be injected
        result["fields"]!["a"]![0]!["b"]![0]!["x"]!.Value<string>().Should().Be("");
    }

    // ── Integration test: prove BuildRepeaterTemplateMap works ──────

    /// <summary>
    /// Test-only entity model with nested Repeaters to verify template map building.
    /// </summary>
    private class TestEntityFields : EntityFieldsModel
    {
        [JsonProperty("items")]
        [Repeater("Items", "", typeof(TestRepeaterItem), "Add Item")]
        public List<TestRepeaterItem> Items = [];
    }

    private class TestRepeaterItem : BaseModel
    {
        [JsonProperty("name")]
        public string Name = "";

        [JsonProperty("nested")]
        [Repeater("Nested", "", typeof(TestNestedItem), "Add Nested")]
        public List<TestNestedItem> Nested = [];
    }

    private class TestNestedItem : BaseModel
    {
        [JsonProperty("value")]
        public string Value = "";
    }

    [Fact]
    public void BuildRepeaterTemplateMap_WithNestedRepeaterTypes()
    {
        var map = EntityDefaultsMerger.BuildRepeaterTemplateMap(typeof(TestEntityFields));

        map.Should().NotBeNull();
        map.Should().ContainKey("fields.items");
        map.Should().ContainKey("fields.items.nested");

        // Outer template should have its fields
        map["fields.items"]["name"].Should().NotBeNull();

        // Nested template should have its fields
        map["fields.items.nested"]["value"].Should().NotBeNull();
    }
}

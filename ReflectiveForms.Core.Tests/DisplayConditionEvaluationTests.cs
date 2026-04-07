using FluentAssertions;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Operation;
using Xunit;

namespace ReflectiveForms.Core.Tests;

public class DisplayConditionEvaluationTests
{
    private static EntityOperationState CreateState(JObject fieldsObj)
    {
        return EntityOperationState.CreateStateForSanityCheck(fieldsObj);
    }

    [Fact]
    public void UnquotedStringCondition_HidesField_WhenNotMatched()
    {
        // "question_type == rating" with unquoted string — question_type is "text"
        var fields = JObject.Parse("""{ "question_type": "text", "min_rating": 1 }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("min_rating", "question_type == rating");

        state.TestVisibilityForSanityCheck("min_rating").Should().BeFalse();
    }

    [Fact]
    public void UnquotedStringCondition_ShowsField_WhenMatched()
    {
        // question_type IS "rating" — field should be visible
        var fields = JObject.Parse("""{ "question_type": "rating", "min_rating": 1 }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("min_rating", "question_type == rating");

        state.TestVisibilityForSanityCheck("min_rating").Should().BeTrue();
    }

    [Fact]
    public void QuotedStringCondition_HidesField_WhenNotMatched()
    {
        var fields = JObject.Parse("""{ "question_type": "text", "min_rating": 1 }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("min_rating", "question_type == 'rating'");

        state.TestVisibilityForSanityCheck("min_rating").Should().BeFalse();
    }

    [Fact]
    public void QuotedStringCondition_ShowsField_WhenMatched()
    {
        var fields = JObject.Parse("""{ "question_type": "rating", "min_rating": 1 }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("min_rating", "question_type == 'rating'");

        state.TestVisibilityForSanityCheck("min_rating").Should().BeTrue();
    }

    [Fact]
    public void BooleanCondition_HidesField_WhenFalse()
    {
        var fields = JObject.Parse("""{ "is_required": false, "help_text": "" }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("help_text", "is_required == true");

        state.TestVisibilityForSanityCheck("help_text").Should().BeFalse();
    }

    [Fact]
    public void BooleanCondition_ShowsField_WhenTrue()
    {
        var fields = JObject.Parse("""{ "is_required": true, "help_text": "" }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("help_text", "is_required == true");

        state.TestVisibilityForSanityCheck("help_text").Should().BeTrue();
    }

    [Fact]
    public void NotEqualsCondition_Works()
    {
        var fields = JObject.Parse("""{ "status": "draft", "published_date": "" }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("published_date", "status != draft");

        state.TestVisibilityForSanityCheck("published_date").Should().BeFalse();
    }

    [Fact]
    public void NestedField_DisplayCondition_WorksInsideGroup()
    {
        var fields = JObject.Parse("""{ "address": { "is_domestic": true, "city": "" } }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("address.city", "is_domestic == true");

        state.TestVisibilityForSanityCheck("address.city").Should().BeTrue();
    }

    [Fact]
    public void NestedField_DisplayCondition_HiddenInsideGroup()
    {
        var fields = JObject.Parse("""{ "address": { "is_domestic": false, "city": "" } }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("address.city", "is_domestic == true");

        state.TestVisibilityForSanityCheck("address.city").Should().BeFalse();
    }

    [Fact]
    public void CompoundAndCondition_Works()
    {
        var fields = JObject.Parse("""{ "has_scoring": true, "scoring_mode": "weighted", "weights": "" }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("weights", "has_scoring == true && scoring_mode == weighted");

        state.TestVisibilityForSanityCheck("weights").Should().BeTrue();
    }

    [Fact]
    public void CompoundAndCondition_HidesWhenOneFails()
    {
        var fields = JObject.Parse("""{ "has_scoring": false, "scoring_mode": "weighted", "weights": "" }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("weights", "has_scoring == true && scoring_mode == weighted");

        state.TestVisibilityForSanityCheck("weights").Should().BeFalse();
    }

    [Fact]
    public void CompoundOrCondition_ShowsWhenOneMatches()
    {
        var fields = JObject.Parse("""{ "type": "a", "mode": "x", "extra": "" }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("extra", "type == b || mode == x");

        state.TestVisibilityForSanityCheck("extra").Should().BeTrue();
    }

    [Fact]
    public void CompoundOrCondition_HidesWhenNoneMatch()
    {
        var fields = JObject.Parse("""{ "type": "a", "mode": "y", "extra": "" }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("extra", "type == b || mode == x");

        state.TestVisibilityForSanityCheck("extra").Should().BeFalse();
    }

    [Fact]
    public void ParentHidden_ChildAlsoHidden()
    {
        var fields = JObject.Parse("""{ "show_section": false, "section": { "detail": "" } }""");
        var state = CreateState(fields);

        // Parent condition hides section
        state.FeedConditionForSanityCheck("section", "show_section == true");
        state.TestVisibilityForSanityCheck("section").Should().BeFalse();

        // Child inside hidden parent should also be hidden
        state.TestVisibilityForSanityCheck("section.detail").Should().BeFalse();
    }

    [Fact]
    public void MissingField_DefaultsToHidden_ForStringCondition()
    {
        // Field referenced in condition doesn't exist → evaluated as empty/false → condition not met
        var fields = JObject.Parse("""{ "dependent_field": "" }""");
        var state = CreateState(fields);

        state.FeedConditionForSanityCheck("dependent_field", "nonexistent == some_value");

        state.TestVisibilityForSanityCheck("dependent_field").Should().BeFalse();
    }
}

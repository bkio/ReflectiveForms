// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using ReflectiveForms.Core.Schema.Models;

namespace ReflectiveForms.Core.Ai;

internal enum FieldPriority
{
    /// <summary>Title and fields that other fields depend on via display conditions.</summary>
    Critical,
    /// <summary>Select, Checkbox, Number, DatePicker, short Text — structured values.</summary>
    Structural,
    /// <summary>WysiwygEditor, TextArea — long-form content fields.</summary>
    Content,
    /// <summary>Fields computable from other fields: slug, excerpt, reading_time, SEO group fields.</summary>
    Derived,
    /// <summary>Fields with display conditions that haven't been resolved yet.</summary>
    Conditional
}

internal record PlanEntry(
    string FieldName,
    FieldSchema Schema,
    FieldPriority Priority,
    string? DependsOn,
    bool IsDerived)
{
    internal bool IsConditional => !string.IsNullOrEmpty(Schema.DisplayCondition);
}

internal record GenerationPlan(List<PlanEntry> Entries)
{
    /// <summary>Returns entries for a given priority, excluding already-generated fields.</summary>
    internal IEnumerable<PlanEntry> GetByPriority(FieldPriority priority)
        => Entries.Where(e => e.Priority == priority);

    /// <summary>Returns conditional entries whose dependency field has been set to the expected value.</summary>
    internal IEnumerable<PlanEntry> GetResolvedConditionals(Newtonsoft.Json.Linq.JObject currentDraft)
        => Entries.Where(e => e.Priority == FieldPriority.Conditional
            && AiEntityGeneratorValidator.ExtractConditionDependency(e.Schema.DisplayCondition!) != null
            && IsConditionMet(e.Schema.DisplayCondition!, currentDraft));

    private static bool IsConditionMet(string condition, Newtonsoft.Json.Linq.JObject values)
        => AiEntityGeneratorValidator.IsDisplayConditionMet(condition, values);
}

/// <summary>
/// Deterministic pre-planning for entity generation.
/// Analyzes the field schema to produce a topologically sorted generation plan
/// that respects display-condition dependencies, marks derived fields, and
/// categorizes fields by generation strategy (batch structured vs. individual content).
/// </summary>
internal static class GenerationPlanner
{
    private static readonly HashSet<string> SlugPatterns = new(StringComparer.OrdinalIgnoreCase)
        { "slug", "url_slug", "url-slug" };

    private static readonly HashSet<string> ExcerptPatterns = new(StringComparer.OrdinalIgnoreCase)
        { "excerpt", "summary" };

    private static readonly HashSet<string> ReadingTimePatterns = new(StringComparer.OrdinalIgnoreCase)
        { "reading_time", "reading_time_minutes", "read_time" };

    internal static GenerationPlan BuildPlan(List<FieldSchema> fields)
    {
        // First pass: collect all dependency field names (fields that other fields depend on)
        var dependencyTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (!string.IsNullOrEmpty(field.DisplayCondition))
            {
                var dep = AiEntityGeneratorValidator.ExtractConditionDependency(field.DisplayCondition);
                if (dep != null) dependencyTargets.Add(dep);
            }
        }

        var entries = new List<PlanEntry>();

        foreach (var field in fields)
        {
            // Skip non-generatable types
            if (field.Type is FieldSchemaType.Relation or FieldSchemaType.MediaSourceBase64)
                continue;
            if (field.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
                continue;

            var dependsOn = !string.IsNullOrEmpty(field.DisplayCondition)
                ? AiEntityGeneratorValidator.ExtractConditionDependency(field.DisplayCondition)
                : null;

            var isDerived = IsDerivedField(field);
            var priority = ClassifyPriority(field, dependencyTargets, isDerived);

            entries.Add(new PlanEntry(field.Name, field, priority, dependsOn, isDerived));
        }

        // Topological sort: Critical first, then Structural, then Conditional (deferred),
        // then Content, then Derived last.
        entries.Sort((a, b) =>
        {
            var cmp = a.Priority.CompareTo(b.Priority);
            if (cmp != 0) return cmp;
            // Within same priority, put fields that others depend on first
            var aIsDep = dependencyTargets.Contains(a.FieldName);
            var bIsDep = dependencyTargets.Contains(b.FieldName);
            if (aIsDep && !bIsDep) return -1;
            if (!aIsDep && bIsDep) return 1;
            return 0;
        });

        return new GenerationPlan(entries);
    }

    private static FieldPriority ClassifyPriority(
        FieldSchema field, HashSet<string> dependencyTargets, bool isDerived)
    {
        if (isDerived)
            return FieldPriority.Derived;

        // Fields with unresolved display conditions are Conditional
        if (!string.IsNullOrEmpty(field.DisplayCondition))
            return FieldPriority.Conditional;

        // Fields that other fields depend on are Critical
        if (dependencyTargets.Contains(field.Name))
            return FieldPriority.Critical;

        // Content fields
        if (field.Type is FieldSchemaType.WysiwygEditor or FieldSchemaType.TextArea)
            return FieldPriority.Content;

        // Groups and repeaters are structural (their children are handled recursively)
        if (field.Type is FieldSchemaType.Group or FieldSchemaType.Repeater)
            return FieldPriority.Structural;

        // Everything else (Select, Checkbox, Number, DatePicker, Text, Email, Url) is structural
        return FieldPriority.Structural;
    }

    private static bool IsDerivedField(FieldSchema field)
    {
        var name = field.Name.ToLowerInvariant();

        // Slug fields — derived from title
        if (field.Type == FieldSchemaType.Text && (SlugPatterns.Contains(name) || name.Contains("slug")))
            return true;

        // Excerpt fields — derived from content
        if (field.Type == FieldSchemaType.TextArea && ExcerptPatterns.Any(p => name.Contains(p)))
            return true;

        // Reading time — derived from content word count
        if (field.Type is FieldSchemaType.Number or FieldSchemaType.Range
            && ReadingTimePatterns.Any(p => name.Contains(p)))
            return true;

        return false;
    }

    /// <summary>
    /// Builds a compact schema description for a batch prompt, listing fields with types and constraints.
    /// </summary>
    internal static string BuildBatchSchemaPrompt(
        IEnumerable<PlanEntry> entries,
        Dictionary<string, List<SelectChoice>>? resolvedDynamicChoices = null)
    {
        var lines = new List<string>();
        foreach (var entry in entries)
        {
            var f = entry.Schema;
            var desc = $"- \"{f.Name}\"";

            // Check for resolved dynamic choices first, then static choices
            List<SelectChoice>? effectiveChoices = null;
            if (f.Type == FieldSchemaType.Select)
            {
                if (resolvedDynamicChoices != null &&
                    resolvedDynamicChoices.TryGetValue(f.Name, out var dynChoices))
                    effectiveChoices = dynChoices;
                else if (f.SelectOptions?.Choices is { Count: > 0 })
                    effectiveChoices = f.SelectOptions.Choices;
            }

            switch (f.Type)
            {
                case FieldSchemaType.Select when effectiveChoices is { Count: > 0 }:
                    var choices = string.Join(", ", effectiveChoices.Select(c => $"\"{c.Value}\""));
                    desc += f.SelectOptions?.AllowMultiple == true
                        ? $" (multi-select, choices: [{choices}])"
                        : $" (pick one: [{choices}])";
                    break;
                case FieldSchemaType.Select:
                    desc += " (select, free text — no predefined choices available)";
                    break;
                case FieldSchemaType.Checkbox:
                    desc += " (boolean)";
                    break;
                case FieldSchemaType.Number or FieldSchemaType.Range:
                    desc += " (number";
                    if (f.NumberOptions?.Min != null) desc += $", min: {f.NumberOptions.Min}";
                    if (f.NumberOptions?.Max != null) desc += $", max: {f.NumberOptions.Max}";
                    desc += ")";
                    break;
                case FieldSchemaType.DatePicker:
                    desc += $" (date, format: {f.DateOptions?.Format ?? "yyyy-MM-dd"})";
                    break;
                case FieldSchemaType.Text:
                    desc += " (short text)";
                    break;
                case FieldSchemaType.Email:
                    desc += " (email)";
                    break;
                case FieldSchemaType.Url:
                    desc += " (URL)";
                    break;
                default:
                    desc += $" ({f.Type})";
                    break;
            }

            // Add label for context
            desc += $": {f.Label}";

            // Truncated instructions
            if (!string.IsNullOrEmpty(f.Instructions))
            {
                var instr = System.Text.RegularExpressions.Regex.Replace(f.Instructions, @"<[^>]+>", " ").Trim();
                if (instr.Length > 50) instr = instr[..50] + "...";
                desc += $" — {instr}";
            }

            lines.Add(desc);
        }
        return string.Join("\n", lines);
    }
}

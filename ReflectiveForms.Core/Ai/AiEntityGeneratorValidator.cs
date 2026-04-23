// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Schema.Models;

namespace ReflectiveForms.Core.Ai;

internal enum FieldValidationErrorType
{
    InvalidChoice,
    OutOfRange,
    WrongType,
    MissingRequired,
    ContentTooShort,
    InvalidFormat,
    MissingConditional
}

internal record FieldValidationError(
    string FieldName,
    string FieldPath,
    FieldValidationErrorType Type,
    string Message,
    JToken? SuggestedFix = null)
{
    internal bool IsAutoFixable => SuggestedFix != null;
}

/// <summary>
/// Schema-aware validation for AI-generated entity drafts.
/// Validates field values against their schema constraints and returns errors
/// with auto-fix suggestions where possible.
/// </summary>
internal static class AiEntityGeneratorValidator
{
    internal static List<FieldValidationError> ValidateDraft(
        JObject draft, List<FieldSchema> fields, JObject? rootDraft = null)
    {
        rootDraft ??= draft;
        var errors = new List<FieldValidationError>();

        foreach (var field in fields)
        {
            // Skip non-generatable fields
            if (field.Type is FieldSchemaType.Relation or FieldSchemaType.MediaSourceBase64)
                continue;
            if (field.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
                continue;

            var fieldPath = field.Name;

            // Check display conditions — if condition is not met, field should be absent
            if (!string.IsNullOrEmpty(field.DisplayCondition))
            {
                if (!IsDisplayConditionMet(field.DisplayCondition, rootDraft))
                    continue; // Condition not met → field correctly absent
            }

            // Group → recurse
            if (field.Type == FieldSchemaType.Group && field.GroupOptions?.ChildSchema != null)
            {
                if (draft[field.Name] is JObject groupObj)
                {
                    var groupErrors = ValidateDraft(groupObj, field.GroupOptions.ChildSchema, rootDraft);
                    errors.AddRange(groupErrors.Select(e => e with
                    {
                        FieldPath = $"{fieldPath}.{e.FieldPath}"
                    }));
                }
                continue;
            }

            // Repeater → recurse per item
            if (field.Type == FieldSchemaType.Repeater && field.RepeaterOptions?.ItemSchema != null)
            {
                if (draft[field.Name] is JArray arr)
                {
                    for (var i = 0; i < arr.Count; i++)
                    {
                        if (arr[i] is JObject itemObj)
                        {
                            var itemErrors = ValidateDraft(itemObj, field.RepeaterOptions.ItemSchema, rootDraft);
                            errors.AddRange(itemErrors.Select(e => e with
                            {
                                FieldPath = $"{fieldPath}[{i}].{e.FieldPath}"
                            }));
                        }
                    }
                }
                continue;
            }

            var token = draft[field.Name];

            // Check required fields that are missing
            if (field.Required && (token == null || token.Type == JTokenType.Null))
            {
                var fix = GetDefaultFix(field);
                errors.Add(new FieldValidationError(field.Name, fieldPath,
                    fix != null ? FieldValidationErrorType.MissingRequired : FieldValidationErrorType.MissingRequired,
                    $"Required field '{field.Label}' is missing.",
                    fix));
                continue;
            }

            // Check conditional fields that should be present but aren't
            if (!string.IsNullOrEmpty(field.DisplayCondition)
                && IsDisplayConditionMet(field.DisplayCondition, rootDraft)
                && (token == null || token.Type == JTokenType.Null))
            {
                // Only flag as error if the field is required or would benefit from a value
                if (field.Required)
                {
                    errors.Add(new FieldValidationError(field.Name, fieldPath,
                        FieldValidationErrorType.MissingConditional,
                        $"Conditional field '{field.Label}' should have a value (condition '{field.DisplayCondition}' is met)."));
                }
                continue;
            }

            if (token == null || token.Type == JTokenType.Null)
                continue;

            // Type-specific validation
            switch (field.Type)
            {
                case FieldSchemaType.Select:
                    ValidateSelectField(field, token, fieldPath, errors);
                    break;
                case FieldSchemaType.Number or FieldSchemaType.Range:
                    ValidateNumberField(field, token, fieldPath, errors);
                    break;
                case FieldSchemaType.DatePicker:
                    ValidateDateField(field, token, fieldPath, errors);
                    break;
                case FieldSchemaType.Checkbox:
                    ValidateCheckboxField(field, token, fieldPath, errors);
                    break;
                case FieldSchemaType.WysiwygEditor or FieldSchemaType.TextArea:
                    ValidateContentField(field, token, fieldPath, errors);
                    break;
            }
        }

        return errors;
    }

    /// <summary>
    /// Applies all auto-fixable errors to the draft and returns the remaining unfixable errors.
    /// </summary>
    internal static List<FieldValidationError> ApplyAutoFixes(
        JObject draft, List<FieldSchema> fields, List<FieldValidationError> errors)
    {
        var remaining = new List<FieldValidationError>();

        foreach (var error in errors)
        {
            if (error.IsAutoFixable)
            {
                SetNestedValue(draft, error.FieldPath, error.SuggestedFix!);
            }
            else
            {
                remaining.Add(error);
            }
        }

        return remaining;
    }

    // ════════════════════════════════════════════════════════════════
    // Field-specific validators
    // ════════════════════════════════════════════════════════════════

    private static void ValidateSelectField(
        FieldSchema field, JToken token, string fieldPath, List<FieldValidationError> errors)
    {
        if (field.SelectOptions?.Choices is not { Count: > 0 })
            return;

        // Multi-select
        if (field.SelectOptions.AllowMultiple && token is JArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                var val = arr[i].Value<string>();
                if (val == null) continue;
                if (!field.SelectOptions.Choices.Any(c => c.Value.Equals(val, StringComparison.OrdinalIgnoreCase)))
                {
                    var closest = FuzzyMatchChoice(val, field.SelectOptions.Choices);
                    errors.Add(new FieldValidationError(field.Name, $"{fieldPath}[{i}]",
                        FieldValidationErrorType.InvalidChoice,
                        $"Value '{val}' is not a valid choice for '{field.Label}'.",
                        closest != null ? JToken.FromObject(closest) : null));
                }
            }
            return;
        }

        // Single select
        var value = token.Value<string>();
        if (string.IsNullOrEmpty(value)) return;

        if (!field.SelectOptions.Choices.Any(c => c.Value.Equals(value, StringComparison.OrdinalIgnoreCase)))
        {
            var closest = FuzzyMatchChoice(value, field.SelectOptions.Choices);
            errors.Add(new FieldValidationError(field.Name, fieldPath,
                FieldValidationErrorType.InvalidChoice,
                $"Value '{value}' is not a valid choice for '{field.Label}'. Valid: [{string.Join(", ", field.SelectOptions.Choices.Select(c => c.Value))}]",
                closest != null ? JToken.FromObject(closest) : JToken.FromObject(field.SelectOptions.Choices[0].Value)));
        }
    }

    private static void ValidateNumberField(
        FieldSchema field, JToken token, string fieldPath, List<FieldValidationError> errors)
    {
        if (token.Type is not (JTokenType.Integer or JTokenType.Float))
        {
            // Try parsing string as number
            if (token.Type == JTokenType.String && double.TryParse(token.Value<string>(),
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                errors.Add(new FieldValidationError(field.Name, fieldPath,
                    FieldValidationErrorType.WrongType,
                    $"Field '{field.Label}' should be a number, got string.",
                    JToken.FromObject(ClampToRange(parsed, field))));
                return;
            }

            errors.Add(new FieldValidationError(field.Name, fieldPath,
                FieldValidationErrorType.WrongType,
                $"Field '{field.Label}' should be a number."));
            return;
        }

        var num = token.Value<double>();
        var clamped = ClampToRange(num, field);
        if (Math.Abs(clamped - num) > 0.0001)
        {
            errors.Add(new FieldValidationError(field.Name, fieldPath,
                FieldValidationErrorType.OutOfRange,
                $"Value {num} for '{field.Label}' is out of range [{field.NumberOptions?.Min}..{field.NumberOptions?.Max}].",
                JToken.FromObject(clamped)));
        }
    }

    private static void ValidateDateField(
        FieldSchema field, JToken token, string fieldPath, List<FieldValidationError> errors)
    {
        var value = token.Value<string>();
        if (string.IsNullOrEmpty(value)) return;

        var targetFmt = field.DateOptions?.Format ?? "yyyy-MM-dd";

        // Try parsing with the target format
        if (DateTime.TryParseExact(value, targetFmt,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _))
            return; // Valid

        // Try common formats and reformat
        string[] commonFormats = ["yyyy-MM-dd", "yyyyMMdd", "MM/dd/yyyy", "dd/MM/yyyy", "yyyy-MM-ddTHH:mm:ss"];
        foreach (var fmt in commonFormats)
        {
            if (DateTime.TryParseExact(value, fmt,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
            {
                try
                {
                    errors.Add(new FieldValidationError(field.Name, fieldPath,
                        FieldValidationErrorType.InvalidFormat,
                        $"Date '{value}' for '{field.Label}' is not in expected format '{targetFmt}'.",
                        JToken.FromObject(parsed.ToString(targetFmt))));
                    return;
                }
                catch { /* format conversion failed */ }
            }
        }

        // Try general parse
        if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var generalParsed))
        {
            try
            {
                errors.Add(new FieldValidationError(field.Name, fieldPath,
                    FieldValidationErrorType.InvalidFormat,
                    $"Date '{value}' for '{field.Label}' is not in expected format '{targetFmt}'.",
                    JToken.FromObject(generalParsed.ToString(targetFmt))));
                return;
            }
            catch { /* format conversion failed */ }
        }

        // Completely unparseable — suggest today
        var today = DateTime.UtcNow;
        try
        {
            errors.Add(new FieldValidationError(field.Name, fieldPath,
                FieldValidationErrorType.InvalidFormat,
                $"Date '{value}' for '{field.Label}' could not be parsed.",
                JToken.FromObject(today.ToString(targetFmt))));
        }
        catch
        {
            errors.Add(new FieldValidationError(field.Name, fieldPath,
                FieldValidationErrorType.InvalidFormat,
                $"Date '{value}' for '{field.Label}' could not be parsed."));
        }
    }

    private static void ValidateCheckboxField(
        FieldSchema field, JToken token, string fieldPath, List<FieldValidationError> errors)
    {
        if (token.Type == JTokenType.Boolean)
            return;

        // Try coercing string to boolean
        if (token.Type == JTokenType.String)
        {
            var str = token.Value<string>()?.ToLowerInvariant().Trim();
            if (str is "true" or "yes" or "1")
            {
                errors.Add(new FieldValidationError(field.Name, fieldPath,
                    FieldValidationErrorType.WrongType,
                    $"Field '{field.Label}' should be a boolean, got string.",
                    JToken.FromObject(true)));
                return;
            }
            if (str is "false" or "no" or "0")
            {
                errors.Add(new FieldValidationError(field.Name, fieldPath,
                    FieldValidationErrorType.WrongType,
                    $"Field '{field.Label}' should be a boolean, got string.",
                    JToken.FromObject(false)));
                return;
            }
        }

        // Default to field default or false
        var defaultVal = field.DefaultValue is bool dv ? dv : false;
        errors.Add(new FieldValidationError(field.Name, fieldPath,
            FieldValidationErrorType.WrongType,
            $"Field '{field.Label}' should be a boolean.",
            JToken.FromObject(defaultVal)));
    }

    private static void ValidateContentField(
        FieldSchema field, JToken token, string fieldPath, List<FieldValidationError> errors)
    {
        if (token.Type != JTokenType.String)
            return;

        var value = token.Value<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            if (field.Required)
            {
                errors.Add(new FieldValidationError(field.Name, fieldPath,
                    FieldValidationErrorType.ContentTooShort,
                    $"Content field '{field.Label}' is empty."));
            }
            return;
        }

        // Content should have reasonable length (at least 50 chars for non-excerpt text fields)
        if (field.Required && value.Length < 50
            && !field.Name.Contains("excerpt", StringComparison.OrdinalIgnoreCase)
            && !field.Name.Contains("description", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new FieldValidationError(field.Name, fieldPath,
                FieldValidationErrorType.ContentTooShort,
                $"Content field '{field.Label}' is too short ({value.Length} chars, expected at least 50)."));
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════

    private static string? FuzzyMatchChoice(string value, List<SelectChoice> choices)
    {
        // Try case-insensitive exact match
        var exact = choices.FirstOrDefault(c =>
            c.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact.Value;

        // Try contains
        var contains = choices.FirstOrDefault(c =>
            value.Contains(c.Value, StringComparison.OrdinalIgnoreCase));
        if (contains != null) return contains.Value;

        // Try label match
        var labelMatch = choices.FirstOrDefault(c =>
            c.Label != null && value.Contains(c.Label, StringComparison.OrdinalIgnoreCase));
        if (labelMatch != null) return labelMatch.Value;

        // Normalize separators (hyphens, underscores, spaces)
        var normalized = value.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant();
        var fuzzy = choices.FirstOrDefault(c =>
            normalized.Contains(c.Value.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant())
            || (c.Label != null && normalized.Contains(c.Label.Replace('_', ' ').Replace('-', ' ').ToLowerInvariant())));
        if (fuzzy != null) return fuzzy.Value;

        return null;
    }

    private static double ClampToRange(double value, FieldSchema field)
    {
        var min = field.NumberOptions?.Min;
        var max = field.NumberOptions?.Max;
        if (min.HasValue && value < min.Value) return min.Value;
        if (max.HasValue && value > max.Value) return max.Value;
        return value;
    }

    private static JToken? GetDefaultFix(FieldSchema field)
    {
        return field.Type switch
        {
            FieldSchemaType.Checkbox => JToken.FromObject(field.DefaultValue is bool dv ? dv : false),
            FieldSchemaType.Select when field.SelectOptions?.Choices is { Count: > 0 } =>
                !string.IsNullOrEmpty(field.DefaultValue?.ToString())
                    ? JToken.FromObject(field.DefaultValue.ToString()!)
                    : JToken.FromObject(field.SelectOptions.Choices[0].Value),
            FieldSchemaType.DatePicker => JToken.FromObject(FormatToday(field)),
            FieldSchemaType.Number or FieldSchemaType.Range =>
                field.DefaultValue is double dNum ? JToken.FromObject(dNum)
                : field.NumberOptions?.Min != null ? JToken.FromObject(field.NumberOptions.Min.Value)
                : JToken.FromObject(0),
            _ => null
        };
    }

    private static string FormatToday(FieldSchema field)
    {
        var fmt = field.DateOptions?.Format;
        if (!string.IsNullOrEmpty(fmt))
        {
            try { return DateTime.UtcNow.ToString(fmt); }
            catch { /* invalid format */ }
        }
        return DateTime.UtcNow.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// Evaluates simple display conditions like "fieldName == 'value'" against the draft.
    /// Supports single-quoted, double-quoted, and unquoted values.
    /// Returns true if the condition is met or cannot be parsed.
    /// </summary>
    internal static bool IsDisplayConditionMet(string condition, JObject currentValues)
    {
        var match = Regex.Match(condition, @"^(\w+)\s*(==|!=)\s*(?:'([^']*)'|""([^""]*)""|(\S+))$");
        if (!match.Success)
            return true; // Can't parse → assume met

        var fieldName = match.Groups[1].Value;
        var op = match.Groups[2].Value;
        var expected = match.Groups[3].Success ? match.Groups[3].Value
            : match.Groups[4].Success ? match.Groups[4].Value
            : match.Groups[5].Value;

        var actual = currentValues[fieldName]?.ToString();
        if (actual == null)
            return op != "==";

        var equals = actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
        return op == "==" ? equals : !equals;
    }

    /// <summary>
    /// Extracts the dependency field name from a display condition string.
    /// E.g., "status == 'scheduled'" → "status".
    /// Returns null if the condition cannot be parsed.
    /// </summary>
    internal static string? ExtractConditionDependency(string condition)
    {
        var match = Regex.Match(condition, @"^(\w+)\s*(==|!=|>=?|<=?)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void SetNestedValue(JObject root, string path, JToken value)
    {
        // Handle simple paths (no dots, no brackets)
        if (!path.Contains('.') && !path.Contains('['))
        {
            root[path] = value;
            return;
        }

        // Handle dotted paths like "seo_metadata.meta_title" and array paths like "sections[0].question_text"
        var segments = path.Split('.');
        JToken current = root;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var seg = segments[i];
            var bracketIdx = seg.IndexOf('[');
            if (bracketIdx >= 0)
            {
                var propName = seg[..bracketIdx];
                var indexStr = seg[(bracketIdx + 1)..seg.IndexOf(']')];
                if (int.TryParse(indexStr, out var idx))
                {
                    var arr = current[propName] as JArray;
                    if (arr != null && idx < arr.Count)
                        current = arr[idx];
                    else
                        return; // Can't navigate further
                }
                else return;
            }
            else
            {
                var next = current[seg];
                if (next == null)
                {
                    // Create intermediate object
                    var newObj = new JObject();
                    ((JObject)current)[seg] = newObj;
                    current = newObj;
                }
                else
                {
                    current = next;
                }
            }
        }

        var lastSeg = segments[^1];
        if (current is JObject lastObj)
            lastObj[lastSeg] = value;
    }

    /// <summary>
    /// Attempts to resolve dynamic runtime choices for a Select field by evaluating the
    /// DynamicChoicesJsFunction in Jint with the current draft as input.
    /// Returns the resolved choices, or null if evaluation fails or no JS function is defined.
    /// </summary>
    internal static List<SelectChoice>? ResolveDynamicRuntimeChoices(
        FieldSchema field, JObject draft)
    {
        if (!field.HasDynamicChoicesRuntime)
            return null;

        var jsFunction = field.SelectOptions?.DynamicChoicesJsFunction;
        if (string.IsNullOrWhiteSpace(jsFunction))
            return null;

        try
        {
            var engine = new Jint.Engine();

            // Set up the browser-like environment the JS expects
            var windowObj = new JObject
            {
                ["latest_dynamic_options_input"] = draft.DeepClone()
            };
            engine.Execute($"var window = {windowObj.ToString(Newtonsoft.Json.Formatting.None)};");

            // Wrap the function body in a self-executing function
            var wrappedJs = $"(function() {{ {jsFunction} }})()";
            var result = engine.Evaluate(wrappedJs);

            if (result is not Jint.Native.JsArray jsArray)
                return null;

            var choices = new List<SelectChoice>();
            foreach (var item in jsArray)
            {
                var str = item.ToString();
                if (string.IsNullOrWhiteSpace(str)) continue;

                // Parse "value : label" format
                var colonIdx = str.IndexOf(':');
                if (colonIdx > 0)
                {
                    var value = str[..colonIdx].Trim();
                    var label = str[(colonIdx + 1)..].Trim();
                    if (!string.IsNullOrEmpty(value))
                        choices.Add(new SelectChoice { Value = value, Label = label });
                }
                else
                {
                    choices.Add(new SelectChoice { Value = str.Trim(), Label = str.Trim() });
                }
            }

            return choices.Count > 0 ? choices : null;
        }
        catch (Exception ex)
        {
            RfConfiguration.LogError(ex);
            return null;
        }
    }
}

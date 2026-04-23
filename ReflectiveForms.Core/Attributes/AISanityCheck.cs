// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

namespace ReflectiveForms.Core.Attributes;

/// <summary>
/// Marks a field for AI-powered sanity checking.
/// Multiple checks can be applied to the same field.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
public sealed class AISanityCheck : Attribute
{
    /// <summary>
    /// The check expressed as a question the LLM answers with pass/fail.
    /// Example: "Is this description professional and free of spelling errors?"
    /// </summary>
    public string CheckPrompt { get; }

    /// <summary>
    /// Whether a failure blocks the save (Error) or just warns (Warning).
    /// Default: Warning.
    /// </summary>
    public AISanityCheckSeverity Severity { get; }

    public AISanityCheck(string checkPrompt, AISanityCheckSeverity severity = AISanityCheckSeverity.Warning)
    {
        CheckPrompt = checkPrompt;
        Severity = severity;
    }
}

public enum AISanityCheckSeverity
{
    Warning,
    Error
}

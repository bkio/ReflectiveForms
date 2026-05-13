// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Interfaces;

namespace ReflectiveForms.Core.Ai;

/// <summary>
/// Optional AI service configuration. When null on <see cref="RfConfigurationBuilder"/>,
/// all AI features are disabled and the system behaves identically to a non-AI setup.
/// </summary>
public record AiServiceConfiguration(
    ILLMService HeavyLlmService,
    ILLMService LightLlmService,
    IVectorService VectorService,
    ILLMService? EmbeddingLlmService = null)
{
    // HeavyLlmService: used for complex, creative tasks (entity generation, revision diff summaries).
    // Example: GPT-4o, Gemma3 12B, Claude Sonnet.

    // LightLlmService: used for fast, cheap, structured tasks (field suggestions, AI sanity checks,
    // NL filter parsing, embeddings). Example: GPT-4o-mini, Gemma3 4B, Phi-3.

    /// <summary>
    /// System prompt prefix prepended to all AI requests.
    /// Allows consumers to customize the AI's persona.
    /// </summary>
    public string SystemPromptPrefix { get; init; } =
        "You are an assistant for a schema-driven content management system. " +
        "Entities have typed fields (text, select, date, checkbox, number, repeater, group) with validation rules.";

    /// <summary>
    /// Maximum tokens for completion requests on the heavy model.
    /// </summary>
    public int MaxCompletionTokens { get; init; } = 1024;

    /// <summary>
    /// Maximum tokens for completion requests on the light model.
    /// </summary>
    public int MaxLightCompletionTokens { get; init; } = 512;

    /// <summary>
    /// Temperature for heavy completion requests (focused/deterministic).
    /// </summary>
    public double Temperature { get; init; } = 0.3;

    /// <summary>
    /// Temperature for light completion requests (very deterministic for structured tasks).
    /// </summary>
    public double LightTemperature { get; init; } = 0.1;

    /// <summary>
    /// Interval for the periodic vector-DB synchronization check.
    /// A background timer fires at this interval; one instance acquires a distributed lock,
    /// checks a persistent DB timestamp, and runs an incremental sync if due.
    /// </summary>
    public TimeSpan SyncInterval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Controls the entity generation strategy used by the AI entity generator.
    /// </summary>
    public AiGenerationStrategy GenerationStrategy { get; init; } = AiGenerationStrategy.Auto;
}

/// <summary>
/// Controls how the AI entity generator produces draft entities.
/// </summary>
public enum AiGenerationStrategy
{
    /// <summary>
    /// Automatically selects the best strategy based on the LLM capabilities.
    /// Uses Agentic if the heavy model supports tool calling, otherwise BatchJson.
    /// </summary>
    Auto,

    /// <summary>
    /// Agentic multi-turn approach: the LLM decides which fields to fill via tool calls
    /// (get_schema, set_fields, get_draft, get_examples). Best with capable models (GPT-4o, Claude).
    /// </summary>
    Agentic,

    /// <summary>
    /// Plan-based batch JSON approach: deterministic planner topologically sorts fields,
    /// then batch-generates structured fields and content fields in separate LLM calls.
    /// Works well with all models including smaller ones.
    /// </summary>
    BatchJson,

    /// <summary>
    /// Legacy field-by-field approach: generates one field at a time with isolated prompts.
    /// Most compatible but slowest and least context-aware.
    /// </summary>
    FieldByField
}

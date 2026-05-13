// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Interfaces;

namespace ReflectiveForms.Core.Ai;

/// <summary>
/// Static singleton holding AI service references, populated during <see cref="RfConfiguration.Initialize"/>.
/// All Ai* static classes read services from here.
/// </summary>
internal static class AiConfiguration
{
    internal static IDatabaseService DatabaseService { get; private set; } = null!;
    internal static IMemoryService MemoryService { get; private set; } = null!;
    internal static IVectorService VectorService { get; private set; } = null!;
    internal static ILLMService HeavyLlmService { get; private set; } = null!;
    internal static ILLMService LightLlmService { get; private set; } = null!;
    internal static ILLMService EmbeddingLlmService { get; private set; } = null!;
    internal static int EmbeddingDimensions { get; private set; }

    internal static bool IsInitialized { get; private set; }

    internal static void Initialize(
        IDatabaseService db, IMemoryService memory,
        IVectorService vector, ILLMService heavyLlm, ILLMService lightLlm,
        int embeddingDimensions, ILLMService? embeddingLlm = null)
    {
        DatabaseService = db;
        MemoryService = memory;
        VectorService = vector;
        HeavyLlmService = heavyLlm;
        LightLlmService = lightLlm;
        EmbeddingLlmService = embeddingLlm ?? lightLlm;
        EmbeddingDimensions = embeddingDimensions;
        IsInitialized = true;
    }
}

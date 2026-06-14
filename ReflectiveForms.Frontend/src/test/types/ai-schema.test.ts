import { describe, it, expect } from 'vitest';
import type {
  EntityFeatures,
  FieldSchema,
  AiSuggestionSchema,
  AiSanityCheckSchema,
  AiRelationSuggestionSchema,
  ApiEndpoints,
} from '../../types/schema';

describe('AI Schema Types', () => {
  it('EntityFeatures should include AI flags', () => {
    const features: EntityFeatures = {
      has_author: false,
      has_tags: false,
      has_categories: false,
      has_parent_child: false,
      require_title_uniqueness: false,
      supports_frontend_edit: true,
      show_in_navigation: true,
      has_individual_sharing: false,
      supports_semantic_search: true,
      supports_ai_generation: true,
      supports_ai_diff_summary: false,
      supports_natural_language_filter: true,
    };

    expect(features.supports_semantic_search).toBe(true);
    expect(features.supports_ai_generation).toBe(true);
    expect(features.supports_ai_diff_summary).toBe(false);
    expect(features.supports_natural_language_filter).toBe(true);
  });

  it('FieldSchema should support AI properties', () => {
    const field: FieldSchema = {
      name: 'description',
      type: 'TextArea',
      label: 'Description',
      required: false,
      has_dynamic_choices_runtime: false,
      has_dynamic_choices_compile_time: false,
      has_logic_sanity_check: false,
      ai_suggestion: {
        prompt: 'Suggest a description',
        source_fields: ['title', 'category'],
      },
      ai_sanity_checks: [
        { prompt: 'Check grammar', severity: 'Warning' },
        { prompt: 'Check tone', severity: 'Error' },
      ],
    };

    expect(field.ai_suggestion?.prompt).toBe('Suggest a description');
    expect(field.ai_suggestion?.source_fields).toEqual(['title', 'category']);
    expect(field.ai_sanity_checks).toHaveLength(2);
    expect(field.ai_sanity_checks![0].severity).toBe('Warning');
    expect(field.ai_sanity_checks![1].severity).toBe('Error');
  });

  it('FieldSchema should support AI relation suggestion', () => {
    const field: FieldSchema = {
      name: 'author_ref',
      type: 'Relation',
      label: 'Author',
      required: false,
      has_dynamic_choices_runtime: false,
      has_dynamic_choices_compile_time: false,
      has_logic_sanity_check: false,
      ai_relation_suggestion: { top_k: 5 },
    };

    expect(field.ai_relation_suggestion?.top_k).toBe(5);
  });

  it('AiSuggestionSchema should have correct shape', () => {
    const schema: AiSuggestionSchema = {
      prompt: 'Suggest',
      source_fields: ['a', 'b'],
    };

    expect(schema.prompt).toBe('Suggest');
    expect(schema.source_fields).toEqual(['a', 'b']);
  });

  it('AiSanityCheckSchema should have correct shape', () => {
    const schema: AiSanityCheckSchema = {
      prompt: 'Check quality',
      severity: 'Error',
    };

    expect(schema.prompt).toBe('Check quality');
    expect(schema.severity).toBe('Error');
  });

  it('AiRelationSuggestionSchema should have correct shape', () => {
    const schema: AiRelationSuggestionSchema = {
      top_k: 10,
    };

    expect(schema.top_k).toBe(10);
  });

  it('ApiEndpoints should include optional AI and OpenApi', () => {
    const endpoints: ApiEndpoints = {
      crud: '/crud',
      sanity_check: '/sanity_check',
      entity_lock: '/entity_lock',
      media: '/media',
      openapi: '/openapi.json',
      ai: {
        semantic_search: '/ai/semantic_search',
        generate: '/ai/generate',
        suggest: '/ai/suggest',
        sanity_check: '/ai/sanity_check',
        diff_summary: '/ai/diff_summary',
        nl_filter: '/ai/nl_filter',
        relation_suggest: '/ai/relation_suggest',
        reindex: '/ai/reindex',
      },
    };

    expect(endpoints.ai?.semantic_search).toBe('/ai/semantic_search');
    expect(endpoints.openapi).toBe('/openapi.json');
  });

  it('ApiEndpoints AI and OpenApi should be optional', () => {
    const endpoints: ApiEndpoints = {
      crud: '/crud',
      sanity_check: '/sanity_check',
      entity_lock: '/entity_lock',
      media: '/media',
    };

    expect(endpoints.ai).toBeUndefined();
    expect(endpoints.openapi).toBeUndefined();
  });
});

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import {
  aiSemanticSearch,
  aiSanityCheck,
  aiDiffSummary,
  aiNaturalLanguageFilter,
  aiRelationSuggest,
  setApiBaseUrl,
  setAiBaseUrl,
  setAiDisabled,
  isAiDisabled,
} from '../../api/client';

const mockFetch = vi.fn();
(globalThis as any).fetch = mockFetch;

describe('AI API Client', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    setApiBaseUrl('http://localhost:9000/rf/api');
    setAiBaseUrl(null);
    setAiDisabled(false);
  });

  afterEach(() => {
    vi.resetAllMocks();
  });

  describe('aiSemanticSearch', () => {
    it('should send POST with query', async () => {
      const mockResults = [{ entity_id: 1, title: 'Test', entity_name: 'blog', score: 0.9 }];
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ results: mockResults }),
      });

      const result = await aiSemanticSearch('test query');

      expect(result.data).toEqual(mockResults);
      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:9000/rf/api/ai/semantic_search',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ query: 'test query' }),
        }),
      );
    });

    it('should include entityName and topK when provided', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ results: [] }),
      });

      await aiSemanticSearch('test', 'blog', 5);

      expect(mockFetch).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({
          body: JSON.stringify({ query: 'test', entity_name: 'blog', top_k: 5 }),
        }),
      );
    });

    it('should handle errors', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 500,
        json: () => Promise.resolve({ message: 'AI service unavailable' }),
      });

      const result = await aiSemanticSearch('test');
      expect(result.error).toBeDefined();
    });
  });

  describe('aiSanityCheck', () => {
    it('should send POST with field name and value', async () => {
      const mockResults = [{ field: 'email', passed: false, message: 'Invalid format', severity: 'Error' }];
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockResults),
      });

      const result = await aiSanityCheck('users', 'email', 'not-an-email');

      expect(result.data).toEqual(mockResults);
      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:9000/rf/api/ai/sanity_check?type=users',
        expect.objectContaining({ method: 'POST' }),
      );
    });
  });

  describe('aiDiffSummary', () => {
    it('should send POST with entity info', async () => {
      const mockResult = { summary: 'Title was changed from A to B' };
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockResult),
      });

      const result = await aiDiffSummary('blog', 42, 3);

      expect(result.data).toEqual(mockResult);
      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:9000/rf/api/ai/diff_summary?type=blog',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ entity_id: 42, revision_index: 3 }),
        }),
      );
    });
  });

  describe('aiNaturalLanguageFilter', () => {
    it('should send POST with query', async () => {
      const mockResult = { filter: { status: 'active' }, description: 'Filtering by active status' };
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockResult),
      });

      const result = await aiNaturalLanguageFilter('blog', 'show me active posts');

      expect(result.data).toEqual(mockResult);
      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:9000/rf/api/ai/nl_filter?type=blog',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ query: 'show me active posts' }),
        }),
      );
    });
  });

  describe('aiRelationSuggest', () => {
    it('should send POST with relation info', async () => {
      const mockResults = [{ id: 5, title: 'Suggested Relation', score: 0.85 }];
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockResults),
      });

      const result = await aiRelationSuggest('blog', 'author_ref', 'John');

      expect(result.data).toEqual(mockResults);
      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:9000/rf/api/ai/relation_suggest?type=blog',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ relation_field: 'author_ref', current_text: 'John' }),
        }),
      );
    });

    it('should handle network errors', async () => {
      mockFetch.mockRejectedValueOnce(new Error('Network error'));

      const result = await aiRelationSuggest('blog', 'author_ref', 'John');
      expect(result.error).toBe('Network error');
    });
  });

  describe('AI disabled flag', () => {
    it('should return error when AI is disabled', async () => {
      setAiDisabled(true);
      expect(isAiDisabled()).toBe(true);

      const result = await aiSemanticSearch('test');
      expect(result.error).toBe('AI features are disabled');
      expect(mockFetch).not.toHaveBeenCalled();
    });

    it('should affect all AI functions when disabled', async () => {
      setAiDisabled(true);

      const results = await Promise.all([
        aiSanityCheck('blog', 'f', 'v'),
        aiDiffSummary('blog', 1, 1),
        aiNaturalLanguageFilter('blog', 'q'),
        aiRelationSuggest('blog', 'f', 't'),
      ]);

      for (const r of results) {
        expect(r.error).toBe('AI features are disabled');
      }
      expect(mockFetch).not.toHaveBeenCalled();
    });

    it('should allow requests when re-enabled', async () => {
      setAiDisabled(true);
      setAiDisabled(false);
      expect(isAiDisabled()).toBe(false);

      mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve({ results: [] }) });
      const result = await aiSemanticSearch('test');
      expect(result.data).toEqual([]);
    });
  });

  describe('AI base URL override', () => {
    it('should use custom AI base URL when set', async () => {
      setAiBaseUrl('http://ai-server:5000/v1');
      mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve({ results: [] }) });

      await aiSemanticSearch('test');

      expect(mockFetch).toHaveBeenCalledWith(
        'http://ai-server:5000/v1/semantic_search',
        expect.objectContaining({ method: 'POST' }),
      );
    });

    it('should fall back to default when aiBaseUrl is null', async () => {
      setAiBaseUrl(null);
      mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve({ results: [] }) });

      await aiSemanticSearch('test');

      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:9000/rf/api/ai/semantic_search',
        expect.objectContaining({ method: 'POST' }),
      );
    });

    it('should apply custom base to all AI functions', async () => {
      setAiBaseUrl('http://ai:3000/api');
      mockFetch.mockResolvedValue({ ok: true, json: () => Promise.resolve({}) });

      await aiSanityCheck('blog', 'title', 'test');
      expect(mockFetch).toHaveBeenCalledWith(
        'http://ai:3000/api/sanity_check?type=blog',
        expect.any(Object),
      );

      mockFetch.mockClear();
      await aiDiffSummary('blog', 1, 2);
      expect(mockFetch).toHaveBeenCalledWith(
        'http://ai:3000/api/diff_summary?type=blog',
        expect.any(Object),
      );
    });
  });
});

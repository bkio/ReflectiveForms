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

describe('AI API Client Edge Cases', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    setApiBaseUrl('http://localhost:9000/rf/api');
    setAiBaseUrl(null);
    setAiDisabled(false);
  });

  afterEach(() => {
    vi.resetAllMocks();
    setAiDisabled(false);
    setAiBaseUrl(null);
  });

  // --- HTTP status code handling ---

  describe('HTTP error status codes', () => {
    it('should handle 401 Unauthorized', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 401,
        json: () => Promise.resolve({ message: 'Unauthorized' }),
      });

      const result = await aiSemanticSearch('test');
      expect(result.error).toBeDefined();
    });

    it('should handle 403 Forbidden', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 403,
        json: () => Promise.resolve({ message: 'Forbidden' }),
      });

      const result = await aiSanityCheck('blog', 'field', 'value');
      expect(result.error).toBeDefined();
    });

    it('should handle 404 Not Found', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 404,
        json: () => Promise.resolve({ message: 'Not found' }),
      });

      const result = await aiDiffSummary('blog', 42, 1);
      expect(result.error).toBeDefined();
    });

    it('should handle 409 Conflict (e.g., concurrent reindex)', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 409,
        json: () => Promise.resolve({ message: 'Reindex already in progress' }),
      });

      const result = await aiSemanticSearch('test');
      expect(result.error).toBeDefined();
    });

    it('should handle 501 Not Implemented (AI not configured)', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 501,
        json: () => Promise.resolve({ message: 'AI not configured' }),
      });

      const result = await aiRelationSuggest('blog', 'author_ref', 'test');
      expect(result.error).toBeDefined();
    });

    it('should handle 503 Service Unavailable', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 503,
        json: () => Promise.resolve({ message: 'LLM service down' }),
      });

      const result = await aiSanityCheck('blog', 'field', 'value');
      expect(result.error).toBeDefined();
    });
  });

  // --- Network failures ---

  describe('Network failures', () => {
    it('should handle fetch rejection (network down)', async () => {
      mockFetch.mockRejectedValueOnce(new TypeError('Failed to fetch'));

      const result = await aiSemanticSearch('test');
      expect(result.error).toBeDefined();
    });

    it('should handle timeout (AbortError)', async () => {
      mockFetch.mockRejectedValueOnce(new DOMException('The operation was aborted', 'AbortError'));

      const result = await aiDiffSummary('blog', 1, 1);
      expect(result.error).toBeDefined();
    });

    it('should handle non-JSON response body', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: false,
        status: 502,
        json: () => Promise.reject(new SyntaxError('Unexpected token < in JSON')),
      });

      const result = await aiNaturalLanguageFilter('blog', 'show active');
      expect(result.error).toBeDefined();
    });
  });

  // --- Disable/enable toggling ---

  describe('Rapid disable/enable toggling', () => {
    it('should respect the final state after multiple toggles', async () => {
      setAiDisabled(true);
      setAiDisabled(false);
      setAiDisabled(true);
      setAiDisabled(false);

      expect(isAiDisabled()).toBe(false);

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve([]),
      });

      const result = await aiSemanticSearch('test');
      expect(result.data).toEqual([]);
      expect(mockFetch).toHaveBeenCalledTimes(1);
    });

    it('isAiDisabled should be consistent', () => {
      expect(isAiDisabled()).toBe(false);
      setAiDisabled(true);
      expect(isAiDisabled()).toBe(true);
      setAiDisabled(false);
      expect(isAiDisabled()).toBe(false);
    });
  });

  // --- AI base URL edge cases ---

  describe('AI base URL edge cases', () => {
    it('should handle base URL with trailing slash', async () => {
      setAiBaseUrl('http://ai-server:5000/v1/');
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ results: [] }),
      });

      await aiSemanticSearch('test');

      const calledUrl = mockFetch.mock.calls[0][0];
      expect(calledUrl).toContain('semantic_search');
    });

    it('should handle empty string base URL', async () => {
      setAiBaseUrl('');
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ results: [] }),
      });

      await aiSemanticSearch('test');
      expect(mockFetch).toHaveBeenCalled();
    });

    it('should switch back to default when aiBaseUrl set to null', async () => {
      setAiBaseUrl('http://custom:3000/ai');
      setAiBaseUrl(null);

      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ results: [] }),
      });

      await aiSemanticSearch('test');

      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:9000/rf/api/ai/semantic_search',
        expect.any(Object),
      );
    });
  });

  // --- Request body verification for all endpoints ---

  describe('Request body shapes', () => {
    it('aiSemanticSearch with all params', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ results: [] }),
      });

      await aiSemanticSearch('my query', 'articles', 10);

      expect(mockFetch).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ query: 'my query', entity_name: 'articles', top_k: 10 }),
        }),
      );
    });

    it('aiSanityCheck includes field details', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve([]),
      });

      await aiSanityCheck('blog', 'email', 'user@test.com');

      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('sanity_check'),
        expect.objectContaining({
          method: 'POST',
        }),
      );
    });

    it('aiRelationSuggest sends correct body', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve([]),
      });

      await aiRelationSuggest('blog', 'author_ref', 'John');

      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('relation_suggest'),
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ relation_field: 'author_ref', current_text: 'John' }),
        }),
      );
    });
  });

  // --- Empty/special input handling ---

  describe('Special input handling', () => {
    it('aiSemanticSearch with special characters in query', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve([]),
      });

      const specialQuery = 'query with "quotes" & <special> chars';
      await aiSemanticSearch(specialQuery);

      // Verify the body was sent (JSON.stringify handles escaping)
      expect(mockFetch).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({
          body: JSON.stringify({ query: specialQuery }),
        }),
      );
    });

    it('aiDiffSummary with large entity', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve({ summary: 'Changes detected' }),
      });

      const result = await aiDiffSummary('blog', 99999, 50);
      expect(result.data).toBeDefined();
      expect(mockFetch).toHaveBeenCalled();
    });

    it('aiRelationSuggest with nested query', async () => {
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve([]),
      });

      await aiRelationSuggest('blog', 'author_ref', 'John Doe');

      expect(mockFetch).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({
          body: expect.stringContaining('"current_text":'),
        }),
      );
    });
  });

  // --- Concurrent requests ---

  describe('Concurrent requests', () => {
    it('should handle concurrent calls to different endpoints', async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: () => Promise.resolve({}),
      });

      const results = await Promise.all([
        aiSemanticSearch('query1'),
        aiSanityCheck('blog', 'field', 'value'),
        aiDiffSummary('blog', 1, 1),
      ]);

      expect(mockFetch).toHaveBeenCalledTimes(3);
      results.forEach((r) => expect(r.error).toBeUndefined());
    });

    it('should handle concurrent calls to the same endpoint', async () => {
      let callCount = 0;
      mockFetch.mockImplementation(() => {
        callCount++;
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve([{ id: callCount }]),
        });
      });

      const results = await Promise.all([
        aiSemanticSearch('query1'),
        aiSemanticSearch('query2'),
        aiSemanticSearch('query3'),
      ]);

      expect(mockFetch).toHaveBeenCalledTimes(3);
      results.forEach((r) => expect(r.data).toBeDefined());
    });
  });
});

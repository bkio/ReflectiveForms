import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  AiAssistantProvider,
  useAiAssistant,
  useAiAssistantOptional,
} from '../../lib/AiAssistantContext';
import * as client from '../../api/client';

vi.mock('../../api/client');

function Wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return createElement(QueryClientProvider, { client: queryClient },
    createElement(MemoryRouter, null,
      createElement(AiAssistantProvider, null, children),
    ),
  );
}

function mockChat(overrides?: Partial<client.AiAgentChatResponse>) {
  vi.mocked(client.aiAgentChat).mockResolvedValue({
    data: {
      response: 'Mock response',
      tool_calls_made: [],
      proposed_actions: [],
      ...overrides,
    },
  });
}

describe('AiAssistantContext', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('useAiAssistant throws outside provider', () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});
    expect(() => renderHook(() => useAiAssistant())).toThrow(
      'useAiAssistant must be used within AiAssistantProvider',
    );
    spy.mockRestore();
  });

  it('useAiAssistantOptional returns null outside provider', () => {
    const { result } = renderHook(() => useAiAssistantOptional());
    expect(result.current).toBeNull();
  });

  it('provides initial state', () => {
    const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });
    expect(result.current.isOpen).toBe(false);
    expect(result.current.isSending).toBe(false);
    expect(result.current.conversation).toHaveLength(1);
    expect(result.current.conversation[0].role).toBe('assistant');
    expect(result.current.pendingActions).toEqual([]);
    expect(result.current.lastExecutedActions).toEqual([]);
    expect(result.current.context).toEqual({});
  });

  it('open / close / toggle', () => {
    const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });

    act(() => result.current.open());
    expect(result.current.isOpen).toBe(true);

    act(() => result.current.close());
    expect(result.current.isOpen).toBe(false);

    act(() => result.current.toggle());
    expect(result.current.isOpen).toBe(true);
    act(() => result.current.toggle());
    expect(result.current.isOpen).toBe(false);
  });

  it('setContext merges partially', () => {
    const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });

    act(() => result.current.setContext({ current_page: 'entity-list', entity_type: 'blog' }));
    expect(result.current.context.current_page).toBe('entity-list');
    expect(result.current.context.entity_type).toBe('blog');

    act(() => result.current.setContext({ entity_id: 42 }));
    expect(result.current.context).toEqual({
      current_page: 'entity-list',
      entity_type: 'blog',
      entity_id: 42,
    });
  });

  it('sendMessage adds user and assistant messages', async () => {
    mockChat({ response: 'Hello from AI!' });
    const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });

    await act(() => result.current.sendMessage('Hi there'));

    expect(result.current.conversation).toHaveLength(3);
    expect(result.current.conversation[1]).toEqual({ role: 'user', content: 'Hi there' });
    expect(result.current.conversation[2].content).toContain('Hello from AI!');
  });

  it('sendMessage handles API error', async () => {
    vi.mocked(client.aiAgentChat).mockResolvedValue({ error: 'Network error' });
    const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });

    await act(() => result.current.sendMessage('Test'));

    const last = result.current.conversation[result.current.conversation.length - 1];
    expect(last.role).toBe('assistant');
    expect(last.content).toContain('something went wrong');
  });

  it('sendMessage appends tool summary', async () => {
    mockChat({
      response: 'Found results.',
      tool_calls_made: [
        { tool: 'search_entities', arguments: {} as Record<string, unknown>, result_preview: '...' },
        { tool: 'get_entity', arguments: {} as Record<string, unknown>, result_preview: '...' },
      ],
    });
    const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });

    await act(() => result.current.sendMessage('Search'));

    const last = result.current.conversation[result.current.conversation.length - 1];
    expect(last.content).toBe('Found results.');
    expect(last.tools_used).toEqual(['search_entities', 'get_entity']);
  });

  it('proposed actions: approval-required go to pending, auto-execute fire handlers', async () => {
    const handler = vi.fn();
    mockChat({
      proposed_actions: [
        {
          action_id: 'a1', action_type: 'create_entity',
          description: 'Create blog', requires_approval: true, entity_type: 'blog',
        },
        {
          action_id: 'a2', action_type: 'navigate',
          description: 'Go to list', requires_approval: false,
          payload: { page: 'entity-list', entity_type: 'blog' },
        },
      ],
    });

    const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });
    act(() => { result.current.subscribeAutoAction(handler); });

    await act(() => result.current.sendMessage('Create and navigate'));

    expect(result.current.pendingActions).toHaveLength(1);
    expect(result.current.pendingActions[0].action_id).toBe('a1');
    expect(handler).toHaveBeenCalledWith(
      expect.objectContaining({ action_id: 'a2', action_type: 'navigate' }),
    );
  });

  it('confirmAction for create_entity navigates instead of sending to backend', async () => {
    const handler = vi.fn();
    const action: client.AiProposedAction = {
      action_id: 'a1', action_type: 'create_entity',
      description: 'Create blog', requires_approval: true,
      entity_type: 'blog', payload: { title: 'Test', fields: {} },
    };
    mockChat({ proposed_actions: [action] });

    const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });
    act(() => { result.current.subscribeAutoAction(handler); });
    await act(() => result.current.sendMessage('Create blog'));
    expect(result.current.pendingActions).toHaveLength(1);

    await act(() => result.current.confirmAction(action));

    expect(result.current.pendingActions).toHaveLength(0);
    // Should NOT have called the backend again
    expect(client.aiAgentChat).toHaveBeenCalledTimes(1); // only the initial sendMessage
    // Should have fired a navigate auto-action with prefill data
    expect(handler).toHaveBeenCalledWith(
      expect.objectContaining({
        action_type: 'navigate',
        entity_type: 'blog',
        payload: expect.objectContaining({
          page: 'entity-create',
          prefill: { title: 'Test', fields: {} },
        }),
      }),
    );
  });

  it('confirmAction fires auto-action handlers for set_field', async () => {
    const handler = vi.fn();
    const action: client.AiProposedAction = {
      action_id: 'sf1', action_type: 'set_field',
      description: 'Set body', requires_approval: true,
      entity_type: 'blog', payload: { field_name: 'body', suggested_value: 'X' },
    };
    mockChat({ proposed_actions: [action] });

    const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });
    act(() => { result.current.subscribeAutoAction(handler); });

    await act(() => result.current.sendMessage('Suggest body'));
    expect(result.current.pendingActions).toHaveLength(1);
    expect(handler).not.toHaveBeenCalled();

    mockChat({ response: 'Applied!' });
    await act(() => result.current.confirmAction(action));

    expect(handler).toHaveBeenCalledWith(
      expect.objectContaining({ action_type: 'set_field' }),
    );
  });

  it('rejectAction removes from pending and sends rejection', async () => {
    const action: client.AiProposedAction = {
      action_id: 'a1', action_type: 'delete_entity',
      description: 'Delete entity', requires_approval: true,
      entity_type: 'blog', entity_id: 5,
    };
    mockChat({ proposed_actions: [action] });

    const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });
    await act(() => result.current.sendMessage('Delete entity 5'));
    expect(result.current.pendingActions).toHaveLength(1);

    mockChat({ response: 'OK, cancelled.' });
    await act(() => result.current.rejectAction(action));

    expect(result.current.pendingActions).toHaveLength(0);
    expect(client.aiAgentChat).toHaveBeenLastCalledWith(
      expect.stringContaining('rejected'),
      expect.anything(),
      expect.arrayContaining([
        expect.objectContaining({ action_id: 'a1', approved: false }),
      ]),
    );
  });

  it('clearConversation resets to initial message', async () => {
    mockChat();
    const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });

    await act(() => result.current.sendMessage('Hi'));
    expect(result.current.conversation.length).toBeGreaterThan(1);

    act(() => result.current.clearConversation());
    expect(result.current.conversation).toHaveLength(1);
    expect(result.current.conversation[0].role).toBe('assistant');
    expect(result.current.pendingActions).toEqual([]);
  });

  it('triggerMessage opens panel and merges context', async () => {
    mockChat({ response: 'Suggestion result' });
    const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });

    act(() => result.current.setContext({ current_page: 'entity-edit', entity_type: 'blog' }));
    await act(() => result.current.triggerMessage('Suggest body', { selected_field: 'body' }));

    expect(result.current.isOpen).toBe(true);
    expect(client.aiAgentChat).toHaveBeenCalledWith(
      'Suggest body',
      expect.objectContaining({
        current_page: 'entity-edit',
        entity_type: 'blog',
        selected_field: 'body',
      }),
      undefined,
    );
  });

  it('subscribeAutoAction supports multiple subscribers and unsubscribe', async () => {
    const h1 = vi.fn();
    const h2 = vi.fn();
    mockChat({
      proposed_actions: [{
        action_id: 'nav1', action_type: 'navigate',
        description: 'Go', requires_approval: false, payload: { page: 'entity-list' },
      }],
    });

    const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });
    let unsub1: () => void;
    act(() => {
      unsub1 = result.current.subscribeAutoAction(h1);
      result.current.subscribeAutoAction(h2);
    });

    await act(() => result.current.sendMessage('Go'));
    expect(h1).toHaveBeenCalledTimes(1);
    expect(h2).toHaveBeenCalledTimes(1);

    // Unsubscribe h1
    act(() => unsub1!());

    mockChat({
      proposed_actions: [{
        action_id: 'nav2', action_type: 'navigate',
        description: 'Go again', requires_approval: false,
      }],
    });
    await act(() => result.current.sendMessage('Go again'));

    expect(h1).toHaveBeenCalledTimes(1); // unchanged
    expect(h2).toHaveBeenCalledTimes(2);
  });
});

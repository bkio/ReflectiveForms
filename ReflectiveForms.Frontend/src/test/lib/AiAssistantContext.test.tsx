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
      expect.anything(),
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
      expect.anything(),
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

  describe('sheet-specific actions', () => {
    it('setContext merges sheet_sources and selected_cell', () => {
      const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });

      act(() => result.current.setContext({
        current_page: 'sheet-edit',
        sheet_sources: ['orders', 'products'],
        selected_cell: 'B3',
      }));

      expect(result.current.context.current_page).toBe('sheet-edit');
      expect(result.current.context.sheet_sources).toEqual(['orders', 'products']);
      expect(result.current.context.selected_cell).toBe('B3');

      // Subsequent merge preserves sheet fields
      act(() => result.current.setContext({ entity_id: 7 }));
      expect(result.current.context.sheet_sources).toEqual(['orders', 'products']);
      expect(result.current.context.selected_cell).toBe('B3');
      expect(result.current.context.entity_id).toBe(7);
    });

    it('sheet_sources and selected_cell are sent to backend', async () => {
      mockChat({ response: 'Formula suggestion' });
      const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });

      act(() => result.current.setContext({
        current_page: 'sheet-edit',
        sheet_sources: ['invoices'],
        selected_cell: 'A1',
      }));
      await act(() => result.current.sendMessage('Suggest formula'));

      expect(client.aiAgentChat).toHaveBeenCalledWith(
        'Suggest formula',
        expect.objectContaining({
          current_page: 'sheet-edit',
          sheet_sources: ['invoices'],
          selected_cell: 'A1',
        }),
        undefined,
        expect.anything(),
      );
    });

    it('sheet_edit auto-action fires handler without backend call', async () => {
      const handler = vi.fn();
      const sheetEditAction: client.AiProposedAction = {
        action_id: 'se1',
        action_type: 'sheet_edit',
        description: 'Set cell A1=100',
        requires_approval: true,
        entity_type: 'rf-sheets',
        entity_id: 5,
        payload: { operations: [{ row: 0, col: 0, value: 100 }] },
      };
      mockChat({ proposed_actions: [sheetEditAction] });

      const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });
      act(() => { result.current.subscribeAutoAction(handler); });
      await act(() => result.current.sendMessage('Set A1 to 100'));
      expect(result.current.pendingActions).toHaveLength(1);
      expect(handler).not.toHaveBeenCalled();

      mockChat({ response: 'Applied.' });
      await act(() => result.current.confirmAction(sheetEditAction));

      expect(handler).toHaveBeenCalledWith(
        expect.objectContaining({ action_type: 'sheet_edit', action_id: 'se1' }),
      );
      expect(result.current.pendingActions).toHaveLength(0);
    });

    it('sheet_add_source auto-action fires handler without backend call', async () => {
      const handler = vi.fn();
      const addSourceAction: client.AiProposedAction = {
        action_id: 'as1',
        action_type: 'sheet_add_source',
        description: 'Add orders source',
        requires_approval: true,
        entity_type: 'rf-sheets',
        entity_id: 5,
        payload: { entity_type: 'orders' },
      };
      mockChat({ proposed_actions: [addSourceAction] });

      const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });
      act(() => { result.current.subscribeAutoAction(handler); });
      await act(() => result.current.sendMessage('Add orders source'));
      expect(result.current.pendingActions).toHaveLength(1);

      mockChat({ response: 'Source added.' });
      await act(() => result.current.confirmAction(addSourceAction));

      expect(handler).toHaveBeenCalledWith(
        expect.objectContaining({ action_type: 'sheet_add_source', action_id: 'as1' }),
      );
      expect(result.current.pendingActions).toHaveLength(0);
    });

    it('sheet_edit with requires_approval=false fires handler immediately', async () => {
      const handler = vi.fn();
      mockChat({
        proposed_actions: [{
          action_id: 'se-auto',
          action_type: 'sheet_edit',
          description: 'Auto-apply formula',
          requires_approval: false,
          entity_type: 'rf-sheets',
          entity_id: 5,
          payload: { operations: [{ row: 0, col: 0, formula: '=RF.LOOKUP("orders","total")' }] },
        }],
      });

      const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });
      act(() => { result.current.subscribeAutoAction(handler); });
      await act(() => result.current.sendMessage('Apply formula'));

      // Auto-execute: should NOT be in pending, handler fires immediately
      expect(result.current.pendingActions).toHaveLength(0);
      expect(handler).toHaveBeenCalledWith(
        expect.objectContaining({ action_type: 'sheet_edit', action_id: 'se-auto' }),
      );
    });

    it('sheet_edit confirmation sends to backend alongside auto-action', async () => {
      const handler = vi.fn();
      const sheetEditAction: client.AiProposedAction = {
        action_id: 'se2',
        action_type: 'sheet_edit',
        description: 'Set B2=Hello',
        requires_approval: true,
        entity_type: 'rf-sheets',
        entity_id: 3,
        payload: { operations: [{ row: 1, col: 1, value: 'Hello' }] },
      };
      mockChat({ proposed_actions: [sheetEditAction] });

      const { result } = renderHook(() => useAiAssistant(), { wrapper: Wrapper });
      act(() => { result.current.subscribeAutoAction(handler); });
      await act(() => result.current.sendMessage('Edit B2'));
      expect(result.current.pendingActions).toHaveLength(1);

      // Confirm — should fire the auto-action handler
      mockChat({ response: 'Applied.' });
      await act(() => result.current.confirmAction(sheetEditAction));

      expect(handler).toHaveBeenCalledWith(
        expect.objectContaining({ action_type: 'sheet_edit' }),
      );
      expect(result.current.pendingActions).toHaveLength(0);
      // Two backend calls: initial sendMessage + confirmation
      expect(client.aiAgentChat).toHaveBeenCalledTimes(2);
    });
  });
});

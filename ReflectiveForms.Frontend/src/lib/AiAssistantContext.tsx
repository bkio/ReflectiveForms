import { createContext, useContext, useState, useCallback, useMemo, useRef, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { aiAgentChat } from '../api/client';
import type {
  AiChatMessage,
  AiAgentContext,
  AiProposedAction,
  AiActionConfirmation,
  AiExecutedAction,
} from '../api/client';

export interface AiAssistantState {
  isOpen: boolean;
  isMinimized: boolean;
  isSending: boolean;
  conversation: AiChatMessage[];
  pendingActions: AiProposedAction[];
  lastExecutedActions: AiExecutedAction[];
  context: AiAgentContext;
  open: () => void;
  close: () => void;
  toggle: () => void;
  minimize: () => void;
  restore: () => void;
  sendMessage: (message: string) => Promise<void>;
  confirmAction: (action: AiProposedAction) => Promise<void>;
  rejectAction: (action: AiProposedAction) => Promise<void>;
  setContext: (ctx: Partial<AiAgentContext>) => void;
  clearConversation: () => void;
  /** Trigger a message from another component (e.g. suggest button, sanity check) */
  triggerMessage: (message: string, ctx?: Partial<AiAgentContext>) => Promise<void>;
  /** Subscribe to auto-executed actions (navigate, set_field when approved). Returns unsubscribe fn. */
  subscribeAutoAction: (handler: (action: AiProposedAction) => void) => () => void;
}

const INITIAL_MESSAGE: AiChatMessage = {
  role: 'assistant',
  content: 'Hi! I can help you browse, search, create, update, and check your content. What would you like to do?',
};

const AiAssistantCtx = createContext<AiAssistantState | null>(null);

export function useAiAssistant(): AiAssistantState {
  const ctx = useContext(AiAssistantCtx);
  if (!ctx) throw new Error('useAiAssistant must be used within AiAssistantProvider');
  return ctx;
}

/** Safe version: returns null if outside provider (for optional integration). */
export function useAiAssistantOptional(): AiAssistantState | null {
  return useContext(AiAssistantCtx);
}

export function AiAssistantProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [isOpen, setIsOpen] = useState(false);
  const [isMinimized, setIsMinimized] = useState(false);
  const [isSending, setIsSending] = useState(false);
  const [conversation, setConversation] = useState<AiChatMessage[]>([INITIAL_MESSAGE]);
  const [pendingActions, setPendingActions] = useState<AiProposedAction[]>([]);
  const [lastExecutedActions, setLastExecutedActions] = useState<AiExecutedAction[]>([]);
  const [agentContext, setAgentContext] = useState<AiAgentContext>({});
  const autoActionHandlersRef = useRef(new Set<(action: AiProposedAction) => void>());
  // Track content keys of actions that were approved or rejected to prevent re-proposal loops
  const resolvedActionKeysRef = useRef(new Set<string>());

  const open = useCallback(() => { setIsOpen(true); setIsMinimized(false); }, []);
  const close = useCallback(() => setIsOpen(false), []);
  const toggle = useCallback(() => setIsOpen(v => !v), []);
  const minimize = useCallback(() => setIsMinimized(true), []);
  const restore = useCallback(() => setIsMinimized(false), []);

  const setContext = useCallback((ctx: Partial<AiAgentContext>) => {
    setAgentContext(prev => {
      const next = { ...prev, ...ctx };
      // Shallow equality check to avoid infinite update loops
      const keys = new Set([...Object.keys(prev), ...Object.keys(next)]);
      for (const k of keys) {
        if ((prev as any)[k] !== (next as any)[k]) return next;
      }
      return prev;
    });
  }, []);

  const clearConversation = useCallback(() => {
    setConversation([INITIAL_MESSAGE]);
    setPendingActions([]);
    setLastExecutedActions([]);
    resolvedActionKeysRef.current.clear();
  }, []);

  const sendMessageInternal = useCallback(async (
    message: string,
    contextOverride?: AiAgentContext,
    confirmations?: AiActionConfirmation[]
  ) => {
    // Only add user message if not a confirmation-only call
    if (message.trim()) {
      setConversation(prev => [...prev, { role: 'user', content: message }]);
    }
    setIsSending(true);

    try {
      const ctx = contextOverride ?? agentContext;

      // Build condensed history from conversation for multi-turn context.
      // Skip the first message (hardcoded UI greeting — not a real LLM response).
      // Only include user/assistant text — tool call details are omitted to save tokens.
      const historyEntries = conversation
        .slice(1)
        .filter(m => m.content && m.content.trim())
        .map(m => ({ role: m.role, content: m.content }));

      const result = await aiAgentChat(message, ctx, confirmations, historyEntries);

      if (result.error) {
        setConversation(prev => [
          ...prev,
          { role: 'assistant', content: `Sorry, something went wrong: ${result.error}` },
        ]);
        return;
      }

      if (!result.data) {
        setConversation(prev => [
          ...prev,
          { role: 'assistant', content: 'Sorry, I received an empty response.' },
        ]);
        return;
      }

      const { response, tool_calls_made, proposed_actions, executed_actions } = result.data;

      // Build response content (tool names stored separately, not in message text)
      const toolsUsed = tool_calls_made?.length
        ? [...new Set(tool_calls_made.map(tc => tc.tool))]
        : undefined;

      setConversation(prev => [...prev, { role: 'assistant', content: response, tools_used: toolsUsed }]);

      // Update pending actions (only those that require approval), deduplicate by action_id, content, and resolved history
      if (proposed_actions?.length) {
        const needsApproval = proposed_actions.filter(a => a.requires_approval);
        const autoActions = proposed_actions.filter(a => !a.requires_approval);
        if (needsApproval.length > 0) {
          // First deduplicate within the incoming batch itself
          const batchSeen = new Set<string>();
          const uniqueBatch = needsApproval.filter(a => {
            const key = `${a.action_type}|${a.entity_type}|${a.description}`;
            // Skip if already approved/rejected in this session
            if (resolvedActionKeysRef.current.has(key)) return false;
            if (batchSeen.has(key)) return false;
            batchSeen.add(key);
            return true;
          });
          setPendingActions(prev => {
            const existingIds = new Set(prev.map(a => a.action_id));
            const existingKeys = new Set(prev.map(a => `${a.action_type}|${a.entity_type}|${a.description}`));
            const newActions = uniqueBatch.filter(
              a => !existingIds.has(a.action_id) && !existingKeys.has(`${a.action_type}|${a.entity_type}|${a.description}`)
            );
            return newActions.length > 0 ? [...prev, ...newActions] : prev;
          });
        }
        // Auto-execute actions that don't need approval (e.g., navigate)
        for (const action of autoActions) {
          for (const handler of autoActionHandlersRef.current) {
            handler(action);
          }
        }
      }

      // Update executed actions
      if (executed_actions?.length) {
        setLastExecutedActions(executed_actions);
        // Remove executed actions from pending
        const executedIds = new Set(executed_actions.map(a => a.action_id));
        setPendingActions(prev => prev.filter(a => !executedIds.has(a.action_id)));

        // Invalidate React Query cache for successful mutations so pages show fresh data
        for (const ea of executed_actions) {
          if (ea.success && ea.entity_type) {
            if (ea.action_type === 'update_entity' || ea.action_type === 'delete_entity') {
              queryClient.invalidateQueries({ queryKey: ['entities', ea.entity_type] });
              queryClient.invalidateQueries({ queryKey: ['entities-paginated', ea.entity_type] });
              const eid = ea.entity_id ?? ea.result?.id;
              if (ea.action_type === 'update_entity' && eid != null) {
                queryClient.invalidateQueries({ queryKey: ['entity', ea.entity_type, eid] });
              }
            }
          }
        }

        // Auto-navigate to the edit page for successfully created entities
        for (const ea of executed_actions) {
          if (ea.success && ea.action_type === 'create_entity' && ea.entity_type && ea.result) {
            const entityId = ea.result.id as number | undefined;
            if (entityId != null) {
              const navAction: AiProposedAction = {
                action_id: `nav-${ea.action_id}`,
                action_type: 'navigate',
                description: `Navigate to edit ${ea.entity_type} #${entityId}`,
                requires_approval: false,
                entity_type: ea.entity_type,
                entity_id: entityId,
                payload: { page: 'entity-edit' },
              };
              for (const handler of autoActionHandlersRef.current) {
                handler(navAction);
              }
            }
          }
        }
      }
    } finally {
      setIsSending(false);
    }
  }, [agentContext, queryClient]);

  const sendMessage = useCallback(async (message: string) => {
    await sendMessageInternal(message);
  }, [sendMessageInternal]);

  const confirmAction = useCallback(async (action: AiProposedAction) => {
    const confirmation: AiActionConfirmation = {
      action_id: action.action_id,
      approved: true,
      action,
    };

    // Remove from pending immediately for responsive UI and track as resolved
    const contentKey = `${action.action_type}|${action.entity_type}|${action.description}`;
    resolvedActionKeysRef.current.add(contentKey);
    setPendingActions(prev => prev.filter(a => a.action_id !== action.action_id));

    // For client-side actions (set_field), fire the auto-action handlers
    if (action.action_type === 'set_field') {
      for (const handler of autoActionHandlersRef.current) {
        handler(action);
      }
    }

    // For create_entity, navigate to the create page with pre-populated data
    // instead of executing server-side (lets the user review and fix fields first)
    if (action.action_type === 'create_entity' && action.entity_type) {
      const navAction: AiProposedAction = {
        action_id: `nav-${action.action_id}`,
        action_type: 'navigate',
        description: `Navigate to create ${action.entity_type}`,
        requires_approval: false,
        entity_type: action.entity_type,
        payload: { page: 'entity-create', prefill: action.payload },
      };
      for (const handler of autoActionHandlersRef.current) {
        handler(navAction);
      }
      return;
    }

    // Send confirmation to backend with a status message
    await sendMessageInternal(
      `I approved: ${action.description}`,
      undefined,
      [confirmation]
    );
  }, [sendMessageInternal]);

  const rejectAction = useCallback(async (action: AiProposedAction) => {
    const confirmation: AiActionConfirmation = {
      action_id: action.action_id,
      approved: false,
      action,
    };

    // Remove from pending and track as resolved
    const contentKey = `${action.action_type}|${action.entity_type}|${action.description}`;
    resolvedActionKeysRef.current.add(contentKey);
    setPendingActions(prev => prev.filter(a => a.action_id !== action.action_id));

    await sendMessageInternal(
      `I rejected: ${action.description}`,
      undefined,
      [confirmation]
    );
  }, [sendMessageInternal]);

  const triggerMessage = useCallback(async (message: string, ctx?: Partial<AiAgentContext>) => {
    setIsOpen(true);
    setIsMinimized(false);
    const merged = ctx ? { ...agentContext, ...ctx } : agentContext;
    await sendMessageInternal(message, merged);
  }, [agentContext, sendMessageInternal]);

  const subscribeAutoAction = useCallback((handler: (action: AiProposedAction) => void) => {
    autoActionHandlersRef.current.add(handler);
    return () => { autoActionHandlersRef.current.delete(handler); };
  }, []);

  const value = useMemo<AiAssistantState>(() => ({
    isOpen,
    isMinimized,
    isSending,
    conversation,
    pendingActions,
    lastExecutedActions,
    context: agentContext,
    open,
    close,
    toggle,
    minimize,
    restore,
    sendMessage,
    confirmAction,
    rejectAction,
    setContext,
    clearConversation,
    triggerMessage,
    subscribeAutoAction,
  }), [
    isOpen, isMinimized, isSending, conversation, pendingActions, lastExecutedActions, agentContext,
    open, close, toggle, minimize, restore, sendMessage, confirmAction, rejectAction, setContext,
    clearConversation, triggerMessage, subscribeAutoAction,
  ]);

  return (
    <AiAssistantCtx.Provider value={value}>
      {children}
    </AiAssistantCtx.Provider>
  );
}

import { useState, useRef, useEffect, useCallback } from 'react';
import { MessageSquare, Minimize2, Copy, Check, Send, Sparkles, CheckCircle, XCircle, Trash2, Plus, Pencil, Lightbulb, FileText, Database, Hash, AlertTriangle, Layout } from 'lucide-react';
import { useAiAssistant } from '../../lib/AiAssistantContext';
import type { AiProposedAction } from '../../api/client';

export function AiAgentChat() {
  const {
    isOpen,
    isMinimized,
    isSending,
    conversation,
    pendingActions,
    context: agentContext,
    minimize,
    restore,
    sendMessage,
    confirmAction,
    rejectAction,
    clearConversation,
  } = useAiAssistant();

  const [input, setInput] = useState('');
  const [copied, setCopied] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (!isMinimized && isOpen) {
      messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    }
  }, [conversation, isMinimized, isOpen]);

  useEffect(() => {
    if (!isMinimized && isOpen) {
      inputRef.current?.focus();
    }
  }, [isMinimized, isOpen]);

  const handleSend = useCallback(async () => {
    const text = input.trim();
    if (!text || isSending) return;
    setInput('');
    await sendMessage(text);
  }, [input, isSending, sendMessage]);

  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  }, [handleSend]);

  const handleCopy = useCallback(async () => {
    const text = conversation
      .map(msg => (msg.role === 'user' ? `You: ${msg.content}` : `AI: ${msg.content}`))
      .join('\n\n');
    await navigator.clipboard.writeText(text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }, [conversation]);

  if (!isOpen) return null;

  if (isMinimized) {
    return (
      <div className="fixed bottom-4 right-4 z-40" data-testid="ai-assistant-minimized">
        <button
          onClick={restore}
          className="flex items-center gap-2 px-4 py-3 bg-purple-600 text-white rounded-full shadow-lg hover:bg-purple-700 transition-all hover:scale-105"
        >
          <Sparkles className="w-4 h-4" />
          <span className="text-sm font-medium">AI Assistant</span>
          {pendingActions.length > 0 && (
            <span className="bg-orange-500 text-xs px-1.5 py-0.5 rounded-full">{pendingActions.length}</span>
          )}
        </button>
      </div>
    );
  }

  return (
    <div
      className="fixed bottom-4 right-4 z-40 w-[420px] max-h-[36rem] flex flex-col bg-white dark:bg-gray-800 rounded-xl shadow-2xl border border-gray-200 dark:border-gray-700"
      data-testid="ai-assistant"
    >
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-200 dark:border-gray-700 bg-purple-50 dark:bg-purple-900/20 rounded-t-xl">
        <div className="flex items-center gap-2">
          <Sparkles className="w-4 h-4 text-purple-600 dark:text-purple-400" />
          <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">AI Assistant</span>
        </div>
        <div className="flex items-center gap-1">
          <button onClick={clearConversation} className="p-1 text-gray-400 hover:text-gray-600 dark:hover:text-gray-200 transition-colors" title="Clear conversation">
            <Trash2 className="w-4 h-4" />
          </button>
          <button onClick={handleCopy} className="p-1 text-gray-400 hover:text-gray-600 dark:hover:text-gray-200 transition-colors" title={copied ? 'Copied!' : 'Copy conversation'}>
            {copied ? <Check className="w-4 h-4 text-green-500" /> : <Copy className="w-4 h-4" />}
          </button>
          <button onClick={minimize} className="p-1 text-gray-400 hover:text-gray-600 dark:hover:text-gray-200 transition-colors" title="Minimize">
            <Minimize2 className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Context pills */}
      <ContextBar context={agentContext} />

      {/* Messages */}
      <div className="flex-1 overflow-y-auto px-4 py-3 space-y-3 min-h-[12rem] max-h-[22rem]" data-testid="ai-assistant-messages">
        {conversation.map((msg, i) => (
          <div key={i} className={`flex ${msg.role === 'user' ? 'justify-end' : 'justify-start'}`}>
            <div className="max-w-[85%]">
              <div
                className={`px-3 py-2 rounded-lg text-sm whitespace-pre-wrap break-words ${
                  msg.role === 'user'
                    ? 'bg-purple-600 text-white rounded-br-sm'
                    : 'bg-gray-100 dark:bg-gray-700 text-gray-900 dark:text-gray-100 rounded-bl-sm'
                }`}
              >
                {msg.content}
              </div>
              {msg.tools_used && msg.tools_used.length > 0 && (
                <div className="flex flex-wrap gap-1 mt-1">
                  {msg.tools_used.map((tool, j) => (
                    <span
                      key={j}
                      className="inline-flex items-center px-1.5 py-0.5 rounded text-[10px] font-mono bg-gray-200 text-gray-600 dark:bg-gray-600 dark:text-gray-300"
                    >
                      {tool}
                    </span>
                  ))}
                </div>
              )}
            </div>
          </div>
        ))}

        {/* Pending actions */}
        {pendingActions.length > 0 && (
          <div className="space-y-2" data-testid="ai-assistant-pending-actions">
            {pendingActions.map(action => (
              <ActionCard
                key={action.action_id}
                action={action}
                onApprove={() => confirmAction(action)}
                onReject={() => rejectAction(action)}
                disabled={isSending}
              />
            ))}
          </div>
        )}

        {isSending && (
          <div className="flex justify-start">
            <div className="bg-gray-100 dark:bg-gray-700 px-3 py-2 rounded-lg rounded-bl-sm text-sm text-gray-400">
              <span className="animate-pulse">Thinking...</span>
            </div>
          </div>
        )}
        <div ref={messagesEndRef} />
      </div>

      {/* Input */}
      <div className="border-t border-gray-200 dark:border-gray-700 px-3 py-2">
        <div className="flex items-end gap-2">
          <textarea
            ref={inputRef}
            value={input}
            onChange={e => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Ask about your data..."
            rows={1}
            className="flex-1 px-3 py-2 text-sm border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-700 text-gray-900 dark:text-gray-100 placeholder-gray-400 resize-none focus:outline-none focus:ring-2 focus:ring-purple-500"
            disabled={isSending}
            data-testid="ai-assistant-input"
          />
          <button
            onClick={handleSend}
            disabled={!input.trim() || isSending}
            className="p-2 bg-purple-600 text-white rounded-lg hover:bg-purple-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
            data-testid="ai-assistant-send"
          >
            <Send className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
}

// --- Context bar showing what the AI assistant knows about ---

const PAGE_LABELS: Record<string, string> = {
  dashboard: 'Dashboard',
  'entity-list': 'List',
  'entity-edit': 'Editing',
  'entity-create': 'Creating',
  'entity-view': 'Viewing',
  'revision-diff': 'Diff',
  'sheet-list': 'Sheets',
  'sheet-edit': 'Sheet',
};

function ContextBar({ context }: { context: import('../../api/client').AiAgentContext }) {
  const pills: { icon: typeof FileText; label: string; color: string }[] = [];

  if (context.current_page) {
    pills.push({
      icon: Layout,
      label: PAGE_LABELS[context.current_page] ?? context.current_page,
      color: 'bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300',
    });
  }

  if (context.entity_type) {
    pills.push({
      icon: Database,
      label: context.entity_type,
      color: 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300',
    });
  }

  if (context.entity_id != null) {
    pills.push({
      icon: Hash,
      label: `#${context.entity_id}`,
      color: 'bg-gray-100 text-gray-700 dark:bg-gray-700 dark:text-gray-300',
    });
  }

  if (context.current_fields) {
    const count = Object.keys(context.current_fields).length;
    if (count > 0) {
      pills.push({
        icon: FileText,
        label: `${count} field${count !== 1 ? 's' : ''}`,
        color: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300',
      });
    }
  }

  if (context.errors && context.errors.length > 0) {
    pills.push({
      icon: AlertTriangle,
      label: `${context.errors.length} error${context.errors.length !== 1 ? 's' : ''}`,
      color: 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300',
    });
  }

  if (pills.length === 0) return null;

  return (
    <div className="flex items-center gap-1.5 px-3 py-1.5 border-b border-gray-200 dark:border-gray-700 overflow-x-auto" data-testid="ai-assistant-context">
      {pills.map((pill, i) => (
        <span
          key={i}
          className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium whitespace-nowrap ${pill.color}`}
        >
          <pill.icon className="w-3 h-3" />
          {pill.label}
        </span>
      ))}
    </div>
  );
}

// --- Action card component ---

const ACTION_ICONS: Record<string, typeof Plus> = {
  create_entity: Plus,
  update_entity: Pencil,
  delete_entity: Trash2,
  set_field: Lightbulb,
};

const ACTION_COLORS: Record<string, string> = {
  create_entity: 'border-green-200 bg-green-50 dark:border-green-800 dark:bg-green-900/20',
  update_entity: 'border-blue-200 bg-blue-50 dark:border-blue-800 dark:bg-blue-900/20',
  delete_entity: 'border-red-200 bg-red-50 dark:border-red-800 dark:bg-red-900/20',
  set_field: 'border-yellow-200 bg-yellow-50 dark:border-yellow-800 dark:bg-yellow-900/20',
};

function ActionCard({
  action,
  onApprove,
  onReject,
  disabled,
}: {
  action: AiProposedAction;
  onApprove: () => void;
  onReject: () => void;
  disabled: boolean;
}) {
  const Icon = ACTION_ICONS[action.action_type] ?? MessageSquare;
  const colorClass = ACTION_COLORS[action.action_type] ?? 'border-gray-200 bg-gray-50 dark:border-gray-700 dark:bg-gray-800';

  return (
    <div className={`rounded-lg border p-3 ${colorClass}`} data-testid={`ai-action-${action.action_id}`}>
      <div className="flex items-start gap-2">
        <Icon className="w-4 h-4 mt-0.5 flex-shrink-0 text-gray-600 dark:text-gray-300" />
        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{action.description}</p>
          {action.entity_type && (
            <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">
              {action.entity_type}{action.entity_id ? ` #${action.entity_id}` : ''}
            </p>
          )}
        </div>
      </div>
      {action.requires_approval && (
        <div className="flex items-center gap-2 mt-2">
          <button
            onClick={onApprove}
            disabled={disabled}
            className="flex items-center gap-1 px-3 py-1 text-xs font-medium text-green-700 bg-green-100 hover:bg-green-200 dark:text-green-300 dark:bg-green-900/40 dark:hover:bg-green-900/60 rounded-md disabled:opacity-50 transition-colors"
            data-testid={`ai-action-approve-${action.action_id}`}
          >
            <CheckCircle className="w-3 h-3" />
            Apply
          </button>
          <button
            onClick={onReject}
            disabled={disabled}
            className="flex items-center gap-1 px-3 py-1 text-xs font-medium text-red-700 bg-red-100 hover:bg-red-200 dark:text-red-300 dark:bg-red-900/40 dark:hover:bg-red-900/60 rounded-md disabled:opacity-50 transition-colors"
            data-testid={`ai-action-reject-${action.action_id}`}
          >
            <XCircle className="w-3 h-3" />
            Reject
          </button>
        </div>
      )}
    </div>
  );
}

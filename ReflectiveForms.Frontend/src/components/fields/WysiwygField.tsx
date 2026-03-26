import { useRef, useEffect, useCallback, useState } from 'react';
import { useFormContext, Controller } from 'react-hook-form';
import { Bold, Italic, Underline, List, ListOrdered, Link as LinkIcon, Quote, Code, Heading1, Heading2, Undo, Redo } from 'lucide-react';
import { FieldComponentProps } from './types';

interface ToolbarAction {
  command: string;
  icon: React.ReactNode;
  title: string;
  value?: string;
}

const TOOLBAR_ACTIONS: ToolbarAction[] = [
  { command: 'bold', icon: <Bold className="w-4 h-4" />, title: 'Bold (Ctrl+B)' },
  { command: 'italic', icon: <Italic className="w-4 h-4" />, title: 'Italic (Ctrl+I)' },
  { command: 'underline', icon: <Underline className="w-4 h-4" />, title: 'Underline (Ctrl+U)' },
  { command: 'separator' } as ToolbarAction,
  { command: 'formatBlock', icon: <Heading1 className="w-4 h-4" />, title: 'Heading 1', value: 'h1' },
  { command: 'formatBlock', icon: <Heading2 className="w-4 h-4" />, title: 'Heading 2', value: 'h2' },
  { command: 'separator' } as ToolbarAction,
  { command: 'insertUnorderedList', icon: <List className="w-4 h-4" />, title: 'Bullet List' },
  { command: 'insertOrderedList', icon: <ListOrdered className="w-4 h-4" />, title: 'Numbered List' },
  { command: 'separator' } as ToolbarAction,
  { command: 'formatBlock', icon: <Quote className="w-4 h-4" />, title: 'Quote', value: 'blockquote' },
  { command: 'formatBlock', icon: <Code className="w-4 h-4" />, title: 'Code Block', value: 'pre' },
  { command: 'separator' } as ToolbarAction,
  { command: 'createLink', icon: <LinkIcon className="w-4 h-4" />, title: 'Insert Link' },
  { command: 'separator' } as ToolbarAction,
  { command: 'undo', icon: <Undo className="w-4 h-4" />, title: 'Undo (Ctrl+Z)' },
  { command: 'redo', icon: <Redo className="w-4 h-4" />, title: 'Redo (Ctrl+Y)' },
];

export function WysiwygField({ schema, path }: FieldComponentProps) {
  const { control } = useFormContext();

  return (
    <Controller
      name={path}
      control={control}
      render={({ field: { value, onChange }, fieldState: { error } }) => (
        <div>
          <WysiwygEditor
            content={value || ''}
            onChange={onChange}
            hasError={!!error}
            placeholder={schema.text_options?.placeholder}
          />
          {error && <p className="mt-1 text-sm text-red-600">{error.message}</p>}
        </div>
      )}
    />
  );
}

interface WysiwygEditorProps {
  content: string;
  onChange: (html: string) => void;
  hasError?: boolean;
  placeholder?: string;
}

function WysiwygEditor({ content, onChange, hasError, placeholder }: WysiwygEditorProps) {
  const editorRef = useRef<HTMLDivElement>(null);
  const [isSourceMode, setIsSourceMode] = useState(false);
  const [sourceContent, setSourceContent] = useState(content);

  // Initialize content
  useEffect(() => {
    if (editorRef.current && content !== editorRef.current.innerHTML) {
      editorRef.current.innerHTML = content;
    }
  }, [content]);

  const execCommand = useCallback((command: string, value?: string) => {
    if (command === 'createLink') {
      const url = prompt('Enter URL:');
      if (url) {
        document.execCommand(command, false, url);
      }
    } else if (value) {
      document.execCommand(command, false, value);
    } else {
      document.execCommand(command, false);
    }

    editorRef.current?.focus();
  }, []);

  const handleInput = useCallback(() => {
    if (editorRef.current) {
      const html = editorRef.current.innerHTML;
      onChange(html);
      setSourceContent(html);
    }
  }, [onChange]);

  const handleSourceChange = useCallback((e: React.ChangeEvent<HTMLTextAreaElement>) => {
    const html = e.target.value;
    setSourceContent(html);
    onChange(html);
  }, [onChange]);

  const toggleSourceMode = useCallback(() => {
    if (isSourceMode && editorRef.current) {
      editorRef.current.innerHTML = sourceContent;
    } else if (!isSourceMode && editorRef.current) {
      setSourceContent(editorRef.current.innerHTML);
    }
    setIsSourceMode(!isSourceMode);
  }, [isSourceMode, sourceContent]);

  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    // Handle tab key for indentation
    if (e.key === 'Tab') {
      e.preventDefault();
      document.execCommand('insertText', false, '    ');
    }
  }, []);

  return (
    <div className={`border rounded-lg overflow-hidden ${hasError ? 'border-red-500' : 'border-gray-300'}`}>
      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-0.5 p-2 border-b border-gray-200 bg-gray-50">
        {TOOLBAR_ACTIONS.map((action, index) => {
          if (action.command === 'separator') {
            return (
              <div key={index} className="w-px h-6 bg-gray-300 mx-1" />
            );
          }
          return (
            <button
              key={`${action.command}-${index}`}
              type="button"
              onClick={() => execCommand(action.command, action.value)}
              className="p-2 rounded hover:bg-gray-200 text-gray-600 hover:text-gray-900 transition-colors"
              title={action.title}
              disabled={isSourceMode}
            >
              {action.icon}
            </button>
          );
        })}

        <div className="flex-1" />

        {/* Source mode toggle */}
        <button
          type="button"
          onClick={toggleSourceMode}
          className={`px-2 py-1 text-xs rounded ${isSourceMode ? 'bg-blue-500 text-white' : 'bg-gray-200 text-gray-700'}`}
          title="Toggle source mode"
        >
          {isSourceMode ? 'Preview' : 'HTML'}
        </button>
      </div>

      {/* Editor area */}
      {isSourceMode ? (
        <textarea
          value={sourceContent}
          onChange={handleSourceChange}
          className="w-full min-h-[200px] p-4 font-mono text-sm resize-y focus:outline-none"
          placeholder="Enter HTML..."
        />
      ) : (
        <div
          ref={editorRef}
          contentEditable
          onInput={handleInput}
          onKeyDown={handleKeyDown}
          className="w-full min-h-[200px] p-4 prose prose-sm max-w-none focus:outline-none"
          data-placeholder={placeholder || 'Start writing...'}
          style={{
            WebkitUserModify: 'read-write',
          }}
        />
      )}

      {/* Character count (optional) */}
      {/* Character count */}
      <div className="px-4 py-2 border-t border-gray-100 bg-gray-50">
        <span className="text-xs text-gray-400">
          {isSourceMode
            ? `${sourceContent.length} characters (HTML)`
            : editorRef.current
              ? `${editorRef.current.innerText.length} characters`
              : '0 characters'
          }
        </span>
      </div>
    </div>
  );
}

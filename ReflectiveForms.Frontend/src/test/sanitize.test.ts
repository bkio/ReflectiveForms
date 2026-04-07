import { describe, it, expect } from 'vitest';
import { sanitizeHtml, sanitizeWysiwygHtml, stripHtml } from '../lib/sanitize';

describe('sanitize', () => {
  describe('sanitizeHtml', () => {
    it('should allow safe HTML tags', () => {
      const input = '<p>Hello <strong>World</strong></p>';
      const result = sanitizeHtml(input);
      expect(result).toBe('<p>Hello <strong>World</strong></p>');
    });

    it('should allow links with href', () => {
      const input = '<a href="https://example.com">Link</a>';
      const result = sanitizeHtml(input);
      expect(result).toContain('href="https://example.com"');
      expect(result).toContain('Link');
    });

    it('should strip script tags', () => {
      const input = '<p>Hello</p><script>alert("xss")</script>';
      const result = sanitizeHtml(input);
      expect(result).toBe('<p>Hello</p>');
      expect(result).not.toContain('script');
      expect(result).not.toContain('alert');
    });

    it('should strip onclick handlers', () => {
      const input = '<button onclick="alert(\'xss\')">Click</button>';
      const result = sanitizeHtml(input);
      expect(result).not.toContain('onclick');
    });

    it('should strip javascript: URLs', () => {
      const input = '<a href="javascript:alert(\'xss\')">Click</a>';
      const result = sanitizeHtml(input);
      expect(result).not.toContain('javascript:');
    });

    it('should strip onerror handlers', () => {
      const input = '<img src="x" onerror="alert(\'xss\')">';
      const result = sanitizeHtml(input);
      expect(result).not.toContain('onerror');
    });

    it('should strip style tags', () => {
      const input = '<style>body { display: none; }</style><p>Text</p>';
      const result = sanitizeHtml(input);
      expect(result).toBe('<p>Text</p>');
    });

    it('should strip iframe tags', () => {
      const input = '<iframe src="https://evil.com"></iframe>';
      const result = sanitizeHtml(input);
      expect(result).toBe('');
    });

    it('should allow formatting tags', () => {
      const input = '<em>italic</em> <code>code</code> <u>underline</u>';
      const result = sanitizeHtml(input);
      expect(result).toBe('<em>italic</em> <code>code</code> <u>underline</u>');
    });

    it('should allow lists', () => {
      const input = '<ul><li>Item 1</li><li>Item 2</li></ul>';
      const result = sanitizeHtml(input);
      expect(result).toBe('<ul><li>Item 1</li><li>Item 2</li></ul>');
    });

    it('should allow headings', () => {
      const input = '<h1>Title</h1><h2>Subtitle</h2>';
      const result = sanitizeHtml(input);
      expect(result).toBe('<h1>Title</h1><h2>Subtitle</h2>');
    });
  });

  describe('sanitizeWysiwygHtml', () => {
    it('should allow images', () => {
      const input = '<img src="https://example.com/image.jpg" alt="Test">';
      const result = sanitizeWysiwygHtml(input);
      expect(result).toContain('src="https://example.com/image.jpg"');
      expect(result).toContain('alt="Test"');
    });

    it('should allow tables', () => {
      const input = '<table><tr><td>Cell</td></tr></table>';
      const result = sanitizeWysiwygHtml(input);
      expect(result).toContain('<table>');
      expect(result).toContain('<td>Cell</td>');
    });

    it('should allow figures and figcaptions', () => {
      const input = '<figure><img src="img.jpg"><figcaption>Caption</figcaption></figure>';
      const result = sanitizeWysiwygHtml(input);
      expect(result).toContain('<figure>');
      expect(result).toContain('<figcaption>Caption</figcaption>');
    });

    it('should still strip scripts', () => {
      const input = '<p>Text</p><script>alert("xss")</script>';
      const result = sanitizeWysiwygHtml(input);
      expect(result).not.toContain('script');
    });

    it('should still strip event handlers', () => {
      const input = '<img src="x" onerror="alert(\'xss\')">';
      const result = sanitizeWysiwygHtml(input);
      expect(result).not.toContain('onerror');
    });
  });

  describe('stripHtml', () => {
    it('should remove all HTML tags', () => {
      const input = '<p>Hello <strong>World</strong></p>';
      const result = stripHtml(input);
      expect(result).toBe('Hello World');
    });

    it('should remove links but keep text', () => {
      const input = '<a href="https://example.com">Click here</a>';
      const result = stripHtml(input);
      expect(result).toBe('Click here');
    });

    it('should handle nested tags', () => {
      const input = '<div><p><span>Nested</span> text</p></div>';
      const result = stripHtml(input);
      expect(result).toBe('Nested text');
    });

    it('should handle empty input', () => {
      const result = stripHtml('');
      expect(result).toBe('');
    });

    it('should handle plain text', () => {
      const result = stripHtml('Just plain text');
      expect(result).toBe('Just plain text');
    });
  });
});

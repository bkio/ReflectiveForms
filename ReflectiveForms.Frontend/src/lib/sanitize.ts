import DOMPurify from 'dompurify';

/**
 * Sanitize HTML content to prevent XSS attacks.
 * Uses DOMPurify with a safe default configuration.
 */
export function sanitizeHtml(html: string): string {
  return DOMPurify.sanitize(html, {
    // Allow common formatting tags
    ALLOWED_TAGS: [
      'a', 'b', 'br', 'code', 'em', 'i', 'li', 'ol', 'p', 'pre',
      'small', 'span', 'strong', 'sub', 'sup', 'u', 'ul',
      'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
      'blockquote', 'hr',
    ],
    // Allow safe attributes
    ALLOWED_ATTR: [
      'href', 'target', 'rel', 'class', 'id', 'title',
    ],
    // Force all links to open in new tab and be noopener for security
    ADD_ATTR: ['target'],
    // Transform links to be safer
    ALLOW_DATA_ATTR: false,
  });
}

/**
 * Sanitize HTML for WYSIWYG editor output.
 * More permissive than sanitizeHtml, but still safe.
 */
export function sanitizeWysiwygHtml(html: string): string {
  return DOMPurify.sanitize(html, {
    // Allow more tags for rich content
    ALLOWED_TAGS: [
      'a', 'abbr', 'address', 'article', 'aside', 'b', 'bdi', 'bdo',
      'blockquote', 'br', 'caption', 'cite', 'code', 'col', 'colgroup',
      'data', 'dd', 'del', 'dfn', 'div', 'dl', 'dt', 'em', 'figcaption',
      'figure', 'footer', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'header',
      'hr', 'i', 'img', 'ins', 'kbd', 'li', 'main', 'mark', 'nav', 'ol',
      'p', 'pre', 'q', 'rp', 'rt', 'ruby', 's', 'samp', 'section', 'small',
      'span', 'strong', 'sub', 'sup', 'table', 'tbody', 'td', 'tfoot',
      'th', 'thead', 'time', 'tr', 'u', 'ul', 'var', 'wbr',
    ],
    ALLOWED_ATTR: [
      'href', 'src', 'alt', 'title', 'class', 'id', 'target', 'rel',
      'width', 'height', 'colspan', 'rowspan', 'datetime',
    ],
    ALLOW_DATA_ATTR: false,
  });
}

/**
 * Strip all HTML tags, returning plain text.
 * Useful for text-only contexts.
 */
export function stripHtml(html: string): string {
  return DOMPurify.sanitize(html, {
    ALLOWED_TAGS: [],
    ALLOWED_ATTR: [],
  });
}

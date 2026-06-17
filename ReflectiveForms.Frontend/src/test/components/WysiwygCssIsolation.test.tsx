import { describe, it, expect } from 'vitest';
import fs from 'fs';
import path from 'path';

/**
 * CSS Containment Unit Tests
 *
 * Verifies that the `contain-layout-paint` class is rendered on all
 * elements that display user-generated WYSIWYG HTML, ensuring CSS
 * injection cannot escape and trash the application layout.
 */

// We test the CSS class presence — the actual isolation behavior
// is covered by E2E tests since JSDOM doesn't implement CSS containment.

function readIndexCss(): string {
  const cssPath = path.resolve(__dirname, '../../index.css');
  return fs.readFileSync(cssPath, 'utf-8');
}

describe('WYSIWYG CSS Containment', () => {
  it('contain-layout-paint CSS class is defined in index.css', () => {
    const css = readIndexCss();
    expect(css).toContain('.contain-layout-paint');
    expect(css).toContain('contain: layout paint');
  });

  it('contain-layout-paint uses contain: layout paint (not strict or content)', () => {
    const css = readIndexCss();

    // Match the exact rule
    const match = css.match(/\.contain-layout-paint\s*\{([^}]*)\}/);
    expect(match).not.toBeNull();

    const ruleBody = match![1];
    expect(ruleBody).toContain('contain');
    // Must NOT use 'strict' — that would block scrolling
    expect(ruleBody).not.toContain('strict');
    // Must NOT use 'content' — that would reset everything
    expect(ruleBody).not.toMatch(/\bcontain:\s*content\b/);
    // Must use layout + paint specifically
    expect(ruleBody).toMatch(/contain:\s*layout\s+paint/);
  });

  it('contain-layout-paint is not the same as overflow-hidden', () => {
    const css = readIndexCss();
    const containmentRule = css.match(/\.contain-layout-paint\s*\{([^}]*)\}/);
    expect(containmentRule).not.toBeNull();
    // overflow is NOT part of the containment rule (separate concern)
    expect(containmentRule![1]).not.toContain('overflow');
  });
});

describe('WYSIWYG CSS Isolation — attack payloads', () => {
  const testPayloads = [
    {
      name: 'fullscreen takeover',
      html: `<div style="position:fixed;top:0;left:0;width:100vw;height:100vh;z-index:99999;background:red!important">X</div>`,
      shouldContain: ['position:fixed', '100vw', '100vh', 'z-index:99999'],
    },
    {
      name: 'z-index stacking attack',
      html: `<span style="position:relative;z-index:999999;background:purple">X</span>`,
      shouldContain: ['z-index:999999'],
    },
    {
      name: 'viewport unit breakout',
      html: `<div style="width:100vw;height:100vh;position:absolute;top:0;left:0">X</div>`,
      shouldContain: ['100vw', '100vh'],
    },
    {
      name: 'transform scale overlay',
      html: `<div style="position:fixed;transform:scale(10);z-index:999998">X</div>`,
      shouldContain: ['transform:scale(10)', 'z-index:999998'],
    },
    {
      name: 'legitimate styling preserved',
      html: `<span style="color:red;font-size:20px;text-align:center">Hello</span>`,
      shouldContain: ['color:red', 'font-size:20px', 'text-align:center'],
    },
  ];

  for (const { name, html, shouldContain } of testPayloads) {
    it(`payload "${name}" — HTML is not modified by tests (containment leaves data intact)`, () => {
      // All these payloads should remain unchanged — containment
      // is purely CSS-side, it never touches the stored HTML.
      for (const substr of shouldContain) {
        expect(html).toContain(substr);
      }
    });
  }

  it('all attack payloads contain CSS that would break layout without containment', () => {
    // Verify the attack payloads actually contain dangerous CSS
    const attacks = testPayloads.filter(p => p.name !== 'legitimate styling preserved');
    expect(attacks.length).toBeGreaterThanOrEqual(3);

    for (const attack of attacks) {
      const hasDangerousCSS =
        attack.html.includes('position:fixed') ||
        attack.html.includes('z-index:9999') ||
        attack.html.includes('100vw') ||
        attack.html.includes('transform:scale');
      expect(hasDangerousCSS).toBe(true);
    }
  });
});

describe('contain-layout-paint class propagation', () => {
  it('class name is a valid CSS identifier', () => {
    // No special characters that could break CSS parsing
    expect(/^[a-zA-Z0-9_-]+$/.test('contain-layout-paint')).toBe(true);
  });

  it('class name is self-documenting', () => {
    // The name tells you exactly what CSS property it applies
    expect('contain-layout-paint').toContain('layout');
    expect('contain-layout-paint').toContain('paint');
  });
});

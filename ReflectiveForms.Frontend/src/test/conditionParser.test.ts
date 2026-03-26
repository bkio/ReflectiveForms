import { describe, it, expect } from 'vitest';
import { evaluateCondition, evaluateCompoundCondition } from '../lib/conditionParser';

describe('conditionParser', () => {
  describe('evaluateCondition', () => {
    it('should evaluate simple equality with string value', () => {
      const formValues = { field1: 'test' };
      expect(evaluateCondition("field1 == 'test'", formValues)).toBe(true);
      expect(evaluateCondition("field1 == 'other'", formValues)).toBe(false);
    });

    it('should evaluate simple inequality', () => {
      const formValues = { field1: 'test' };
      expect(evaluateCondition("field1 != 'other'", formValues)).toBe(true);
      expect(evaluateCondition("field1 != 'test'", formValues)).toBe(false);
    });

    it('should evaluate boolean values', () => {
      const formValues = { isActive: true };
      expect(evaluateCondition('isActive == true', formValues)).toBe(true);
      expect(evaluateCondition('isActive == false', formValues)).toBe(false);
    });

    it('should evaluate numeric comparisons', () => {
      const formValues = { count: 10 };
      expect(evaluateCondition('count > 5', formValues)).toBe(true);
      expect(evaluateCondition('count < 5', formValues)).toBe(false);
      expect(evaluateCondition('count >= 10', formValues)).toBe(true);
      expect(evaluateCondition('count <= 10', formValues)).toBe(true);
    });

    it('should evaluate nested field paths', () => {
      const formValues = {
        user: {
          profile: {
            name: 'John'
          }
        }
      };
      expect(evaluateCondition("user.profile.name == 'John'", formValues)).toBe(true);
      expect(evaluateCondition("user.profile.name == 'Jane'", formValues)).toBe(false);
    });

    it('should handle undefined fields gracefully', () => {
      const formValues = {};
      expect(evaluateCondition("nonexistent == ''", formValues)).toBe(true);
      expect(evaluateCondition("nonexistent != 'something'", formValues)).toBe(true);
    });

    it('should handle null values', () => {
      const formValues = { field1: null };
      expect(evaluateCondition("field1 == ''", formValues)).toBe(true);
    });

    it('should return true for unparseable conditions', () => {
      const formValues = { field1: 'test' };
      expect(evaluateCondition('invalid condition format', formValues)).toBe(true);
    });
  });

  describe('evaluateCompoundCondition', () => {
    it('should evaluate AND conditions', () => {
      const formValues = { field1: 'a', field2: 'b' };
      expect(evaluateCompoundCondition("field1 == 'a' && field2 == 'b'", formValues)).toBe(true);
      expect(evaluateCompoundCondition("field1 == 'a' && field2 == 'c'", formValues)).toBe(false);
    });

    it('should evaluate OR conditions', () => {
      const formValues = { field1: 'a', field2: 'b' };
      expect(evaluateCompoundCondition("field1 == 'a' || field2 == 'c'", formValues)).toBe(true);
      expect(evaluateCompoundCondition("field1 == 'x' || field2 == 'b'", formValues)).toBe(true);
      expect(evaluateCompoundCondition("field1 == 'x' || field2 == 'y'", formValues)).toBe(false);
    });

    it('should handle complex compound conditions', () => {
      const formValues = { type: 'user', role: 'admin', active: true };
      expect(
        evaluateCompoundCondition("type == 'user' && role == 'admin'", formValues)
      ).toBe(true);
      expect(
        evaluateCompoundCondition("type == 'guest' || role == 'admin'", formValues)
      ).toBe(true);
    });
  });
});

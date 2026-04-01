import { describe, it, expect } from 'vitest';
import { humanizeSanityError } from '../../components/form/DynamicForm';

describe('humanizeSanityError', () => {
  it('strips "Sanity check for ... has failed with" prefix', () => {
    const msg = 'Sanity check for blog-post, entity id: 42 has failed with Something went wrong';
    expect(humanizeSanityError(msg)).toBe('Something went wrong');
  });

  it('passes through title length error from backend', () => {
    const msg = 'Title must be between 1 and 256 characters.';
    expect(humanizeSanityError(msg)).toBe('Title must be between 1 and 256 characters.');
  });

  it('passes through title missing error', () => {
    const msg = 'Title is missing or incorrect.';
    expect(humanizeSanityError(msg)).toBe('Title is missing or incorrect.');
  });

  it('passes through title cannot be empty', () => {
    const msg = 'Title cannot be empty.';
    expect(humanizeSanityError(msg)).toBe('Title cannot be empty.');
  });

  it('passes through title uniqueness error', () => {
    const msg = 'Title of the entity must be globally unique.';
    expect(humanizeSanityError(msg)).toBe('Title of the entity must be globally unique.');
  });

  it('passes through field label error from backend', () => {
    const msg = 'E-Mail Address: Should have at least one character.';
    expect(humanizeSanityError(msg)).toBe('E-Mail Address: Should have at least one character.');
  });

  it('passes through relation mandatory error from backend', () => {
    const msg = 'Role is mandatory, but missing.';
    expect(humanizeSanityError(msg)).toBe('Role is mandatory, but missing.');
  });

  it('strips prefix AND passes through readable error', () => {
    const msg = 'Sanity check for page, entity id: 5 has failed with Title must be between 1 and 256 characters.';
    expect(humanizeSanityError(msg)).toBe('Title must be between 1 and 256 characters.');
  });

  it('leaves unrecognized messages mostly intact', () => {
    const msg = 'Something unexpected happened';
    expect(humanizeSanityError(msg)).toBe('Something unexpected happened');
  });

  it('handles mandatory date fields error', () => {
    const msg = 'Fields Date, Date GMT, Modified and Modified GMT are mandatory.';
    expect(humanizeSanityError(msg)).toBe('Fields Date, Date GMT, Modified and Modified GMT are mandatory.');
  });
});

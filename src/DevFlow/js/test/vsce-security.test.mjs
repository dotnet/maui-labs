import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import test from 'node:test';

const inspectorRequire = createRequire(new URL('../vscode-inspector/package.json', import.meta.url));
const vsceRequire = createRequire(inspectorRequire.resolve('@vscode/vsce/package.json'));
const { Minimatch } = vsceRequire('minimatch');

test('VSCE glob matching bounds recursive-parent pattern expansion', () => {
  const pattern = Array(8).fill('**/../a/b').join('/');
  let matcher;
  try {
    matcher = new Minimatch(pattern, { optimizationLevel: 2, braceExpandMax: 64 });
  } catch (error) {
    if (!(error instanceof RangeError))
      throw error;
    assert.match(error.message, /too many patterns/i);
    return;
  }
  assert.ok(matcher.set.length <= 64, 'pattern expansion exceeded its limit');
});

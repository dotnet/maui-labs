#!/usr/bin/env node
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { existsSync, lstatSync, readdirSync, readFileSync, realpathSync } from 'node:fs';
import { extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const excluded = new Set(['.git', '.vs', '.idea', 'bin', 'obj', 'artifacts', 'node_modules', '.packages']);
const limitations = 'Textual warning scan, not C# semantic analysis or migration certification. '
  + 'Does not evaluate projects, preprocessor branches, linked/generated sources, aliases, '
  + 'interpolation expressions, or arbitrary helper/loop behavior. Inspect those paths separately.';

// Preserve offsets and line numbers while excluding comments and literal text.
function codeOnly(source) {
  return source.replace(
    /\/\/[^\r\n]*|\/\*[\s\S]*?\*\/|("{3,})[\s\S]*?\1|@"(?:[^"]|"")*"|"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'/g,
    value => value.replace(/[^\r\n]/g, ' '));
}

function closing(code, start, open, close) {
  let depth = 0;
  for (let index = start; index < code.length; index++) {
    if (code[index] === open) depth++;
    if (code[index] === close && --depth === 0) return index;
  }
  return -1;
}

export function auditSource(source, file) {
  const code = codeOnly(source);
  const findings = [];
  const add = (rule, index, message) => findings.push({
    rule, file, line: source.slice(0, index).split('\n').length, severity: 'warning', message,
  });
  const classes = [...code.matchAll(/\bclass\s+(\w+)([^;{}]*)\{/g)].map(match => ({
    name: match[1], header: match[2], start: match.index,
    end: closing(code, match.index + match[0].length - 1, '{', '}'),
  }));
  for (const match of code.matchAll(/(?<![\w.])(?:this\s*\.\s*|base\s*\.\s*)?\bWindow\s*(?:[?!]\s*)?\./g)) {
    if (code.slice(0, match.index).trimEnd().endsWith('.')) continue;
    const owner = classes.filter(type => type.start < match.index && type.end > match.index)
      .sort((left, right) => right.start - left.start)[0];
    if (owner && (/AppDelegate$/.test(owner.name) || /\b(?:MauiUIApplicationDelegate|UIApplicationDelegate)\b/.test(owner.header))) {
      add('X27_APP_WINDOW', match.index,
        'Possible app-delegate Window access. Under MAUI scenes this window is null, even in '
        + 'FinishedLaunching. Trace window-dependent work to the owning scene; retain fallback code only with an explicit justification.');
    }
  }

  for (const match of code.matchAll(/\bUrlContexts\b/g)) {
    let cursor = match.index + match[0].length;
    while (cursor < code.length) {
      while (/\s/.test(code[cursor] ?? '')) cursor++;
      if (code[cursor] === '!' && code[cursor + 1] !== '=') {
        cursor++;
        continue;
      }
      if (code[cursor] === '?' && code[cursor + 1] === '[') cursor++;
      if (code[cursor] === '[') {
        const end = closing(code, cursor, '[', ']');
        if (end < 0) break;
        const index = code.slice(cursor + 1, end).trim();
        if (index && !index.includes('..')) {
          add('X27_SINGLE_URL', cursor,
            'Indexed selection from UrlContexts may discard other cold URLs. Verify every context still reaches its original behavior.');
        }
        break;
      }
      const member = /^(?:\?\.|\.)\s*(\w+)/.exec(code.slice(cursor));
      if (!member) break;
      const memberStart = cursor;
      cursor += member[0].length;
      while (/\s/.test(code[cursor] ?? '')) cursor++;
      if (code[cursor] === '<') {
        const end = closing(code, cursor, '<', '>');
        if (end < 0) break;
        cursor = end + 1;
        while (/\s/.test(code[cursor] ?? '')) cursor++;
      }
      if (code[cursor] !== '(') continue;
      const end = closing(code, cursor, '(', ')');
      if (end < 0) break;
      const argumentsText = code.slice(cursor + 1, end).trim();
      if (/^(?:First|FirstOrDefault|Single|SingleOrDefault|ElementAt|ElementAtOrDefault)$/.test(member[1])
          || (member[1] === 'Take' && argumentsText === '1')) {
        add('X27_SINGLE_URL', memberStart,
          `${member[1]} selection from UrlContexts may discard other cold URLs. `
          + 'Verify all contexts are processed; selecting one for an additional preview is not itself proof of loss.');
        break;
      }
      cursor = end + 1;
    }
  }
  for (const match of code.matchAll(/\b([A-Za-z_]\w*)\s*\.\s*RootViewController\s*!?\s*\.\s*Title\s*=/g)) {
    const variable = match[1].replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const prefix = code.slice(Math.max(0, match.index - 500), match.index);
    const guarded = new RegExp(
      `if\\s*\\(\\s*${variable}\\s*\\.\\s*RootViewController\\s*(?:is\\s+not\\s+null|!=\\s*null)\\s*\\)\\s*\\{?\\s*$`)
      .test(prefix)
      || new RegExp(
        `if\\s*\\(\\s*${variable}\\s*\\.\\s*RootViewController\\s*(?:is\\s+null|==\\s*null)\\s*\\)\\s*(?:return|throw\\b[^;]*;)\\s*$`)
        .test(prefix);
    if (!guarded) {
      add('X27_UNGUARDED_ROOT', match.index,
        'Cold-launch work directly dereferences RootViewController without a recognized null guard. '
        + 'Guard the scene window root before applying the side effect.');
    }
  }
  return findings;
}

export function auditDirectory(directory) {
  const root = resolve(directory);
  const report = { root, status: '', filesScanned: 0, excludedDirectories: [], findings: [], errors: [], limitations };
  const sources = [];
  function walk(path) {
    const name = relative(root, path).replaceAll('\\', '/') || '.';
    try {
      const entry = lstatSync(path);
      if (entry.isSymbolicLink()) {
        report.errors.push({ file: name, message: 'Symbolic link not followed; audit its intended source separately.' });
      } else if (entry.isDirectory()) {
        for (const child of readdirSync(path).sort()) {
          if (excluded.has(child.toLowerCase())) report.excludedDirectories.push(`${name}/${child}`);
          else walk(join(path, child));
        }
      } else if (entry.isFile() && extname(path).toLowerCase() === '.cs') {
        const bytes = readFileSync(path);
        const encoding = bytes[0] === 0xff && bytes[1] === 0xfe ? 'utf-16le'
          : bytes[0] === 0xfe && bytes[1] === 0xff ? 'utf-16be' : 'utf-8';
        const source = new TextDecoder(encoding, { fatal: true }).decode(bytes);
        sources.push({ file: name, source, code: codeOnly(source) });
        report.findings.push(...auditSource(source, name));
        report.filesScanned++;
      }
    } catch (error) {
      report.errors.push({ file: name, message: error.message });
    }
  }
  try {
    if (!lstatSync(root).isDirectory()) throw new Error('Supply an app source directory, not a project file or symbolic link.');
    walk(root);
  } catch (error) {
    report.errors.push({ file: '.', message: error.message });
  }
  const sceneActivityHandler = sources.some(({ code }) =>
    /\boverride\s+bool\s+ContinueUserActivity\s*\(\s*UIScene\b/.test(code)
    || /\.\s*SceneContinueUserActivity\s*\(/.test(code));
  if (sceneActivityHandler) {
    for (const { file, source, code } of sources) {
      for (const match of code.matchAll(/\.\s*ContinueUserActivity\s*\(\s*\(/g)) {
        report.findings.push({
          rule: 'X27_DUPLICATE_ACTIVITY',
          file,
          line: source.slice(0, match.index).split('\n').length,
          severity: 'warning',
          message: 'Application-level ContinueUserActivity remains registered alongside scene activity handling. '
            + 'Move the original behavior to one scene path and remove the obsolete registration to avoid dead or duplicate delivery.',
        });
      }
    }
  }
  if (report.filesScanned === 0) report.errors.push({ file: '.', message: 'No C# source files scanned.' });
  report.status = report.errors.length ? 'incomplete' : report.findings.length ? 'review-required' : 'no-patterns-found';
  return report;
}

function main(argumentsList) {
  const paths = argumentsList.filter(argument => argument !== '--json');
  if (paths.length !== 1 || paths[0].startsWith('-')) {
    console.error('Usage: node audit-lifecycle.mjs <app-source-directory> [--json]');
    return 2;
  }
  const report = auditDirectory(paths[0]);
  if (argumentsList.includes('--json')) console.log(JSON.stringify(report, null, 2));
  else {
    console.log(`${report.status}: scanned ${report.filesScanned} C# files under ${report.root}`);
    for (const finding of report.findings)
      console.log(`${finding.file}:${finding.line} ${finding.rule}: ${finding.message}`);
    for (const error of report.errors) console.error(`${error.file}: ${error.message}`);
    console.log(report.limitations);
  }
  return report.errors.length ? 2 : report.findings.length ? 1 : 0;
}

if (process.argv[1] && existsSync(process.argv[1])
    && realpathSync(fileURLToPath(import.meta.url)) === realpathSync(process.argv[1]))
  process.exitCode = main(process.argv.slice(2));

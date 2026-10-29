import test from 'node:test';
import assert from 'node:assert/strict';
import {
  countSyllables,
  hasPauseAfter,
  parseLrcTimestamp,
  estimateLineWords,
  parseEnhancedLineWords,
  parseLrc
} from '../dist/src/lyricsParser.js';

test('countSyllables - counts Cyrillic and Latin vowels with minimum 1', () => {
  // Latin vowels
  assert.strictEqual(countSyllables('hello'), 2); // e, o
  assert.strictEqual(countSyllables('world'), 1); // o
  assert.strictEqual(countSyllables('sky'), 1); // y
  assert.strictEqual(countSyllables('rhythm'), 1); // y
  assert.strictEqual(countSyllables('a'), 1); // a

  // Words without vowels get weight 1 (минимум 1)
  assert.strictEqual(countSyllables('shh'), 1);
  assert.strictEqual(countSyllables('в'), 1);
  assert.strictEqual(countSyllables('с'), 1);
  assert.strictEqual(countSyllables('к'), 1);

  // Cyrillic vowels
  assert.strictEqual(countSyllables('привет'), 2); // и, е
  assert.strictEqual(countSyllables('мир'), 1); // и
  assert.strictEqual(countSyllables('прекрасный'), 3); // е, а, ы
  assert.strictEqual(countSyllables('солнце'), 2); // о, е
  assert.strictEqual(countSyllables('я'), 1); // я
  assert.strictEqual(countSyllables('привіт'), 2); // и, і (Ukrainian)

  // Case insensitivity
  assert.strictEqual(countSyllables('HELLO'), 2);
  assert.strictEqual(countSyllables('МИР'), 1);
});

test('hasPauseAfter - detects pause punctuation (+120 ms after , . ! ? …)', () => {
  assert.strictEqual(hasPauseAfter('hello,'), true);
  assert.strictEqual(hasPauseAfter('world.'), true);
  assert.strictEqual(hasPauseAfter('stop!'), true);
  assert.strictEqual(hasPauseAfter('why?'), true);
  assert.strictEqual(hasPauseAfter('wait…'), true);
  assert.strictEqual(hasPauseAfter('wait...'), true);
  assert.strictEqual(hasPauseAfter('really?!'), true);
  assert.strictEqual(hasPauseAfter('"quoted,"'), true);

  assert.strictEqual(hasPauseAfter('hello'), false);
  assert.strictEqual(hasPauseAfter('world'), false);
  assert.strictEqual(hasPauseAfter('Aura'), false);
});

test('parseLrcTimestamp - parses mm:ss.xx and mm:ss.xxx into milliseconds', () => {
  assert.strictEqual(parseLrcTimestamp('01', '23', '45'), 83450);
  assert.strictEqual(parseLrcTimestamp('00', '12', '34'), 12340);
  assert.strictEqual(parseLrcTimestamp('00', '05', '5'), 5500);
  assert.strictEqual(parseLrcTimestamp('00', '05', '50'), 5500);
  assert.strictEqual(parseLrcTimestamp('00', '05', '500'), 5500);
  assert.strictEqual(parseLrcTimestamp('00', '01', '05'), 1050);
  assert.strictEqual(parseLrcTimestamp('02', '00'), 120000);
});

test('estimateLineWords - distributes duration by syllable weight and adds 120ms pauses', () => {
  // Line duration = 3000 ms, lineStart = 1000 ms
  // Words: "Stop!" (weight 1, pause 120) and "Listen" (weight 2, no pause)
  const words = ['Stop!', 'Listen'];
  const res = estimateLineWords(words, 1000, 3000);

  assert.strictEqual(res.length, 2);
  assert.strictEqual(res[0].w, 'Stop!');
  assert.strictEqual(res[0].t, 1000);

  // Stop! has pause 120ms, so Listen starts at res[0].t + res[0].d + 120
  assert.strictEqual(res[1].w, 'Listen');
  assert.strictEqual(res[1].t, res[0].t + res[0].d + 120);

  // Proportions: availableSinging = 3000 - 120 = 2880 ms
  // Stop! weight 1, Listen weight 2 -> totalWeight 3
  // res[0].d = round(2880 * 1/3) = 960 ms
  // res[1].d = 2880 - 960 = 1920 ms (twice duration of Stop!)
  assert.strictEqual(res[0].d, 960);
  assert.strictEqual(res[1].d, 1920);

  // Total elapsed time equals totalDuration
  const totalElapsed = (res[1].t + res[1].d) - 1000;
  assert.strictEqual(totalElapsed, 3000);
});

test('parseEnhancedLineWords - parses <mm:ss.xx> tags inside a line', () => {
  const line = '<00:10.00> Never <00:10.50> gonna <00:11.00> give <00:12.00> you <00:12.50> up <00:13.00>';
  const { text, words } = parseEnhancedLineWords(line, 10000, 4000);

  assert.strictEqual(text, 'Never gonna give you up');
  assert.strictEqual(words.length, 5);

  assert.deepStrictEqual(words[0], { t: 10000, d: 500, w: 'Never' });
  assert.deepStrictEqual(words[1], { t: 10500, d: 500, w: 'gonna' });
  assert.strictEqual(words[1].t, 10500);
  assert.strictEqual(words[1].d, 500);
  assert.strictEqual(words[2].t, 11000);
  assert.strictEqual(words[2].d, 1000);
  assert.strictEqual(words[2].w, 'give');
  assert.strictEqual(words[3].t, 12000);
  assert.strictEqual(words[3].d, 500);
  assert.strictEqual(words[3].w, 'you');
  assert.strictEqual(words[4].t, 12500);
  assert.strictEqual(words[4].d, 500);
  assert.strictEqual(words[4].w, 'up');
});

test('parseEnhancedLineWords - handles line with prefix and without trailing closing tag', () => {
  const line = 'Hello <00:11.00> world';
  const { text, words } = parseEnhancedLineWords(line, 10000, 3000);

  assert.strictEqual(text, 'Hello world');
  assert.strictEqual(words.length, 2);
  assert.strictEqual(words[0].w, 'Hello');
  assert.strictEqual(words[0].t, 10000);
  assert.strictEqual(words[0].d, 1000);

  assert.strictEqual(words[1].w, 'world');
  assert.strictEqual(words[1].t, 11000);
  // lineEnd is 10000 + 3000 = 13000, so d is 13000 - 11000 = 2000
  assert.strictEqual(words[1].d, 2000);
});

test('parseLrc - parses full LRC and enforces maximum 8s line duration cap', () => {
  const lrc = `
[ti:Synthetic Test Song]
[ar:Aura Team]
[00:01.00] First line of test
[00:20.00] Second line arrives 19s later
[00:25.00] Third line
`;

  const parsed = parseLrc(lrc);
  assert.strictEqual(parsed.source, 'lrclib');
  assert.strictEqual(parsed.synced, true);
  assert.strictEqual(parsed.lines.length, 3);

  // Line 0: t = 1000 ms. Next line is at 20000 ms (19s later).
  // Rule: line duration = up to next line (maximum 8 s = 8000 ms)
  const line0 = parsed.lines[0];
  assert.strictEqual(line0.t, 1000);
  assert.strictEqual(line0.text, 'First line of test');
  assert.strictEqual(line0.words.length, 4);

  // The last word in line 0 ends at line0.t + duration
  const lastWord = line0.words[line0.words.length - 1];
  const totalElapsed = (lastWord.t + lastWord.d) - line0.t;
  // Should not exceed 8000 ms
  assert.ok(totalElapsed <= 8000, `totalElapsed was ${totalElapsed}, expected <= 8000`);

  // Line 1: t = 20000 ms. Next line at 25000 ms (diff = 5000 ms < 8000 ms).
  const line1 = parsed.lines[1];
  assert.strictEqual(line1.t, 20000);
  assert.strictEqual(line1.text, 'Second line arrives 19s later');
});

test('parseLrc - parses enhanced LRC lines when present', () => {
  const lrc = `
[00:02.00] <00:02.00> Test <00:02.80> enhanced <00:03.50>
[00:05.00] Regular fallback line
`;
  const parsed = parseLrc(lrc);
  assert.strictEqual(parsed.lines.length, 2);

  const enhancedLine = parsed.lines[0];
  assert.strictEqual(enhancedLine.t, 2000);
  assert.strictEqual(enhancedLine.text, 'Test enhanced');
  assert.strictEqual(enhancedLine.words.length, 2);
  assert.strictEqual(enhancedLine.words[0].t, 2000);
  assert.strictEqual(enhancedLine.words[0].d, 800);
  assert.strictEqual(enhancedLine.words[0].w, 'Test');
  assert.strictEqual(enhancedLine.words[1].t, 2800);
  assert.strictEqual(enhancedLine.words[1].d, 700);
  assert.strictEqual(enhancedLine.words[1].w, 'enhanced');
});

test('parseLrc - handles unsynced plain lyrics fallback', () => {
  const plainText = `Line one of song\nLine two of song\nLine three`;
  const parsed = parseLrc(plainText);

  assert.strictEqual(parsed.synced, false);
  assert.strictEqual(parsed.lines.length, 3);
  assert.strictEqual(parsed.lines[0].text, 'Line one of song');
  assert.strictEqual(parsed.lines[0].t, 0);
  assert.deepStrictEqual(parsed.lines[0].words, []);
});

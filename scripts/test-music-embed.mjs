import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const root = new URL('../', import.meta.url);
const read = path => readFileSync(new URL(path, root), 'utf8');
const normalize = value => JSON.parse(JSON.stringify(value));
const wrapperHtml = read('lobby-api/public/embed/player.html');
const clientHtml = read('docs/redesign/ingame/player.html');
const scripts = html => [...html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)]
  .filter(match => !/\bsrc\s*=/.test(match[1])).map(match => match[2]);
const between = (source, start, end) => {
  const from = source.indexOf(start);
  const to = source.indexOf(end, from + start.length);
  assert.ok(from >= 0 && to > from, `Source anchors missing: ${start} / ${end}`);
  return source.slice(from, to);
};
const parentOrigin = 'http://127.0.0.1:49812';
const embedOrigin = 'https://lobby-api.vercel.app';
const channel = '0123456789abcdef0123456789abcdef';
const videoId = 'M7lc1UVf-VE';
let passed = 0;
let failed = 0;

async function test(name, callback) {
  try {
    await callback();
    passed++;
    process.stdout.write(`PASS ${name}\n`);
  } catch (error) {
    failed++;
    process.stderr.write(`FAIL ${name}: ${error.stack}\n`);
  }
}

function environment() {
  const listeners = new Map();
  const timers = new Map();
  const scriptsAdded = [];
  let nextTimer = 1;
  const ctx = {
    URL,
    URLSearchParams,
    console: { log() {}, warn() {}, error() {} },
    crypto: {
      randomUUID: () => channel,
      getRandomValues: array => { array.fill(17); return array; }
    },
    location: new URL(`${embedOrigin}/embed/player.html?parentOrigin=${encodeURIComponent(parentOrigin)}&channel=${channel}`),
    document: {
      readyState: 'complete',
      referrer: `${parentOrigin}/player.html`,
      head: { appendChild: script => scriptsAdded.push(script) },
      body: { appendChild: script => scriptsAdded.push(script) },
      createElement: tag => ({ tagName: tag, style: {}, setAttribute() {}, addEventListener() {} }),
      getElementById: () => null,
      addEventListener() {},
      querySelector: () => null
    },
    addEventListener(name, fn) {
      const group = listeners.get(name) || [];
      group.push(fn);
      listeners.set(name, group);
    },
    removeEventListener(name, fn) {
      listeners.set(name, (listeners.get(name) || []).filter(item => item !== fn));
    },
    setTimeout(fn, delay) { const id = nextTimer++; timers.set(id, { fn, delay, repeat: false }); return id; },
    clearTimeout(id) { timers.delete(id); },
    setInterval(fn, delay) { const id = nextTimer++; timers.set(id, { fn, delay, repeat: true }); return id; },
    clearInterval(id) { timers.delete(id); }
  };
  ctx.window = ctx;
  ctx.globalThis = ctx;
  vm.createContext(ctx);
  return {
    ctx,
    scriptsAdded,
    timers,
    dispatch(name, event) { for (const fn of [...(listeners.get(name) || [])]) fn(event); },
    run(source) { return vm.runInContext(source, ctx); },
    tick(repeat) {
      for (const [id, timer] of [...timers]) {
        if (timer.repeat !== repeat) continue;
        if (!repeat) timers.delete(id);
        timer.fn();
      }
    }
  };
}

function wrapper(options = {}) {
  const env = environment();
  const calls = [];
  const messages = [];
  const parent = { postMessage: (data, origin) => messages.push({ data: normalize(data), origin }) };
  env.ctx.parent = parent;
  env.ctx.location = new URL(options.url || env.ctx.location.href);
  let playerFrame;
  env.ctx.document.getElementById = id => id === 'player' ? { replaceWith: frame => { playerFrame = frame; } } : null;
  let playerOptions;
  let currentTime = 12.5;
  let duration = 182;
  const player = {
    getCurrentTime: () => currentTime,
    getDuration: () => duration,
    getPlayerState: () => 1,
    getVideoData: () => ({ video_id: videoId }),
    getIframe: () => ({ setAttribute() {} }),
    destroy: () => calls.push(['destroy'])
  };
  for (const name of ['loadVideoById', 'cueVideoById', 'playVideo', 'pauseVideo', 'stopVideo', 'seekTo', 'setVolume']) {
    player[name] = (...args) => calls.push([name, ...normalize(args)]);
  }
  env.ctx.YT = {
    PlayerState: { PLAYING: 1, PAUSED: 2, ENDED: 0, BUFFERING: 3, CUED: 5, UNSTARTED: -1 },
    Player: function(_element, config) { playerOptions = config; return player; }
  };
  for (const script of scripts(wrapperHtml)) env.run(script);
  if (typeof env.ctx.onYouTubeIframeAPIReady === 'function') env.ctx.onYouTubeIframeAPIReady();
  const send = (data, overrides = {}) => env.dispatch('message', {
    source: parent,
    origin: parentOrigin,
    data: { source: 'aura-yt-host', channel, ...data },
    ...overrides
  });
  return {
    ...env, calls, messages, parent, player, send,
    get frame() { return playerFrame; },
    get config() { return playerOptions; },
    ready() { assert.ok(playerOptions, 'YouTube player was not created'); playerOptions.events.onReady({ target: player }); },
    time(time, length) { currentTime = time; duration = length; }
  };
}

function adapter() {
  const env = environment();
  const messages = [];
  const errors = [];
  const states = [];
  const toasts = [];
  const played = [];
  const frameListeners = new Map();
  const frame = {
    style: {},
    contentWindow: { postMessage: (data, origin) => messages.push({ data: normalize(data), origin }) },
    setAttribute() {},
    addEventListener(name, fn) { frameListeners.set(name, fn); }
  };
  const box = { innerHTML: '', appendChild(child) { assert.equal(child, frame); } };
  env.ctx.location = new URL(`${parentOrigin}/player.html`);
  env.ctx.document.createElement = tag => { assert.equal(tag, 'iframe'); return frame; };
  env.ctx.document.getElementById = id => id === 'ytPlayer' ? box : null;
  Object.assign(env.ctx, {
    state: { currentTime: 0, duration: 0, volume: 0.65, isPlaying: false, isYouTube: true, currentVideoId: videoId, currentTrack: {} },
    ytPlayer: null,
    ytReady: false,
    pendingVideoId: null,
    onYtError: event => errors.push(event.data),
    onYtStateChange: event => states.push(event.data),
    showToast: value => toasts.push(value),
    playYtVideo: id => played.push(id),
    updateTimeDisplay() {},
    updateProgressBar() {},
    updatePlayBtn() {},
    lyricsManager: { onTimeUpdate() {} }
  });
  env.run(between(clientHtml, 'const EMBED_ORIGIN', 'function onYtStateChange'));
  const src = new URL(frame.src);
  const actualChannel = src.searchParams.get('channel');
  const send = (data, overrides = {}) => env.dispatch('message', {
    source: frame.contentWindow,
    origin: embedOrigin,
    data: { source: 'aura-yt-embed', channel: actualChannel, ...data },
    ...overrides
  });
  return { ...env, frame, messages, errors, states, toasts, played, send, src, channel: actualChannel };
}

await test('all shipped client and wrapper inline scripts parse', () => {
  for (const path of [
    'docs/redesign/ingame/player.html',
    'aura-ui/src/main/resources/assets/aura-ui/web/player.html',
    'aura-ui-1.21/src/main/resources/assets/aura-ui/web/player.html',
    'lobby-api/public/embed/player.html'
  ]) {
    for (const [index, source] of scripts(read(path)).entries()) new vm.Script(source, { filename: `${path}:inline-${index}` });
  }
  for (const path of ['aura-ui/src/main/resources/assets/aura-ui/web/player.html', 'aura-ui-1.21/src/main/resources/assets/aura-ui/web/player.html']) {
    assert.equal(read(path), clientHtml, `Shipped player differs from source: ${path}`);
  }
});

await test('Vercel handler serves the canonical wrapper with referrer headers', () => {
  const source = read('lobby-api/api/embed/player.ts');
  const js = source.replace('export const HTML_CONTENT', 'const HTML_CONTENT')
    .replace(/export default function handler\(req: any, res: any\)/, 'function handler(req, res)');
  const ctx = vm.createContext({});
  vm.runInContext(js, ctx);
  assert.equal(vm.runInContext('HTML_CONTENT', ctx).trim(), wrapperHtml.trim());
  const call = method => {
    const headers = {};
    let body;
    const response = { setHeader: (key, value) => { headers[key.toLowerCase()] = value; }, end: value => { body = value; } };
    ctx.handler({ method }, response);
    return { response, headers, body };
  };
  const get = call('GET');
  assert.equal(get.response.statusCode, 200);
  assert.equal(get.body.trim(), wrapperHtml.trim());
  assert.match(get.headers['content-type'], /text\/html/);
  assert.equal(get.headers['referrer-policy'], 'strict-origin-when-cross-origin');
  const head = call('HEAD');
  assert.equal(head.response.statusCode, 200);
  assert.ok(!head.body);
  assert.equal(call('POST').response.statusCode, 405);
});

await test('wrapper identifies HTTPS host and emits ready to its bound parent', () => {
  const env = wrapper();
  env.ready();
  const youtubeUrl = new URL(env.frame.src);
  assert.equal(youtubeUrl.origin, 'https://www.youtube.com');
  assert.equal(youtubeUrl.searchParams.get('origin'), embedOrigin);
  assert.equal(env.frame.referrerPolicy, 'strict-origin-when-cross-origin');
  const message = env.messages.find(item => item.data.event === 'ready');
  assert.ok(message);
  assert.equal(message.origin, parentOrigin);
  assert.equal(message.data.source, 'aura-yt-embed');
  assert.equal(message.data.channel, channel);
});

await test('wrapper rejects commands from wrong origin, source, channel and marker', () => {
  const env = wrapper();
  env.ready();
  const count = env.calls.length;
  env.send({ op: 'play' }, { origin: 'https://untrusted.invalid' });
  env.send({ op: 'play' }, { source: {} });
  env.send({ op: 'play', channel: 'wrong-channel' });
  env.send({ op: 'play', channel: undefined });
  env.send({ op: 'play', source: 'wrong-host' });
  assert.equal(env.calls.length, count);
  env.send({ op: 'play' });
  assert.equal(env.calls.at(-1)[0], 'playVideo');
});

await test('wrapper does not initialize for untrusted or missing parent configuration', () => {
  for (const query of [
    `parentOrigin=${encodeURIComponent('https://untrusted.invalid')}&channel=${channel}`,
    `parentOrigin=${encodeURIComponent(parentOrigin)}`,
    `channel=${channel}`,
    `parentOrigin=${encodeURIComponent('null')}&channel=${channel}`
  ]) {
    const env = wrapper({ url: `${embedOrigin}/embed/player.html?${query}` });
    assert.equal(env.config, undefined);
    assert.equal(env.scriptsAdded.length, 0);
    env.send({ op: 'play' });
    assert.equal(env.calls.length, 0);
  }
});

await test('wrapper ignores early commands and reports script or player failure', () => {
  const env = wrapper();
  env.send({ op: 'load', videoId });
  assert.equal(env.calls.length, 0);
  const apiScript = env.scriptsAdded.find(script => script.src === 'https://www.youtube.com/iframe_api');
  assert.ok(apiScript);
  apiScript.onerror();
  assert.ok(env.messages.some(item => item.data.event === 'unavailable'));
  env.ready();
  env.player.playVideo = () => { throw new Error('Player disconnected'); };
  const count = env.messages.length;
  env.send({ op: 'play' });
  assert.equal(env.messages.length, count + 1);
  assert.equal(env.messages.at(-1).data.event, 'unavailable');
});

await test('wrapper executes all playback commands and validates numeric input', () => {
  const env = wrapper();
  env.ready();
  env.send({ op: 'load', videoId, startSeconds: 7 });
  env.send({ op: 'cue', videoId });
  env.send({ op: 'play' });
  env.send({ op: 'pause' });
  env.send({ op: 'stop' });
  env.send({ op: 'seek', seconds: 42 });
  env.send({ op: 'volume', value: 73 });
  for (const name of ['loadVideoById', 'cueVideoById', 'playVideo', 'pauseVideo', 'stopVideo', 'seekTo', 'setVolume']) {
    assert.ok(env.calls.some(call => call[0] === name), `Missing command ${name}`);
  }
  assert.equal(env.calls.find(call => call[0] === 'loadVideoById')[1].startSeconds, 7);
  assert.equal(env.calls.find(call => call[0] === 'seekTo')[1], 42);
  assert.equal(env.calls.find(call => call[0] === 'setVolume')[1], 73);
  const count = env.calls.length;
  env.send({ op: 'load', videoId: '../invalid' });
  env.send({ op: 'seek', seconds: Infinity });
  env.send({ op: 'volume', value: NaN });
  assert.equal(env.calls.length, count);
});

await test('wrapper forwards progress, player states, errors and autoplay denial', () => {
  const env = wrapper();
  env.ready();
  env.config.events.onStateChange({ data: 1 });
  env.time(32.25, 244);
  env.tick(true);
  assert.ok(env.messages.some(item => item.data.event === 'timeUpdate' && item.data.currentTime === 32.25 && item.data.duration === 244));
  assert.ok(env.messages.some(item => item.data.event === 'stateChange' && item.data.state === 1));
  env.config.events.onError({ data: 153 });
  assert.ok(env.messages.some(item => item.data.event === 'error' && item.data.code === 153));
  env.config.events.onAutoplayBlocked({});
  assert.ok(env.messages.some(item => item.data.event === 'autoplayBlocked'));
  assert.ok(env.messages.every(item => item.origin === parentOrigin && item.data.channel === channel));
  const count = env.messages.length;
  env.dispatch('pagehide', {});
  env.tick(true);
  assert.equal(env.messages.length, count);
});

await test('adapter constructs the HTTPS wrapper and authenticates incoming ready', () => {
  const env = adapter();
  assert.equal(env.src.origin, embedOrigin);
  assert.equal(env.src.pathname, '/embed/player.html');
  assert.equal(env.src.searchParams.get('parentOrigin'), parentOrigin);
  assert.ok(env.channel);
  env.send({ event: 'ready' }, { origin: 'https://untrusted.invalid' });
  env.send({ event: 'ready' }, { source: {} });
  env.send({ event: 'ready', channel: 'wrong-channel' });
  env.send({ event: 'ready', channel: undefined });
  env.send({ event: 'ready', source: 'wrong-wrapper' });
  assert.equal(env.run('ytReady'), false);
  env.send({ event: 'ready' });
  assert.equal(env.run('ytReady'), true);
  const volume = env.messages.find(item => item.data.op === 'volume');
  assert.equal(volume.data.value, 65);
});

await test('adapter sends all playback operations only to the HTTPS wrapper', () => {
  const env = adapter();
  env.send({ event: 'ready' });
  env.run(`ytPlayer.loadVideoById({ videoId: '${videoId}', startSeconds: 9 }); ytPlayer.cueVideoById('${videoId}'); ytPlayer.playVideo(); ytPlayer.pauseVideo(); ytPlayer.stopVideo(); ytPlayer.seekTo(18, true); ytPlayer.setVolume(81);`);
  for (const op of ['load', 'cue', 'play', 'pause', 'stop', 'seek', 'volume']) assert.ok(env.messages.some(item => item.data.op === op), `Missing ${op}`);
  assert.ok(env.messages.every(item => item.origin === embedOrigin && item.data.channel === env.channel && item.data.source === 'aura-yt-host'));
});

await test('adapter updates cached time and forwards only authenticated states and errors', () => {
  const env = adapter();
  env.send({ event: 'ready' });
  env.send({ event: 'timeUpdate', currentTime: 26.75, duration: 201 });
  assert.equal(env.ctx.state.currentTime, 26.75);
  assert.equal(env.ctx.state.duration, 201);
  assert.equal(env.run('ytPlayer.getCurrentTime()'), 26.75);
  assert.equal(env.run('ytPlayer.getDuration()'), 201);
  env.send({ event: 'timeUpdate', currentTime: 999, duration: 999 }, { source: {} });
  assert.equal(env.ctx.state.currentTime, 26.75);
  env.send({ event: 'stateChange', state: 2 });
  assert.deepEqual(env.states, [2]);
  env.send({ event: 'error', code: 153 });
  assert.deepEqual(env.errors, [153]);
  env.send({ event: 'error', code: 150 }, { origin: 'https://untrusted.invalid' });
  assert.deepEqual(env.errors, [153]);
  env.send({ event: 'timeUpdate', currentTime: 170, duration: 200, videoId: 'aaaaaaaaaaa' });
  env.send({ event: 'error', code: 150, videoId: 'aaaaaaaaaaa' });
  assert.equal(env.ctx.state.currentTime, 26.75);
  assert.deepEqual(env.errors, [153]);
  env.send({ event: 'timeUpdate', currentTime: NaN, duration: Infinity });
  assert.equal(env.ctx.state.currentTime, 26.75);
  assert.equal(env.ctx.state.duration, 201);
});

await test('adapter keeps failures local and applies pending playback only after ready', () => {
  const env = adapter();
  env.ctx.pendingVideoId = videoId;
  env.send({ event: 'ready' });
  assert.deepEqual(env.played, [videoId]);
  assert.equal(env.ctx.pendingVideoId, null);
  env.ctx.state.isPlaying = true;
  env.send({ event: 'autoplayBlocked', videoId });
  assert.equal(env.ctx.state.isPlaying, false);
  assert.equal(env.errors.length, 0);
  assert.ok(env.toasts.length);
  env.send({ event: 'unavailable' });
  assert.equal(env.run('ytReady'), false);
  assert.equal(env.ctx.state.isPlaying, false);
  const timeout = adapter();
  timeout.tick(false);
  assert.equal(timeout.run('ytReady'), false);
  assert.ok(timeout.toasts.length);
});

await test('client never reports player configuration errors 153/5 as blocked videos', async () => {
  for (const code of [153, 5, 101, 150]) {
    const env = environment();
    const requests = [];
    Object.assign(env.ctx, {
      state: { currentVideoId: videoId, currentTrack: null, isPlaying: true },
      currentCandidates: [], currentCandidateIdx: 0, currentCacheKey: '',
      localStorage: { removeItem() {} },
      fetch: async (url, options) => { requests.push({ url, options }); return { ok: true }; },
      showToast() {}, updatePlayBtn() {}, playNext() {}, playYtVideo() {}, fallbackToSoundCloud() {}
    });
    env.run(between(clientHtml, 'async function reportBlockedVideo', 'const EMBED_ORIGIN'));
    env.run(between(clientHtml, 'function onYtError(event)', 'async function fallbackToSoundCloud'));
    await env.ctx.onYtError({ data: code });
    await Promise.resolve();
    const reports = requests.filter(request => request.url.includes('/api/music/blocked'));
    assert.equal(reports.length, code === 101 || code === 150 ? 1 : 0, `Blacklist reports for error ${code}`);
    if (reports.length) assert.equal(JSON.parse(reports[0].options.body).code, code);
  }
});

await test('blocked endpoint rejects non-owner failures before writing shared state', async () => {
  const source = read('lobby-api/api/music/blocked.ts')
    .replace(/^import[^\n]*\n/gm, '')
    .replace('export default async function handler', 'async function handler')
    .replace(/: any\b/g, '');
  for (const code of [undefined, 5, 153, 152, 100, 101, 150]) {
    const writes = [];
    let result;
    const ctx = vm.createContext({
      getStore: () => ({ checkRateLimit: async () => true }),
      getClientIp: () => '127.0.0.1',
      parseJson: async () => ({ videoId, code }),
      redisSet: async (...args) => writes.push(args),
      sendJson: (_res, status, body) => { result = { status, body }; }
    });
    vm.runInContext(source, ctx);
    await ctx.handler({ method: 'POST', headers: {} }, {});
    if (code === 101 || code === 150) {
      assert.equal(result.status, 200);
      assert.equal(writes.length, 1);
      assert.ok(writes[0][0].endsWith(`:${videoId}`));
      assert.notEqual(writes[0][0], `music:blocked:${videoId}`);
    } else {
      assert.equal(result.status, 400, `Endpoint accepted error ${code}`);
      assert.equal(writes.length, 0);
    }
  }
});

process.stdout.write(`Music embed VM checks: ${passed} passed, ${failed} failed. Browser, HTTPS deployment and MCEF playback are not covered.\n`);
if (failed) process.exitCode = 1;

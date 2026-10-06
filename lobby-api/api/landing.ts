import type { IncomingMessage, ServerResponse } from 'http';
import { generateLandingHtml } from '../src/landing.js';

export default function handler(req: any, res: any) {
  const type = req.query.type === 'friend' ? 'friend' : 'join';
  const code = typeof req.query.code === 'string' ? req.query.code : '';

  const html = generateLandingHtml(type, code);
  if (!html) {
    if (typeof res.status === 'function') {
      return res.status(404).send('Not Found');
    }
    res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
    return res.end('Not Found');
  }

  if (typeof res.status === 'function') {
    res.setHeader('Content-Type', 'text/html; charset=utf-8');
    return res.status(200).send(html);
  }

  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  res.end(html);
}

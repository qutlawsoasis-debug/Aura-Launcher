import crypto from 'crypto';

export interface SkinValidation {
  valid: boolean;
  error?: string;
  width?: number;
  height?: number;
  buffer?: Buffer;
  sha1?: string;
}

export function validatePngSkin(skinBase64: string): SkinValidation {
  if (!skinBase64 || typeof skinBase64 !== 'string') {
    return { valid: false, error: 'Skin data is empty' };
  }

  // Handle optional data URL prefix
  const cleanB64 = skinBase64.replace(/^data:image\/png;base64,/, '').trim();
  const buf = Buffer.from(cleanB64, 'base64');

  if (buf.length === 0) {
    return { valid: false, error: 'Skin data could not be decoded' };
  }

  if (buf.length > 32 * 1024) {
    return { valid: false, error: `Skin size exceeds 32 KB limit (${buf.length} bytes)` };
  }

  // Check PNG signature: 89 50 4E 47 0D 0A 1A 0A
  const pngHeader = Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
  if (buf.length < 8 || !buf.subarray(0, 8).equals(pngHeader)) {
    return { valid: false, error: 'Invalid file format: must be a valid PNG' };
  }

  // Check IHDR chunk
  if (buf.length < 24) {
    return { valid: false, error: 'PNG file is corrupt or truncated' };
  }

  const ihdrType = buf.subarray(12, 16).toString('ascii');
  if (ihdrType !== 'IHDR') {
    return { valid: false, error: 'Invalid PNG: missing IHDR chunk' };
  }

  const width = buf.readUInt32BE(16);
  const height = buf.readUInt32BE(20);

  if (!((width === 64 && height === 64) || (width === 64 && height === 32))) {
    return { valid: false, error: `Invalid skin dimensions: ${width}x${height}. Required: 64x64 or 64x32` };
  }

  const sha1 = crypto.createHash('sha1').update(buf).digest('hex');

  return {
    valid: true,
    width,
    height,
    buffer: buf,
    sha1
  };
}

const NICKNAME_REGEX = /^[a-zA-Z0-9_]{3,16}$/;

export function isValidNickname(nick: string): boolean {
  return typeof nick === 'string' && NICKNAME_REGEX.test(nick);
}

export function getClientIp(req: any): string {
  const forwarded = req.headers['x-forwarded-for'];
  if (forwarded) {
    const list = typeof forwarded === 'string' ? forwarded.split(',') : forwarded;
    return list[0].trim();
  }
  return req.socket?.remoteAddress || req.connection?.remoteAddress || '127.0.0.1';
}

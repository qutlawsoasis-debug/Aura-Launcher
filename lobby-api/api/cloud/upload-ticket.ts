import { uploadTicket } from '../../src/cloudProxy.js';

export default function handler(req: any, res: any) {
  return uploadTicket(req, res);
}

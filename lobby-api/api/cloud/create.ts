import { proxyToManager } from '../../src/cloudProxy.js';

// Starting a world can take a while on the manager
export const config = { maxDuration: 60 };

export default function handler(req: any, res: any) {
  return proxyToManager(req, res, 'create');
}

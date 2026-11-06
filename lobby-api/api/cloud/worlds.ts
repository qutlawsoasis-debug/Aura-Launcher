import { proxyToManager } from '../../src/cloudProxy.js';


export default function handler(req: any, res: any) {
  return proxyToManager(req, res, 'worlds');
}

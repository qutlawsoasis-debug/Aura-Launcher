import crypto from 'crypto';
import { getStore } from '../src/store.js';
import { authenticate, parseJson, sendJson } from '../src/friendRoutes.js';
export default async function handler(req, res) {
    if (req.method === 'OPTIONS') {
        res.setHeader('Access-Control-Allow-Origin', '*');
        res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-Aura-Client, X-User-Id, X-User-Token');
        return res.status ? res.status(204).end() : (res.writeHead(204), res.end());
    }
    if (req.method !== 'POST') {
        return sendJson(res, 405, { error: 'Method Not Allowed' });
    }
    try {
        const store = getStore();
        // 1. Auth check
        const user = await authenticate(req, res, store);
        if (!user)
            return;
        // 2. Rate limit: 5 reports per hour per user profile
        const rateKey = `report:${user.id}`;
        const allowed = await store.checkRateLimit(rateKey, 5, 3600);
        if (!allowed) {
            return sendJson(res, 429, { error: 'Лимит отчётов превышен (максимум 5 в час)' });
        }
        // 3. Parse JSON payload: { zipBase64, errorText, launcherVersion, os, userComment }
        const body = await parseJson(req);
        const zipBase64 = typeof body.zipBase64 === 'string' ? body.zipBase64.trim() : '';
        if (!zipBase64) {
            return sendJson(res, 400, { error: 'Файл отчёта пуст' });
        }
        // Check size limit: max 1 MB compressed (base64 length approx 1.37 MB)
        const cleanB64 = zipBase64.replace(/^data:application\/zip;base64,/, '').trim();
        const zipBuffer = Buffer.from(cleanB64, 'base64');
        if (zipBuffer.length === 0) {
            return sendJson(res, 400, { error: 'Не удалось декодировать zip-архив' });
        }
        if (zipBuffer.length > 1024 * 1024) {
            return sendJson(res, 400, { error: 'Размер отчёта превышает лимит 1 МБ' });
        }
        // Generate report ID «R-XXXXXX» (6 alphanumeric chars)
        const randomChars = crypto.randomBytes(4).toString('hex').toUpperCase().substring(0, 6);
        const reportId = `R-${randomChars}`;
        const launcherVer = typeof body.launcherVersion === 'string' && body.launcherVersion.trim()
            ? body.launcherVersion.trim()
            : 'beta 1.0.22';
        const osVer = typeof body.os === 'string' && body.os.trim()
            ? body.os.trim()
            : 'Windows';
        let errText = typeof body.errorText === 'string' ? body.errorText.trim() : '';
        if (errText.length > 800) {
            errText = errText.substring(0, 800) + '...';
        }
        let userComment = typeof body.userComment === 'string' ? body.userComment.trim() : '';
        if (userComment.length > 300) {
            userComment = userComment.substring(0, 300);
        }
        // 4. Send to Discord Webhook if configured
        const webhookUrl = process.env.DISCORD_REPORT_WEBHOOK;
        if (webhookUrl && webhookUrl.trim().startsWith('http')) {
            try {
                const formData = new FormData();
                const zipBlob = new Blob([zipBuffer], { type: 'application/zip' });
                formData.append('files[0]', zipBlob, `${reportId}-logs.zip`);
                const msgLines = [
                    `📋 **Отчёт ${reportId}**`,
                    `👤 **Игрок:** \`${user.nick}\` | **ID профиля:** \`${user.friendCode}\``,
                    `💻 **Лаунчер:** \`${launcherVer}\` | **ОС:** \`${osVer}\``
                ];
                if (userComment) {
                    msgLines.push(`💬 **Что произошло:** ${userComment}`);
                }
                if (errText) {
                    msgLines.push(`⚠️ **Текст ошибки:** \`\`\`\n${errText}\n\`\`\``);
                }
                formData.append('payload_json', JSON.stringify({
                    content: msgLines.join('\n')
                }));
                await fetch(webhookUrl.trim(), {
                    method: 'POST',
                    body: formData
                });
            }
            catch (webhookErr) {
                // Do NOT log webhook URL or secrets
            }
        }
        return sendJson(res, 200, {
            success: true,
            reportId
        });
    }
    catch (err) {
        return sendJson(res, 500, { error: err.message || 'Internal Server Error' });
    }
}

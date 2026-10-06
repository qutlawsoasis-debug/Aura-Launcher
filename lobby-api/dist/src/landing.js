const CODE_REGEX = /^[A-Z0-9]{6,8}$/;
export function escapeHtml(str) {
    return str.replace(/[&<>"']/g, (m) => {
        switch (m) {
            case '&': return '&amp;';
            case '<': return '&lt;';
            case '>': return '&gt;';
            case '"': return '&quot;';
            case "'": return '&#39;';
            default: return m;
        }
    });
}
export function generateLandingHtml(type, rawCode) {
    const code = (rawCode || '').trim();
    if (!CODE_REGEX.test(code)) {
        return null;
    }
    const safeCode = escapeHtml(code);
    const auraUri = `aura://${type}/${safeCode}`;
    return `<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Открываем Aura…</title>
  <style>
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      background-color: #0B141A;
      color: #E2ECF2;
      font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
      min-height: 100vh;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      text-align: center;
      padding: 24px;
    }
    h1 {
      font-size: 26px;
      font-weight: 600;
      margin-bottom: 24px;
      color: #FFFFFF;
    }
    .btn-container {
      display: flex;
      flex-direction: column;
      gap: 12px;
      width: 100%;
      max-width: 280px;
    }
    .btn {
      display: block;
      padding: 12px 20px;
      border-radius: 4px;
      font-size: 15px;
      font-weight: 600;
      text-decoration: none;
      text-align: center;
      transition: opacity 0.15s ease;
      cursor: pointer;
    }
    .btn:hover { opacity: 0.9; }
    .btn-primary {
      background-color: #FF7A00;
      color: #0B141A;
      border: none;
    }
    .btn-secondary {
      background-color: rgba(255, 255, 255, 0.08);
      color: #E2ECF2;
      border: 1px solid rgba(255, 255, 255, 0.15);
    }
  </style>
  <script>
    window.location.href = "${auraUri}";
  </script>
</head>
<body>
  <h1>Открываем Aura…</h1>
  <div class="btn-container">
    <a href="${auraUri}" class="btn btn-primary">Открыть Aura</a>
    <a href="https://github.com/qutlawsoasis-debug/Aura-Launcher/releases/latest" class="btn btn-secondary">Скачать лаунчер</a>
  </div>
</body>
</html>`;
}

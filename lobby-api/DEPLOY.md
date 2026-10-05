# Деплой Lobby API на Vercel

Serverless бэкенд для P2P LAN-лобби Aura Launcher.

## 1. Настройка Upstash Redis

1. Зайдите на [console.upstash.com](https://console.upstash.com/) и создайте базу данных Redis.
2. В деталях базы найдите блок **REST API**.
3. Скопируйте значения:
   - `UPSTASH_REDIS_REST_URL` (например, `https://your-db.upstash.io`)
   - `UPSTASH_REDIS_REST_TOKEN` (токен доступа)

---

## 2. Команды деплоя на Vercel

Выполняйте в каталоге `lobby-api/`:

```bash
cd lobby-api

# 1. Авторизация в Vercel CLI
vercel login

# 2. Привязка проекта к Vercel
vercel link

# 3. Добавление переменных окружения для Upstash Redis
vercel env add UPSTASH_REDIS_REST_URL production
vercel env add UPSTASH_REDIS_REST_TOKEN production

# 4. Продакшен-деплой
vercel --prod
```

---

## 3. Подключение в лаунчере

После успешного деплоя Vercel выдаст публичный URL (например, `https://aura-lobby-api.vercel.app`).

В `%APPDATA%\Aura\config.json` (или через профиль) укажите:

```json
{
  "LobbyApiBaseUrl": "https://aura-lobby-api.vercel.app"
}
```

Все запросы лобби автоматически пойдут на Vercel. Без переменных Upstash сервер автоматически использует локальный `InMemoryStore`.

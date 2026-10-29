package net.auramc.glass.client.music;

import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.atomic.AtomicLong;

public final class BridgeMusicSource implements MusicSource, AutoCloseable {
    private static final Duration CONNECT_TIMEOUT = Duration.ofMillis(1000);
    private static final Duration POLL_TIMEOUT = Duration.ofMillis(2000);
    private static final Duration CMD_TIMEOUT = Duration.ofMillis(1000);
    private static final Duration SEARCH_TIMEOUT = Duration.ofMillis(2000);

    private final Path configPath;
    private final HttpClient httpClient;
    private final Thread pollerThread;
    private final AtomicLong httpPollCount = new AtomicLong();
    private final Object pollLock = new Object();
    private final Object lifecycleLock = new Object();
    private final AtomicLong generation = new AtomicLong(0);

    private volatile boolean running = true;
    private volatile boolean closed = false;

    private volatile BridgeConfig currentConfig;
    private volatile boolean available = false;
    private volatile NowPlaying cachedNow = NowPlaying.stopped();
    private volatile int consecutiveFailures = 0;
    private volatile long lastSuccessTimeMs = -1L;

    public BridgeMusicSource() {
        this(resolveDefaultConfigPath());
    }

    public BridgeMusicSource(Path configPath) {
        this(configPath, HttpClient.newBuilder()
                .connectTimeout(CONNECT_TIMEOUT)
                .build(), true);
    }

    public BridgeMusicSource(Path configPath, HttpClient httpClient) {
        this(configPath, httpClient, true);
    }

    public BridgeMusicSource(Path configPath, HttpClient httpClient, boolean startPoller) {
        this.configPath = configPath;
        this.httpClient = httpClient;
        if (startPoller) {
            this.pollerThread = new Thread(this::pollLoop, "Aura-Music-Bridge-Poller");
            this.pollerThread.setDaemon(true);
            this.pollerThread.start();
        } else {
            this.pollerThread = null;
        }
    }

    public static Path resolveDefaultConfigPath() {
        try {
            Class<?> flClass = Class.forName("net.fabricmc.loader.api.FabricLoader");
            Object instance = flClass.getMethod("getInstance").invoke(null);
            Object configDir = flClass.getMethod("getConfigDir").invoke(instance);
            if (configDir instanceof Path p) {
                return p.resolve("aura-music-bridge.json");
            }
        } catch (Throwable ignored) {
        }
        return Path.of("config", "aura-music-bridge.json");
    }

    @Override
    public NowPlaying now() {
        if (closed) {
            return NowPlaying.stopped();
        }
        if (available && lastSuccessTimeMs > 0 && (System.currentTimeMillis() - lastSuccessTimeMs >= 2000L)) {
            return NowPlaying.stopped();
        }
        return cachedNow;
    }

    @Override
    public boolean available() {
        if (closed) {
            return false;
        }
        if (available && lastSuccessTimeMs > 0 && (System.currentTimeMillis() - lastSuccessTimeMs >= 2000L)) {
            return false;
        }
        return available;
    }

    @Override
    public void play() {
        postAsync("/play", "{}");
    }

    @Override
    public void pause() {
        postAsync("/pause", "{}");
    }

    @Override
    public void next() {
        postAsync("/next", "{}");
    }

    @Override
    public void prev() {
        postAsync("/prev", "{}");
    }

    @Override
    public void seek(long ms) {
        long targetMs = Math.max(0L, ms);
        postAsync("/seek", "{\"ms\":" + targetMs + "}");
    }

    @Override
    public void volume(float v) {
        float clamped = Math.max(0.0f, Math.min(1.0f, v));
        postAsync("/volume", "{\"volume\":" + clamped + "}");
    }

    @Override
    public void queue(String trackId) {
        if (trackId == null || trackId.isBlank()) return;
        postAsync("/queue", "{\"trackId\":\"" + escapeJson(trackId) + "\"}");
    }

    @Override
    public List<Track> search(String q) {
        if (closed || !running || !available || currentConfig == null) {
            return Collections.emptyList();
        }
        try {
            String query = (q != null) ? q : "";
            String encoded = URLEncoder.encode(query, StandardCharsets.UTF_8);
            String url = "http://127.0.0.1:" + currentConfig.port() + "/search?q=" + encoded;

            HttpRequest req = HttpRequest.newBuilder()
                    .uri(URI.create(url))
                    .timeout(SEARCH_TIMEOUT)
                    .header("Authorization", "Bearer " + currentConfig.token())
                    .header("Accept", "application/json")
                    .GET()
                    .build();

            httpPollCount.incrementAndGet();
            HttpResponse<String> resp = httpClient.send(req, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
            if (resp.statusCode() == 200) {
                return MusicJsonParser.parseTrackList(resp.body());
            }
        } catch (Exception ignored) {
            // Quiet fallback
        }
        return Collections.emptyList();
    }

    public void pollOnce() {
        if (closed || !running) {
            return;
        }
        long myGen = generation.get();
        synchronized (pollLock) {
            if (closed || !running || myGen != generation.get()) {
                return;
            }

            BridgeConfig config = BridgeConfig.load(configPath);
            if (config == null || !config.isValid()) {
                synchronized (lifecycleLock) {
                    if (closed || myGen != generation.get()) return;
                    this.currentConfig = null;
                    this.available = false;
                    this.cachedNow = NowPlaying.stopped();
                    this.consecutiveFailures = 0;
                    this.lastSuccessTimeMs = -1L;
                }
                return;
            }

            this.currentConfig = config;
            try {
                String url = "http://127.0.0.1:" + config.port() + "/now";
                HttpRequest req = HttpRequest.newBuilder()
                        .uri(URI.create(url))
                        .timeout(POLL_TIMEOUT)
                        .header("Authorization", "Bearer " + config.token())
                        .header("Accept", "application/json")
                        .GET()
                        .build();

                httpPollCount.incrementAndGet();
                HttpResponse<String> resp = httpClient.send(req, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
                synchronized (lifecycleLock) {
                    if (closed || !running || myGen != generation.get()) {
                        return;
                    }
                    if (resp.statusCode() == 200) {
                        NowPlaying parsed = MusicJsonParser.parseNowPlaying(resp.body());
                        this.cachedNow = parsed;
                        this.available = true;
                        this.consecutiveFailures = 0;
                        this.lastSuccessTimeMs = System.currentTimeMillis();
                    } else {
                        handleFailureLocked();
                    }
                }
            } catch (Exception ignored) {
                synchronized (lifecycleLock) {
                    if (closed || !running || myGen != generation.get()) {
                        return;
                    }
                    handleFailureLocked();
                }
            }
        }
    }

    private void handleFailureLocked() {
        this.consecutiveFailures++;
        long now = System.currentTimeMillis();
        boolean timeoutExpired = (this.lastSuccessTimeMs > 0 && (now - this.lastSuccessTimeMs >= 2000L));
        if (this.consecutiveFailures >= 3 || timeoutExpired || this.lastSuccessTimeMs < 0 || !Files.exists(configPath)) {
            this.available = false;
            this.cachedNow = NowPlaying.stopped();
        }
    }

    private void pollLoop() {
        while (running && !closed) {
            try {
                pollOnce();
            } catch (Throwable ignored) {
                synchronized (lifecycleLock) {
                    if (!closed) {
                        handleFailureLocked();
                    }
                }
            }

            try {
                Thread.sleep(500L);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                break;
            }
        }
    }

    private CompletableFuture<Void> postAsync(String endpoint, String jsonBody) {
        if (closed || !running || !available || currentConfig == null) {
            return CompletableFuture.completedFuture(null);
        }
        long myGen = generation.get();
        try {
            String url = "http://127.0.0.1:" + currentConfig.port() + endpoint;
            HttpRequest req = HttpRequest.newBuilder()
                    .uri(URI.create(url))
                    .timeout(CMD_TIMEOUT)
                    .header("Authorization", "Bearer " + currentConfig.token())
                    .header("Content-Type", "application/json")
                    .POST(HttpRequest.BodyPublishers.ofString(jsonBody, StandardCharsets.UTF_8))
                    .build();

            CompletableFuture<Void> future = new CompletableFuture<>();
            httpClient.sendAsync(req, HttpResponse.BodyHandlers.discarding())
                    .whenComplete((resp, ex) -> {
                        if (!closed && running && myGen == generation.get()) {
                            CompletableFuture.runAsync(this::pollOnce);
                        }
                        future.complete(null);
                    });
            return future;
        } catch (Exception ignored) {
            return CompletableFuture.completedFuture(null);
        }
    }

    private static String escapeJson(String s) {
        return s.replace("\\", "\\\\").replace("\"", "\\\"");
    }

    public long httpPollCount() {
        return httpPollCount.get();
    }

    @Override
    public void close() {
        synchronized (lifecycleLock) {
            closed = true;
            running = false;
            available = false;
            cachedNow = NowPlaying.stopped();
            generation.incrementAndGet();
        }
        if (pollerThread != null) {
            pollerThread.interrupt();
        }
    }
}

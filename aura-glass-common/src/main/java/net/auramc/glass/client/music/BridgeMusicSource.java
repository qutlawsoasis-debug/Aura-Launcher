package net.auramc.glass.client.music;

import net.fabricmc.loader.api.FabricLoader;

import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.time.Duration;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.CompletableFuture;

public final class BridgeMusicSource implements MusicSource, AutoCloseable {
    private static final Duration CONNECT_TIMEOUT = Duration.ofMillis(1000);
    private static final Duration POLL_TIMEOUT = Duration.ofMillis(450);
    private static final Duration CMD_TIMEOUT = Duration.ofMillis(1000);
    private static final Duration SEARCH_TIMEOUT = Duration.ofMillis(2000);

    private final Path configPath;
    private final HttpClient httpClient;
    private final Thread pollerThread;
    private volatile boolean running = true;

    private volatile BridgeConfig currentConfig;
    private volatile boolean available = false;
    private volatile NowPlaying cachedNow = NowPlaying.stopped();

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
            return FabricLoader.getInstance().getConfigDir().resolve("aura-music-bridge.json");
        } catch (Throwable ignored) {
            return Path.of("config", "aura-music-bridge.json");
        }
    }

    @Override
    public NowPlaying now() {
        return cachedNow;
    }

    @Override
    public boolean available() {
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
        if (!available || currentConfig == null) {
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
        BridgeConfig config = BridgeConfig.load(configPath);
        if (config == null || !config.isValid()) {
            this.currentConfig = null;
            this.available = false;
            this.cachedNow = NowPlaying.stopped();
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

            HttpResponse<String> resp = httpClient.send(req, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
            if (resp.statusCode() == 200) {
                NowPlaying parsed = MusicJsonParser.parseNowPlaying(resp.body());
                this.cachedNow = parsed;
                this.available = true;
            } else {
                this.available = false;
                this.cachedNow = NowPlaying.stopped();
            }
        } catch (Exception ignored) {
            // Quiet wait: server offline or error -> available = false
            this.available = false;
            this.cachedNow = NowPlaying.stopped();
        }
    }

    private void pollLoop() {
        while (running) {
            try {
                pollOnce();
            } catch (Throwable ignored) {
                this.available = false;
                this.cachedNow = NowPlaying.stopped();
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
        if (!available || currentConfig == null) {
            return CompletableFuture.completedFuture(null);
        }

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
                    .whenComplete((resp, ex) -> future.complete(null));
            return future;
        } catch (Exception ignored) {
            return CompletableFuture.completedFuture(null);
        }
    }

    private static String escapeJson(String s) {
        return s.replace("\\", "\\\\").replace("\"", "\\\"");
    }

    @Override
    public void close() {
        running = false;
        if (pollerThread != null) {
            pollerThread.interrupt();
        }
    }
}

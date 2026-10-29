package net.auramc.glass.client.lyrics;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.fabricmc.loader.api.FabricLoader;

import java.io.IOException;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.FileTime;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.time.Duration;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;
import java.util.concurrent.CompletableFuture;
import java.util.stream.Collectors;
import java.util.stream.Stream;

/**
 * Client for fetching and caching timed song lyrics from lobby-api.
 * Uses java.net.http, asynchronous operations, 5-second timeout,
 * and a 200-file LRU disk cache with 24-hour negative caching.
 */
public class LyricsClient {
    public static final String DEFAULT_BASE_URL = "https://lobby-api.vercel.app";
    public static final Duration TIMEOUT = Duration.ofSeconds(5);
    public static final long NEGATIVE_CACHE_TTL_MS = 24L * 60L * 60L * 1000L; // 24 hours
    public static final int MAX_CACHE_FILES = 200;

    private final String baseUrl;
    private final Path cacheDir;
    private final HttpClient httpClient;

    public LyricsClient() {
        this(resolveConfigDir());
    }

    public LyricsClient(Path configDir) {
        this(loadConfigBaseUrl(configDir), resolveCacheDir(configDir), createDefaultHttpClient());
    }

    public LyricsClient(String baseUrl, Path cacheDir, HttpClient httpClient) {
        this.baseUrl = normalizeBaseUrl(baseUrl);
        this.cacheDir = cacheDir;
        this.httpClient = httpClient != null ? httpClient : createDefaultHttpClient();
    }

    public static HttpClient createDefaultHttpClient() {
        return HttpClient.newBuilder()
                .connectTimeout(TIMEOUT)
                .followRedirects(HttpClient.Redirect.NORMAL)
                .build();
    }

    public static Path resolveConfigDir() {
        try {
            if (FabricLoader.getInstance() != null) {
                Path dir = FabricLoader.getInstance().getConfigDir();
                if (dir != null) {
                    return dir;
                }
            }
        } catch (Throwable ignored) {
        }
        return Path.of("config");
    }

    public static Path resolveCacheDir(Path configDir) {
        return configDir.resolve("aura-glass").resolve("lyrics-cache");
    }

    private static String normalizeBaseUrl(String url) {
        if (url == null || url.isBlank()) {
            return DEFAULT_BASE_URL;
        }
        String trimmed = url.trim();
        while (trimmed.endsWith("/")) {
            trimmed = trimmed.substring(0, trimmed.length() - 1);
        }
        return trimmed;
    }

    private static String loadConfigBaseUrl(Path configDir) {
        Path configFile = configDir.resolve("aura-glass").resolve("lyrics.json");
        if (Files.exists(configFile)) {
            try {
                String content = Files.readString(configFile, StandardCharsets.UTF_8);
                JsonObject obj = JsonParser.parseString(content).getAsJsonObject();
                if (obj.has("baseUrl") && !obj.get("baseUrl").isJsonNull()) {
                    String customUrl = obj.get("baseUrl").getAsString().trim();
                    if (!customUrl.isEmpty()) {
                        return customUrl;
                    }
                }
            } catch (Exception ignored) {
            }
        }
        return DEFAULT_BASE_URL;
    }

    /**
     * Calculates the SHA-1 hash for normalized artist and title.
     */
    public static String computeSha1(String artist, String title) {
        String normA = artist == null ? "" : artist.trim().toLowerCase();
        String normT = title == null ? "" : title.trim().toLowerCase();
        String key = normA + "|" + normT;
        try {
            MessageDigest md = MessageDigest.getInstance("SHA-1");
            byte[] digest = md.digest(key.getBytes(StandardCharsets.UTF_8));
            StringBuilder sb = new StringBuilder(40);
            for (byte b : digest) {
                sb.append(String.format("%02x", b));
            }
            return sb.toString();
        } catch (NoSuchAlgorithmException e) {
            throw new RuntimeException("SHA-1 digest unavailable", e);
        }
    }

    public String getBaseUrl() {
        return baseUrl;
    }

    public Path getCacheDir() {
        return cacheDir;
    }

    /**
     * Asynchronously fetches lyrics for a track, checking disk cache first.
     *
     * @param artist      Song artist
     * @param title       Song track title
     * @param durationSec Audio duration in seconds (0 if unknown)
     * @return CompletableFuture containing Optional with Lyrics if found, or empty
     */
    public CompletableFuture<Optional<Lyrics>> getLyricsAsync(String artist, String title, double durationSec) {
        if (artist == null || artist.isBlank() || title == null || title.isBlank()) {
            return CompletableFuture.completedFuture(Optional.empty());
        }

        String sha1 = computeSha1(artist, title);
        Path cacheFile = cacheDir.resolve(sha1 + ".json");

        // 1. Check disk cache
        Optional<CacheEntry> cached = readCacheEntry(cacheFile);
        if (cached.isPresent()) {
            CacheEntry entry = cached.get();
            if (entry.notFound) {
                long age = System.currentTimeMillis() - entry.cachedAt;
                if (age < NEGATIVE_CACHE_TTL_MS) {
                    // Negative cache hit
                    touchFile(cacheFile);
                    return CompletableFuture.completedFuture(Optional.empty());
                }
                // Negative cache expired, fall through to fetch
            } else if (entry.lyrics != null) {
                // Positive cache hit
                touchFile(cacheFile);
                return CompletableFuture.completedFuture(Optional.of(entry.lyrics));
            }
        }

        // 2. Fetch from lobby-api HTTP endpoint
        StringBuilder urlBuilder = new StringBuilder(baseUrl)
                .append("/api/lyrics?artist=")
                .append(URLEncoder.encode(artist.trim(), StandardCharsets.UTF_8))
                .append("&title=")
                .append(URLEncoder.encode(title.trim(), StandardCharsets.UTF_8));

        if (durationSec > 0.0) {
            urlBuilder.append("&duration=").append(durationSec);
        }

        HttpRequest request;
        try {
            request = HttpRequest.newBuilder()
                    .uri(URI.create(urlBuilder.toString()))
                    .timeout(TIMEOUT)
                    .header("User-Agent", "AuraLauncher (github.com/qutlawsoasis-debug/Aura-Launcher)")
                    .header("Accept", "application/json")
                    .GET()
                    .build();
        } catch (Exception e) {
            return CompletableFuture.completedFuture(Optional.empty());
        }

        return httpClient.sendAsync(request, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8))
                .<Optional<Lyrics>>thenApply(response -> {
                    int statusCode = response.statusCode();
                    if (statusCode == 200) {
                        try {
                            Lyrics lyrics = Lyrics.parseJson(response.body());
                            writeCacheEntry(cacheFile, false, lyrics);
                            return Optional.of(lyrics);
                        } catch (Exception e) {
                            return Optional.empty();
                        }
                    } else if (statusCode == 404) {
                        // Negative cache: 24h
                        writeCacheEntry(cacheFile, true, null);
                        return Optional.empty();
                    } else {
                        // Transient server error: do not write negative cache
                        return Optional.empty();
                    }
                })
                .exceptionally(ex -> Optional.empty());
    }

    public CompletableFuture<Optional<Lyrics>> getLyricsAsync(String artist, String title) {
        return getLyricsAsync(artist, title, 0.0);
    }

    public Optional<Lyrics> getLyrics(String artist, String title, double durationSec) {
        try {
            return getLyricsAsync(artist, title, durationSec).get();
        } catch (Exception e) {
            return Optional.empty();
        }
    }

    private Optional<CacheEntry> readCacheEntry(Path file) {
        if (!Files.exists(file)) {
            return Optional.empty();
        }
        try {
            String content = Files.readString(file, StandardCharsets.UTF_8);
            JsonObject obj = JsonParser.parseString(content).getAsJsonObject();
            long cachedAt = obj.has("cachedAt") ? obj.get("cachedAt").getAsLong() : 0L;
            boolean notFound = obj.has("notFound") && obj.get("notFound").getAsBoolean();
            Lyrics lyrics = null;
            if (!notFound && obj.has("lyrics") && obj.get("lyrics").isJsonObject()) {
                lyrics = Lyrics.fromJson(obj.getAsJsonObject("lyrics"));
            }
            return Optional.of(new CacheEntry(cachedAt, notFound, lyrics));
        } catch (Exception e) {
            return Optional.empty();
        }
    }

    public synchronized void writeCacheEntry(Path file, boolean notFound, Lyrics lyrics) {
        try {
            if (!Files.exists(cacheDir)) {
                Files.createDirectories(cacheDir);
            }
            JsonObject obj = new JsonObject();
            obj.addProperty("cachedAt", System.currentTimeMillis());
            obj.addProperty("notFound", notFound);
            if (lyrics != null) {
                obj.add("lyrics", lyrics.toJson());
            }
            Files.writeString(file, obj.toString(), StandardCharsets.UTF_8);
            enforceLruLimit();
        } catch (Exception ignored) {
        }
    }

    public synchronized void enforceLruLimit() {
        try {
            if (!Files.exists(cacheDir)) {
                return;
            }
            List<Path> files;
            try (Stream<Path> stream = Files.list(cacheDir)) {
                files = stream
                        .filter(p -> p.getFileName().toString().endsWith(".json"))
                        .collect(Collectors.toList());
            }

            if (files.size() > MAX_CACHE_FILES) {
                // Sort ascending by last modified time (oldest first)
                files.sort(Comparator.comparingLong(p -> {
                    try {
                        return Files.getLastModifiedTime(p).toMillis();
                    } catch (IOException e) {
                        return 0L;
                    }
                }));

                int toDelete = files.size() - MAX_CACHE_FILES;
                for (int i = 0; i < toDelete; i++) {
                    try {
                        Files.deleteIfExists(files.get(i));
                    } catch (IOException ignored) {
                    }
                }
            }
        } catch (Exception ignored) {
        }
    }

    private void touchFile(Path file) {
        try {
            Files.setLastModifiedTime(file, FileTime.fromMillis(System.currentTimeMillis()));
        } catch (Exception ignored) {
        }
    }

    private record CacheEntry(long cachedAt, boolean notFound, Lyrics lyrics) {
    }
}

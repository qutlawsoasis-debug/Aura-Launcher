package net.auramc.glass.client.lyrics;

import com.google.gson.JsonObject;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.FileTime;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;
import java.util.stream.Collectors;
import java.util.stream.Stream;

public final class LyricsCacheTest {
    public static void main(String[] args) throws Exception {
        testSha1Computation();
        testPositiveAndNegativeCache();
        testLruEviction200Limit();
        System.out.println("LyricsCacheTest: PASS");
    }

    public static void testSha1Computation() {
        String hash1 = LyricsClient.computeSha1("Queen", "Bohemian Rhapsody");
        String hash2 = LyricsClient.computeSha1("  queen  ", "bohemian rhapsody  ");
        check(hash1 != null && hash1.length() == 40, "sha1 is 40 hex chars");
        check(hash1.equals(hash2), "sha1 is normalized and case-insensitive");

        String hash3 = LyricsClient.computeSha1("Queen", "Another One Bites The Dust");
        check(!hash1.equals(hash3), "different titles produce different sha1");
    }

    public static void testPositiveAndNegativeCache() throws IOException {
        Path tempDir = Files.createTempDirectory("lyrics-cache-test-");
        try {
            Path cacheDir = tempDir.resolve("lyrics-cache");
            LyricsClient client = new LyricsClient("http://127.0.0.1:9999", cacheDir, null);

            // 1. Positive cache write & read
            Path posFile = cacheDir.resolve("pos-test.json");
            Word w = new Word(1000L, 500L, "Test");
            Line line = new Line(1000L, "Test", List.of(w));
            Lyrics lyrics = new Lyrics("lrclib", true, List.of(line));

            client.writeCacheEntry(posFile, false, lyrics);
            check(Files.exists(posFile), "positive cache file written");

            // Read back via getLyricsAsync
            Optional<Lyrics> retrieved = client.getLyricsAsync("Artist", "PosTrack").join();
            // Note: Artist/PosTrack hash won't match pos-test.json unless we write to computeSha1
            String shaPos = LyricsClient.computeSha1("Artist", "PosTrack");
            Path shaPosFile = cacheDir.resolve(shaPos + ".json");
            client.writeCacheEntry(shaPosFile, false, lyrics);

            Optional<Lyrics> hit = client.getLyricsAsync("Artist", "PosTrack").join();
            check(hit.isPresent(), "positive cache hit");
            check("lrclib".equals(hit.get().source()), "positive cache source matches");
            check(hit.get().lines().size() == 1, "positive cache lines match");

            // 2. Negative cache write (active within 24h)
            String shaNeg = LyricsClient.computeSha1("Unknown", "Track");
            Path shaNegFile = cacheDir.resolve(shaNeg + ".json");
            client.writeCacheEntry(shaNegFile, true, null);

            Optional<Lyrics> negHit = client.getLyricsAsync("Unknown", "Track").join();
            check(negHit.isEmpty(), "negative cache hit returns empty immediately");

            // 3. Expired negative cache (> 24h)
            long expiredTime = System.currentTimeMillis() - (LyricsClient.NEGATIVE_CACHE_TTL_MS + 1000L);
            JsonObject expiredObj = new JsonObject();
            expiredObj.addProperty("cachedAt", expiredTime);
            expiredObj.addProperty("notFound", true);
            Files.writeString(shaNegFile, expiredObj.toString(), StandardCharsets.UTF_8);

            // Since server at 127.0.0.1:9999 is not running, expired negative cache attempts fetch and gracefully fails
            Optional<Lyrics> expiredResult = client.getLyricsAsync("Unknown", "Track").join();
            check(expiredResult.isEmpty(), "fetch after expired negative cache completes gracefully");

        } finally {
            deleteRecursively(tempDir);
        }
    }

    public static void testLruEviction200Limit() throws IOException {
        Path tempDir = Files.createTempDirectory("lyrics-lru-test-");
        try {
            Path cacheDir = tempDir.resolve("lyrics-cache");
            Files.createDirectories(cacheDir);

            LyricsClient client = new LyricsClient("http://127.0.0.1:9999", cacheDir, null);

            // Create 210 dummy cache files with sequential timestamps
            long baseTime = System.currentTimeMillis() - 100_000_000L;
            for (int i = 0; i < 210; i++) {
                Path file = cacheDir.resolve(String.format("file_%03d.json", i));
                JsonObject obj = new JsonObject();
                obj.addProperty("cachedAt", baseTime + i * 1000L);
                obj.addProperty("notFound", true);
                Files.writeString(file, obj.toString(), StandardCharsets.UTF_8);
                // Set explicit file last modified time
                Files.setLastModifiedTime(file, FileTime.fromMillis(baseTime + i * 1000L));
            }

            try (Stream<Path> stream = Files.list(cacheDir)) {
                check(stream.count() == 210, "initial count is 210 files");
            }

            // Enforce LRU
            client.enforceLruLimit();

            List<Path> remaining;
            try (Stream<Path> stream = Files.list(cacheDir)) {
                remaining = stream.collect(Collectors.toList());
            }

            check(remaining.size() == 200, "after LRU eviction count is exactly 200");

            // The oldest 10 files (file_000.json to file_009.json) should have been deleted
            for (int i = 0; i < 10; i++) {
                Path evicted = cacheDir.resolve(String.format("file_%03d.json", i));
                check(!Files.exists(evicted), "oldest file " + evicted.getFileName() + " was evicted");
            }

            // The newer files (file_010.json to file_209.json) should still exist
            for (int i = 10; i < 210; i++) {
                Path kept = cacheDir.resolve(String.format("file_%03d.json", i));
                check(Files.exists(kept), "newer file " + kept.getFileName() + " was kept");
            }

        } finally {
            deleteRecursively(tempDir);
        }
    }

    private static void deleteRecursively(Path dir) {
        if (!Files.exists(dir)) return;
        try (Stream<Path> stream = Files.walk(dir)) {
            stream.sorted(Comparator.reverseOrder()).forEach(p -> {
                try {
                    Files.deleteIfExists(p);
                } catch (IOException ignored) {}
            });
        } catch (IOException ignored) {}
    }

    private static void check(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError("Check failed: " + message);
        }
    }
}

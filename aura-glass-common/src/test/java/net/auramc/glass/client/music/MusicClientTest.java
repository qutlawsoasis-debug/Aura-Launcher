package net.auramc.glass.client.music;

import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpHandler;
import com.sun.net.httpserver.HttpServer;

import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.Random;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicLong;

public final class MusicClientTest {
    public static void main(String[] args) throws Exception {
        System.out.println("--- Starting Music Client Unit Tests ---");

        testExtrapolation();
        System.out.println("1. Extrapolation PASS");

        testResynchronization();
        System.out.println("2. Resynchronization and smoothing PASS");

        testMonotonicityWithJitter();
        System.out.println("3. Monotonicity on 1000 frames with ±60ms jitter PASS");

        testEpochCounter();
        System.out.println("4. Epoch counter on seek, track change and hard resync PASS");

        testJsonParsing();
        System.out.println("5. JSON parsing PASS");

        testUnavailableBridge();
        System.out.println("6. Unavailable bridge and HTTP integration PASS");

        System.out.println("All Aura Music Client tests PASSED successfully!");
    }

    private static void testExtrapolation() {
        AtomicLong mockTime = new AtomicLong(10_000L);
        MusicClock clock = new MusicClock(mockTime::get);

        Track track = new Track("t1", "Artist A", "Title A", 180_000L, "http://cover.jpg");
        NowPlaying playing = new NowPlaying(PlaybackState.PLAYING, track, 5_000L, 10_000L, 0.8f);

        clock.update(playing);

        // At t = 10_000, position is 5_000
        check(clock.positionMs() == 5_000L, "Extrapolation initial position at t=10_000");

        // At t = 10_500 (+500 ms elapsed), position should be 5_500
        mockTime.set(10_500L);
        check(clock.positionMs() == 5_500L, "Extrapolation after 500 ms");

        // At t = 12_000 (+2000 ms elapsed), position should be 7_000
        mockTime.set(12_000L);
        check(clock.positionMs() == 7_000L, "Extrapolation after 2000 ms");

        // Test paused state: should not advance
        NowPlaying paused = new NowPlaying(PlaybackState.PAUSED, track, 7_000L, 12_000L, 0.8f);
        clock.update(paused);
        mockTime.set(15_000L);
        check(clock.positionMs() == 7_000L, "Paused state does not extrapolate with time");

        // Test stopped state: returns 0
        NowPlaying stopped = new NowPlaying(PlaybackState.STOPPED, track, 0L, 15_000L, 0.8f);
        clock.update(stopped);
        mockTime.set(16_000L);
        check(clock.positionMs() == 0L, "Stopped state returns 0");

        // Test duration clamping
        NowPlaying nearEnd = new NowPlaying(PlaybackState.PLAYING, track, 179_000L, 20_000L, 1.0f);
        clock.update(nearEnd);
        mockTime.set(25_000L); // 5000 ms later -> 179_000 + 5_000 = 184_000, capped at 180_000
        check(clock.positionMs() == 180_000L, "Position clamped to track duration");
    }

    private static void testResynchronization() {
        AtomicLong mockTime = new AtomicLong(10_000L);
        MusicClock clock = new MusicClock(mockTime::get);

        Track track = new Track("t2", "Artist B", "Title B", 300_000L, "");
        NowPlaying np = new NowPlaying(PlaybackState.PLAYING, track, 20_000L, 10_000L, 1.0f);
        clock.update(np);

        // Frame 1 at t=10_000
        long pos1 = clock.positionMs();
        check(pos1 == 20_000L, "Initial position 20_000");

        // Simulate frame 2 at t=10_016 (+16 ms)
        mockTime.set(10_016L);
        long pos2 = clock.positionMs();
        check(pos2 >= 20_015L && pos2 <= 20_017L, "Frame 2 smooth progression +16 ms");

        // Test small jitter <= 40 ms smoothing:
        // Server reports position with +35 ms jitter at t=10_032
        mockTime.set(10_032L);
        NowPlaying npJitter = new NowPlaying(PlaybackState.PLAYING, track, 20_067L, 10_032L, 1.0f); // 35 ms jitter
        clock.update(npJitter);
        long pos3 = clock.positionMs();
        // With smoothing, pos3 should NOT jump all the way to 20_067 abruptly (+35 ms jump in 16 ms)
        check(pos3 < 20_060L, "Small jump <= 40 ms is smoothed, not popping abruptly (pos=" + pos3 + ")");

        // Test large discrepancy > 250 ms: hard resync!
        mockTime.set(10_050L);
        NowPlaying npJump = new NowPlaying(PlaybackState.PLAYING, track, 50_000L, 10_050L, 1.0f); // +30 seconds jump
        clock.update(npJump);
        long posHard = clock.positionMs();
        check(posHard == 50_000L, "Hard resync on deviation > 250 ms (expected 50000, got " + posHard + ")");

        // Test explicit seek: immediate hard resync to seek target
        clock.seek(85_000L);
        long posAfterSeek = clock.positionMs();
        check(posAfterSeek == 85_000L, "Immediate hard resync after seek to 85_000");
    }

    private static void testMonotonicityWithJitter() {
        AtomicLong mockTime = new AtomicLong(10_000L);
        MusicClock clock = new MusicClock(mockTime::get);

        Track track = new Track("mono_1", "Aura Test", "Monotonic Flow", 300_000L, "");
        long initialPos = 10_000L;
        clock.update(new NowPlaying(PlaybackState.PLAYING, track, initialPos, mockTime.get(), 1.0f));

        Random rng = new Random(42L);
        long prevPos = clock.positionMs();
        long serverBaseTime = mockTime.get();
        long serverBasePos = initialPos;

        // 1000 frames simulated (16 ms each frame, total 16 seconds)
        for (int frame = 1; frame <= 1000; frame++) {
            mockTime.addAndGet(16L); // Advance 16 ms per frame

            // Every ~500 ms (every 31 frames), poller reports a new /now snapshot with ±60 ms jitter
            if (frame % 31 == 0) {
                long trueServerPos = serverBasePos + (mockTime.get() - serverBaseTime);
                int noiseMs = rng.nextInt(121) - 60; // [-60, +60] ms noise
                long noisyPos = Math.max(0L, trueServerPos + noiseMs);
                clock.update(new NowPlaying(PlaybackState.PLAYING, track, noisyPos, mockTime.get(), 1.0f));
            }

            long currentPos = clock.positionMs();
            check(currentPos >= prevPos, "Monotonicity violated at frame " + frame + ": currentPos=" + currentPos + " < prevPos=" + prevPos);
            prevPos = currentPos;
        }

        // Verify clock stayed close to true time (total elapsed ~16_000 ms)
        long totalElapsed = prevPos - initialPos;
        check(Math.abs(totalElapsed - 16_000L) < 200L, "Clock accurately tracked 16 seconds within drift tolerance (elapsed=" + totalElapsed + ")");
    }

    private static void testEpochCounter() {
        AtomicLong mockTime = new AtomicLong(10_000L);
        MusicClock clock = new MusicClock(mockTime::get);

        Track track1 = new Track("ep_1", "Artist 1", "Song 1", 200_000L, "");
        clock.update(new NowPlaying(PlaybackState.PLAYING, track1, 0L, mockTime.get(), 1.0f));

        long epoch0 = clock.epoch();
        clock.positionMs(); // frame 1 triggers track start
        long epochAfterInit = clock.epoch();
        check(epochAfterInit > epoch0, "Epoch incremented on initial track detection");

        // Normal playback and small jitter should NOT change epoch
        for (int i = 0; i < 50; i++) {
            mockTime.addAndGet(16L);
            clock.positionMs();
        }
        check(clock.epoch() == epochAfterInit, "Epoch remains stable during continuous playback");

        // Small jitter update (±40 ms) does NOT change epoch
        clock.update(new NowPlaying(PlaybackState.PLAYING, track1, clock.positionMs() + 30L, mockTime.get(), 1.0f));
        clock.positionMs();
        check(clock.epoch() == epochAfterInit, "Epoch unchanged on small jitter (<= 250 ms)");

        // Hard jump > 250 ms MUST increment epoch
        clock.update(new NowPlaying(PlaybackState.PLAYING, track1, clock.positionMs() + 500L, mockTime.get(), 1.0f));
        clock.positionMs();
        long epochAfterHardJump = clock.epoch();
        check(epochAfterHardJump > epochAfterInit, "Epoch incremented on hard jump > 250 ms");

        // Seek MUST increment epoch
        clock.seek(50_000L);
        clock.positionMs();
        long epochAfterSeek = clock.epoch();
        check(epochAfterSeek > epochAfterHardJump, "Epoch incremented on seek()");

        // Track change MUST increment epoch
        Track track2 = new Track("ep_2", "Artist 2", "Song 2", 150_000L, "");
        clock.update(new NowPlaying(PlaybackState.PLAYING, track2, 0L, mockTime.get(), 1.0f));
        clock.positionMs();
        long epochAfterTrackChange = clock.epoch();
        check(epochAfterTrackChange > epochAfterSeek, "Epoch incremented on track change");
    }

    private static void testJsonParsing() {
        // 1. Standard /now JSON
        String validJson = "{\n" +
                "  \"state\": \"playing\",\n" +
                "  \"track\": {\n" +
                "    \"id\": \"trk_42\",\n" +
                "    \"artist\": \"Aura Lab\",\n" +
                "    \"title\": \"Liquid Glass\",\n" +
                "    \"durationMs\": 215000,\n" +
                "    \"coverUrl\": \"https://img.auramc.net/cover.png\"\n" +
                "  },\n" +
                "  \"positionMs\": 45000,\n" +
                "  \"updatedAtUnixMs\": 1700000000000,\n" +
                "  \"volume\": 0.75\n" +
                "}";

        NowPlaying np = MusicJsonParser.parseNowPlaying(validJson);
        check(np.isPlaying(), "Parsed state is PLAYING");
        check(np.hasTrack(), "Parsed track is present");
        check("trk_42".equals(np.track().id()), "Parsed track id");
        check("Aura Lab".equals(np.track().artist()), "Parsed track artist");
        check("Liquid Glass".equals(np.track().title()), "Parsed track title");
        check(np.track().durationMs() == 215_000L, "Parsed track durationMs");
        check("https://img.auramc.net/cover.png".equals(np.track().coverUrl()), "Parsed coverUrl");
        check(np.positionMs() == 45_000L, "Parsed positionMs");
        check(np.updatedAtUnixMs() == 1700000000000L, "Parsed updatedAtUnixMs");
        check(Math.abs(np.volume() - 0.75f) < 0.001f, "Parsed volume");

        // 2. Alternate field names (duration, position, updatedAt)
        String altJson = "{\n" +
                "  \"state\": \"paused\",\n" +
                "  \"track\": { \"id\": \"t_alt\", \"title\": \"Alt Title\", \"duration\": 120000 },\n" +
                "  \"position\": 30000,\n" +
                "  \"updatedAt\": 1699999999000,\n" +
                "  \"volume\": 0.5\n" +
                "}";
        NowPlaying npAlt = MusicJsonParser.parseNowPlaying(altJson);
        check(npAlt.isPaused(), "Parsed paused state");
        check(npAlt.positionMs() == 30_000L, "Parsed position from alternate key");
        check(npAlt.updatedAtUnixMs() == 1699999999000L, "Parsed updatedAt from alternate key");
        check(npAlt.track().durationMs() == 120_000L, "Parsed duration from alternate key");

        // 3. Null / empty / malformed JSON
        NowPlaying malformed = MusicJsonParser.parseNowPlaying("{invalid json string");
        check(malformed.isStopped(), "Malformed JSON returns stopped state");
        NowPlaying empty = MusicJsonParser.parseNowPlaying("");
        check(empty.isStopped(), "Empty JSON returns stopped state");

        // 4. Track list search parsing
        String searchJson = "[\n" +
                "  {\"id\": \"s1\", \"artist\": \"Art1\", \"title\": \"Song1\", \"durationMs\": 180000},\n" +
                "  {\"id\": \"s2\", \"artist\": \"Art2\", \"title\": \"Song2\", \"durationMs\": 240000}\n" +
                "]";
        List<Track> list = MusicJsonParser.parseTrackList(searchJson);
        check(list.size() == 2, "Parsed search track list size");
        check("s1".equals(list.get(0).id()), "First track id");
        check("s2".equals(list.get(1).id()), "Second track id");

        // 5. BridgeConfig parsing
        String configJson = "{\"port\": 54321, \"token\": \"test-secret-token-32b\"}";
        BridgeConfig config = BridgeConfig.parse(configJson);
        check(config != null && config.isValid(), "Parsed valid bridge config");
        check(config.port() == 54321, "Config port matches");
        check("test-secret-token-32b".equals(config.token()), "Config token matches");

        check(BridgeConfig.parse("{\"port\": 0, \"token\": \"tok\"}") == null, "Invalid port 0 rejected");
        check(BridgeConfig.parse("{\"port\": 1000, \"token\": \"\"}") == null, "Empty token rejected");
    }

    private static void testUnavailableBridge() throws Exception {
        Path tempDir = Files.createTempDirectory("aura-music-test-");
        Path missingConfig = tempDir.resolve("missing-bridge.json");

        // 1. Missing config file -> unavailable, quiet
        try (BridgeMusicSource source = new BridgeMusicSource(missingConfig, java.net.http.HttpClient.newHttpClient(), false)) {
            source.pollOnce();
            check(!source.available(), "Source unavailable when config missing");
            check(source.now().isStopped(), "NowPlaying is stopped when config missing");

            // Commands should silently do nothing without throwing
            source.play();
            source.pause();
            source.next();
            source.prev();
            source.seek(10_000L);
            source.volume(0.5f);
            check(source.search("query").isEmpty(), "Search returns empty when unavailable");
        }

        // 2. Unreachable port -> unavailable, quiet
        Path unreachableConfig = tempDir.resolve("unreachable-bridge.json");
        Files.writeString(unreachableConfig, "{\"port\": 64999, \"token\": \"dummy\"}", StandardCharsets.UTF_8);
        try (BridgeMusicSource source = new BridgeMusicSource(unreachableConfig, java.net.http.HttpClient.newHttpClient(), false)) {
            source.pollOnce();
            check(!source.available(), "Source unavailable on connection refused");
            check(source.now().isStopped(), "NowPlaying is stopped on connection refused");
        }

        // 3. Live local HTTP server integration
        HttpServer server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        int realPort = server.getAddress().getPort();
        String validToken = "auth-token-xyz-1234567890";

        AtomicBoolean playReceived = new AtomicBoolean(false);
        AtomicBoolean pauseReceived = new AtomicBoolean(false);
        AtomicLong seekReceived = new AtomicLong(-1L);
        AtomicInteger volumeReceived = new AtomicInteger(-1);

        server.createContext("/now", new HttpHandler() {
            @Override
            public void handle(HttpExchange exchange) throws IOException {
                String auth = exchange.getRequestHeaders().getFirst("Authorization");
                if (("Bearer " + validToken).equals(auth)) {
                    String resp = "{\n" +
                            "  \"state\": \"playing\",\n" +
                            "  \"track\": {\"id\": \"live_1\", \"artist\": \"Live Artist\", \"title\": \"Live Beat\", \"durationMs\": 200000},\n" +
                            "  \"positionMs\": 12000,\n" +
                            "  \"updatedAtUnixMs\": " + System.currentTimeMillis() + ",\n" +
                            "  \"volume\": 0.9\n" +
                            "}";
                    byte[] bytes = resp.getBytes(StandardCharsets.UTF_8);
                    exchange.getResponseHeaders().set("Content-Type", "application/json");
                    exchange.sendResponseHeaders(200, bytes.length);
                    try (OutputStream os = exchange.getResponseBody()) {
                        os.write(bytes);
                    }
                } else {
                    exchange.sendResponseHeaders(401, -1);
                }
            }
        });

        server.createContext("/play", exchange -> {
            playReceived.set(true);
            exchange.sendResponseHeaders(200, -1);
        });

        server.createContext("/pause", exchange -> {
            pauseReceived.set(true);
            exchange.sendResponseHeaders(200, -1);
        });

        server.createContext("/seek", exchange -> {
            String body = new String(exchange.getRequestBody().readAllBytes(), StandardCharsets.UTF_8);
            if (body.contains("55000")) seekReceived.set(55000L);
            exchange.sendResponseHeaders(200, -1);
        });

        server.createContext("/volume", exchange -> {
            String body = new String(exchange.getRequestBody().readAllBytes(), StandardCharsets.UTF_8);
            if (body.contains("0.4")) volumeReceived.set(40);
            exchange.sendResponseHeaders(200, -1);
        });

        server.createContext("/search", exchange -> {
            String resp = "[{\"id\":\"search_1\",\"artist\":\"Searched\",\"title\":\"Found Song\",\"durationMs\":150000}]";
            byte[] bytes = resp.getBytes(StandardCharsets.UTF_8);
            exchange.getResponseHeaders().set("Content-Type", "application/json");
            exchange.sendResponseHeaders(200, bytes.length);
            try (OutputStream os = exchange.getResponseBody()) {
                os.write(bytes);
            }
        });

        server.start();

        try {
            Path liveConfig = tempDir.resolve("live-bridge.json");
            Files.writeString(liveConfig, "{\"port\": " + realPort + ", \"token\": \"" + validToken + "\"}", StandardCharsets.UTF_8);

            try (BridgeMusicSource liveSource = new BridgeMusicSource(liveConfig, java.net.http.HttpClient.newHttpClient(), false)) {
                liveSource.pollOnce();
                check(liveSource.available(), "Bridge is available when server responds with 200");
                NowPlaying np = liveSource.now();
                check(np.isPlaying(), "NowPlaying is playing");
                check("live_1".equals(np.track().id()), "Track id live_1 received");
                check(np.positionMs() == 12_000L, "Position 12_000 received");

                // Test asynchronous commands
                liveSource.play();
                liveSource.pause();
                liveSource.seek(55_000L);
                liveSource.volume(0.4f);

                // Give async requests up to 1 second to arrive
                for (int i = 0; i < 20; i++) {
                    if (playReceived.get() && pauseReceived.get() && seekReceived.get() == 55_000L && volumeReceived.get() == 40) {
                        break;
                    }
                    Thread.sleep(50L);
                }

                check(playReceived.get(), "Command /play delivered");
                check(pauseReceived.get(), "Command /pause delivered");
                check(seekReceived.get() == 55_000L, "Command /seek delivered");
                check(volumeReceived.get() == 40, "Command /volume delivered");

                // Test search
                List<Track> searchResults = liveSource.search("Found");
                check(searchResults.size() == 1, "Search returns 1 result");
                check("search_1".equals(searchResults.get(0).id()), "Search result track matches");
            }
        } finally {
            server.stop(0);
        }

        // Test StubMusicSource for QA
        StubMusicSource stub = new StubMusicSource();
        check(stub.available(), "Stub is available");
        NowPlaying stubNow = stub.now();
        check(stubNow.hasTrack(), "Stub provides QA track timeline");
        check(stubNow.isPlaying(), "Stub starts in playing state");
        stub.pause();
        check(stub.now().isPaused(), "Stub pauses");
        stub.play();
        check(stub.now().isPlaying(), "Stub resumes");
        stub.seek(60_000L);
        check(stub.now().positionMs() >= 60_000L, "Stub seeks to 60_000");
        check(!stub.search("Odyssey").isEmpty(), "Stub search finds track");
    }

    private static void check(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError("FAILED: " + message);
        }
    }
}

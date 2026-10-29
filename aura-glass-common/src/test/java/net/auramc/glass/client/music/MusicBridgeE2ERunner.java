package net.auramc.glass.client.music;

import java.net.http.HttpClient;
import java.nio.file.Path;
import java.time.Duration;

public final class MusicBridgeE2ERunner {
    private static void check(boolean condition, String message) {
        if (!condition) {
            System.err.println("ASSERTION FAILED: " + message);
            throw new AssertionError(message);
        }
    }

    public static void main(String[] args) throws Exception {
        if (args.length < 1) {
            System.err.println("Usage: MusicBridgeE2ERunner <configPath> [durationSec]");
            System.exit(1);
        }

        Path configPath = Path.of(args[0]);
        int durationSec = (args.length > 1) ? Integer.parseInt(args[1]) : 60;

        System.out.println("=== Starting MusicBridge E2E Integration Test ===");
        System.out.println("Config path: " + configPath.toAbsolutePath());
        System.out.println("Playback track test duration: " + durationSec + "s");

        HttpClient httpClient = HttpClient.newBuilder()
                .connectTimeout(Duration.ofMillis(1000))
                .build();

        try (BridgeMusicSource source = new BridgeMusicSource(configPath, httpClient, true)) {
            // 1. Wait for bridge to become available
            System.out.println("Step 1: Connecting to bridge...");
            long connectDeadline = System.currentTimeMillis() + 10_000L;
            while (System.currentTimeMillis() < connectDeadline && !source.available()) {
                Thread.sleep(100L);
            }
            check(source.available(), "Bridge did not become available within 10 seconds");
            System.out.println("[PASS] Bridge is available!");

            // 2. Verify track in queue
            NowPlaying initial = source.now();
            check(initial.hasTrack(), "Initial NowPlaying has no track");
            check(initial.track().id() != null && !initial.track().id().isEmpty(), "Track ID is missing");
            System.out.println("[PASS] 1. Initial track in queue: '" + initial.track().title() + "' by '" + initial.track().artist() + "' (state=" + initial.state() + ")");

            // 3. Client controls: play/pause/seek/next/volume
            System.out.println("Step 2: Testing client transport controls...");

            // Play command
            source.play();
            long playDeadline = System.currentTimeMillis() + 4000L;
            while (System.currentTimeMillis() < playDeadline && !source.now().isPlaying()) {
                Thread.sleep(100L);
            }
            check(source.now().isPlaying(), "State should be PLAYING after source.play()");
            System.out.println("[PASS] 2a. Play confirmed: " + source.now().state());

            // Pause command
            source.pause();
            long pauseDeadline = System.currentTimeMillis() + 4000L;
            while (System.currentTimeMillis() < pauseDeadline && !source.now().isPaused()) {
                Thread.sleep(100L);
            }
            check(source.now().isPaused(), "State should be PAUSED after source.pause()");
            System.out.println("[PASS] 2b. Pause confirmed: " + source.now().state());

            // Resume playback
            source.play();
            long resumeDeadline = System.currentTimeMillis() + 4000L;
            while (System.currentTimeMillis() < resumeDeadline && !source.now().isPlaying()) {
                Thread.sleep(100L);
            }
            check(source.now().isPlaying(), "State should be PLAYING after resume");
            System.out.println("[PASS] 2c. Resume confirmed");

            // Volume command
            float targetVol = 0.65f;
            source.volume(targetVol);
            long volDeadline = System.currentTimeMillis() + 4000L;
            while (System.currentTimeMillis() < volDeadline && Math.abs(source.now().volume() - targetVol) > 0.05f) {
                Thread.sleep(100L);
            }
            check(Math.abs(source.now().volume() - targetVol) <= 0.05f, "Volume should be ~0.65, got: " + source.now().volume());
            System.out.println("[PASS] 2d. Volume confirmed: " + source.now().volume());

            // Next / Prev commands
            String track1Id = source.now().track().id();
            source.next();
            long nextDeadline = System.currentTimeMillis() + 4000L;
            while (System.currentTimeMillis() < nextDeadline && source.now().track() != null && source.now().track().id().equals(track1Id)) {
                Thread.sleep(100L);
            }
            check(source.now().track() != null && !source.now().track().id().equals(track1Id), "Track should advance after source.next()");
            System.out.println("[PASS] 2e. Next track confirmed: '" + source.now().track().title() + "'");

            source.prev();
            long prevDeadline = System.currentTimeMillis() + 4000L;
            while (System.currentTimeMillis() < prevDeadline && source.now().track() != null && !source.now().track().id().equals(track1Id)) {
                Thread.sleep(100L);
            }
            check(source.now().track() != null && source.now().track().id().equals(track1Id), "Track should return after source.prev()");
            System.out.println("[PASS] 2f. Prev track confirmed: '" + source.now().track().title() + "'");

            // 4. MusicClock tracking over 60s (±150 ms)
            System.out.println("Step 3: Verifying MusicClock tracking over " + durationSec + "s...");
            MusicClock clock = new MusicClock(source);
            if (!source.now().isPlaying()) {
                source.play();
                Thread.sleep(200L);
            }

            long observationStart = System.currentTimeMillis();
            long observationEnd = observationStart + (durationSec * 1000L);
            int samples = 0;
            long maxDrift = 0L;

            while (System.currentTimeMillis() < observationEnd) {
                long now = System.currentTimeMillis();
                long clockPos = clock.positionMs(now);
                NowPlaying current = source.now();

                long elapsedSinceUpdate = current.isPlaying() ? Math.max(0L, now - current.updatedAtUnixMs()) : 0L;
                long playerPos = current.positionMs() + elapsedSinceUpdate;
                long drift = Math.abs(clockPos - playerPos);
                if (drift > maxDrift) maxDrift = drift;

                check(drift <= 150L, "MusicClock drift exceeded 150 ms: drift=" + drift + " ms (clock=" + clockPos + ", player=" + playerPos + ")");
                samples++;
                Thread.sleep(50L);
            }
            System.out.println("[PASS] 3. MusicClock tracked player position over " + durationSec + "s (" + samples + " samples, max drift = " + maxDrift + " ms <= 150 ms)");

            // 5. Seek convergence ≤ 300 ms
            System.out.println("Step 4: Testing seek convergence (<= 300 ms)...");
            long seekTarget = 35_000L;
            long seekCallTime = System.currentTimeMillis();
            clock.seek(seekTarget);

            long convergedAt = -1L;
            long seekDeadline = seekCallTime + 2000L;

            while (System.currentTimeMillis() < seekDeadline) {
                long now = System.currentTimeMillis();
                long clockPos = clock.positionMs(now);
                long expectedPos = seekTarget + (now - seekCallTime);
                long diff = Math.abs(clockPos - expectedPos);

                if (diff <= 150L && source.now().positionMs() >= seekTarget - 200L) {
                    convergedAt = now;
                    break;
                }
                Thread.sleep(10L);
            }

            check(convergedAt > 0, "Clock failed to converge after seek within 2000 ms");
            long convergenceTime = convergedAt - seekCallTime;
            check(convergenceTime <= 300L, "Seek convergence took " + convergenceTime + " ms, expected <= 300 ms");
            System.out.println("[PASS] 4. Seek convergence verified: converged in " + convergenceTime + " ms (<= 300 ms)");

            // 6. Bridge closed: available=false, zero polls
            System.out.println("Step 5: Testing bridge closed state (available=false, 0 polls)...");
            System.out.println("SIGNAL:READY_FOR_BRIDGE_CLOSE");

            // Wait until bridge config is deleted or server closed
            long closeDeadline = System.currentTimeMillis() + 15_000L;
            while (System.currentTimeMillis() < closeDeadline && source.available()) {
                Thread.sleep(100L);
            }

            check(!source.available(), "Bridge should become unavailable when closed/deleted");
            check(source.now().isStopped(), "NowPlaying must report STOPPED when bridge is unavailable");

            // Observe HTTP poll count for 2 seconds
            long pollsBefore = source.httpPollCount();
            Thread.sleep(2000L);
            long pollsAfter = source.httpPollCount();
            long diffPolls = pollsAfter - pollsBefore;

            check(diffPolls == 0L, "Expected 0 HTTP polls after bridge closed, got " + diffPolls);
            System.out.println("[PASS] 5. Bridge closed verified: available=false, zero HTTP polls (" + diffPolls + " requests over 2s)");

            System.out.println("=== ALL E2E MUSIC BRIDGE TESTS PASSED ===");
        }
    }
}

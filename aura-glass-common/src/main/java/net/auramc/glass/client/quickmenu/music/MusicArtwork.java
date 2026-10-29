package net.auramc.glass.client.quickmenu.music;

import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.texture.NativeImage;
import net.minecraft.client.texture.NativeImageBackedTexture;
import net.minecraft.util.Identifier;
import javax.imageio.ImageIO;
import javax.imageio.ImageReader;
import javax.imageio.stream.MemoryCacheImageInputStream;
import java.awt.RenderingHints;
import java.awt.image.BufferedImage;
import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.ThreadPoolExecutor;
import java.util.concurrent.TimeUnit;

public final class MusicArtwork {
    private static final int MAX_BYTES = 4 * 1024 * 1024, MAX_DIMENSION = 4096, THUMBNAIL = 256, MAX_CACHED = 4;
    private static final HttpClient HTTP = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(6))
            .followRedirects(HttpClient.Redirect.NEVER).build();
    private static final ExecutorService WORK = new ThreadPoolExecutor(1, 1, 0, TimeUnit.MILLISECONDS, new ArrayBlockingQueue<>(4), task -> {
        Thread thread = new Thread(task, "Aura artwork"); thread.setDaemon(true); return thread;
    }, new ThreadPoolExecutor.DiscardOldestPolicy());
    private static final Map<String, Entry> CACHE = new LinkedHashMap<>(8, .75f, true);
    private static long generation;
    private static final class Entry {
        Identifier texture;
    }
    private MusicArtwork() {}

    public static Identifier texture(net.auramc.glass.client.music.Track track) {
        if (track == null || track.coverUrl() == null || track.coverUrl().isBlank()) return null;
        return textureUrl(track.coverUrl());
    }

    public static Identifier texture(MusicTrack track) {
        if (track == null || track.artwork() == null || track.artwork().isBlank()) return null;
        return textureUrl(track.artwork());
    }

    public static Identifier textureUrl(String key) {
        if (key == null || key.isBlank() || key.length() > 4096) return null;
        synchronized (CACHE) {
            Entry existing = CACHE.get(key);
            if (existing != null) return existing.texture;
            Entry entry = new Entry();
            CACHE.put(key, entry);
            while (CACHE.size() > MAX_CACHED) {
                Iterator<Entry> oldest = CACHE.values().iterator();
                Entry removed = oldest.next(); oldest.remove(); dispose(removed.texture);
            }
            long requestGeneration = generation;
            WORK.execute(() -> load(key, entry, requestGeneration));
            return null;
        }
    }

    private static void load(String key, Entry entry, long requestGeneration) {
        BufferedImage decoded = null;
        try {
            synchronized (CACHE) {
                if (generation != requestGeneration || CACHE.get(key) != entry) return;
            }
            byte[] bytes = read(key);
            decoded = decode(bytes);
            int width = decoded.getWidth(), height = decoded.getHeight();
            double scale = Math.min(1, THUMBNAIL / (double) Math.max(width, height));
            int targetWidth = Math.max(1, (int) Math.round(width * scale));
            int targetHeight = Math.max(1, (int) Math.round(height * scale));
            BufferedImage thumbnail = new BufferedImage(targetWidth, targetHeight, BufferedImage.TYPE_INT_ARGB);
            var graphics = thumbnail.createGraphics();
            try {
                graphics.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BILINEAR);
                graphics.drawImage(decoded, 0, 0, targetWidth, targetHeight, null);
            } finally { graphics.dispose(); }
            int[] pixels = thumbnail.getRGB(0, 0, targetWidth, targetHeight, null, 0, targetWidth);
            thumbnail.flush();
            MinecraftClient.getInstance().execute(() -> upload(key, entry, requestGeneration, targetWidth, targetHeight, pixels));
        } catch (Exception error) {
            System.err.println("[AuraMusic] Artwork unavailable: " + error.getClass().getSimpleName());
        } finally { if (decoded != null) decoded.flush(); }
    }

    private static byte[] read(String value) throws Exception {
        URI uri = URI.create(value);
        if ("https".equalsIgnoreCase(uri.getScheme())) {
            String host = uri.getHost();
            if (host == null || uri.getUserInfo() != null || uri.getPort() != -1 && uri.getPort() != 443 || uri.getFragment() != null)
                throw new IOException("Artwork URL rejected");
            host = host.toLowerCase(java.util.Locale.ROOT);
            if (!(host.equals("ytimg.com") || host.endsWith(".ytimg.com") || host.equals("ggpht.com") || host.endsWith(".ggpht.com")))
                throw new IOException("Artwork host rejected");
            HttpRequest request = HttpRequest.newBuilder(uri).timeout(Duration.ofSeconds(10)).header("Accept", "image/png,image/jpeg,image/webp").GET().build();
            HttpResponse<InputStream> response = HTTP.send(request, HttpResponse.BodyHandlers.ofInputStream());
            try (InputStream stream = response.body()) {
                if (response.statusCode() != 200 || response.headers().firstValueAsLong("Content-Length").orElse(0) > MAX_BYTES)
                    throw new IOException("Artwork response rejected");
                return bounded(stream);
            }
        }
        if (uri.getScheme() != null && !"file".equalsIgnoreCase(uri.getScheme())) throw new IOException("Artwork scheme rejected");
        if (uri.getAuthority() != null || uri.getQuery() != null || uri.getFragment() != null) throw new IOException("Artwork file URL rejected");
        Path root = FabricLoader.getInstance().getConfigDir().resolve("aura-glass/music").toRealPath();
        Path path = (uri.getScheme() == null ? root.resolve(value) : Path.of(uri)).toRealPath();
        if (!path.startsWith(root) || !Files.isRegularFile(path) || Files.size(path) > MAX_BYTES) throw new IOException("Artwork outside music directory");
        try (InputStream stream = Files.newInputStream(path)) { return bounded(stream); }
    }

    private static byte[] bounded(InputStream stream) throws IOException {
        byte[] bytes = stream.readNBytes(MAX_BYTES + 1);
        if (bytes.length == 0 || bytes.length > MAX_BYTES) throw new IOException("Artwork exceeds byte limit");
        return bytes;
    }

    private static BufferedImage decode(byte[] bytes) throws IOException {
        try (var input = new MemoryCacheImageInputStream(new ByteArrayInputStream(bytes))) {
            Iterator<ImageReader> readers = ImageIO.getImageReaders(input);
            if (!readers.hasNext()) throw new IOException("Unsupported artwork image");
            ImageReader reader = readers.next();
            try {
                reader.setInput(input, true, true);
                int width = reader.getWidth(0), height = reader.getHeight(0);
                if (width <= 0 || height <= 0 || width > MAX_DIMENSION || height > MAX_DIMENSION)
                    throw new IOException("Artwork dimensions rejected");
                var parameters = reader.getDefaultReadParam();
                int subsample = Math.max(1, Math.max(width, height) / THUMBNAIL);
                parameters.setSourceSubsampling(subsample, subsample, 0, 0);
                BufferedImage image = reader.read(0, parameters);
                if (image == null) throw new IOException("Unreadable artwork");
                return image;
            } finally { reader.dispose(); }
        }
    }

    private static void upload(String key, Entry entry, long requestGeneration, int width, int height, int[] pixels) {
        synchronized (CACHE) {
            if (generation != requestGeneration || CACHE.get(key) != entry) return;
            NativeImage image = new NativeImage(width, height, false);
            NativeImageBackedTexture texture = null;
            boolean owned = false;
            try {
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
                    int argb = pixels[y * width + x];
                    int abgr = argb & 0xFF00FF00 | (argb >> 16 & 255) | (argb & 255) << 16;
                    image.setColor(x, y, abgr);
                }
                texture = new NativeImageBackedTexture(image);
                entry.texture = MinecraftClient.getInstance().getTextureManager().registerDynamicTexture("aura_music_artwork", texture);
                owned = true;
            } finally {
                if (!owned) { if (texture != null) texture.close(); else image.close(); }
            }
        }
    }

    private static void dispose(Identifier identifier) {
        if (identifier != null) MinecraftClient.getInstance().execute(() -> MinecraftClient.getInstance().getTextureManager().destroyTexture(identifier));
    }

    public static void close() {
        synchronized (CACHE) {
            generation++;
            CACHE.values().forEach(entry -> dispose(entry.texture));
            CACHE.clear();
        }
    }
}
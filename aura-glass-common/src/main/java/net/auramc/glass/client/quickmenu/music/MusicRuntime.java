package net.auramc.glass.client.quickmenu.music;

import com.google.gson.*;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.sound.OggAudioStream;
import javax.sound.sampled.*;
import java.io.*;
import java.net.URI;
import java.net.URLEncoder;
import java.net.http.*;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.time.Duration;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicLong;

public final class MusicRuntime {
    private static final String API = "https://lobby-api.vercel.app/api/music/";
    private static final int MAX_PCM_BYTES = 96 * 1024 * 1024;
    private static final HttpClient HTTP = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(8)).followRedirects(HttpClient.Redirect.NEVER).build();
    private static final ExecutorService WORK = Executors.newCachedThreadPool(task -> { Thread t = new Thread(task, "Aura music"); t.setDaemon(true); return t; });
    private static final Object LOCK = new Object();
    private static final AtomicLong GENERATION = new AtomicLong(), SEARCH_GENERATION = new AtomicLong();
    private static volatile List<MusicTrack> localTracks = List.of(), results = List.of(), queue = List.of();
    private static volatile MusicTrack current;
    private static volatile SourceDataLine line;
    private static volatile boolean playing, loading, initialized;
    private static volatile String status = "Добавьте WAV, AIFF, AU или OGG в config/aura-glass/music";
    private static volatile double volume = .5;
    private static volatile long positionBaseFrames, lineFrameBase, seekFrame = -1, durationMs;
    private static volatile float sampleRate = 44100;
    private static volatile byte[] pcm;
    private static volatile AudioFormat format;
    private static volatile int queueIndex = -1;
    private static Path directory;
    private MusicRuntime() {}

    public static void initialize() {
        if (initialized) return;
        initialized = true;
        directory = FabricLoader.getInstance().getConfigDir().resolve("aura-glass/music");
        refreshLocal();
        Runtime.getRuntime().addShutdownHook(new Thread(MusicRuntime::close, "Aura music shutdown"));
    }
    public static Path directory() { initialize(); return directory; }
    public static List<MusicTrack> tracks() { initialize(); return localTracks; }
    public static List<MusicTrack> results() { initialize(); return results; }
    public static List<MusicTrack> queue() { return queue; }
    public static MusicTrack current() { return current; }
    public static MusicTrack currentTrack() { return current; }
    public static boolean playing() { return playing; }
    public static boolean loading() { return loading; }
    public static String status() { return status; }
    public static double volume() { return volume; }
    public static long durationMs() { return durationMs; }
    public static long positionMs() {
        SourceDataLine active = line;
        long consumed = active == null ? 0 : Math.max(0, active.getLongFramePosition() - lineFrameBase);
        return Math.max(0, Math.min(durationMs, Math.round((positionBaseFrames + consumed) * 1000.0 / sampleRate)));
    }
    public static long audioClockMs() { return positionMs(); }
    public static long outputLatencyMs() {
        SourceDataLine active = line; AudioFormat f = format;
        return active == null || f == null ? 0 : Math.round(Math.max(0, active.getBufferSize() - active.available()) * 1000.0 / f.getFrameSize() / f.getFrameRate());
    }
    public static void volume(double value) { volume = Math.max(0, Math.min(1, value)); }
    public static void setVolume(double value) { volume(value); }
    public static void refreshLocal() {
        initializeDirectory();
        WORK.execute(() -> {
            try {
                Files.createDirectories(directory);
                List<MusicTrack> found = new ArrayList<>();
                try (var files = Files.list(directory)) {
                    for (Path path : files.filter(Files::isRegularFile).sorted().toList()) {
                        String name = path.getFileName().toString(), lower = name.toLowerCase(Locale.ROOT);
                        if (!lower.endsWith(".wav") && !lower.endsWith(".aiff") && !lower.endsWith(".aif") && !lower.endsWith(".au") && !lower.endsWith(".ogg")) continue;
                        String label = name.substring(0, name.lastIndexOf('.')); String[] names = label.split(" - ", 2);
                        String author = names.length == 2 ? names[0].trim() : "", title = names.length == 2 ? names[1].trim() : label;
                        Path cover = directory.resolve(label + ".png");
                        if (!Files.isRegularFile(cover)) cover = directory.resolve(label + ".jpg");
                        found.add(new MusicTrack(path.toUri().toString(), title, author, Files.isRegularFile(cover) ? cover.toUri().toString() : "", 0, path.toUri().toString(), true));
                    }
                }
                localTracks = List.copyOf(found); results = localTracks;
                status = found.isEmpty() ? "Добавьте WAV, AIFF, AU или OGG в config/aura-glass/music" : "Локальная музыка";
            } catch (Exception error) { status = "Не удалось открыть папку музыки: " + message(error); }
        });
    }
    private static void initializeDirectory() { if (directory == null) directory = FabricLoader.getInstance().getConfigDir().resolve("aura-glass/music"); }
    public static CompletableFuture<List<MusicTrack>> searchAsync(String query) {
        initialize(); String q = query == null ? "" : query.trim(); long request = SEARCH_GENERATION.incrementAndGet();
        if (q.isEmpty()) { results = localTracks; return CompletableFuture.completedFuture(results); }
        loading = true;
        return CompletableFuture.supplyAsync(() -> {
            List<MusicTrack> found = new ArrayList<>();
            String lower = q.toLowerCase(Locale.ROOT);
            localTracks.stream().filter(t -> (t.title() + " " + t.author()).toLowerCase(Locale.ROOT).contains(lower)).forEach(found::add);
            try {
                HttpRequest req = HttpRequest.newBuilder(URI.create(API + "search?kind=tracks&q=" + URLEncoder.encode(q, StandardCharsets.UTF_8))).timeout(Duration.ofSeconds(12)).header("X-Aura-Client", "aura-glass").GET().build();
                HttpResponse<String> response = HTTP.send(req, HttpResponse.BodyHandlers.ofString(StandardCharsets.UTF_8));
                if (response.statusCode() != 200) throw new IOException("Каталог недоступен (HTTP " + response.statusCode() + ")");
                JsonObject doc = JsonParser.parseString(response.body()).getAsJsonObject();
                for (JsonElement item : doc.getAsJsonArray("items")) {
                    JsonObject row = item.getAsJsonObject();String id = str(row,"id");
                    if (!id.matches("[a-zA-Z0-9_-]{11}")) continue;
                    String audio = str(row,"audioUrl");
                    if (!audio.isEmpty() && (!audio.startsWith("https://") || audio.contains("youtube.com/") || audio.contains("youtu.be/"))) audio = "";
                    found.add(new MusicTrack(id,str(row,"title"),str(row,"artist"),str(row,"artwork"),row.has("durationSec") ? row.get("durationSec").getAsLong()*1000 : 0,audio,false));
                }
                status = "Каталог: YouTube требует встроенный проигрыватель; локальные файлы доступны";
            } catch (Exception error) { status = message(error); }
            finally { if (SEARCH_GENERATION.get() == request) loading = false; }
            List<MusicTrack> completed = List.copyOf(found); if (SEARCH_GENERATION.get() == request) results = completed; return completed;
        }, WORK);
    }
    public static void query(String query) { searchAsync(query); }
    public static void playTrack(MusicTrack track) { play(track); }
    public static void play(MusicTrack track) {
        initialize(); if (track == null) return;
        if (!track.playable()) { status = "Этот трек доступен только через YouTube. Встроенный браузер не подключён"; return; }
        if (!queue.contains(track)) { queue = List.copyOf(results.isEmpty() ? List.of(track) : results); if (!queue.contains(track)) queue = List.of(track); }
        queueIndex = queue.indexOf(track); current = track; playing = false; loading = true; status = "Загрузка";
        long token = GENERATION.incrementAndGet(); closeLine(); synchronized (LOCK) { LOCK.notifyAll(); }
        WORK.execute(() -> {
            try {
                Pcm decoded = decode(track);
                if (GENERATION.get() != token) return;
                SourceDataLine output = AudioSystem.getSourceDataLine(decoded.format());
                output.open(decoded.format(), Math.max(4096, (int)(decoded.format().getFrameRate() * decoded.format().getFrameSize() * .08)));
                synchronized (LOCK) {
                    if (GENERATION.get() != token) { output.close(); return; }
                    pcm = decoded.bytes(); format = decoded.format(); sampleRate = format.getFrameRate(); line = output; positionBaseFrames = 0; lineFrameBase = output.getLongFramePosition(); seekFrame = -1;
                    durationMs = Math.round(pcm.length * 1000.0 / format.getFrameSize() / sampleRate); playing = true; loading = false; status = "Воспроизведение"; output.start();
                }
                try { pump(token, output, decoded); }
                finally { synchronized (LOCK) { if (line == output) line = null; output.stop(); output.flush(); output.close(); } }
            } catch (Exception error) { if (GENERATION.get() == token) { playing = false; loading = false; status = "Не удалось воспроизвести: " + message(error); closeLine(); } }
        });
    }
    public static void togglePlayPause() { if (playing) pause(); else resume(); }
    public static void pause() {
        synchronized (LOCK) { playing = false; SourceDataLine active = line; if (active != null) active.stop(); status = "Пауза"; }
    }
    public static void resume() {
        synchronized (LOCK) { SourceDataLine active = line; if (active != null && pcm != null) { playing = true; active.start(); LOCK.notifyAll(); status = "Воспроизведение"; return; } }
        MusicTrack track = current; if (track == null && !localTracks.isEmpty()) track = localTracks.get(0); if (track != null) play(track);
    }
    public static void seek(long ms) {
        synchronized (LOCK) { if (pcm == null || format == null || line == null) return; seekFrame = Math.max(0, Math.min(pcm.length / format.getFrameSize(), Math.round(ms * sampleRate / 1000.0))); LOCK.notifyAll(); }
    }
    public static void next() { move(1); }
    public static void previous() { if (positionMs() > 3000) seek(0); else move(-1); }
    private static void move(int direction) { List<MusicTrack> list = queue.isEmpty() ? localTracks : queue; if (list.isEmpty()) return; play(list.get(Math.floorMod(queueIndex + direction, list.size()))); }
    public static void setQueue(List<MusicTrack> tracks) { queue = List.copyOf(tracks); }
    public static void stop() { GENERATION.incrementAndGet(); playing = false; loading = false; closeLine(); pcm = null; positionBaseFrames = 0; durationMs = 0; status = "Остановлено"; synchronized (LOCK) { LOCK.notifyAll(); } }
    public static void close() { stop(); }
    private static void closeLine() { SourceDataLine old = line; line = null; if (old != null) { old.stop(); old.flush(); old.close(); } }
    private static void pump(long token, SourceDataLine output, Pcm decoded) throws Exception {
        int position = 0, frame = decoded.format().getFrameSize(); byte[] adjusted = new byte[8192 - 8192 % frame];
        while (GENERATION.get() == token && output.isOpen()) {
            synchronized (LOCK) {
                if (seekFrame >= 0) {
                    output.stop();output.flush();position = Math.toIntExact(seekFrame * frame);positionBaseFrames = seekFrame;lineFrameBase = output.getLongFramePosition();seekFrame = -1;if(playing)output.start();
                }
                while (!playing && GENERATION.get() == token && seekFrame < 0) LOCK.wait();
            }
            if (GENERATION.get() != token) return;
            if (position >= decoded.bytes().length) {
                if (output.available() < output.getBufferSize()) { Thread.sleep(10); continue; }
                playing = false;status = "Завершено";if(queueIndex + 1 < queue.size()) next();return;
            }
            int available = Math.max(0, output.available());int length = Math.min(Math.min(adjusted.length, decoded.bytes().length-position), available);length -= length % frame;
            if(length <= 0){Thread.sleep(5);continue;}
            System.arraycopy(decoded.bytes(),position,adjusted,0,length);scalePcm16(adjusted,length,volume);
            int written = output.write(adjusted,0,length);position += written;
        }
    }
    static void scalePcm16(byte[] data,int length,double gain) {
        for(int i=0;i+1<length;i+=2){int value=(short)((data[i]&255)|(data[i+1]<<8));int scaled=(int)Math.round(value*gain);data[i]=(byte)scaled;data[i+1]=(byte)(scaled>>8);}
    }
    private record Pcm(byte[] bytes, AudioFormat format) {}
    private static Pcm decode(MusicTrack track) throws Exception {
        URI uri=URI.create(track.source());
        if(track.local()){
            Path source=Path.of(uri).toRealPath();initializeDirectory();Path allowed=directory.toRealPath();
            if(!source.startsWith(allowed)||!Files.isRegularFile(source))throw new IOException("Файл вне папки музыки");
            try(InputStream input=Files.newInputStream(source)){return decode(input,source.getFileName().toString());}
        }
        if(!"https".equals(uri.getScheme())||uri.getHost()==null)throw new IOException("Требуется HTTPS audioUrl");
        HttpResponse<InputStream> response=HTTP.send(HttpRequest.newBuilder(uri).timeout(Duration.ofSeconds(20)).GET().build(),HttpResponse.BodyHandlers.ofInputStream());
        try(InputStream input=response.body()){if(response.statusCode()!=200)throw new IOException("Audio HTTP "+response.statusCode());return decode(input,uri.getPath());}
    }
    private static Pcm decode(InputStream source,String name)throws Exception{
        try(BufferedInputStream input=new BufferedInputStream(source)){
            if(name.toLowerCase(Locale.ROOT).endsWith(".ogg")){
                try(OggAudioStream ogg=new OggAudioStream(input);ByteArrayOutputStream bytes=new ByteArrayOutputStream()){
                    for(;;){ByteBuffer chunk=net.auramc.glass.client.quickmenu.QuickMenuAudioPlatform.read(ogg,32768);if(!chunk.hasRemaining())break;byte[] part=new byte[chunk.remaining()];chunk.get(part);bytes.write(part);if(bytes.size()>MAX_PCM_BYTES)throw new IOException("Трек слишком большой");}
                    AudioFormat src=ogg.getFormat();return new Pcm(bytes.toByteArray(),new AudioFormat(src.getSampleRate(),16,src.getChannels(),true,false));
                }
            }
            try(AudioInputStream original=AudioSystem.getAudioInputStream(input)){
                AudioFormat src=original.getFormat();AudioFormat target=new AudioFormat(AudioFormat.Encoding.PCM_SIGNED,src.getSampleRate(),16,src.getChannels(),src.getChannels()*2,src.getSampleRate(),false);
                try(AudioInputStream converted=AudioSystem.getAudioInputStream(target,original)){
                    byte[] bytes=converted.readNBytes(MAX_PCM_BYTES+1);if(bytes.length>MAX_PCM_BYTES)throw new IOException("Трек слишком большой");return new Pcm(bytes,target);
                }
            }
        }
    }
    private static String str(JsonObject object,String key){return object.has(key)&&!object.get(key).isJsonNull()?object.get(key).getAsString():"";}
    private static String message(Throwable error){Throwable cause=error;while(cause.getCause()!=null)cause=cause.getCause();String text=cause.getMessage();return text==null?cause.getClass().getSimpleName():text;}
}
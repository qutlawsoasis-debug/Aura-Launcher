package net.auramc.glass.client.quickmenu;

import com.mojang.blaze3d.systems.RenderSystem;
import net.auramc.glass.common.render.AuraRenderer;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.GlassPreset;
import net.auramc.glass.common.style.GlassStyles;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.text.Text;
import net.auramc.glass.client.music.*;
import net.auramc.glass.client.quickmenu.music.MusicSourceProvider;

import org.lwjgl.glfw.GLFW;
import java.time.LocalTime;
import java.time.format.DateTimeFormatter;
import java.util.*;
import java.util.concurrent.*;
import java.util.function.*;

public final class QuickMenuScreen extends Screen {
    private static final String[] SECTIONS = {"Визуал", "Мир", "Музыка"};
    private static final DateTimeFormatter CLOCK = DateTimeFormatter.ofPattern("HH:mm");
    private static final int ACCENT = 0xFF64D2FF, ACCENT_TEXT = 0xFF00283A;
    private static final int[] COLORS = {0xFFFFFF, 0x64D2FF, 0x5FD39A, 0xFFCC66, 0xFF8A7A};
    private final QuickMenuMotion motion = new QuickMenuMotion();
    private final List<Control> controls = new ArrayList<>();
    private final ExecutorService searchExecutor = Executors.newSingleThreadExecutor(r -> {
        Thread t = new Thread(r, "Aura-QuickMenu-Search");
        t.setDaemon(true);
        return t;
    });
    private volatile List<Track> cachedTracks = List.of();
    private String lastSearchedQuery = null;
    private long searchRequestEpoch = 0;

    private int section, focused, scroll;
    private boolean dragging, menuKeyHeld = true, searching;
    private String searchText = "";
    private int musicTab;
    private double dragX, dragY;
    private long lastHeaderClick, renderedFrames, lastScrollTime;
    private Control pressed;
    private float textOpacity = 1;
    private float clipLeft, clipTop, clipRight, clipBottom;
    private int mouseX, mouseY;

    private record Control(float x, float y, float w, float h, String label, Runnable action, DoubleConsumer slide, boolean navigation, double sliderValue) {
        Control(float x, float y, float w, float h, String label, Runnable action, DoubleConsumer slide) {
            this(x, y, w, h, label, action, slide, false, 0);
        }
        boolean contains(double px, double py) {
            return px >= x && px <= x + w && py >= y && py <= y + h;
        }
    }

    public QuickMenuScreen() {
        super(Text.literal("Aura"));
    }

    @Override
    protected void init() {
        var config = QuickMenuConfig.get();
        if (renderedFrames == 0) motion.setOpen(true, System.nanoTime());
        motion.update(System.nanoTime(), width, height, config.centerX, config.bottomY < 0 ? Double.NaN : config.bottomY);
        triggerSearchAsync(searchText);
    }

    public QuickMenuMotion.Geometry geometry() {
        return motion.geometry();
    }

    public long renderedFrames() {
        return renderedFrames;
    }

    public int section() {
        return section;
    }

    public void selectSection(int index) {
        section = Math.floorMod(index, SECTIONS.length);
        scroll = 0;
        focused = 0;
        if (section == 2) {
            triggerSearchAsync(searchText);
        }
    }

    public void toggle() {
        motion.setOpen(!motion.isOpen(), System.nanoTime());
    }

    @Override
    public void close() {
        motion.setOpen(false, System.nanoTime());
    }

    @Override
    public boolean shouldPause() {
        return true;
    }

    @Override
    public void removed() {
        searchExecutor.shutdownNow();
    }

    private void triggerSearchAsync(String query) {
        if (Objects.equals(query, lastSearchedQuery)) return;
        lastSearchedQuery = query;
        final long epoch = ++searchRequestEpoch;
        final var src = MusicSourceProvider.get();
        if (!src.available()) {
            cachedTracks = List.of();
            return;
        }
        searchExecutor.submit(() -> {
            try {
                List<Track> results = src.search(query);
                if (epoch == searchRequestEpoch) {
                    cachedTracks = (results != null) ? List.copyOf(results) : List.of();
                }
            } catch (Throwable ignored) {}
        });
    }

    private float computeBackdropBrightness() {
        float dayFactor = 0.5f;
        if (client != null && client.world != null) {
            long time = client.world.getTimeOfDay() % 24000L;
            var cfg = QuickMenuConfig.get();
            if (cfg != null && cfg.visualTime) {
                time = (long) cfg.timeOfDay % 24000L;
            }
            float skyAngle = (float) time / 24000.0f - 0.25f;
            float sun = (float) Math.cos(skyAngle * 2.0 * Math.PI);
            dayFactor = Math.max(0.0f, Math.min(1.0f, (sun + 0.25f) / 1.25f));
        }
        return dayFactor;
    }

    @Override
    public void render(DrawContext context, int mx, int my, float delta) {
        mouseX = mx;
        mouseY = my;
        renderedFrames++;
        var config = QuickMenuConfig.get();
        var box = motion.update(System.nanoTime(), width, height, config.centerX, config.bottomY < 0 ? Double.NaN : config.bottomY);
        controls.clear();
        clipLeft = box.x() + 8;
        clipTop = box.y();
        clipRight = box.x() + box.width() - 8;
        clipBottom = box.bottom() - 8;
        if (motion.settledClosed()) {
            client.setScreen(null);
            return;
        }
        context.draw();

        // Authentic Apple Liquid Glass panel with adaptive contrast tone
        if (GlassStyles.quality().needsBackdrop()) {
            QuickMenuRuntime.totalCaptureRequests++;
            GlassRenderer.captureBackdrop();
        }
        var style = GlassStyles.frame(GlassPreset.PANEL);
        style.radius = box.radius();
        style.bevel = 2.5f;
        if (GlassStyles.quality().needsBackdrop()) {
            style.refraction = 0.35f;
            style.dispersion = 0;
            style.wobble = 0;
            style.depth = 0.02f;
            style.frost = 2.5f;
            style.gloss = 0.12f;
            style.rimOuter = 0.20f;
            style.rimInner = 0.40f;
            style.light = 0.22f;

            // Adaptive tone: on bright sky / day, darken tone up to rgba(30, 32, 40, 0.45) for contrast >= 4.5:1
            // On dark night background, keep translucent tint rgba(26, 31, 41, 0.20)
            float brightness = computeBackdropBrightness();
            float t = Math.max(0.0f, Math.min(1.0f, (brightness - 0.35f) / 0.45f));
            style.tintR = 0.10f + (30f / 255f - 0.10f) * t;
            style.tintG = 0.12f + (32f / 255f - 0.12f) * t;
            style.tintB = 0.16f + (40f / 255f - 0.16f) * t;
            style.tintA = 0.20f + 0.25f * t;
        } else {
            style.tintR = 0.08f;
            style.tintG = 0.09f;
            style.tintB = 0.12f;
            style.tintA = 0.92f; // Dense solid fill only for LOW / OFF
        }

        int before = GlassRenderer.drawCalls();
        GlassRenderer.draw(box.x(), box.y(), box.width(), box.height(), style, Math.min(1, box.progress() * 5 + 0.05f));
        GlassRenderer.flush();
        QuickMenuRuntime.totalDrawCalls += Math.max(0, GlassRenderer.drawCalls() - before);

        if (box.contentOpacity() <= 0.01f) return;

        context.enableScissor((int) Math.ceil(box.x() + 8), (int) Math.ceil(box.y() + 8), (int) (box.x() + box.width() - 8), (int) (box.bottom() - 8));
        boolean scissorActive = true;
        try {
            if (!AuraRenderer.begin(context.getMatrices().peek().getPositionMatrix(), RenderSystem.getProjectionMatrix(), RenderSystem.getModelViewMatrix())) return;
            textOpacity = box.sectionOpacity(0);
            float x = box.x(), y = box.y() + box.sectionOffset(0), right = x + box.width();

            // 1px top highlight rgba(255, 255, 255, 0.40)
            float rad = box.radius();
            AuraRenderer.rect(x + rad, y, box.width() - rad * 2, 1, 0, 0, color(0x66FFFFFF), 0);

            // Aura brand and user info (Typography: header 26px)
            text("Aura", x + 24, y + 11, 26, 0xFFFFFFFF, 600);
            String name = client.player == null ? client.getSession().getUsername() : client.player.getName().getString();
            text(name, right - 205, y + 16, 15, 0xFFFFFFFF, 500);
            text(LocalTime.now().format(CLOCK), right - 69, y + 16, 15, 0xFFFFFFFF, 500);

            // Subtle divider under header
            AuraRenderer.rect(x + 16, y + 44, box.width() - 32, 1, 0, 0, color(0x1AFFFFFF), 0);

            // Navigation items (17px labels, 16px centered icons)
            float navWidth = Math.min(170, box.width() * 0.27f);
            float navH = 34;
            for (int i = 0; i < SECTIONS.length; i++) {
                final int item = i;
                float ny = y + 62 + i * 38;
                boolean active = i == section;
                rect(x + 14, ny, navWidth - 22, navH, 8, active ? 0x5C767680 : 0x00767680, active ? 0x1AFFFFFF : 0);
                icon(i, x + 22, ny + 9, active ? ACCENT : 0xDDFFFFFF);
                text(SECTIONS[i], x + 46, ny + 8, 17, 0xFFFFFFFF, active ? 600 : 400);
                controls.add(new Control(x + 14, ny, navWidth - 22, navH, SECTIONS[i], () -> selectSection(item), null, true, 0));
            }

            // Section Header (38px)
            float contentX = x + navWidth + 8, contentW = box.width() - navWidth - 32;
            textOpacity = box.sectionOpacity(1);
            float contentY = y + 58 + box.sectionOffset(1);
            text(SECTIONS[section], contentX, contentY, 38, 0xFFFFFFFF, 600);
            flushNative();

            context.disableScissor();
            scissorActive = false;

            // Content viewport with pure alpha mask soft edge fade (without dark background bars)
            float viewportTop = y + 112;
            float viewportBottom = box.bottom() - 39;
            float viewportH = Math.max(1, viewportBottom - viewportTop);

            context.enableScissor((int) contentX, (int) viewportTop, (int) (right - 18), (int) viewportBottom);
            scissorActive = true;
            if (AuraRenderer.begin(context.getMatrices().peek().getPositionMatrix(), RenderSystem.getProjectionMatrix(), RenderSystem.getModelViewMatrix())) {
                clipLeft = contentX;
                clipTop = viewportTop;
                clipRight = right - 18;
                clipBottom = viewportBottom;
                float rowsY = viewportTop + 6 - scroll + box.sectionOffset(2);
                textOpacity = box.sectionOpacity(2);

                if (section == 0) visual(contentX, rowsY, contentW);
                else if (section == 1) world(contentX, rowsY, contentW);
                else if (section == 2) music(context, contentX, rowsY, contentW);
                else text("Скоро", contentX, rowsY + 14, 20, 0xCCFFFFFF, 400);

                // macOS-style scroll indicator (thin capsule, appears during scroll)
                int maxScroll = maxScroll(viewportH);
                if (maxScroll > 0) {
                    long elapsed = System.currentTimeMillis() - lastScrollTime;
                    float scrollbarAlpha = 0.0f;
                    if (elapsed < 1400) {
                        if (elapsed < 800) scrollbarAlpha = 1.0f;
                        else scrollbarAlpha = 1.0f - (elapsed - 800) / 600.0f;
                    }
                    float scrollbarX = right - 12;
                    float scrollbarW = 3.5f;
                    if (mouseX >= scrollbarX - 6 && mouseX <= right - 2 && mouseY >= viewportTop && mouseY <= viewportBottom) {
                        scrollbarAlpha = Math.max(scrollbarAlpha, 0.85f);
                    }
                    if (scrollbarAlpha > 0.01f) {
                        float thumbH = Math.max(26.0f, viewportH * (viewportH / (float) (maxScroll + viewportH)));
                        float thumbY = viewportTop + (viewportH - thumbH) * ((float) scroll / (float) maxScroll);
                        int barColor = ((int) (scrollbarAlpha * 150 * textOpacity) << 24) | 0xFFFFFF;
                        int barEdge = ((int) (scrollbarAlpha * 40 * textOpacity) << 24) | 0x000000;
                        rect(scrollbarX, thumbY, scrollbarW, thumbH, scrollbarW * 0.5f, barColor, barEdge);
                    }
                }

                flushNative();
            }
            context.disableScissor();
            scissorActive = false;

            // Footer and keyboard focus indicator
            if (AuraRenderer.begin(context.getMatrices().peek().getPositionMatrix(), RenderSystem.getProjectionMatrix(), RenderSystem.getModelViewMatrix())) {
                clipLeft = box.x() + 8;
                clipTop = box.y();
                clipRight = box.x() + box.width() - 8;
                clipBottom = box.bottom() - 8;
                textOpacity = box.contentOpacity();
                if (!controls.isEmpty()) {
                    var focus = controls.get(Math.min(focused, controls.size() - 1));
                    if (focus.y >= box.y() + 44 && focus.y + focus.h < box.bottom() - 36) {
                        rect(focus.x - 2, focus.y - 2, focus.w + 4, focus.h + 4, 9, 0, ACCENT);
                    }
                }
                text("F8 — закрыть", x + 24, box.bottom() - 28, 14, 0xEEFFFFFF, 500);
                flushNative();
            }

            if (box.contentOpacity() > 0.9f) {
                var skin = QuickMenuPlatform.skin(client);
                if (skin != null) {
                    RenderSystem.setShaderColor(1, 1, 1, box.contentOpacity());
                    context.drawTexture(skin, (int) (right - 234), (int) (y + 12), 20, 20, 8, 8, 8, 8, 64, 64);
                    context.draw();
                    RenderSystem.setShaderColor(1, 1, 1, 1);
                }
            }
        } finally {
            if (scissorActive) context.disableScissor();
            RenderSystem.setShaderColor(1, 1, 1, 1);
        }
    }

    private void nativeClip() {
        double scale = client.getWindow().getScaleFactor();
        int left = (int) Math.ceil(clipLeft * scale), right = (int) Math.floor(clipRight * scale);
        int top = (int) Math.ceil(clipTop * scale), bottom = (int) Math.floor(clipBottom * scale);
        org.lwjgl.opengl.GL33C.glEnable(org.lwjgl.opengl.GL33C.GL_SCISSOR_TEST);
        org.lwjgl.opengl.GL33C.glScissor(left, client.getWindow().getFramebufferHeight() - bottom, Math.max(0, right - left), Math.max(0, bottom - top));
    }

    private void flushNative() {
        nativeClip();
        AuraRenderer.flush();
    }

    // Pure alpha mask fading without dark background rectangles
    private float edgeFade(float itemY, float itemH) {
        if (clipBottom <= clipTop) return 1.0f;
        if (itemY + itemH <= clipTop || itemY >= clipBottom) return 0.0f;
        float topFade = Math.min(1.0f, Math.max(0.0f, (itemY - clipTop) / 16.0f));
        float bottomFade = Math.min(1.0f, Math.max(0.0f, (clipBottom - (itemY + itemH)) / 16.0f));
        return Math.max(0.0f, Math.min(1.0f, Math.min(topFade, bottomFade)));
    }

    private int maxScroll(float viewportH) {
        float contentH;
        if (section == 0) {
            contentH = 29 * 38 + 24;
        } else if (section == 1) {
            contentH = 3 * 44 + 80;
        } else if (section == 2) {
            contentH = 320 + Math.min(10, cachedTracks.size()) * 42 + 20;
        } else {
            contentH = 80;
        }
        return (int) Math.max(0, contentH - viewportH);
    }

    private void visual(float x, float y, float w) {
        var c = QuickMenuConfig.get();
        toggle(x, y, w, "Zoom · удерживать C", () -> c.zoomEnabled, v -> c.zoomEnabled = v);
        slider(x, y += 38, w, "Кратность", c.zoomFactor, 1, 16, v -> c.zoomFactor = v, v -> String.format(Locale.ROOT, "%.1f×", v));
        toggle(x, y += 38, w, "Fullbright", () -> c.fullbright, v -> c.fullbright = v);
        slider(x, y += 38, w, "Яркость", c.brightness, 0, 1, v -> c.brightness = v, v -> Math.round(v * 100) + " %");
        choice(x, y += 38, w, "Прицел", new String[]{"Ванильный", "Точка", "Крест", "Круг"}, c.crosshairStyle, v -> {
            c.crosshairStyle = v;
            c.customCrosshair = v != 0;
        });
        choice(x, y += 38, w, "Цвет прицела", new String[]{"Белый", "Голубой", "Зелёный", "Янтарный", "Красный"}, colorIndex(c.crosshairColor), v -> c.crosshairColor = COLORS[v]);
        slider(x, y += 38, w, "Размер прицела", c.crosshairSize, 2, 16, v -> c.crosshairSize = v, v -> String.valueOf((int) Math.round(v)));
        toggle(x, y += 38, w, "Контур блока", () -> c.blockOutline, v -> c.blockOutline = v);
        choice(x, y += 38, w, "Цвет контура", new String[]{"Белый", "Голубой", "Зелёный", "Янтарный", "Красный"}, colorIndex(c.outlineColor), v -> c.outlineColor = COLORS[v]);
        slider(x, y += 38, w, "Толщина контура", c.outlineWidth, 1, 5, v -> c.outlineWidth = v, v -> String.valueOf((int) Math.round(v)));
        choice(x, y += 38, w, "Частицы", new String[]{"Все", "Меньше", "Минимум"}, c.particles, v -> c.particles = v);
        toggle(x, y += 38, w, "Низкий огонь", () -> c.lowFire, v -> c.lowFire = v);
        toggle(x, y += 38, w, "Низкий щит", () -> c.lowShield, v -> c.lowShield = v);
        toggle(x, y += 38, w, "Частицы удара", () -> c.hitParticles, v -> c.hitParticles = v);
        choice(x, y += 38, w, "Стиль частиц", new String[]{"Крит", "Искры", "Сердца"}, c.hitParticleStyle, v -> c.hitParticleStyle = v);
        choice(x, y += 38, w, "Цвет частиц", new String[]{"Белый", "Голубой", "Зелёный", "Янтарный", "Красный"}, colorIndex(c.hitParticleColor), v -> c.hitParticleColor = COLORS[v]);
        slider(x, y += 38, w, "Количество частиц", c.hitParticleCount, 1, 24, v -> c.hitParticleCount = (int) Math.round(v), v -> String.valueOf((int) Math.round(v)));
        toggle(x, y += 38, w, "Числа урона и лечения", () -> c.damageNumbers, v -> c.damageNumbers = v);
        toggle(x, y += 38, w, "Плашка цели", () -> c.targetHud, v -> c.targetHud = v);
        toggle(x, y += 38, w, "Своя траектория", () -> c.projectilePrediction, v -> c.projectilePrediction = v);
        toggle(x, y += 38, w, "Цвет попадания", () -> c.hitTint, v -> c.hitTint = v);
        choice(x, y += 38, w, "Оттенок попадания", new String[]{"Белый", "Голубой", "Зелёный", "Янтарный", "Красный"}, colorIndex(c.hitTintColor), v -> c.hitTintColor = COLORS[v]);
        toggle(x, y += 38, w, "Положение руки", () -> c.handTransform, v -> c.handTransform = v);
        slider(x, y += 38, w, "Рука · X", c.handOffsetX, -1, 1, v -> c.handOffsetX = v, v -> String.format(Locale.ROOT, "%.2f", v));
        slider(x, y += 38, w, "Рука · Y", c.handOffsetY, -1, 1, v -> c.handOffsetY = v, v -> String.format(Locale.ROOT, "%.2f", v));
        slider(x, y += 38, w, "Рука · Z", c.handOffsetZ, -1, 1, v -> c.handOffsetZ = v, v -> String.format(Locale.ROOT, "%.2f", v));
        slider(x, y += 38, w, "Масштаб руки", c.handScale, 0.5, 1.5, v -> c.handScale = v, v -> String.format(Locale.ROOT, "%.1f×", v));
        choice(x, y += 38, w, "Взмах", new String[]{"Ванильный", "Мягкий", "Компактный"}, c.swingAnimation, v -> c.swingAnimation = v);
        toggle(x, y += 38, w, "Кулдауны у прицела", () -> c.cooldownHud, v -> c.cooldownHud = v);
    }

    private void world(float x, float y, float w) {
        var c = QuickMenuConfig.get();
        toggle(x, y, w, "Свое время суток", () -> c.visualTime, v -> c.visualTime = v);
        slider(x, y += 44, w, "Время", c.timeOfDay, 0, 23999, v -> c.timeOfDay = v, v -> String.format(Locale.ROOT, "%02d:00", (int) (v / 1000) % 24));
        toggle(x, y += 44, w, "Без осадков", () -> c.clearWeather, v -> c.clearWeather = v);
        float rowFade = edgeFade(y + 52, 14);
        if (rowFade > 0.01f) {
            text("Меняется только отображение на этом клиенте.", x, y + 52, 13, 0xDDFFFFFF, 400, rowFade);
        }
    }

    private void music(DrawContext context, float x, float y, float w) {
        var source = MusicSourceProvider.get();
        var now = source.now();
        var current = now.track();
        String title;
        String artist;
        if (!source.available()) {
            title = "Aura Music: launcher is not running";
            artist = "Aura Music: лаунчер не запущен";
        } else {
            title = current == null ? "Ничего не играет" : current.title();
            artist = current == null ? "Выберите трек из каталога" : current.artist();
        }

        // Empty artwork in Music: Rounded 84x84 card (radius 10px, fill .24), note glyph with soft blue accent
        float cardFade = edgeFade(y, 84);
        if (cardFade > 0.01f) {
            rect(x, y, 84, 84, 10, 0x3D767680, 0x1AFFFFFF, cardFade);
            if (current != null) {
                var cover = net.auramc.glass.client.quickmenu.music.MusicArtwork.texture(current);
                if (cover != null) {
                    flushNative();
                    context.drawTexture(cover, (int) x, (int) y, 0, 0, 84, 84, 84, 84);
                    context.draw();
                    AuraRenderer.begin(context.getMatrices().peek().getPositionMatrix(), RenderSystem.getProjectionMatrix(), RenderSystem.getModelViewMatrix());
                } else {
                    int noteColor = 0x8064D2FF;
                    rect(x + 24, y + 46, 16, 12, 6, noteColor, 0, cardFade);
                    rect(x + 46, y + 40, 16, 12, 6, noteColor, 0, cardFade);
                    rect(x + 36, y + 22, 4, 28, 2, noteColor, 0, cardFade);
                    rect(x + 58, y + 16, 4, 28, 2, noteColor, 0, cardFade);
                    rect(x + 36, y + 16, 26, 7, 2, noteColor, 0, cardFade);
                }
            } else {
                int noteColor = 0x5C767680;
                rect(x + 24, y + 46, 16, 12, 6, noteColor, 0, cardFade);
                rect(x + 46, y + 40, 16, 12, 6, noteColor, 0, cardFade);
                rect(x + 36, y + 22, 4, 28, 2, noteColor, 0, cardFade);
                rect(x + 58, y + 16, 4, 28, 2, noteColor, 0, cardFade);
                rect(x + 36, y + 16, 26, 7, 2, noteColor, 0, cardFade);
            }
        }

        float textFade = edgeFade(y + 8, 22);
        if (textFade > 0.01f) text(fit(title, w - 104, 22), x + 100, y + 8, 22, 0xFFFFFFFF, 600, textFade);
        float artistFade = edgeFade(y + 38, 16);
        if (artistFade > 0.01f) text(fit(artist, w - 104, 16), x + 100, y + 38, 16, 0xEEFFFFFF, 400, artistFade);

        String status;
        int statusColor;
        if (!source.available()) {
            status = "○ Aura Music: launcher is not running";
            statusColor = 0xDDFFFFFF;
        } else if (now.isPlaying()) {
            status = "● Воспроизведение · Aura Music";
            statusColor = ACCENT;
        } else if (now.isPaused()) {
            status = "⏸ На паузе · Aura Music";
            statusColor = 0xEEFFFFFF;
        } else {
            status = "○ Остановлено · Aura Music";
            statusColor = 0xDDFFFFFF;
        }
        float statusFade = edgeFade(y + 62, 14);
        if (statusFade > 0.01f) text(fit(status, w - 104, 14), x + 100, y + 62, 14, statusColor, 400, statusFade);

        long duration = current == null ? 0 : current.durationMs();
        long pos = now.currentPositionMs();

        // Number formatting: Track position ONLY "0:45 / 3:14" (no duplicate number on right)
        slider(x, y + 100, w, "Позиция · " + time(pos) + " / " + time(duration), pos, 0, Math.max(1, duration), v -> source.seek((long) v), null);

        action(x, y + 144, 76, "Назад", source::prev);
        action(x + 88, y + 144, 110, now.isPlaying() ? "Пауза" : "Играть", now.isPlaying() ? source::pause : source::play);
        action(x + 210, y + 144, 76, "Далее", source::next);

        // Volume formatted as percentage "50 %"
        slider(x, y + 188, w, "Громкость", now.volume(), 0, 1, v -> {
            source.volume((float) v);
            QuickMenuConfig.get().musicVolume = v;
        }, v -> Math.round(v * 100) + " %");

        action(x, y + 236, w, searching ? "Поиск: " + searchText : searchText.isBlank() ? "Поиск в каталоге Aura..." : ("Поиск: " + searchText), () -> searching = true);

        action(x, y + 276, 120, "Каталог Aura", () -> musicTab = 0);
        action(x + 130, y + 276, 100, "Очередь", () -> musicTab = 1);

        // Asynchronous cached tracks - no network calls on render thread!
        var tracks = cachedTracks;
        float ty = y + 318;
        if (!source.available()) {
            float waitingFade = edgeFade(ty, 15);
            if (waitingFade > 0.01f) text("Aura Music: launcher is not running", x, ty, 15, 0xDDFFFFFF, 400, waitingFade);
        } else if (tracks.isEmpty()) {
            float emptyFade = edgeFade(ty, 15);
            if (emptyFade > 0.01f) text("Нет треков по запросу.", x, ty, 15, 0xDDFFFFFF, 400, emptyFade);
        } else {
            for (int i = 0; i < Math.min(10, tracks.size()); i++) {
                var track = tracks.get(i);
                float rowY = ty + i * 42;
                float rowFade = edgeFade(rowY, 36);
                if (rowFade <= 0.01f) continue;
                boolean isCurrent = current != null && current.id().equals(track.id());
                rect(x, rowY, w, 36, 8, isCurrent ? 0x5C767680 : 0x3D767680, isCurrent ? ACCENT : 0x1AFFFFFF, rowFade);
                text(fit(track.title(), w - 80, 16), x + 12, rowY + 4, 16, 0xFFFFFFFF, isCurrent ? 600 : 500, rowFade);
                text(fit(track.artist(), w - 80, 13), x + 12, rowY + 20, 13, 0xDDFFFFFF, 400, rowFade);
                String dur = time(track.durationMs());
                text(dur, x + w - AuraRenderer.textWidth(dur, 13) - 12, rowY + 11, 13, 0xCCFFFFFF, 400, rowFade);
                controls.add(new Control(x, rowY, w, 36, track.title(), () -> source.playTrack(track), null));
            }
        }
    }

    private void action(float x, float y, float w, String label, Runnable action) {
        float rowFade = edgeFade(y, 30);
        if (rowFade <= 0.01f) return;
        rect(x, y, w, 30, 8, 0x5C767680, 0x1AFFFFFF, rowFade);
        text(fit(label, w - 20, 15), x + 10, y + 7, 15, 0xFFFFFFFF, 500, rowFade);
        controls.add(new Control(x, y, w, 30, label, action, null));
    }

    private static String time(long ms) {
        long s = Math.max(0, ms) / 1000;
        return String.format(Locale.ROOT, "%d:%02d", s / 60, s % 60);
    }

    private static String fit(String text, float width, float size) {
        if (AuraRenderer.textWidth(text, size) <= width) return text;
        int end = text.length();
        while (end > 0 && AuraRenderer.textWidth(text.substring(0, end) + "…", size) > width) end--;
        return text.substring(0, end) + "…";
    }

    private void toggle(float x, float y, float w, String label, BooleanSupplier get, Consumer<Boolean> set) {
        float rowFade = edgeFade(y, 32);
        if (rowFade <= 0.01f) return;
        row(x, y, w, label, rowFade);
        boolean on = get.getAsBoolean();
        rect(x + w - 44, y + 4, 42, 23, 11.5f, on ? ACCENT : 0x80767680, 0x1AFFFFFF, rowFade);
        rect(x + w - (on ? 22 : 40), y + 7, 17, 17, 8.5f, on ? ACCENT_TEXT : 0xFFFFFFFF, 0, rowFade);
        controls.add(new Control(x, y, w, 32, label, () -> {
            set.accept(!get.getAsBoolean());
            QuickMenuConfig.save();
        }, null));
    }

    private void choice(float x, float y, float w, String label, String[] values, int selected, IntConsumer set) {
        float rowFade = edgeFade(y, 32);
        if (rowFade <= 0.01f) return;
        row(x, y, w, label, rowFade);
        String value = values[Math.floorMod(selected, values.length)];
        float tw = AuraRenderer.textWidth(value, 15);
        rect(x + w - tw - 21, y + 2, tw + 20, 28, 8, 0x5C767680, 0x1AFFFFFF, rowFade);
        text(value, x + w - tw - 11, y + 8, 15, 0xFFFFFFFF, 500, rowFade);
        controls.add(new Control(x, y, w, 32, label, () -> {
            set.accept((selected + 1) % values.length);
            QuickMenuConfig.save();
        }, null));
    }

    private void slider(float x, float y, float w, String label, double value, double min, double max, DoubleConsumer set, Function<Double, String> format) {
        float rowFade = edgeFade(y, 36);
        if (rowFade <= 0.01f) return;
        text(label, x, y + 1, 18, 0xFFFFFFFF, 400, rowFade);
        if (format != null) {
            String display = format.apply(value);
            if (display != null && !display.isEmpty()) {
                text(display, x + w - AuraRenderer.textWidth(display, 15), y + 2, 15, 0xFFFFFFFF, 500, rowFade);
            }
        }
        float trackW = w - 8, t = (float) ((value - min) / (max - min));
        rect(x + 4, y + 26, trackW, 3, 1.5f, 0x80767680, 0, rowFade);
        rect(x + 4, y + 26, trackW * t, 3, 1.5f, ACCENT, 0, rowFade);
        rect(x + 4 + trackW * t - 8, y + 20, 16, 15, 7.5f, 0xFFFFFFFF, 0x1AFFFFFF, rowFade);
        DoubleConsumer change = normalized -> {
            set.accept(min + normalized * (max - min));
            QuickMenuConfig.save();
        };
        controls.add(new Control(x, y, w, 36, label, () -> change.accept(Math.min(1, (value - min) / (max - min) + 0.05)), change, false, (value - min) / (max - min)));
    }

    private void row(float x, float y, float w, String label, float fade) {
        text(label, x, y + 7, 18, 0xFFFFFFFF, 400, fade);
        rect(x, y + 35, w, 1, 0, 0x16767680, 0, fade);
    }

    private static int colorIndex(int rgb) {
        for (int i = 0; i < COLORS.length; i++) {
            if (COLORS[i] == rgb) return i;
        }
        return 0;
    }

    private void text(String text, float x, float y, float size, int color, int weight) {
        AuraRenderer.text(text, x, y, size, color(color), weight);
    }

    private void text(String text, float x, float y, float size, int color, int weight, float extraOpacity) {
        AuraRenderer.text(text, x, y, size, color(color, extraOpacity), weight);
    }

    private int color(int color) {
        return (Math.round((color >>> 24) * textOpacity) << 24) | (color & 0xFFFFFF);
    }

    private int color(int color, float extraOpacity) {
        return (Math.round((color >>> 24) * textOpacity * extraOpacity) << 24) | (color & 0xFFFFFF);
    }

    private void rect(float x, float y, float w, float h, float radius, int fill, int edge) {
        AuraRenderer.rect(x, y, w, h, radius, edge == 0 ? 0 : 1, color(fill), color(edge));
    }

    private void rect(float x, float y, float w, float h, float radius, int fill, int edge, float extraOpacity) {
        AuraRenderer.rect(x, y, w, h, radius, edge == 0 ? 0 : 1, color(fill, extraOpacity), color(edge, extraOpacity));
    }

    // 16px navigation icons, vertically centered in row
    private void icon(int type, float x, float y, int color) {
        switch (type) {
            case 0 -> { // Визуал: Apple Eye icon (16x16)
                rect(x, y + 3, 16, 10, 5, 0, color);
                rect(x + 5.5f, y + 5.5f, 5, 5, 2.5f, color, 0);
                rect(x + 7.5f, y + 6.5f, 2, 2, 1, 0xFFFFFFFF, 0);
            }
            case 1 -> { // Мир: Apple Globe icon (16x16)
                rect(x, y, 16, 16, 8, 0, color);
                rect(x + 1, y + 7.5f, 14, 1.2f, 0.6f, color, 0);
                rect(x + 5, y + 1, 6, 14, 3, 0, color);
            }
            case 2 -> { // Музыка: Apple Beamed Note icon (16x16)
                rect(x + 1, y + 10, 5, 4.5f, 2f, color, 0);
                rect(x + 8.5f, y + 8, 5, 4.5f, 2f, color, 0);
                rect(x + 4.5f, y + 2, 1.5f, 10, 0.75f, color, 0);
                rect(x + 12f, y, 1.5f, 10, 0.75f, color, 0);
                rect(x + 4.5f, y, 9, 3, 1f, color, 0);
            }
            default -> {
                rect(x, y, 4, 14, 1, 0, color);
                rect(x + 5, y + 2, 4, 14, 1, 0, color);
                rect(x + 10, y + 4, 4, 14, 1, 0, color);
            }
        }
    }

    @Override
    public boolean mouseClicked(double x, double y, int button) {
        if (QuickMenuRuntime.menuKey().matchesMouse(button)) {
            while (QuickMenuRuntime.menuKey().wasPressed()) {}
            toggle();
            return true;
        }
        if (button != 0 || geometry().contentOpacity() < 0.8f) return false;
        var box = geometry();
        if (x >= box.x() && x <= box.x() + box.width() && y >= box.y() && y < box.y() + 44) {
            long now = System.nanoTime();
            if (now - lastHeaderClick < 250_000_000L) {
                motion.resetPosition();
                QuickMenuConfig.get().centerX = 0.5;
                QuickMenuConfig.get().bottomY = -1;
                QuickMenuConfig.save();
                lastHeaderClick = 0;
                return true;
            }
            lastHeaderClick = now;
            dragging = true;
            dragX = x - box.centerX();
            dragY = y - box.bottom();
            return true;
        }
        for (int i = 0; i < controls.size(); i++) {
            var c = controls.get(i);
            if (c.contains(x, y) && y > box.y() + (c.navigation ? 44 : 112) && y < box.bottom() - 36) {
                focused = i;
                pressed = c;
                if (c.slide != null) c.slide.accept(Math.max(0, Math.min(1, (x - c.x) / c.w)));
                else c.action.run();
                return true;
            }
        }
        return false;
    }

    @Override
    public boolean mouseDragged(double x, double y, int button, double dx, double dy) {
        if (button != 0) return false;
        if (dragging) {
            motion.setPosition((x - dragX) / width, (y - dragY) / height);
            return true;
        }
        if (pressed != null && pressed.slide != null) {
            pressed.slide.accept(Math.max(0, Math.min(1, (x - pressed.x) / pressed.w)));
            return true;
        }
        return false;
    }

    @Override
    public boolean mouseReleased(double x, double y, int button) {
        if (dragging) {
            dragging = false;
            var c = QuickMenuConfig.get();
            c.centerX = motion.centerX();
            c.bottomY = motion.bottomY();
            QuickMenuConfig.save();
        }
        pressed = null;
        return true;
    }

    public boolean mouseScrolled(double x, double y, double amount) {
        return scroll(amount);
    }

    public boolean mouseScrolled(double x, double y, double horizontal, double vertical) {
        return scroll(vertical);
    }

    private boolean scroll(double amount) {
        float viewportH = Math.max(1, geometry().bottom() - 39 - (geometry().y() + 112));
        int max = maxScroll(viewportH);
        scroll = Math.max(0, Math.min(max, scroll - (int) (amount * 32)));
        lastScrollTime = System.currentTimeMillis();
        return true;
    }

    @Override
    public boolean charTyped(char character, int modifiers) {
        if (searching && !Character.isISOControl(character) && searchText.length() < 120) {
            searchText += character;
            triggerSearchAsync(searchText);
            return true;
        }
        return super.charTyped(character, modifiers);
    }

    @Override
    public boolean keyReleased(int key, int scan, int modifiers) {
        if (QuickMenuRuntime.menuKey().matchesKey(key, scan)) {
            menuKeyHeld = false;
            return true;
        }
        return super.keyReleased(key, scan, modifiers);
    }

    private void revealFocused() {
        if (controls.isEmpty()) return;
        var control = controls.get(Math.min(focused, controls.size() - 1));
        if (control.navigation) return;
        float top = geometry().y() + 112, bottom = geometry().bottom() - 39;
        if (control.y < top) scroll = Math.max(0, scroll - (int) Math.ceil(top - control.y));
        else if (control.y + control.h > bottom) scroll += (int) Math.ceil(control.y + control.h - bottom);
        lastScrollTime = System.currentTimeMillis();
    }

    @Override
    public boolean keyPressed(int key, int scan, int modifiers) {
        if (searching) {
            if (key == GLFW.GLFW_KEY_ESCAPE) {
                searching = false;
                return true;
            }
            if (key == GLFW.GLFW_KEY_BACKSPACE) {
                if (!searchText.isEmpty()) {
                    searchText = searchText.substring(0, searchText.length() - 1);
                    triggerSearchAsync(searchText);
                }
                return true;
            }
            if (key == GLFW.GLFW_KEY_ENTER) {
                searching = false;
                return true;
            }
        }
        if (key == GLFW.GLFW_KEY_ESCAPE) {
            close();
            return true;
        }
        if (QuickMenuRuntime.menuKey().matchesKey(key, scan)) {
            while (QuickMenuRuntime.menuKey().wasPressed()) {}
            if (!menuKeyHeld) {
                menuKeyHeld = true;
                toggle();
            }
            return true;
        }
        if (key == GLFW.GLFW_KEY_TAB) {
            focused = Math.floorMod(focused + ((modifiers & GLFW.GLFW_MOD_SHIFT) != 0 ? -1 : 1), Math.max(1, controls.size()));
            revealFocused();
            return true;
        }
        if (!controls.isEmpty() && (key == GLFW.GLFW_KEY_LEFT || key == GLFW.GLFW_KEY_RIGHT)) {
            var control = controls.get(Math.min(focused, controls.size() - 1));
            if (control.slide != null) {
                control.slide.accept(Math.max(0, Math.min(1, control.sliderValue + (key == GLFW.GLFW_KEY_RIGHT ? 0.05 : -0.05))));
                return true;
            }
        }
        if (!controls.isEmpty() && (key == GLFW.GLFW_KEY_ENTER || key == GLFW.GLFW_KEY_SPACE)) {
            controls.get(Math.min(focused, controls.size() - 1)).action.run();
            return true;
        }
        return super.keyPressed(key, scan, modifiers);
    }
}

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
import net.auramc.glass.client.karaoke.KaraokeConfig;
import net.auramc.glass.client.quickmenu.music.MusicSourceProvider;
import net.auramc.glass.client.quickmenu.music.MusicArtwork;
import net.auramc.glass.common.render.GlassSpring;

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
    private boolean karaokeExpanded;
    public static boolean qaHoverProgress = false;
    private Track currentPlayingTrack;
    private Track previousPlayingTrack;
    private long trackTransitionStart;
    private boolean lastWasPlaying;
    private long playPauseTransitionTime;
    private int pressedControl = -1;
    private final net.auramc.glass.common.render.GlassSpring prevSpring = new net.auramc.glass.common.render.GlassSpring(0.25f, 0.85f);
    private final net.auramc.glass.common.render.GlassSpring playSpring = new net.auramc.glass.common.render.GlassSpring(0.25f, 0.85f);
    private final net.auramc.glass.common.render.GlassSpring nextSpring = new net.auramc.glass.common.render.GlassSpring(0.25f, 0.85f);
    private final net.auramc.glass.common.render.GlassSpring tabSpring = new net.auramc.glass.common.render.GlassSpring(0.30f, 0.85f);
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
            triggerSearchAsync(musicTab == 1 ? searchText : "");
        }
    }

    public void setMusicTab(int tab) {
        this.musicTab = tab;
        if (tab == 1) {
            this.scroll = 220;
        } else {
            this.scroll = 0;
        }
        triggerSearchAsync(tab == 1 ? searchText : "");
    }

    public void setMusicScroll(int s) {
        this.scroll = Math.max(0, s);
    }

    public void setKaraokeExpanded(boolean expanded) {
        this.karaokeExpanded = expanded;
    }

    public void setQaHoverProgress(boolean hover) {
        qaHoverProgress = hover;
    }

    public void setSearchQuery(String query) {
        this.searchText = query != null ? query : "";
        this.searching = true;
        var src = MusicSourceProvider.get();
        if (src != null && src.available()) {
            this.cachedTracks = src.search(this.searchText);
        }
        triggerSearchAsync(this.searchText);
    }

    public void toggle() {
        motion.setOpen(!motion.isOpen(), System.nanoTime());
    }

    @Override
    public void close() {
        qaHoverProgress = false;
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
            style.asSurface();
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
        float topFade = scroll <= 0 ? 1.0f : Math.min(1.0f, Math.max(0.0f, (itemY - clipTop) / 16.0f));
        float bottomFade = Math.min(1.0f, Math.max(0.0f, (clipBottom - (itemY + Math.min(20.0f, itemH))) / 16.0f));
        return Math.max(0.0f, Math.min(1.0f, Math.min(topFade, bottomFade)));
    }

    private int maxScroll(float viewportH) {
        float contentH;
        if (section == 0) {
            contentH = 29 * 38 + 24;
        } else if (section == 1) {
            contentH = 3 * 44 + 80;
        } else if (section == 2) {
            var src = MusicSourceProvider.get();
            if (!src.available()) {
                contentH = 80;
            } else {
                float header = 314;
                int listCount = musicTab == 0 ? (src.queue() != null ? src.queue().size() : 0) : cachedTracks.size();
                float tabContent = (musicTab == 1 ? 48 : 0) + Math.min(10, listCount) * 52;
                float karaoke = 32 + (karaokeExpanded ? 5 * 38 + 12 : 0);
                contentH = header + tabContent + karaoke + 24;
            }
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

    private void noteGlyph(float cx, float cy, float size, int color, float fade) {
        float s = size / 40.0f;
        rect(cx - 10 * s, cy + 8 * s, 13 * s, 10 * s, 5 * s, color, 0, fade);
        rect(cx + 8 * s, cy + 3 * s, 13 * s, 10 * s, 5 * s, color, 0, fade);
        rect(cx - 1 * s, cy - 12 * s, 3 * s, 24 * s, 1.5f * s, color, 0, fade);
        rect(cx + 17 * s, cy - 17 * s, 3 * s, 24 * s, 1.5f * s, color, 0, fade);
        rect(cx - 1 * s, cy - 17 * s, 21 * s, 6 * s, 2 * s, color, 0, fade);
    }

    private void prevIcon(float cx, float cy, float scale, int color, float fade) {
        float s = scale;
        float w = 3 * s, h = 18 * s, r = 1.5f * s;
        rect(cx - 11 * s, cy - h * 0.5f, w, h, r, color, 0, fade);
        for (int i = 0; i <= 5; i++) {
            float t = i / 5.0f;
            float sh = (4 + 14 * t) * s;
            rect(cx - 8 * s + i * 1.5f * s, cy - sh * 0.5f, 1.6f * s, sh, 0.8f * s, color, 0, fade);
        }
        for (int i = 0; i <= 5; i++) {
            float t = i / 5.0f;
            float sh = (4 + 14 * t) * s;
            rect(cx + i * 1.5f * s, cy - sh * 0.5f, 1.6f * s, sh, 0.8f * s, color, 0, fade);
        }
    }

    private void nextIcon(float cx, float cy, float scale, int color, float fade) {
        float s = scale;
        for (int i = 0; i <= 5; i++) {
            float t = (5 - i) / 5.0f;
            float sh = (4 + 14 * t) * s;
            rect(cx - 8 * s + i * 1.5f * s, cy - sh * 0.5f, 1.6f * s, sh, 0.8f * s, color, 0, fade);
        }
        for (int i = 0; i <= 5; i++) {
            float t = (5 - i) / 5.0f;
            float sh = (4 + 14 * t) * s;
            rect(cx + i * 1.5f * s, cy - sh * 0.5f, 1.6f * s, sh, 0.8f * s, color, 0, fade);
        }
        float w = 3 * s, h = 18 * s, r = 1.5f * s;
        rect(cx + 8 * s, cy - h * 0.5f, w, h, r, color, 0, fade);
    }

    private void playIcon(float cx, float cy, float scale, int color, float fade) {
        float s = scale;
        float startX = cx - 7 * s;
        for (int i = 0; i <= 9; i++) {
            float t = (9 - i) / 9.0f;
            float sh = (4 + 20 * t) * s;
            rect(startX + i * 1.8f * s, cy - sh * 0.5f, 1.9f * s, sh, 0.95f * s, color, 0, fade);
        }
    }

    private void pauseIcon(float cx, float cy, float scale, int color, float fade) {
        float s = scale;
        float w = 5 * s, h = 22 * s, r = 2.5f * s;
        rect(cx - 7 * s, cy - h * 0.5f, w, h, r, color, 0, fade);
        rect(cx + 2 * s, cy - h * 0.5f, w, h, r, color, 0, fade);
    }

    private void speakerIcon(float x, float y, boolean loud, int color, float fade) {
        rect(x + 1, y + 5, 3, 6, 1, color, 0, fade);
        rect(x + 4, y + 4, 2, 8, 1, color, 0, fade);
        rect(x + 6, y + 2.5f, 2.5f, 11, 1, color, 0, fade);
        if (loud) {
            rect(x + 10.5f, y + 4.5f, 1.5f, 7, 0.75f, color, 0, fade);
            rect(x + 13.5f, y + 2.5f, 1.5f, 11, 0.75f, color, 0, fade);
        } else {
            rect(x + 10.5f, y + 6, 1.5f, 4, 0.75f, color, 0, fade);
        }
    }

    private void magnifierIcon(float x, float y, int color, float fade) {
        rect(x + 1, y + 1, 10, 10, 5, 0, color, fade);
        rect(x + 9, y + 9, 4, 1.8f, 0.9f, color, 0, fade);
        rect(x + 11, y + 11, 3.5f, 1.8f, 0.9f, color, 0, fade);
    }

    private void equalizerIcon(float x, float y, float w, float h, int color, float fade) {
        long t = System.currentTimeMillis();
        float b1 = 0.3f + 0.7f * (float) Math.abs(Math.sin(t * 0.007));
        float b2 = 0.2f + 0.8f * (float) Math.abs(Math.sin(t * 0.010 + 1.2));
        float b3 = 0.4f + 0.6f * (float) Math.abs(Math.sin(t * 0.008 + 2.4));
        float barW = 2.5f, gap = 2.0f;
        rect(x, y + h * (1.0f - b1), barW, h * b1, 1.25f, color, 0, fade);
        rect(x + barW + gap, y + h * (1.0f - b2), barW, h * b2, 1.25f, color, 0, fade);
        rect(x + (barW + gap) * 2, y + h * (1.0f - b3), barW, h * b3, 1.25f, color, 0, fade);
    }

    private void music(DrawContext context, float x, float y, float w) {
        var c = QuickMenuConfig.get();
        var source = MusicSourceProvider.get();

        // 8. No Bridge Fallback (MusicSource.available() == false)
        if (!source.available()) {
            float viewportTop = geometry().y() + 112;
            float viewportBottom = geometry().bottom() - 39;
            float viewportH = Math.max(1, viewportBottom - viewportTop);
            float noteCy = viewportTop + viewportH * 0.40f;
            float cx = x + w * 0.5f;
            float fade = edgeFade(noteCy - 24, 48);
            if (fade > 0.01f) {
                noteGlyph(cx, noteCy, 48, 0x66FFFFFF, fade);
                String msg = "Откройте Aura Launcher, чтобы управлять музыкой";
                float tw = AuraRenderer.textWidth(msg, 15);
                text(msg, cx - tw * 0.5f, noteCy + 36, 15, 0x99FFFFFF, 400, fade);
            }
            return;
        }

        var now = source.now();
        Track activeTrack = now.track();

        // 9. Track Change Transition: 250ms crossfade between previous and current
        if (currentPlayingTrack == null) {
            currentPlayingTrack = activeTrack;
        } else if (activeTrack != null && !activeTrack.id().equals(currentPlayingTrack.id())) {
            previousPlayingTrack = currentPlayingTrack;
            currentPlayingTrack = activeTrack;
            trackTransitionStart = System.currentTimeMillis();
        }
        long trackTransElapsed = System.currentTimeMillis() - trackTransitionStart;
        float trackCrossfade = trackTransitionStart > 0 && trackTransElapsed < 250
                ? trackTransElapsed / 250.0f : 1.0f;
        if (trackCrossfade >= 1.0f) previousPlayingTrack = null;

        // 1. Now Playing Card: artwork 128x128, radius 20, shadow 0 8 24 rgba(0,0,0,.35)
        float cardFade = edgeFade(y, 128);
        if (cardFade > 0.01f) {
            // Shadow 0 8 24 rgba(0,0,0,.35)
            AuraRenderer.shadow(x, y + 8, 128, 128, 20, color(0x59000000, cardFade));

            var cover = currentPlayingTrack != null ? MusicArtwork.texture(currentPlayingTrack) : null;
            if (cover != null) {
                flushNative();
                context.drawTexture(cover, (int) x, (int) y, 0, 0, 128, 128, 128, 128);
                context.draw();
                AuraRenderer.begin(context.getMatrices().peek().getPositionMatrix(), RenderSystem.getProjectionMatrix(), RenderSystem.getModelViewMatrix());
            } else {
                rect(x, y, 128, 128, 20, 0x3D767680, 0, cardFade);
                noteGlyph(x + 64, y + 64, 40, 0x80FFFFFF, cardFade);
            }

            // Right of artwork: title (22px 600 white 0.95), artist (17px 400 white 0.60), source (13px #64D2FF 0.90)
            float textX = x + 144;
            float maxW = w - 144;
            String title = currentPlayingTrack == null ? "Ничего не играет" : currentPlayingTrack.title();
            String artist = currentPlayingTrack == null ? "Выберите трек из очереди" : currentPlayingTrack.artist();
            text(fit(title, maxW, 22), textX, y + 26, 22, 0xF2FFFFFF, 600, cardFade);
            text(fit(artist, maxW, 17), textX, y + 58, 17, 0x99FFFFFF, 400, cardFade);
            text("Aura Music", textX, y + 86, 13, 0xE664D2FF, 400, cardFade);
        }

        long dur = currentPlayingTrack != null ? currentPlayingTrack.durationMs() : 0;
        long pos = now.currentPositionMs();
        float progressNorm = dur > 0 ? Math.max(0.0f, Math.min(1.0f, pos / (float) dur)) : 0.0f;

        // 2. Full-width Progress Bar
        float progY = y + 146;
        float progFade = edgeFade(progY, 26);
        boolean hoverProg = qaHoverProgress || (mouseX >= x && mouseX <= x + w && mouseY >= progY - 6 && mouseY <= progY + 16);
        boolean activeProg = hoverProg || (pressed != null && "progress".equals(pressed.label()));
        float trackH = activeProg ? 8.0f : 4.0f;
        float trackR = activeProg ? 4.0f : 2.0f;
        float trackY = progY + (4.0f - trackH) * 0.5f;

        if (progFade > 0.01f) {
            rect(x, trackY, w, trackH, trackR, 0x5C767680, 0, progFade);
            rect(x, trackY, w * progressNorm, trackH, trackR, 0xEBFFFFFF, 0, progFade);
            if (activeProg) {
                float thumbCx = x + w * progressNorm;
                rect(thumbCx - 7, progY + 2 - 7, 14, 14, 7, 0xFFFFFFFF, 0, progFade);
            }
            String elapsedStr = time(pos);
            long rem = Math.max(0, dur - pos);
            String remStr = "−" + time(rem);
            float remW = AuraRenderer.textWidth(remStr, 13);
            text(elapsedStr, x, progY + 14, 13, 0x8CFFFFFF, 400, progFade);
            text(remStr, x + w - remW, progY + 14, 13, 0x8CFFFFFF, 400, progFade);
        }
        controls.add(new Control(x, progY - 6, w, 20, "progress", () -> {}, norm -> source.seek((long) (norm * dur)), false, progressNorm));

        // 3. Centered Control Icons (Prev 28px, Play/Pause 44px, Next 28px, white 0.95, spacing 40px)
        float cx = x + w * 0.5f;
        float ctrlCy = progY + 48;
        float ctrlFade = edgeFade(ctrlCy - 22, 44);

        boolean playing = now.isPlaying();
        if (playing != lastWasPlaying) {
            lastWasPlaying = playing;
            playPauseTransitionTime = System.currentTimeMillis();
        }
        long playTransElapsed = System.currentTimeMillis() - playPauseTransitionTime;
        float playTrans = playPauseTransitionTime > 0 && playTransElapsed < 150
                ? playTransElapsed / 150.0f : 1.0f;
        float playAlpha = playing ? playTrans : (1.0f - playTrans);
        float pauseAlpha = playing ? (1.0f - playTrans) : playTrans;

        long nano = System.nanoTime();
        float prevScale = 1.0f - 0.08f * prevSpring.approach(pressedControl == 0 ? 1.0f : 0.0f, nano);
        float playScale = 1.0f - 0.08f * playSpring.approach(pressedControl == 1 ? 1.0f : 0.0f, nano);
        float nextScale = 1.0f - 0.08f * nextSpring.approach(pressedControl == 2 ? 1.0f : 0.0f, nano);

        float prevCx = cx - 76;
        float nextCx = cx + 76;

        boolean hoverPrev = Math.hypot(mouseX - prevCx, mouseY - ctrlCy) <= 22;
        boolean hoverPlay = Math.hypot(mouseX - cx, mouseY - ctrlCy) <= 30;
        boolean hoverNext = Math.hypot(mouseX - nextCx, mouseY - ctrlCy) <= 22;

        if (ctrlFade > 0.01f) {
            if (hoverPrev) rect(prevCx - 22, ctrlCy - 22, 44, 44, 22, 0x1FFFFFFF, 0, ctrlFade);
            if (hoverPlay) rect(cx - 30, ctrlCy - 30, 60, 60, 30, 0x1FFFFFFF, 0, ctrlFade);
            if (hoverNext) rect(nextCx - 22, ctrlCy - 22, 44, 44, 22, 0x1FFFFFFF, 0, ctrlFade);

            prevIcon(prevCx, ctrlCy, prevScale, color(0xF2FFFFFF, ctrlFade), ctrlFade);
            if (playAlpha > 0.01f) playIcon(cx, ctrlCy, playScale, color(0xF2FFFFFF, playAlpha * ctrlFade), ctrlFade * playAlpha);
            if (pauseAlpha > 0.01f) pauseIcon(cx, ctrlCy, playScale, color(0xF2FFFFFF, pauseAlpha * ctrlFade), ctrlFade * pauseAlpha);
            nextIcon(nextCx, ctrlCy, nextScale, color(0xF2FFFFFF, ctrlFade), ctrlFade);
        }
        controls.add(new Control(prevCx - 22, ctrlCy - 22, 44, 44, "prev", source::prev, null));
        controls.add(new Control(cx - 30, ctrlCy - 30, 60, 60, "playPause", () -> {
            if (now.isPlaying()) source.pause(); else source.play();
        }, null));
        controls.add(new Control(nextCx - 22, ctrlCy - 22, 44, 44, "next", source::next, null));

        // 4. Volume Row
        float volY = ctrlCy + 36;
        float volFade = edgeFade(volY, 20);
        float sliderX = x + 26;
        float sliderW = w - 52;
        float vol = now.volume();
        boolean hoverVol = mouseX >= sliderX - 4 && mouseX <= sliderX + sliderW + 4 && mouseY >= volY - 4 && mouseY <= volY + 16;
        boolean activeVol = hoverVol || (pressed != null && "volume".equals(pressed.label()));
        float volH = activeVol ? 8.0f : 4.0f;
        float volR = activeVol ? 4.0f : 2.0f;
        float volTrackY = volY + (4.0f - volH) * 0.5f;

        if (volFade > 0.01f) {
            speakerIcon(x + 2, volY - 2, false, color(0x99FFFFFF, volFade), volFade);
            rect(sliderX, volTrackY, sliderW, volH, volR, 0x5C767680, 0, volFade);
            rect(sliderX, volTrackY, sliderW * vol, volH, volR, 0xEBFFFFFF, 0, volFade);
            if (activeVol) {
                float vThumbX = sliderX + sliderW * vol;
                rect(vThumbX - 7, volY + 2 - 7, 14, 14, 7, 0xFFFFFFFF, 0, volFade);
            }
            speakerIcon(x + w - 18, volY - 2, true, color(0x99FFFFFF, volFade), volFade);
        }
        controls.add(new Control(sliderX, volY - 4, sliderW, 16, "volume", () -> {}, v -> {
            source.volume((float) v);
            c.musicVolume = v;
        }, false, vol));

        // 6. Segmented Tabs «Очередь | Поиск»
        float segY = volY + 28;
        float segFade = edgeFade(segY, 32);
        float segW = (w - 4) * 0.5f;
        if (segFade > 0.01f) {
            rect(x, segY, w, 32, 9, 0x3D767680, 0, segFade);
            float tabProgress = tabSpring.approach(musicTab == 0 ? 0.0f : 1.0f, System.nanoTime());
            float activePillX = x + 2 + tabProgress * segW;
            rect(activePillX, segY + 2, segW, 28, 7, 0x2EFFFFFF, 0, segFade);

            float qW = AuraRenderer.textWidth("Очередь", 14);
            int qColor = musicTab == 0 ? 0xF2FFFFFF : 0x99FFFFFF;
            text("Очередь", x + 2 + (segW - qW) * 0.5f, segY + 8, 14, qColor, musicTab == 0 ? 600 : 500, segFade);

            float sW = AuraRenderer.textWidth("Поиск", 14);
            int sColor = musicTab == 1 ? 0xF2FFFFFF : 0x99FFFFFF;
            text("Поиск", x + 2 + segW + (segW - sW) * 0.5f, segY + 8, 14, sColor, musicTab == 1 ? 600 : 500, segFade);
        }
        controls.add(new Control(x, segY, segW + 2, 32, "tabQueue", () -> {
            musicTab = 0;
            scroll = 0;
            triggerSearchAsync("");
        }, null));
        controls.add(new Control(x + segW + 2, segY, segW + 2, 32, "tabSearch", () -> {
            musicTab = 1;
            scroll = Math.max(scroll, 220);
            triggerSearchAsync(searchText);
        }, null));

        float listY = segY + 40;

        // 5. Search Field if in search tab
        if (musicTab == 1) {
            float sBoxFade = edgeFade(listY, 40);
            if (sBoxFade > 0.01f) {
                rect(x, listY, w, 40, 10, 0x3D767680, 0, sBoxFade);
                magnifierIcon(x + 12, listY + 12, color(0x73FFFFFF, sBoxFade), sBoxFade);
                if (searchText.isEmpty() && !searching) {
                    text("Поиск в Aura Music", x + 36, listY + 12, 14, 0x73FFFFFF, 400, sBoxFade);
                } else {
                    String cursor = searching && (System.currentTimeMillis() % 1000 < 500) ? "|" : "";
                    text(searchText + cursor, x + 36, listY + 12, 14, 0xF2FFFFFF, 400, sBoxFade);
                }
            }
            controls.add(new Control(x, listY, w, 40, "searchBox", () -> searching = true, null));
            listY += 48;
        }

        // List of tracks (Queue or Search Results)
        List<Track> tracks = (musicTab == 0 && source.available()) ? source.queue() : cachedTracks;
        if (tracks == null) tracks = List.of();
        if (tracks.isEmpty() && source.available()) {
            if (lastSearchedQuery == null) triggerSearchAsync("");
        }

        for (int i = 0; i < Math.min(10, tracks.size()); i++) {
            var track = tracks.get(i);
            float rowY = listY + i * 52;
            float rowFade = edgeFade(rowY, 52);
            if (rowFade <= 0.01f) continue;

            boolean hoverRow = mouseX >= x && mouseX <= x + w && mouseY >= rowY && mouseY <= rowY + 52;
            if (hoverRow) {
                rect(x, rowY, w, 52, 10, 0x0FFFFFFF, 0, rowFade);
            }

            var rowCover = MusicArtwork.texture(track);
            if (rowCover != null) {
                flushNative();
                context.drawTexture(rowCover, (int) (x + 6), (int) (rowY + 6), 0, 0, 40, 40, 40, 40);
                context.draw();
                AuraRenderer.begin(context.getMatrices().peek().getPositionMatrix(), RenderSystem.getProjectionMatrix(), RenderSystem.getModelViewMatrix());
            } else {
                rect(x + 6, rowY + 6, 40, 40, 8, 0x3D767680, 0, rowFade);
                noteGlyph(x + 26, rowY + 26, 16, 0x80FFFFFF, rowFade);
            }

            boolean isCurrent = currentPlayingTrack != null && currentPlayingTrack.id().equals(track.id());
            float titleX = x + 54;
            if (isCurrent) {
                equalizerIcon(x + 54, rowY + 20, 14, 14, ACCENT, rowFade);
                titleX = x + 74;
            }

            text(fit(track.title(), w - (titleX - x) - 60, 15), titleX, rowY + 10, 15, 0xEBFFFFFF, isCurrent ? 600 : 500, rowFade);
            text(fit(track.artist(), w - (titleX - x) - 60, 13), titleX, rowY + 30, 13, 0x8CFFFFFF, 400, rowFade);

            String durStr = time(track.durationMs());
            float dw = AuraRenderer.textWidth(durStr, 13);
            text(durStr, x + w - dw - 12, rowY + 20, 13, 0x8CFFFFFF, 400, rowFade);

            controls.add(new Control(x, rowY, w, 52, track.title(), () -> source.playTrack(track), null));
        }

        // 7. Karaoke Foldable Block at bottom
        float ky = listY + Math.min(10, tracks.size()) * 52 + 16;
        float kFade = edgeFade(ky, 30);
        if (kFade > 0.01f) {
            String kTitle = "Караоке " + (karaokeExpanded ? "˅" : "›");
            text(kTitle, x, ky + 4, 15, 0xF2FFFFFF, 600, kFade);
        }
        controls.add(new Control(x, ky, w, 28, "karaokeHeader", () -> karaokeExpanded = !karaokeExpanded, null));

        if (karaokeExpanded) {
            ky += 32;
            toggle(x, ky, w, "Караоке в мире", () -> c.karaoke, v -> {
                c.karaoke = v;
                KaraokeConfig.get().setEnabled(v);
            });
            ky += 38;

            boolean kEnabled = c.karaoke;
            slider(x, ky, w, "Задержка", c.lyricsOffsetMs, -500, 500, v -> {
                int rounded = (int) Math.round(v / 10.0) * 10;
                c.lyricsOffsetMs = rounded;
                KaraokeConfig.get().setTimingOffsetMs(rounded);
            }, v -> {
                int iv = (int) Math.round(v / 10.0) * 10;
                return (iv > 0 ? "+" : "") + iv + " мс";
            }, kEnabled);
            ky += 38;

            slider(x, ky, w, "Размер текста", c.karaokeSize, 0.75, 1.50, v -> {
                double rounded = Math.round(v * 100.0) / 100.0;
                c.karaokeSize = rounded;
                KaraokeConfig.get().setSize((float) rounded);
            }, v -> Math.round(v * 100) + " %", kEnabled);
            ky += 38;

            slider(x, ky, w, "Слов максимум", c.karaokeMaxWords, 1, 6, v -> {
                int rounded = (int) Math.round(v);
                c.karaokeMaxWords = rounded;
                KaraokeConfig.get().setMaxWords(rounded);
            }, v -> String.valueOf((int) Math.round(v)), kEnabled);
            ky += 38;

            toggle(x, ky, w, "Только когда стою", () -> c.karaokeOnlyStanding, v -> {
                c.karaokeOnlyStanding = v;
                KaraokeConfig.get().setOnlyStanding(v);
            });
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
        slider(x, y, w, label, value, min, max, set, format, true);
    }

    private void slider(float x, float y, float w, String label, double value, double min, double max, DoubleConsumer set, Function<Double, String> format, boolean enabled) {
        float rowFade = edgeFade(y, 36);
        if (rowFade <= 0.01f) return;
        int labelColor = enabled ? 0xEBFFFFFF : 0x66FFFFFF;
        int labelWeight = enabled ? 500 : 400;
        text(label, x, y + 1, 18, labelColor, labelWeight, rowFade);
        if (format != null) {
            String display = format.apply(value);
            if (display != null && !display.isEmpty()) {
                text(display, x + w - AuraRenderer.textWidth(display, 15), y + 2, 15, enabled ? 0xEBFFFFFF : 0x66FFFFFF, 500, rowFade);
            }
        }
        float trackW = w - 8, t = (float) ((value - min) / (max - min));
        rect(x + 4, y + 26, trackW, 3, 1.5f, 0x80767680, 0, rowFade);
        rect(x + 4, y + 26, trackW * t, 3, 1.5f, enabled ? ACCENT : 0x80767680, 0, rowFade);
        rect(x + 4 + trackW * t - 8, y + 20, 16, 15, 7.5f, enabled ? 0xFFFFFFFF : 0x99FFFFFF, 0x1AFFFFFF, rowFade);
        if (!enabled) return;
        DoubleConsumer change = normalized -> {
            set.accept(min + normalized * (max - min));
            QuickMenuConfig.save();
        };
        controls.add(new Control(x, y, w, 36, label, () -> change.accept(Math.min(1, (value - min) / (max - min) + 0.05)), change, false, (value - min) / (max - min)));
    }

    private void row(float x, float y, float w, String label, float fade) {
        text(label, x, y + 7, 18, 0xEBFFFFFF, 500, fade);
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
                if ("prev".equals(c.label)) pressedControl = 0;
                else if ("playPause".equals(c.label)) pressedControl = 1;
                else if ("next".equals(c.label)) pressedControl = 2;
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
        pressedControl = -1;
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
                if (!cachedTracks.isEmpty()) {
                    MusicSourceProvider.get().playTrack(cachedTracks.get(0));
                }
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

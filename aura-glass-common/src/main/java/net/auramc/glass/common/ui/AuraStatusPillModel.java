package net.auramc.glass.common.ui;

import net.auramc.glass.common.adapter.AuraPlatform;

import java.time.LocalTime;
import java.time.format.DateTimeFormatter;

/**
 * Р§РёСЃС‚Р°СЏ РїР»Р°С‚С„РѕСЂРјРѕРЅРµР·Р°РІРёСЃРёРјР°СЏ РјРѕРґРµР»СЊ СЃС‚Р°С‚СѓСЃРЅРѕР№ РїР»Р°С€РєРё FPS (StatusPill).
 * РћРіСЂР°РЅРёС‡РёРІР°РµС‚ С‡Р°СЃС‚РѕС‚Сѓ РѕР±РЅРѕРІР»РµРЅРёСЏ С‚РµРєСЃС‚Р° РґРѕ 4 СЂР°Р· РІ СЃРµРєСѓРЅРґСѓ (250 РјСЃ) РІРѕ РёР·Р±РµР¶Р°РЅРёРµ Р°Р»Р»РѕРєР°С†РёР№ РІ РєР°РґСЂРµ.
 */
public class AuraStatusPillModel {
    private static final DateTimeFormatter TIME_FORMAT = DateTimeFormatter.ofPattern("HH:mm");

    private int x;
    private int y;
    private int width = 160;
    private int height = 24;

    private long lastUpdateMs = 0;
    private String cachedText = "Aura В· 0 FPS В· 00:00";
    private int cachedFps = 0;

    public AuraStatusPillModel(int x, int y) {
        this.x = x;
        this.y = y;
        update(0);
    }

    /**
     * РћР±РЅРѕРІР»СЏРµС‚ РєРµС€РёСЂРѕРІР°РЅРЅСѓСЋ СЃС‚СЂРѕРєСѓ СЃС‚Р°С‚СѓСЃР° РЅРµ С‡Р°С‰Рµ 4 СЂР°Р· РІ СЃРµРєСѓРЅРґСѓ (РўР— В§5).
     */
    public void update(long currentSystemTimeMs) {
        if (currentSystemTimeMs - lastUpdateMs >= 250 || lastUpdateMs == 0) {
            this.lastUpdateMs = currentSystemTimeMs;
            int fps = AuraPlatform.get().getCurrentFps();
            this.cachedFps = fps;

            String timeStr;
            try {
                timeStr = LocalTime.now().format(TIME_FORMAT);
            } catch (Throwable ignored) {
                timeStr = "--:--";
            }

            this.cachedText = "Aura В· " + fps + " FPS В· " + timeStr;
        }
    }

    public String getText() {
        return cachedText;
    }

    public int getCachedFps() {
        return cachedFps;
    }

    public int getX() { return x; }
    public void setX(int x) { this.x = x; }

    public int getY() { return y; }
    public void setY(int y) { this.y = y; }

    public int getWidth() { return width; }
    public void setWidth(int width) { this.width = width; }

    public int getHeight() { return height; }
    public void setHeight(int height) { this.height = height; }

    public int getRadius() { return height / 2; }
}

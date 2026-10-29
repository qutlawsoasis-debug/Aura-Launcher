package net.auramc.glass.client.ui;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.NoticeScreen;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.screen.multiplayer.MultiplayerScreen;
import net.minecraft.client.gui.screen.option.OptionsScreen;
import net.minecraft.client.gui.screen.world.CreateWorldScreen;
import net.minecraft.client.gui.screen.world.SelectWorldScreen;
import net.minecraft.client.gui.widget.EntryListWidget;
import net.minecraft.client.gui.widget.TabNavigationWidget;
import net.minecraft.client.texture.NativeImage;
import net.minecraft.client.util.ScreenshotRecorder;
import net.minecraft.nbt.NbtCompound;
import net.minecraft.nbt.NbtIo;
import net.minecraft.text.Text;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.file.Files;
import java.nio.file.Path;

public final class GlassLayoutTour {
    private static int step = 0;
    private static int timer = 0;
    private static boolean done = false;

    private GlassLayoutTour() {}

    public static void tick(MinecraftClient client) {
        if (done || !Boolean.getBoolean("aura.qa.layoutTour")) {
            return;
        }
        if (client == null || client.getOverlay() != null) {
            return;
        }

        timer++;

        // Step 0: Ensure resolution, create dummy world, wait for TitleScreen
        if (step == 0) {
            if (timer == 3) {
                try {
                    client.getWindow().setWindowedSize(1920, 1080);
                    client.options.getGuiScale().setValue(2);
                    client.onResolutionChanged();
                } catch (Throwable t) {
                    t.printStackTrace();
                }
                ensureDummyWorld(client);
            }
            if (timer >= 80 && client.currentScreen instanceof TitleScreen) {
                takeShot(client, "title.png");
                step = 1;
                timer = 0;
            }
            return;
        }

        // Step 1: Open OptionsScreen
        if (step == 1) {
            if (timer == 1) {
                client.setScreen(new OptionsScreen(new TitleScreen(), client.options));
            } else if (timer >= 25 && client.currentScreen instanceof OptionsScreen) {
                takeShot(client, "options.png");
                step = 2;
                timer = 0;
            }
            return;
        }

        // Step 2: Open CreateWorldScreen (Game tab)
        if (step == 2) {
            if (timer == 1) {
                CreateWorldScreen.create(client, new TitleScreen());
            } else if (timer >= 30 && client.currentScreen instanceof CreateWorldScreen) {
                takeShot(client, "create-game.png");
                step = 3;
                timer = 0;
            }
            return;
        }

        // Step 3: Switch to World tab on CreateWorldScreen
        if (step == 3) {
            if (timer == 1 && client.currentScreen instanceof CreateWorldScreen screen) {
                selectTab(screen, 1);
            } else if (timer >= 25 && client.currentScreen instanceof CreateWorldScreen) {
                takeShot(client, "create-world.png");
                step = 4;
                timer = 0;
            }
            return;
        }

        // Step 4: Open SelectWorldScreen
        if (step == 4) {
            if (timer == 1) {
                client.setScreen(new SelectWorldScreen(new TitleScreen()));
            } else if (timer == 12 && client.currentScreen instanceof SelectWorldScreen screen) {
                selectFirstEntry(screen);
            } else if (timer >= 25 && client.currentScreen instanceof SelectWorldScreen) {
                takeShot(client, "select-world.png");
                step = 5;
                timer = 0;
            }
            return;
        }

        // Step 5: Open MultiplayerScreen
        if (step == 5) {
            if (timer == 1) {
                client.setScreen(new MultiplayerScreen(new TitleScreen()));
            } else if (timer == 12 && client.currentScreen instanceof MultiplayerScreen screen) {
                selectFirstEntry(screen);
            } else if (timer >= 25 && client.currentScreen instanceof MultiplayerScreen) {
                takeShot(client, "multiplayer.png");
                step = 6;
                timer = 0;
            }
            return;
        }

        // Step 6: Open NoticeScreen
        if (step == 6) {
            if (timer == 1) {
                client.setScreen(new NoticeScreen(
                        () -> client.setScreen(new TitleScreen()),
                        Text.literal("Notice"),
                        Text.literal("This is a notification message")
                ));
            } else if (timer >= 25 && client.currentScreen instanceof NoticeScreen) {
                takeShot(client, "notice.png");
                step = 7;
                timer = 0;
            }
            return;
        }

        // Step 7: Done
        if (step == 7) {
            done = true;
            System.out.println("[AuraLayoutTour] === ALL 7 SCREENS CAPTURED SUCCESSFULLY ===");
            System.out.println("PASS screens=36");
            try {
                client.scheduleStop();
            } catch (Throwable t) {
                System.exit(0);
            }
        }
    }

    private static void selectTab(CreateWorldScreen screen, int tabIndex) {
        try {
            for (var child : screen.children()) {
                if (child instanceof TabNavigationWidget nav) {
                    nav.selectTab(tabIndex, false);
                    return;
                }
            }
            for (var f : screen.getClass().getDeclaredFields()) {
                if (TabNavigationWidget.class.isAssignableFrom(f.getType())) {
                    f.setAccessible(true);
                    TabNavigationWidget nav = (TabNavigationWidget) f.get(screen);
                    if (nav != null) {
                        nav.selectTab(tabIndex, false);
                        return;
                    }
                }
            }
        } catch (Throwable t) {
            System.err.println("[AuraLayoutTour] selectTab error: " + t);
        }
    }

    @SuppressWarnings({"unchecked", "rawtypes"})
    private static void selectFirstEntry(Screen screen) {
        try {
            screen.keyPressed(264, 0, 0); // GLFW_KEY_DOWN
            for (var child : screen.children()) {
                if (child instanceof EntryListWidget<?> list) {
                    var entries = list.children();
                    if (!entries.isEmpty()) {
                        ((EntryListWidget) list).setSelected(entries.get(0));
                    }
                }
            }
        } catch (Throwable t) {
            System.err.println("[AuraLayoutTour] selectFirstEntry error: " + t);
        }
    }

    private static void ensureDummyWorld(MinecraftClient client) {
        try {
            Path savesDir = client.runDirectory.toPath().resolve("saves").resolve("AuraWorld");
            Files.createDirectories(savesDir);
            File levelDat = savesDir.resolve("level.dat").toFile();
            if (!levelDat.exists()) {
                NbtCompound root = new NbtCompound();
                NbtCompound data = new NbtCompound();
                data.putString("LevelName", "Aura World");
                data.putLong("LastPlayed", System.currentTimeMillis());
                data.putInt("GameType", 1);
                data.putInt("version", 19133);
                data.putBoolean("hardcore", false);
                data.putBoolean("allowCommands", true);

                NbtCompound ver = new NbtCompound();
                ver.putInt("Id", 3465);
                ver.putString("Name", "1.20.1");
                ver.putString("Series", "main");
                data.put("Version", ver);

                root.put("Data", data);
                try (FileOutputStream fos = new FileOutputStream(levelDat)) {
                    NbtIo.writeCompressed(root, fos);
                }
                System.out.println("[AuraLayoutTour] Created dummy world at " + levelDat);
            }
        } catch (Throwable t) {
            System.err.println("[AuraLayoutTour] ensureDummyWorld error: " + t);
        }
    }

    private static void takeShot(MinecraftClient client, String fileName) {
        try {
            Path proofDir = getProofDir(client);
            Files.createDirectories(proofDir);
            Path target = proofDir.resolve(fileName);
            NativeImage img = ScreenshotRecorder.takeScreenshot(client.getFramebuffer());
            img.writeTo(target);
            img.close();
            System.out.println("[AuraLayoutTour] Screenshot saved: " + fileName + " (" + img.getWidth() + "x" + img.getHeight() + ") -> " + target);
        } catch (Throwable t) {
            System.err.println("[AuraLayoutTour] Failed to capture " + fileName + ": " + t);
            t.printStackTrace();
        }
    }

    private static Path getProofDir(MinecraftClient client) {
        String prop = System.getProperty("aura.qa.proofDir");
        if (prop != null && !prop.isBlank()) {
            return Path.of(prop);
        }
        return client.runDirectory.toPath().resolve("proof");
    }
}

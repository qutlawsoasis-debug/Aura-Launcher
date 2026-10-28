# Aura Glass

Independent client-only Fabric mod. Build aura-glass (Minecraft1.20.1/Java17+) or aura-glass-1.21 (Minecraft1.21.1/Java21+) using `./gradlew build` inside the module. aura-glass-common is a shared sourceSet, not a separate distributable JAR. Glass and aura-defaults do not depend on one another.

## Effects

Edit only the existing quality field in config/aura-glass/styles.json; retain presets and other fields. Hot reload applies without a restart:

- `"quality": "FULL"` (alias of HIGH): shared capture/blur plus refraction/dispersion.
- `"quality": "LOW"`: tint and a simple edge, no capture or blur, including modal screens.
- `"quality": "OFF"`: dense flat fill, no capture or blur; lens/gloss/depth/noise disabled. Rounded geometry, labels, thin outline/focus, vanilla actions and spring states remain.
- MEDIUM is retained: blur without High refraction.

Uppercase/lowercase names are accepted; HIGH/LOW/MEDIUM configs remain compatible. Ordinary Off panels use JSON offFill (RGB28/30/36, alpha0.94); alpha must be0.94..1. Primary/selected colors remain distinct at the same minimum opacity. Off skips capture/blur; it does not claim zero GPU cost or unload all cached UI resources.

F6 draws the probe, F7 shows diagnostics, F8 opens the gallery. Off diagnostics: OFF, capture=false, blur-path=0, flat>0; capture/blur timings show not used. Effects selection is currently a JSON setting, not a new in-game settings screen.

This branch is under review. Do not overlay it onto older Aura-Pack UI/defaults binaries as an unofficial update; pack publication is a separate authorized action.

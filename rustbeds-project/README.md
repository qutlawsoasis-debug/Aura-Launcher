# Rust Beds

Shift + right-click a bed with an empty main hand to assign its respawn point to another online player. `/givebed <player> <x> <y> <z>` performs the same operation.

Build each target separately with JDK 21:

```powershell
.\gradlew.bat -PtargetMinecraft=1.20.1 build
.\gradlew.bat -PtargetMinecraft=1.21.1 build
```

Artifacts are in `build/<minecraft-version>/libs/`. The jars use different item APIs and declare an exact Minecraft version; do not reuse the 1.20.1 jar on 1.21.1.

The headless regression test opens the assignment GUI through the real use-block callback, checks recipient profiles and spawn assignment, and rejects a bed removed while its menu is open:

```powershell
.\gradlew.bat --no-daemon -PtargetMinecraft=1.20.1 runGametest
.\gradlew.bat --no-daemon -PtargetMinecraft=1.21.1 runGametest
```

Tests run only in the corresponding sibling `aura-ui/run/rustbeds-qa` or `aura-ui-1.21/run/rustbeds-qa` directory and exit automatically. No installed launcher or user Minecraft profile is used.

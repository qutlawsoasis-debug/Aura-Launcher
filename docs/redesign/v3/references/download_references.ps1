$baseDir = "c:\Users\magne\Documents\GitHub\Aura-Launcher\docs\redesign\v3\references"
$dirs = @(
    "$baseDir\visionos-apple",
    "$baseDir\game-clients",
    "$baseDir\launchers",
    "$baseDir\glass-ui"
)
foreach ($d in $dirs) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

$items = @(
    # --- Category 1: visionos-apple ---
    @{ Path = "$baseDir\visionos-apple\01-visionos-home-screen.png"; Url = "https://upload.wikimedia.org/wikipedia/commons/1/11/Apple_Vision_Pro_home_screen_-_visionOS_27.png" },
    @{ Path = "$baseDir\visionos-apple\02-visionos-materials-system.png"; Url = "https://developer.apple.com/tutorials/images/com.apple.HIG/foundations-materials-thumbnail@2x.png" },
    @{ Path = "$baseDir\visionos-apple\03-visionos-spatial-design.png"; Url = "https://developer.apple.com/tutorials/images/com.apple.HIG/platforms-designing-for-visionos-thumbnail@2x.png" },
    @{ Path = "$baseDir\visionos-apple\04-visionos-windows-depth.png"; Url = "https://developer.apple.com/tutorials/images/com.apple.HIG/components-windows-thumbnail@2x.png" },
    @{ Path = "$baseDir\visionos-apple\05-visionos-tab-bars-floating.png"; Url = "https://developer.apple.com/tutorials/images/com.apple.HIG/components-tab-bars-thumbnail@2x.png" },
    @{ Path = "$baseDir\visionos-apple\06-visionos-segmented-controls.png"; Url = "https://developer.apple.com/tutorials/images/com.apple.HIG/components-segmented-controls-thumbnail@2x.png" },
    @{ Path = "$baseDir\visionos-apple\07-visionos-sliders.png"; Url = "https://developer.apple.com/tutorials/images/com.apple.HIG/components-sliders-thumbnail@2x.png" },
    @{ Path = "$baseDir\visionos-apple\08-visionos-toggles.png"; Url = "https://developer.apple.com/tutorials/images/com.apple.HIG/components-toggles-thumbnail@2x.png" },
    @{ Path = "$baseDir\visionos-apple\09-visionos-buttons.png"; Url = "https://developer.apple.com/tutorials/images/com.apple.HIG/components-buttons-thumbnail@2x.png" },
    @{ Path = "$baseDir\visionos-apple\10-visionos-audio-widget.png"; Url = "https://developer.apple.com/tutorials/images/com.apple.HIG/patterns-playing-audio-thumbnail@2x.png" },
    @{ Path = "$baseDir\visionos-apple\11-visionos-motion-springs.png"; Url = "https://developer.apple.com/tutorials/images/com.apple.HIG/foundations-motion-thumbnail@2x.png" },
    @{ Path = "$baseDir\visionos-apple\12-visionos-typography.png"; Url = "https://developer.apple.com/tutorials/images/com.apple.HIG/foundations-typography-thumbnail@2x.png" },
    @{ Path = "$baseDir\visionos-apple\13-visionos-color-vibrancy.png"; Url = "https://developer.apple.com/tutorials/images/com.apple.HIG/foundations-color-thumbnail@2x.png" },
    @{ Path = "$baseDir\visionos-apple\14-visionos-layout-hierarchy.png"; Url = "https://developer.apple.com/tutorials/images/com.apple.HIG/foundations-layout-thumbnail@2x.png" },

    # --- Category 2: game-clients ---
    @{ Path = "$baseDir\game-clients\01-vape-v4-main-gui.png"; Url = "https://docs.vape.gg/img/vape-gui.png" },
    @{ Path = "$baseDir\game-clients\02-vape-v4-settings-menu.png"; Url = "https://docs.vape.gg/img/gui/open-general-settings.png" },
    @{ Path = "$baseDir\game-clients\03-vape-v4-hud-overlays.png"; Url = "https://docs.vape.gg/img/overlays.png" },
    @{ Path = "$baseDir\game-clients\04-nursultan-clickgui-overview.jpg"; Url = "https://i.ytimg.com/vi/8sN43jc80nk/hqdefault.jpg" },
    @{ Path = "$baseDir\game-clients\05-nursultan-clickgui-modules.jpg"; Url = "https://i.ytimg.com/vi/dG_seOS37uM/hqdefault.jpg" },
    @{ Path = "$baseDir\game-clients\06-nursultan-hud-elements.jpg"; Url = "https://i.ytimg.com/vi/izu20i111YQ/hqdefault.jpg" },
    @{ Path = "$baseDir\game-clients\07-celestial-recode-showcase.jpg"; Url = "https://i.ytimg.com/vi/ajbGcnK1k9w/hqdefault.jpg" },
    @{ Path = "$baseDir\game-clients\08-celestial-clickgui-cards.jpg"; Url = "https://i.ytimg.com/vi/x4N5s0fWTJI/hqdefault.jpg" },
    @{ Path = "$baseDir\game-clients\09-celestial-sliders-switchers.jpg"; Url = "https://i.ytimg.com/vi/76viXY31zes/hqdefault.jpg" },
    @{ Path = "$baseDir\game-clients\10-plintus-visuals-hud.jpg"; Url = "https://i.ytimg.com/vi/pHXNWXtHscU/hqdefault.jpg" },
    @{ Path = "$baseDir\game-clients\11-plintus-visuals-pvp-card.jpg"; Url = "https://i.ytimg.com/vi/tB8uJNTnJ44/hqdefault.jpg" },
    @{ Path = "$baseDir\game-clients\12-plintus-visuals-config.jpg"; Url = "https://i.ytimg.com/vi/oFgHw93GEuE/hqdefault.jpg" },

    # --- Category 3: launchers ---
    @{ Path = "$baseDir\launchers\01-prism-instance-screenshots.png"; Url = "https://prismlauncher.org/img/screenshots/InstanceScreenshotsDark.png" },
    @{ Path = "$baseDir\launchers\02-prism-copy-instance.png"; Url = "https://prismlauncher.org/img/screenshots/CopyInstanceDark.png" },
    @{ Path = "$baseDir\launchers\03-prism-new-instance-technic.png"; Url = "https://prismlauncher.org/img/screenshots/NewInstanceTechnicDark.png" },
    @{ Path = "$baseDir\launchers\04-prism-settings-console.png"; Url = "https://prismlauncher.org/img/screenshots/SettingsLauncherConsoleDark.png" },
    @{ Path = "$baseDir\launchers\05-prism-new-instance-modrinth.png"; Url = "https://prismlauncher.org/img/screenshots/NewInstanceModrinthDark.png" },
    @{ Path = "$baseDir\launchers\06-prism-new-instance-vanilla.png"; Url = "https://prismlauncher.org/img/screenshots/NewInstanceVanillaDark.png" },
    @{ Path = "$baseDir\launchers\07-modrinth-homepage.png"; Url = "https://upload.wikimedia.org/wikipedia/commons/thumb/c/c1/Modrinth%27s_home_page.png/960px-Modrinth%27s_home_page.png" },
    @{ Path = "$baseDir\launchers\08-modrinth-search-opensource.png"; Url = "https://upload.wikimedia.org/wikipedia/commons/4/4e/Modrinth_search_open_source_%28cropped%29.png" },
    @{ Path = "$baseDir\launchers\09-lunar-store-coins.png"; Url = "https://a.storyblok.com/f/150231/1300x500/334fdcdc71/coins-purchase.png" },
    @{ Path = "$baseDir\launchers\10-lunar-download-grid.png"; Url = "https://cmsassets.lunarclientcdn.com/cdn-cgi/image/quality=85,format=auto/assets/edcac7cc5f-lunar-client-download-grid.png" },

    # --- Category 4: glass-ui ---
    @{ Path = "$baseDir\glass-ui\01-glasskit-intro-showcase.jpg"; Url = "https://raw.githubusercontent.com/JUNGHERZ/GlassKit/main/intro_screenshot.jpg" },
    @{ Path = "$baseDir\glass-ui\02-swiftui-glass-materials.png"; Url = "https://raw.githubusercontent.com/TheAppWizard/GlassmorphismSwiftUI/main/glassop.png" },
    @{ Path = "$baseDir\glass-ui\03-vscode-glass-windows.png"; Url = "https://raw.githubusercontent.com/KrautGTI/glass-theme-doyle/master/windows.png" },
    @{ Path = "$baseDir\glass-ui\04-vscode-glass-settings.png"; Url = "https://raw.githubusercontent.com/KrautGTI/glass-theme-doyle/master/windowsSettings.png" },
    @{ Path = "$baseDir\glass-ui\05-glass-switch-dashboard.png"; Url = "https://raw.githubusercontent.com/byte4geek/switch-dashboard/main/images/dashboard.png" },
    @{ Path = "$baseDir\glass-ui\06-dark-glass-refraction.jpg"; Url = "https://images.unsplash.com/photo-1618005182384-a83a8bd57fbe?w=1200&q=80" },
    @{ Path = "$baseDir\glass-ui\07-dark-frosted-geometry.jpg"; Url = "https://images.unsplash.com/photo-1634017839464-5c339ebe3cb4?w=1200&q=80" },
    @{ Path = "$baseDir\glass-ui\08-liquid-glass-flow.jpg"; Url = "https://images.unsplash.com/photo-1618005198919-d3d4b5a92ead?w=1200&q=80" },
    @{ Path = "$baseDir\glass-ui\09-crystal-iridescent-dark.jpg"; Url = "https://images.unsplash.com/photo-1550684848-fac1c5b4e853?w=1200&q=80" }
)

Write-Host ("Downloading " + $items.Count + " references...")
$success = 0
foreach ($item in $items) {
    try {
        Invoke-WebRequest -Uri $item.Url -OutFile $item.Path -UseBasicParsing -UserAgent "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" -TimeoutSec 15
        Write-Host ("OK: " + $item.Path)
        $success++
    } catch {
        Write-Host ("FAIL: " + $item.Url + " - " + $_.Exception.Message)
    }
}
Write-Host ("Completed: " + $success + " / " + $items.Count + " references downloaded.")


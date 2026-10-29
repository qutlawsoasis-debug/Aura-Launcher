package net.auramc.glass.client.quickmenu;

import com.mojang.blaze3d.systems.RenderSystem;
import net.auramc.glass.common.render.AuraRenderer;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.GlassPreset;
import net.auramc.glass.common.style.GlassStyles;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.rendering.v1.HudRenderCallback;
import net.fabricmc.fabric.api.event.player.AttackEntityCallback;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.entity.LivingEntity;
import net.minecraft.item.Items;
import net.minecraft.item.BowItem;
import net.minecraft.particle.DustParticleEffect;
import net.minecraft.particle.ParticleTypes;
import net.minecraft.util.ActionResult;
import net.minecraft.util.hit.HitResult;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.RaycastContext;
import org.joml.Quaternionf;
import org.joml.Vector3f;
import org.joml.Vector4f;
import java.util.ArrayDeque;
import java.util.Locale;

public final class QuickMenuVisuals {
    private static LivingEntity target;
    private static long targetUntil;
    private static float previousHealth;
    private static final ArrayDeque<NumberLabel> numbers = new ArrayDeque<>();
    private static long particleBursts, numberDraws, targetDraws, trajectoryDraws, cooldownDraws, tintedDraws;
    private record NumberLabel(LivingEntity entity, float delta, long began) {}
    private QuickMenuVisuals() {}
    public static long particleBursts() { return particleBursts; }
    public static long numberDraws() { return numberDraws; }
    public static long targetDraws() { return targetDraws; }
    public static long trajectoryDraws() { return trajectoryDraws; }
    public static long cooldownDraws() { return cooldownDraws; }
    public static long tintedDraws() { return tintedDraws; }
    public static void tinted() { tintedDraws++; }
    public static boolean tint(LivingEntity entity) {
        return QuickMenuConfig.get().hitTint && entity == target && entity.hurtTime > 0 && visible(entity);
    }
    public static void initialize() {
        AttackEntityCallback.EVENT.register((player, world, hand, entity, hit) -> {
            var client = MinecraftClient.getInstance();
            if (world.isClient && player == client.player && entity instanceof LivingEntity living && player.canSee(living)) recordHit(living);
            return ActionResult.PASS;
        });
        ClientTickEvents.END_CLIENT_TICK.register(client -> tick());
        HudRenderCallback.EVENT.register((context, delta) -> render(context));
    }
    public static void recordHit(LivingEntity living) {
        target = living; targetUntil = System.nanoTime() + 3_000_000_000L; previousHealth = living.getHealth();
        var c = QuickMenuConfig.get();
        var client = MinecraftClient.getInstance();
        if (!c.hitParticles || client.world == null || !visible(living)) return;
        int count = Math.max(1, Math.min(32, c.hitParticleCount));
        int color = c.hitParticleColor;
        var dust = new DustParticleEffect(new Vector3f(((color >> 16) & 255) / 255f, ((color >> 8) & 255) / 255f, (color & 255) / 255f), .8f);
        for (int i = 0; i < count; i++) {
            double angle = 2 * Math.PI * i / count;
            var type = c.hitParticleStyle == 0 ? ParticleTypes.CRIT : c.hitParticleStyle == 1 ? dust : ParticleTypes.HEART;
            var particle = client.particleManager.addParticle(type, living.getX() + Math.cos(angle) * living.getWidth() * .3,
                    living.getY() + living.getHeight() * .65, living.getZ() + Math.sin(angle) * living.getWidth() * .3,
                    Math.cos(angle) * .06, .06, Math.sin(angle) * .06);
            if (particle != null) particle.setColor(((color >> 16) & 255) / 255f, ((color >> 8) & 255) / 255f, (color & 255) / 255f);
        }
        particleBursts++;
    }
    private static boolean visible(LivingEntity living) {
        var client = MinecraftClient.getInstance();
        return living != null && client.player != null && living.getWorld() == client.world && !living.isRemoved()
                && !living.isInvisibleTo(client.player) && client.player.squaredDistanceTo(living) <= 256 && client.player.canSee(living);
    }
    private static void tick() {
        long now = System.nanoTime();
        var client = MinecraftClient.getInstance();
        if (client.world == null) { target = null; numbers.clear(); return; }
        if (target != null && now < targetUntil) {
            float current = target.getHealth();
            if (QuickMenuConfig.get().damageNumbers && Math.abs(current - previousHealth) > .01f && visible(target)) {
                if (numbers.size() >= 16) numbers.removeFirst();
                numbers.add(new NumberLabel(target, current - previousHealth, now));
            }
            previousHealth = current;
        } else target = null;
        numbers.removeIf(number -> now - number.began > 900_000_000L || !visible(number.entity));
    }
    private static void render(DrawContext context) {
        var client = MinecraftClient.getInstance();
        if (client.player == null || client.world == null || client.options.hudHidden || client.currentScreen != null) return;
        var c = QuickMenuConfig.get();
        if (!c.damageNumbers && !c.targetHud && !c.projectilePrediction && !c.cooldownHud) return;
        context.draw();
        float width = context.getScaledWindowWidth(), height = context.getScaledWindowHeight();
        if (c.targetHud && target != null && System.nanoTime() < targetUntil && visible(target)) {
            float x = width * .5f + 28, y = height * .5f + 34, w = Math.min(190, width - x - 8);
            var style = GlassStyles.get(GlassPreset.PANEL);
            style.radius = 12; style.bevel = 2; style.dispersion = 0; style.refraction = .2f; style.tintA = .92f;
            style.tintR = .025f; style.tintG = .035f; style.tintB = .05f;
            GlassRenderer.draw(x, y, w, 61, style, 1); GlassRenderer.flush();
            if (nativeBegin(context)) {
                String name = target.getName().getString();
                while (name.length() > 1 && AuraRenderer.textWidth(name, 15, 600) > w - 18) name = name.substring(0, name.length() - 1);
                AuraRenderer.text(name, x + 9, y + 7, 15, 0xFFFFFFFF, 600);
                AuraRenderer.text(String.format(Locale.ROOT, "%.1f / %.1f   Броня %d", target.getHealth(), target.getMaxHealth(), target.getArmor()), x + 9, y + 29, 12, 0xFFFFFFFF, 400);
                AuraRenderer.rect(x + 9, y + 49, w - 18, 3, 1.5f, 0, 0x50767680, 0);
                AuraRenderer.rect(x + 9, y + 49, (w - 18) * Math.max(0, Math.min(1, target.getHealth() / target.getMaxHealth())), 3, 1.5f, 0, 0xFF64D2FF, 0);
                AuraRenderer.flush(); targetDraws++;
            }
        }
        if (c.projectilePrediction) trajectory(context);
        if (c.damageNumbers && !numbers.isEmpty() && nativeBegin(context)) {
            for (var number : numbers) {
                if (!visible(number.entity)) continue;
                float age = (System.nanoTime() - number.began) / 900_000_000f;
                var entity = number.entity;
                float[] point = project(entity.getPos().add(0, entity.getHeight() + .25 + age * .5, 0), context);
                if (point == null) continue;
                String value = String.format(Locale.ROOT, "%s%.1f", number.delta > 0 ? "+" : "", number.delta);
                int color = (Math.round(255 * Math.max(0, 1 - age)) << 24) | (number.delta > 0 ? 0x5FD39A : 0xFF8A7A);
                AuraRenderer.text(value, point[0] - AuraRenderer.textWidth(value, 18, 600) * .5f, point[1], 18, color, 600);
                numberDraws++;
            }
            AuraRenderer.flush();
        }
        if (c.cooldownHud && nativeBegin(context)) {
            float y = height * .5f + 15;
            float shield = client.player.getItemCooldownManager().getCooldownProgress(Items.SHIELD, 0);
            float pearl = client.player.getItemCooldownManager().getCooldownProgress(Items.ENDER_PEARL, 0);
            if (shield > 0) { cooldown(context, width * .5f - 42, y, "Щит", shield); cooldownDraws++; }
            if (pearl > 0) { cooldown(context, width * .5f + 5, y, "Жемчуг", pearl); cooldownDraws++; }
            AuraRenderer.flush();
        }
    }
    private static void cooldown(DrawContext context, float x, float y, String name, float progress) {
        AuraRenderer.text(name, x, y, 10, 0xFFFFFFFF, 600);
        AuraRenderer.rect(x, y + 13, 35, 2, 1, 0, 0x60767680, 0);
        AuraRenderer.rect(x, y + 13, 35 * (1 - progress), 2, 1, 0, 0xFF64D2FF, 0);
    }
    private static boolean nativeBegin(DrawContext context) {
        return AuraRenderer.begin(context.getMatrices().peek().getPositionMatrix(), RenderSystem.getProjectionMatrix(), RenderSystem.getModelViewMatrix());
    }
    private static float[] project(Vec3d point, DrawContext context) {
        var client = MinecraftClient.getInstance();
        var camera = client.gameRenderer.getCamera();
        Vec3d relative = point.subtract(camera.getPos());
        Vector3f view = new Vector3f((float) relative.x, (float) relative.y, (float) relative.z);
        view.rotate(new Quaternionf(camera.getRotation()).conjugate());
        double fov = client.options.getFov().getValue() * QuickMenuEffects.zoomScale();
        Vector4f clip = new Vector4f(view, 1).mul(client.gameRenderer.getBasicProjectionMatrix(fov));
        if (clip.w <= 0 || Math.abs(clip.x) > clip.w || Math.abs(clip.y) > clip.w) return null;
        return new float[]{(clip.x / clip.w * .5f + .5f) * context.getScaledWindowWidth(), (.5f - clip.y / clip.w * .5f) * context.getScaledWindowHeight()};
    }
    private static void trajectory(DrawContext context) {
        var client = MinecraftClient.getInstance();
        var player = client.player;
        var stack = player.isUsingItem() ? player.getActiveItem() : player.getMainHandStack();
        if (!stack.isOf(Items.ENDER_PEARL) && !player.isUsingItem() && player.getOffHandStack().isOf(Items.ENDER_PEARL)) stack = player.getOffHandStack();
        double speed, gravity;
        if (stack.isOf(Items.BOW) && player.isUsingItem()) { speed = BowItem.getPullProgress(player.getItemUseTime()) * 3; gravity = .05; }
        else if (stack.isOf(Items.TRIDENT) && player.isUsingItem()) { speed = 2.5; gravity = .05; }
        else if (stack.isOf(Items.ENDER_PEARL)) { speed = 1.5; gravity = .03; }
        else return;
        if (speed < .1) return;
        Vec3d position = player.getEyePos().add(0, -.1, 0);
        Vec3d velocity = player.getRotationVec(1).multiply(speed).add(player.getVelocity().x, player.isOnGround() ? 0 : player.getVelocity().y, player.getVelocity().z);
        float[] previous = project(position, context);
        for (int step = 0; step < 100; step++) {
            Vec3d next = position.add(velocity);
            var hit = client.world.raycast(new RaycastContext(position, next, RaycastContext.ShapeType.COLLIDER, RaycastContext.FluidHandling.NONE, player));
            boolean stopped = hit.getType() != HitResult.Type.MISS;
            if (stopped) next = hit.getPos();
            float[] point = project(next, context);
            if (point != null && previous != null) {
                int pieces = Math.max(1, Math.min(24, (int) Math.hypot(point[0] - previous[0], point[1] - previous[1])));
                for (int i = 0; i <= pieces; i++) {
                    int x = Math.round(previous[0] + (point[0] - previous[0]) * i / pieces), y = Math.round(previous[1] + (point[1] - previous[1]) * i / pieces);
                    context.fill(x, y, x + 1, y + 1, 0xCC64D2FF);
                }
                trajectoryDraws++;
            }
            if (stopped) break;
            previous = point; position = next; velocity = velocity.multiply(.99).subtract(0, gravity, 0);
        }
        context.draw();
    }
}

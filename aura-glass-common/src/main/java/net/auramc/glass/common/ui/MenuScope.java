package net.auramc.glass.common.ui;

public final class MenuScope {
    private MenuScope() {}

    public static boolean applies(String className, String screenPackage, boolean ingame,
                                  boolean handled, boolean chat) {
        return !ingame && !handled && !chat && className.startsWith(screenPackage + ".");
    }
}

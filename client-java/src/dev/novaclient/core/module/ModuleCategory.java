package dev.novaclient.core.module;

/** Categories shown in the click GUI. All are intentionally empty in this release. */
public enum ModuleCategory {
    HUD("HUD"),
    VISUAL("Visual"),
    PERFORMANCE("Performance"),
    UTILITY("Utility"),
    SETTINGS("Settings");

    public final String displayName;

    ModuleCategory(String displayName) {
        this.displayName = displayName;
    }
}

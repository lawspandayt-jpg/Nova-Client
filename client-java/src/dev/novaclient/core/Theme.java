package dev.novaclient.core;

/**
 * Original color themes for the click GUI. The accent color is user-selectable separately.
 */
public final class Theme {

    public final String name;
    public final int background;   // window body
    public final int header;       // window header
    public final int panel;        // controls / inputs
    public final int border;
    public final int text;
    public final int textDim;

    private Theme(String name, int background, int header, int panel, int border, int text, int textDim) {
        this.name = name;
        this.background = background;
        this.header = header;
        this.panel = panel;
        this.border = border;
        this.text = text;
        this.textDim = textDim;
    }

    public static final Theme[] ALL = {
            new Theme("Nova Dark", 0xF0171A21, 0xF01D212B, 0xFF232834, 0xFF2C3240, 0xFFE9EBF1, 0xFF9AA1B0),
            new Theme("Midnight", 0xF00D0F17, 0xF0121522, 0xFF1A1E2E, 0xFF232A3F, 0xFFE6E8F0, 0xFF8B93A8),
            new Theme("Slate", 0xF01C1F26, 0xF0242830, 0xFF2B303A, 0xFF363C48, 0xFFEDEFF2, 0xFFA5ACB8),
    };

    public static Theme byName(String name) {
        for (Theme theme : ALL) {
            if (theme.name.equals(name)) return theme;
        }
        return ALL[0];
    }

    public static Theme next(Theme current) {
        for (int i = 0; i < ALL.length; i++) {
            if (ALL[i] == current) return ALL[(i + 1) % ALL.length];
        }
        return ALL[0];
    }

    /** Accent presets offered in the GUI (any #RRGGBB also works via settings file). */
    public static final int[] ACCENTS = {
            0xFF7C5CFF, // violet
            0xFF3E8EF7, // blue
            0xFF2BC4A8, // teal
            0xFF52C468, // green
            0xFFE5534B, // red
            0xFFF2A33C, // orange
    };

    public static int parseAccent(String hex, int fallback) {
        try {
            return 0xFF000000 | Integer.parseInt(hex.substring(1), 16);
        } catch (Exception e) {
            return fallback;
        }
    }

    public static String accentToHex(int argb) {
        return String.format("#%06X", argb & 0xFFFFFF);
    }
}

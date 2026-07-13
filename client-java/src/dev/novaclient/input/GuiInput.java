package dev.novaclient.input;

import java.util.ArrayDeque;
import java.util.Queue;

/**
 * Input events diverted to the click GUI while it is open (produced by InputBridge on the client
 * thread, consumed by the GUI on the same thread — one frame at most later).
 */
public final class GuiInput {

    public static final class KeyEvent {
        public final int key;
        public final char character;
        public final boolean pressed;

        KeyEvent(int key, char character, boolean pressed) {
            this.key = key;
            this.character = character;
            this.pressed = pressed;
        }
    }

    public static final class MouseEvent {
        public final int button;      // -1 for move/wheel-only events
        public final boolean pressed;
        public final int x, y;        // window pixels, origin bottom-left (LWJGL convention)
        public final int wheel;

        MouseEvent(int button, boolean pressed, int x, int y, int wheel) {
            this.button = button;
            this.pressed = pressed;
            this.x = x;
            this.y = y;
            this.wheel = wheel;
        }
    }

    private static final Queue<KeyEvent> keys = new ArrayDeque<KeyEvent>();
    private static final Queue<MouseEvent> mouse = new ArrayDeque<MouseEvent>();

    private GuiInput() {
    }

    static void pushKey(int key, char character, boolean pressed) {
        if (keys.size() < 256) keys.add(new KeyEvent(key, character, pressed));
    }

    static void pushMouse(int button, boolean pressed, int x, int y, int wheel) {
        if (mouse.size() < 256) mouse.add(new MouseEvent(button, pressed, x, y, wheel));
    }

    public static KeyEvent pollKey() {
        return keys.poll();
    }

    public static MouseEvent pollMouse() {
        return mouse.poll();
    }

    public static void clear() {
        keys.clear();
        mouse.clear();
    }
}

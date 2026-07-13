package dev.novaclient.input;

import dev.novaclient.render.FrameHook;

import java.lang.invoke.MethodHandle;
import java.lang.invoke.MethodHandles;
import java.lang.invoke.MethodType;
import java.util.ArrayDeque;

/**
 * The single place where instrumented LWJGL calls arrive (see LwjglTransformer). While the click
 * GUI is CLOSED every method is a pass-through to the renamed original ("*$nova"). While it is
 * OPEN, the game is fed "no input" — exactly what it experiences when its window loses focus:
 *
 *  - real keyboard/mouse events are diverted to {@link GuiInput} for the GUI,
 *  - one synthetic release event is queued for every key/button held at open time, so vanilla
 *    KeyBindings un-press cleanly (no stuck sprint/walk),
 *  - state polls (isKeyDown/isButtonDown/getDX/getDY) report idle,
 *  - the cursor is un-grabbed; grab requests made by the game meanwhile are recorded and applied
 *    when the GUI closes.
 *
 * Everything runs on the client/render thread (LWJGL input is polled from Display.update()).
 */
public final class InputBridge {

    private InputBridge() {
    }

    private static volatile boolean guiOpen;

    // Synthetic release events fed to the game right after the GUI opens.
    private static final ArrayDeque<int[]> synthKeys = new ArrayDeque<int[]>();    // {keyCode}
    private static final ArrayDeque<int[]> synthButtons = new ArrayDeque<int[]>(); // {button}
    private static int[] currentSynthKey;
    private static int[] currentSynthButton;
    private static boolean synthKeyActive;
    private static boolean synthButtonActive;

    /** What the game believes the grab state is (applied for real once the GUI closes). */
    private static boolean desiredGrab;

    // ------------------------------------------------------------------ MethodHandles to originals

    private static MethodHandle KB_NEXT, KB_IS_KEY_DOWN, KB_EVENT_KEY, KB_EVENT_CHAR, KB_EVENT_STATE;
    private static MethodHandle MS_NEXT, MS_IS_BUTTON_DOWN, MS_EVENT_BUTTON, MS_EVENT_STATE,
            MS_EVENT_X, MS_EVENT_Y, MS_EVENT_DX, MS_EVENT_DY, MS_EVENT_DWHEEL,
            MS_DX, MS_DY, MS_DWHEEL, MS_IS_GRABBED, MS_SET_GRABBED;
    private static MethodHandle DISPLAY_UPDATE;
    private static boolean resolved;

    private static void ensureResolved() {
        if (resolved) return;
        try {
            MethodHandles.Lookup lookup = MethodHandles.publicLookup();
            Class<?> kb = Class.forName("org.lwjgl.input.Keyboard");
            Class<?> ms = Class.forName("org.lwjgl.input.Mouse");
            Class<?> dp = Class.forName("org.lwjgl.opengl.Display");

            KB_NEXT = lookup.findStatic(kb, "next$nova", MethodType.methodType(boolean.class));
            KB_IS_KEY_DOWN = lookup.findStatic(kb, "isKeyDown$nova", MethodType.methodType(boolean.class, int.class));
            KB_EVENT_KEY = lookup.findStatic(kb, "getEventKey$nova", MethodType.methodType(int.class));
            KB_EVENT_CHAR = lookup.findStatic(kb, "getEventCharacter$nova", MethodType.methodType(char.class));
            KB_EVENT_STATE = lookup.findStatic(kb, "getEventKeyState$nova", MethodType.methodType(boolean.class));

            MS_NEXT = lookup.findStatic(ms, "next$nova", MethodType.methodType(boolean.class));
            MS_IS_BUTTON_DOWN = lookup.findStatic(ms, "isButtonDown$nova", MethodType.methodType(boolean.class, int.class));
            MS_EVENT_BUTTON = lookup.findStatic(ms, "getEventButton$nova", MethodType.methodType(int.class));
            MS_EVENT_STATE = lookup.findStatic(ms, "getEventButtonState$nova", MethodType.methodType(boolean.class));
            MS_EVENT_X = lookup.findStatic(ms, "getEventX$nova", MethodType.methodType(int.class));
            MS_EVENT_Y = lookup.findStatic(ms, "getEventY$nova", MethodType.methodType(int.class));
            MS_EVENT_DX = lookup.findStatic(ms, "getEventDX$nova", MethodType.methodType(int.class));
            MS_EVENT_DY = lookup.findStatic(ms, "getEventDY$nova", MethodType.methodType(int.class));
            MS_EVENT_DWHEEL = lookup.findStatic(ms, "getEventDWheel$nova", MethodType.methodType(int.class));
            MS_DX = lookup.findStatic(ms, "getDX$nova", MethodType.methodType(int.class));
            MS_DY = lookup.findStatic(ms, "getDY$nova", MethodType.methodType(int.class));
            MS_DWHEEL = lookup.findStatic(ms, "getDWheel$nova", MethodType.methodType(int.class));
            MS_IS_GRABBED = lookup.findStatic(ms, "isGrabbed$nova", MethodType.methodType(boolean.class));
            MS_SET_GRABBED = lookup.findStatic(ms, "setGrabbed$nova", MethodType.methodType(void.class, boolean.class));

            DISPLAY_UPDATE = lookup.findStatic(dp, "update$nova", MethodType.methodType(void.class));
            resolved = true;
        } catch (Throwable t) {
            throw new IllegalStateException("[NovaClient] Could not bind instrumented LWJGL methods", t);
        }
    }

    // ------------------------------------------------------------------ GUI open/close

    public static boolean isGuiOpen() {
        return guiOpen;
    }

    public static void openGui() {
        if (guiOpen) return;
        ensureResolved();
        try {
            desiredGrab = (boolean) MS_IS_GRABBED.invoke();
            MS_SET_GRABBED.invoke(false);
            // Queue release events for everything currently held so the game un-presses cleanly.
            for (int key = 1; key < 224; key++) {
                if ((boolean) KB_IS_KEY_DOWN.invoke(key)) synthKeys.add(new int[]{key});
            }
            for (int button = 0; button < 3; button++) {
                if ((boolean) MS_IS_BUTTON_DOWN.invoke(button)) synthButtons.add(new int[]{button});
            }
        } catch (Throwable t) {
            log(t);
        }
        GuiInput.clear();
        guiOpen = true;
    }

    public static void closeGui() {
        if (!guiOpen) return;
        guiOpen = false;
        try {
            MS_SET_GRABBED.invoke(desiredGrab); // return mouse control to Minecraft
        } catch (Throwable t) {
            log(t);
        }
        synthKeys.clear();
        synthButtons.clear();
        synthKeyActive = false;
        synthButtonActive = false;
        GuiInput.clear();
    }

    // ------------------------------------------------------------------ real state for the GUI

    public static boolean realIsKeyDown(int key) {
        ensureResolved();
        try {
            return (boolean) KB_IS_KEY_DOWN.invoke(key);
        } catch (Throwable t) {
            log(t);
            return false;
        }
    }

    public static boolean realIsGrabbed() {
        ensureResolved();
        try {
            return (boolean) MS_IS_GRABBED.invoke();
        } catch (Throwable t) {
            log(t);
            return false;
        }
    }

    // ------------------------------------------------------------------ bridges: Display

    public static void displayUpdate() {
        ensureResolved();
        try {
            FrameHook.onFrame();
        } catch (Throwable t) {
            log(t); // the overlay must never crash the game
        }
        try {
            DISPLAY_UPDATE.invoke();
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    // ------------------------------------------------------------------ bridges: Keyboard

    public static boolean kbNext() {
        ensureResolved();
        try {
            if (!guiOpen) {
                synthKeyActive = false;
                return (boolean) KB_NEXT.invoke();
            }
            while ((boolean) KB_NEXT.invoke()) {
                GuiInput.pushKey((int) KB_EVENT_KEY.invoke(), (char) KB_EVENT_CHAR.invoke(),
                        (boolean) KB_EVENT_STATE.invoke());
            }
            currentSynthKey = synthKeys.poll();
            synthKeyActive = currentSynthKey != null;
            return synthKeyActive;
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static boolean kbIsKeyDown(int key) {
        if (guiOpen) return false;
        ensureResolved();
        try {
            return (boolean) KB_IS_KEY_DOWN.invoke(key);
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static int kbGetEventKey() {
        if (synthKeyActive) return currentSynthKey[0];
        ensureResolved();
        try {
            return (int) KB_EVENT_KEY.invoke();
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static char kbGetEventCharacter() {
        if (synthKeyActive) return '\0';
        ensureResolved();
        try {
            return (char) KB_EVENT_CHAR.invoke();
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static boolean kbGetEventKeyState() {
        if (synthKeyActive) return false; // synthetic events are always releases
        ensureResolved();
        try {
            return (boolean) KB_EVENT_STATE.invoke();
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    // ------------------------------------------------------------------ bridges: Mouse

    public static boolean msNext() {
        ensureResolved();
        try {
            if (!guiOpen) {
                synthButtonActive = false;
                return (boolean) MS_NEXT.invoke();
            }
            while ((boolean) MS_NEXT.invoke()) {
                GuiInput.pushMouse((int) MS_EVENT_BUTTON.invoke(), (boolean) MS_EVENT_STATE.invoke(),
                        (int) MS_EVENT_X.invoke(), (int) MS_EVENT_Y.invoke(), (int) MS_EVENT_DWHEEL.invoke());
            }
            currentSynthButton = synthButtons.poll();
            synthButtonActive = currentSynthButton != null;
            return synthButtonActive;
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static boolean msIsButtonDown(int button) {
        if (guiOpen) return false;
        ensureResolved();
        try {
            return (boolean) MS_IS_BUTTON_DOWN.invoke(button);
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static int msGetEventButton() {
        if (synthButtonActive) return currentSynthButton[0];
        ensureResolved();
        try {
            return (int) MS_EVENT_BUTTON.invoke();
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static boolean msGetEventButtonState() {
        if (synthButtonActive) return false;
        ensureResolved();
        try {
            return (boolean) MS_EVENT_STATE.invoke();
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static int msGetEventX() {
        if (synthButtonActive) return 0;
        ensureResolved();
        try {
            return (int) MS_EVENT_X.invoke();
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static int msGetEventY() {
        if (synthButtonActive) return 0;
        ensureResolved();
        try {
            return (int) MS_EVENT_Y.invoke();
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static int msGetEventDX() {
        if (guiOpen) return 0;
        ensureResolved();
        try {
            return (int) MS_EVENT_DX.invoke();
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static int msGetEventDY() {
        if (guiOpen) return 0;
        ensureResolved();
        try {
            return (int) MS_EVENT_DY.invoke();
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static int msGetEventDWheel() {
        if (guiOpen) return 0;
        ensureResolved();
        try {
            return (int) MS_EVENT_DWHEEL.invoke();
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static int msGetDX() {
        ensureResolved();
        try {
            int real = (int) MS_DX.invoke(); // always drain so deltas don't accumulate
            return guiOpen ? 0 : real;
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static int msGetDY() {
        ensureResolved();
        try {
            int real = (int) MS_DY.invoke();
            return guiOpen ? 0 : real;
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static int msGetDWheel() {
        ensureResolved();
        try {
            int real = (int) MS_DWHEEL.invoke();
            return guiOpen ? 0 : real;
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static boolean msIsGrabbed() {
        if (guiOpen) return desiredGrab; // the game sees the state it asked for
        ensureResolved();
        try {
            return (boolean) MS_IS_GRABBED.invoke();
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    public static void msSetGrabbed(boolean grab) {
        ensureResolved();
        if (guiOpen) {
            desiredGrab = grab; // applied for real when the GUI closes
            return;
        }
        try {
            MS_SET_GRABBED.invoke(grab);
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static boolean loggedOnce;

    private static void log(Throwable t) {
        if (!loggedOnce) {
            loggedOnce = true;
            System.err.println("[NovaClient] Input bridge error (logged once): " + t);
            t.printStackTrace();
        }
    }

    private static RuntimeException rethrow(Throwable t) {
        if (t instanceof RuntimeException) return (RuntimeException) t;
        if (t instanceof Error) throw (Error) t;
        return new RuntimeException(t);
    }
}

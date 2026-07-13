package dev.novaclient.bootstrap;

import org.objectweb.asm.ClassReader;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.Opcodes;
import org.objectweb.asm.Type;
import org.objectweb.asm.tree.ClassNode;
import org.objectweb.asm.tree.InsnList;
import org.objectweb.asm.tree.InsnNode;
import org.objectweb.asm.tree.MethodInsnNode;
import org.objectweb.asm.tree.MethodNode;
import org.objectweb.asm.tree.VarInsnNode;

import java.lang.instrument.ClassFileTransformer;
import java.security.ProtectionDomain;
import java.util.HashMap;
import java.util.Map;

/**
 * The complete list of transformations Nova Client performs. Every entry is a mechanical
 * "rename + delegate" patch:
 *
 *   original:  public static boolean next() { ... }
 *   becomes:   public static boolean next$nova() { ... }          (unchanged body)
 *              public static boolean next() { return InputBridge.kbNext(); }
 *
 * The bridge methods (in {@link dev.novaclient.input.InputBridge}) call the renamed originals
 * through MethodHandles, so ALL behavior lives in reviewable plain Java — the bytecode edit
 * itself contains no logic. Scope:
 *
 *   org.lwjgl.opengl.Display.update()          → per-frame render callback (GUI overlay)
 *   org.lwjgl.input.Keyboard (events/state)    → input gate while the client GUI is open
 *   org.lwjgl.input.Mouse    (events/state)    → input gate + cursor grab management
 *
 * When the GUI is closed, every bridge is a straight pass-through to the original method.
 */
public final class LwjglTransformer implements ClassFileTransformer {

    private static final String BRIDGE = "dev/novaclient/input/InputBridge";
    private static final String SUFFIX = "$nova";

    /** class internal name → (methodName + descriptor → bridge method name) */
    private static final Map<String, Map<String, String>> TARGETS = new HashMap<String, Map<String, String>>();

    static {
        Map<String, String> display = new HashMap<String, String>();
        display.put("update()V", "displayUpdate");
        TARGETS.put("org/lwjgl/opengl/Display", display);

        Map<String, String> keyboard = new HashMap<String, String>();
        keyboard.put("next()Z", "kbNext");
        keyboard.put("isKeyDown(I)Z", "kbIsKeyDown");
        keyboard.put("getEventKey()I", "kbGetEventKey");
        keyboard.put("getEventCharacter()C", "kbGetEventCharacter");
        keyboard.put("getEventKeyState()Z", "kbGetEventKeyState");
        TARGETS.put("org/lwjgl/input/Keyboard", keyboard);

        Map<String, String> mouse = new HashMap<String, String>();
        mouse.put("next()Z", "msNext");
        mouse.put("isButtonDown(I)Z", "msIsButtonDown");
        mouse.put("getEventButton()I", "msGetEventButton");
        mouse.put("getEventButtonState()Z", "msGetEventButtonState");
        mouse.put("getEventX()I", "msGetEventX");
        mouse.put("getEventY()I", "msGetEventY");
        mouse.put("getEventDX()I", "msGetEventDX");
        mouse.put("getEventDY()I", "msGetEventDY");
        mouse.put("getEventDWheel()I", "msGetEventDWheel");
        mouse.put("getDX()I", "msGetDX");
        mouse.put("getDY()I", "msGetDY");
        mouse.put("getDWheel()I", "msGetDWheel");
        mouse.put("isGrabbed()Z", "msIsGrabbed");
        mouse.put("setGrabbed(Z)V", "msSetGrabbed");
        TARGETS.put("org/lwjgl/input/Mouse", mouse);
    }

    @Override
    public byte[] transform(ClassLoader loader, String className, Class<?> classBeingRedefined,
                            ProtectionDomain protectionDomain, byte[] classfileBuffer) {
        Map<String, String> methods = TARGETS.get(className);
        if (methods == null) {
            return null; // not a target — leave every other class untouched
        }
        try {
            return patch(classfileBuffer, methods);
        } catch (Throwable t) {
            System.err.println("[NovaClient] Failed to instrument " + className + ": " + t);
            return null; // fail open: the game keeps running without the client GUI
        }
    }

    private static byte[] patch(byte[] bytes, Map<String, String> methods) {
        ClassNode node = new ClassNode();
        new ClassReader(bytes).accept(node, 0);

        Map<MethodNode, String> renamed = new HashMap<MethodNode, String>();
        for (MethodNode method : node.methods) {
            String bridgeName = methods.get(method.name + method.desc);
            if (bridgeName != null && (method.access & Opcodes.ACC_STATIC) != 0) {
                renamed.put(method, method.name);
                method.name = method.name + SUFFIX;
            }
        }

        for (Map.Entry<MethodNode, String> entry : renamed.entrySet()) {
            MethodNode original = entry.getKey();
            String publicName = entry.getValue();
            String bridgeName = methods.get(publicName + original.desc);

            MethodNode wrapper = new MethodNode(
                    Opcodes.ACC_PUBLIC | Opcodes.ACC_STATIC, publicName, original.desc, null, null);
            InsnList body = new InsnList();
            Type[] args = Type.getArgumentTypes(original.desc);
            int slot = 0;
            for (Type arg : args) {
                body.add(new VarInsnNode(arg.getOpcode(Opcodes.ILOAD), slot));
                slot += arg.getSize();
            }
            body.add(new MethodInsnNode(Opcodes.INVOKESTATIC, BRIDGE, bridgeName, original.desc, false));
            body.add(new InsnNode(Type.getReturnType(original.desc).getOpcode(Opcodes.IRETURN)));
            wrapper.instructions = body;
            node.methods.add(wrapper);
        }

        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_MAXS);
        node.accept(writer);
        return writer.toByteArray();
    }
}

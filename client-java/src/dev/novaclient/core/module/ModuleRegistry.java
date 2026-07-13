package dev.novaclient.core.module;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

/**
 * Registry for client modules. Intentionally EMPTY in this release: the framework (registration,
 * categories, search, keybinds, per-frame dispatch) is complete and tested, but no modules are
 * registered — and no hidden ones exist anywhere in the codebase.
 */
public final class ModuleRegistry {

    private final List<Module> modules = new ArrayList<Module>();

    public void register(Module module) {
        modules.add(module);
    }

    public List<Module> all() {
        return Collections.unmodifiableList(modules);
    }

    public List<Module> byCategory(ModuleCategory category) {
        List<Module> result = new ArrayList<Module>();
        for (Module module : modules) {
            if (module.getCategory() == category) result.add(module);
        }
        return result;
    }

    public List<Module> search(String query) {
        List<Module> result = new ArrayList<Module>();
        String needle = query.toLowerCase(Locale.ROOT).trim();
        for (Module module : modules) {
            if (module.getName().toLowerCase(Locale.ROOT).contains(needle)) result.add(module);
        }
        return result;
    }

    /** Dispatches a key press to module keybinds (none exist yet). */
    public void onKeyPressed(int key) {
        for (Module module : modules) {
            if (module.getKeybind() == key && key != 0) module.toggle();
        }
    }

    public void onFrame(int width, int height) {
        for (Module module : modules) {
            if (module.isEnabled()) module.onFrame(width, height);
        }
    }
}

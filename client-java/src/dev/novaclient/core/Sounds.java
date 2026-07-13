package dev.novaclient.core;

import javax.sound.sampled.AudioFormat;
import javax.sound.sampled.AudioSystem;
import javax.sound.sampled.Clip;
import javax.sound.sampled.FloatControl;

/**
 * Generates the GUI click sound programmatically (short decaying sine burst) — no copied assets.
 */
public final class Sounds {

    private static byte[] clickPcm;
    private static AudioFormat format;
    private static boolean unavailable;

    private Sounds() {
    }

    private static void ensureGenerated() {
        if (clickPcm != null || unavailable) return;
        int sampleRate = 44100;
        int samples = sampleRate * 28 / 1000; // 28 ms
        clickPcm = new byte[samples * 2];
        for (int i = 0; i < samples; i++) {
            double t = i / (double) sampleRate;
            double envelope = Math.exp(-t * 90.0);
            double value = Math.sin(2 * Math.PI * 1350 * t) * envelope * 0.6;
            short sample = (short) (value * Short.MAX_VALUE);
            clickPcm[i * 2] = (byte) (sample & 0xFF);
            clickPcm[i * 2 + 1] = (byte) ((sample >> 8) & 0xFF);
        }
        format = new AudioFormat(sampleRate, 16, 1, true, false);
    }

    /** volume: 0..1. Plays asynchronously; failures are silent (sound is cosmetic). */
    public static void click(float volume) {
        if (volume <= 0f || unavailable) return;
        try {
            ensureGenerated();
            Clip clip = AudioSystem.getClip();
            clip.open(format, clickPcm, 0, clickPcm.length);
            if (clip.isControlSupported(FloatControl.Type.MASTER_GAIN)) {
                FloatControl gain = (FloatControl) clip.getControl(FloatControl.Type.MASTER_GAIN);
                float db = (float) (20.0 * Math.log10(Math.max(0.02, volume)));
                gain.setValue(Math.max(gain.getMinimum(), Math.min(gain.getMaximum(), db)));
            }
            clip.addLineListener(event -> {
                if (event.getType() == javax.sound.sampled.LineEvent.Type.STOP) clip.close();
            });
            clip.start();
        } catch (Throwable t) {
            unavailable = true;
        }
    }
}

using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class ShakerSynth : MonoBehaviour
{
    [Range(0f, 1f)] public float level = 0f;
    [Range(200f, 6000f)] public float centerHz = 2500f;
    [Range(0.80f, 0.999f)] public float resonance = 0.96f;
    [Range(0.90f, 0.9999f)] public float energyDecay = 0.995f;
    [Tooltip("Hareket eşiği (Drive'a gelen büyüklük biriminde). Bu değerin altı sessiz.")]
    [Range(0f, 500f)] public float startThreshold = 25f;
    [Tooltip("Eşikten önce uygulanan giriş kazancı. Gyro birimini ölçeklemek için.")]
    [Range(0.01f, 200f)] public float inputGain = 1f;
    [Header("Axis Thresholds (per‑axis offset)")]
    public bool useAxisThresholds = true;
    public Vector3 axisThreshold = new Vector3(80f, 80f, 80f);
    [Header("Roll/Oscillation")]
    [Range(0f, 2f)] public float rollLevel = 2f;
    [Range(40f, 600f)] public float rollFreqHz = 128f;
    [Range(0.85f, 0.999f)] public float rollResonance = 0.9869f;
    public bool onlyRoll = true;
    [Header("Reverb")]
    public bool enableReverb = true;
    public AudioReverbPreset reverbPreset = AudioReverbPreset.Hallway;
    [Header("Delay")]
    public bool enableDelay = true;
    [Range(10f, 5000f)] public float delayMs = 470f;
    [Range(0f, 1f)] public float delayDecay = 0.272f;
    [Range(0f, 1f)] public float delayWet = 0.5f;
    [Range(0f, 1f)] public float delayDry = 1f;

    float energy, prevSpeed;
    float prevFilteredSpeed;
    float a0, a1, a2, b1, b2, x1, x2, y1, y2;
    AudioSource audioSource;
    uint noiseState;
    // roll resonator state
    float r_a1, r_a2, r_gain, r_y1, r_y2;
    int sampleRate;
    AudioReverbFilter reverb;
    AudioEchoFilter echo;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = true;
        audioSource.loop = true;
        if (audioSource.clip == null)
        {
            sampleRate = AudioSettings.outputSampleRate;
            var silent = AudioClip.Create("ShakerSilence", sampleRate, 1, sampleRate, false);
            var zeros = new float[sampleRate];
            silent.SetData(zeros, 0);
            audioSource.clip = silent;
        }
        if (!audioSource.isPlaying) audioSource.Play();

        noiseState = (uint)System.Environment.TickCount;
        SetupReverb();
        SetupDelay();
        RecalcFilter();
    }
    void OnValidate() { if (Application.isPlaying) { SetupReverb(); SetupDelay(); RecalcFilter(); } }

    void SetupReverb()
    {
        if (reverb == null) reverb = GetComponent<AudioReverbFilter>();
        if (enableReverb)
        {
            if (reverb == null) reverb = gameObject.AddComponent<AudioReverbFilter>();
            reverb.enabled = true;
            reverb.reverbPreset = reverbPreset;
        }
        else if (reverb != null)
        {
            reverb.enabled = false;
        }
    }

    void SetupDelay()
    {
        if (echo == null) echo = GetComponent<AudioEchoFilter>();
        if (enableDelay)
        {
            if (echo == null) echo = gameObject.AddComponent<AudioEchoFilter>();
            echo.enabled = true;
            echo.delay = delayMs;
            echo.decayRatio = delayDecay;
            echo.wetMix = delayWet;
            echo.dryMix = delayDry;
        }
        else if (echo != null)
        {
            echo.enabled = false;
        }
    }

    public void Drive(Vector3 gyroRadPerSec)
    {
        Vector3 v = gyroRadPerSec * inputGain;
        float baseMag;
        if (useAxisThresholds)
        {
            float fx = Mathf.Max(0f, Mathf.Abs(v.x) - axisThreshold.x*1000);
            float fy = Mathf.Max(0f, Mathf.Abs(v.y) - axisThreshold.y*1000);
            float fz = Mathf.Max(0f, Mathf.Abs(v.z) - axisThreshold.z*1000);
            baseMag = Mathf.Sqrt(fx * fx + fy * fy + fz * fz);
        }
        else
        {
            baseMag = v.magnitude;
        }
        // apply global startThreshold AFTER axis thresholds so UI value always effective
        float filtered = Mathf.Max(0f, baseMag - startThreshold);
        float jerk = (filtered - prevFilteredSpeed) / Mathf.Max(Time.deltaTime, 1e-4f);
        prevSpeed = baseMag;
        prevFilteredSpeed = filtered;

        if (filtered > 0f)
            energy += filtered * 0.002f + Mathf.Max(0f, Mathf.Abs(jerk)) * 0.0002f;
        energy = Mathf.Clamp01(energy);
    }

    void RecalcFilter()
    {
        if (sampleRate == 0) sampleRate = AudioSettings.outputSampleRate;
        float fs = sampleRate;
        float w0 = 2f * Mathf.PI * Mathf.Clamp(centerHz, 50f, fs * 0.45f) / fs;
        float cosw0 = Mathf.Cos(w0);
        float alpha = Mathf.Sin(w0) / (2f * Mathf.Lerp(0.05f, 10f, 1f - resonance));

        float b0 = alpha, b1n = 0f, b2n = -alpha;
        float a0n = 1f + alpha, a1n = -2f * cosw0, a2n = 1f - alpha;

        a0 = b0 / a0n; a1 = b1n / a0n; a2 = b2n / a0n; b1 = a1n / a0n; b2 = a2n / a0n;

        // roll resonator coefficients
        float clampedFreq = Mathf.Clamp(rollFreqHz, 40f, fs * 0.45f);
        float wRoll = 2f * Mathf.PI * clampedFreq / fs;
        float r = Mathf.Clamp(rollResonance, 0.85f, 0.999f);
        r_a1 = 2f * r * Mathf.Cos(wRoll);
        r_a2 = -r * r;
        r_gain = 1f - r; // excitation gain
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        float e = energy;
        for (int i = 0; i < data.Length; i += channels)
        {
            e *= energyDecay;
            // PRNG (xorshift32) to avoid using UnityEngine.Random on audio thread
            noiseState ^= (noiseState << 13);
            noiseState ^= (noiseState >> 17);
            noiseState ^= (noiseState << 5);
            float white = ((noiseState / (float)uint.MaxValue) * 2f - 1f) * e * level;

            float y = a0 * white + a1 * x1 + a2 * x2 - b1 * y1 - b2 * y2;
            x2 = x1; x1 = white; y2 = y1; y1 = y;

            // probabilistic impacts excite a low-mid resonator to imitate rolling/oscillation
            // probability grows with energy
            noiseState ^= (noiseState << 13); // advance PRNG again
            float u = (noiseState / (float)uint.MaxValue);
            float impactsPerSec = Mathf.Lerp(2f, 45f, e);
            float p = impactsPerSec / (float)sampleRate;
            float impulse = (u < p) ? ((u * 2f - 1f) * e) : 0f;
            float roll = r_gain * impulse + r_a1 * r_y1 + r_a2 * r_y2;
            r_y2 = r_y1; r_y1 = roll;

            float friction = y;
            float sample = (onlyRoll ? 0f : friction) + roll * rollLevel;
            for (int c = 0; c < channels; c++) data[i + c] += sample;
        }
        energy = Mathf.Clamp01(e);
    }
}

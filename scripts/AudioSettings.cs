using Godot;

namespace MpFoundation;

/// <summary>
/// Persisted audio settings — master volume, voice output volume, and the microphone input
/// device — in a new [audio] section of the shared user://settings.cfg, following the
/// DisplaySettings pattern exactly (Load at boot, Apply once buses exist, Set* writes through).
/// Before this existed, the settings panel wrote these straight to AudioServer and every
/// launch silently reset them — the mic regression defeated the picker's whole purpose (the
/// OS default input device is frequently wrong on Windows).
/// </summary>
public static class AudioSettings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "audio";

    /// <summary>Master bus volume, linear 0..1 (stored linear; converted to dB on apply).</summary>
    public static float MasterVolume { get; private set; } = 1.0f;

    /// <summary>Voice output bus volume, linear 0..1.</summary>
    public static float VoiceVolume { get; private set; } = 1.0f;

    /// <summary>Ambient group bus volume, linear 0..1. Added alongside the ambient bed: a
    /// continuous layer the player cannot turn down is a support ticket, and unlike the SFX
    /// one-shots it plays without stopping for the whole session.</summary>
    public static float AmbientVolume { get; private set; } = 1.0f;

    /// <summary>Preferred microphone device name; "" means the OS default.</summary>
    public static string MicDevice { get; private set; } = "";

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok)
            return;
        // Type-checked reads, same rationale as DisplaySettings: a wrong-typed value in a
        // hand-edited cfg must fall back to the default, never throw during boot.
        Variant master = cfg.GetValue(Section, "master_volume", 1.0f);
        if (master.VariantType is Variant.Type.Float or Variant.Type.Int)
            MasterVolume = Mathf.Clamp((float)master.AsDouble(), 0f, 1f);
        Variant voice = cfg.GetValue(Section, "voice_volume", 1.0f);
        if (voice.VariantType is Variant.Type.Float or Variant.Type.Int)
            VoiceVolume = Mathf.Clamp((float)voice.AsDouble(), 0f, 1f);
        Variant ambient = cfg.GetValue(Section, "ambient_volume", 1.0f);
        if (ambient.VariantType is Variant.Type.Float or Variant.Type.Int)
            AmbientVolume = Mathf.Clamp((float)ambient.AsDouble(), 0f, 1f);
        Variant mic = cfg.GetValue(Section, "mic_device", "");
        if (mic.VariantType == Variant.Type.String)
            MicDevice = mic.AsString();
    }

    /// <summary>Applies the loaded values to the live audio buses/devices. Master applies
    /// unconditionally; the voice bus is created on demand by the caller passing its index;
    /// the mic device applies only if it is still present on this machine (an unplugged USB
    /// mic degrades to the OS default rather than a dead capture device).</summary>
    public static void Apply(int masterBusIndex, int voiceBusIndex, int ambientBusIndex = -1)
    {
        AudioServer.SetBusVolumeDb(masterBusIndex, Mathf.LinearToDb(MasterVolume));
        if (voiceBusIndex >= 0)
            AudioServer.SetBusVolumeDb(voiceBusIndex, Mathf.LinearToDb(VoiceVolume));
        if (ambientBusIndex >= 0)
            AudioServer.SetBusVolumeDb(ambientBusIndex, Mathf.LinearToDb(AmbientVolume));
        if (MicDevice.Length > 0 && System.Array.IndexOf(AudioServer.GetInputDeviceList(), MicDevice) >= 0)
            AudioServer.InputDevice = MicDevice;
    }

    public static void SetMasterVolume(float linear)
    {
        MasterVolume = Mathf.Clamp(linear, 0f, 1f);
        Save();
    }

    public static void SetVoiceVolume(float linear)
    {
        VoiceVolume = Mathf.Clamp(linear, 0f, 1f);
        Save();
    }

    public static void SetAmbientVolume(float linear)
    {
        AmbientVolume = Mathf.Clamp(linear, 0f, 1f);
        Save();
    }

    public static void SetMicDevice(string device)
    {
        MicDevice = device;
        Save();
    }

    private static void Save()
    {
        var cfg = new ConfigFile();
        cfg.Load(Path); // keep any other sections
        cfg.SetValue(Section, "master_volume", MasterVolume);
        cfg.SetValue(Section, "voice_volume", VoiceVolume);
        cfg.SetValue(Section, "ambient_volume", AmbientVolume);
        cfg.SetValue(Section, "mic_device", MicDevice);
        cfg.Save(Path);
    }
}

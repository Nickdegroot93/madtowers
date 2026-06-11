using UnityEditor;
using UnityEngine;

// Music tracks under Assets/Audio/Music stream from disk instead of being decompressed
// fully into memory - minutes of stereo audio would otherwise cost tens of MB of RAM on
// the phone. Short SFX keep Unity's defaults (decompress on load - lowest latency).
public sealed class AudioImportSettings : AssetPostprocessor
{
    // Bump whenever the import logic changes so already-imported clips reimport.
    public override uint GetVersion() => 1;

    private void OnPreprocessAudio()
    {
        string path = assetPath.Replace('\\', '/');
        if (!path.Contains("/Audio/Music/")) return;

        AudioImporter importer = (AudioImporter)assetImporter;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.Streaming;
        importer.defaultSampleSettings = settings;
    }
}

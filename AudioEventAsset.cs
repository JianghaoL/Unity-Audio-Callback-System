using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(fileName = "AudioEventAsset", menuName = "Scriptable Objects/AudioEventAsset")]
public class AudioEventAsset : ScriptableObject
{
    [Header("Audio")]
    public AudioClip clip;

    [Space(10)] 
    
    [Range(0f, 1f)] public float volume = 1f;
    [Range(0f, 2f)] public float pitch = 1f;
    public bool loop;
    [Range(0f, 1f)] public float spatialBlend = 1f;
    public AudioMixerGroup audioMixerGroup;

    [Space(15)] [Header("Events")]
    [Tooltip("Allow events to be retriggered when looping")] public bool retrigger = false;
    public List<EventMarker> markers;

    /// <summary>
    /// Checks if any marker time exceeds the clip length and logs an error if so.
    /// </summary>
    private void OnEnable()
    {
        foreach (var marker in markers)
        {
            if (marker.time > clip.length)
                Debug.LogError($"AudioEventAsset : [{marker.name}] marker time must be less than clip length. [Length = {clip.length}]");
        }
    }
}

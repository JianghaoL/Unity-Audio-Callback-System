using System;
using UnityEngine;
using UnityEngine.Events;

[Serializable]

/// <summary>
/// Represents an event marker that can be placed on an audio clip.
/// Each marker has a name, a time (in seconds)
///  at which the event should be triggered, 
/// and a UnityEvent that will be invoked when the marker is reached during audio playback.
/// </summary>
public class EventMarker
{
    public string name;
    public float time;
    public UnityEvent onEvent;
}

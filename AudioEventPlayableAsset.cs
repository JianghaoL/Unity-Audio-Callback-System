using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[System.Serializable]
public class AudioEventPlayableAsset : PlayableAsset, ITimelineClipAsset
{
    public AudioEventAsset audioEvent;

    [System.NonSerialized] public AudioEventPlayer boundPlayer;

    /// <summary>
    /// Creates a playable that will play the audio event. The behaviour is set up with the audio event and the bound player reference.
    /// </summary>
    /// <param name="graph"></param>
    /// <param name="owner"></param>
    /// <returns></returns>
    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<AudioEventPlayableBehaviour>.Create(graph);
        AudioEventPlayableBehaviour behaviour = playable.GetBehaviour();
        behaviour.audioEvent = audioEvent;
        behaviour.player = boundPlayer;
        return playable;
    }

    public ClipCaps clipCaps => ClipCaps.None;
}

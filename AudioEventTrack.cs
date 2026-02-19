using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[TrackBindingType(typeof(AudioEventPlayer))]
[TrackClipType(typeof(AudioEventPlayableAsset))]
public class AudioEventTrack : TrackAsset
{

    /// <summary>
    /// Creates a track mixer and binds the AudioEventPlayer to the clips in the track. 
    /// It retrieves the PlayableDirector from the PlayableGraph and gets the bound AudioEventPlayer.
    ///  Then it iterates through the clips in the track and sets the bound player reference in each
    /// </summary>
    /// <param name="graph"></param>
    /// <param name="go"></param>
    /// <param name="inputCount"></param>
    /// <returns></returns>
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        var director = graph.GetResolver() as PlayableDirector;
        AudioEventPlayer player = director != null
            ? director.GetGenericBinding(this) as AudioEventPlayer
            : null;

        foreach (var clip in GetClips())
        {
            var asset = clip.asset as AudioEventPlayableAsset;
            if (asset != null)
                asset.boundPlayer = player;
        }

        return base.CreateTrackMixer(graph, go, inputCount);
    }
}

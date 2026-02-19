using System.Diagnostics;
using UnityEngine;
using UnityEngine.Playables;

// A behaviour that is attached to a playable
public class AudioEventPlayableBehaviour : PlayableBehaviour
{
    public AudioEventAsset audioEvent;
    public AudioEventPlayer player;

    private bool _started = false;
    
    // Called when the owning graph starts playing
    public override void OnGraphStart(Playable playable)
    {
        
    }

    // Called when the owning graph stops playing
    public override void OnGraphStop(Playable playable)
    {
        
    }

    // Called when the state of the playable is set to Play
    public override void OnBehaviourPlay(Playable playable, FrameData info)
    {
        if (!player || !audioEvent) return;
        
        player.Play();
        _started = true;
    }

    // Called when the state of the playable is set to Paused
    public override void OnBehaviourPause(Playable playable, FrameData info)
    {
        if (!_started || !player) return;
        
        player.Stop();
        _started = false;
    }

    // Called each frame while the state is set to Play
    public override void PrepareFrame(Playable playable, FrameData info)
    {
        
    }
}

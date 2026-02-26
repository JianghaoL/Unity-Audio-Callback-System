using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(AudioSource))]
public class AudioEventPlayer : MonoBehaviour
{
    [SerializeField] private AudioEventAsset asset;

    private AudioSource _audioSource;
    
    
    // Audio Event Trigger Settings

    private double _startDspTime;
    private bool _isPlaying;
    private int _markerIndex;
    private double _loopTimer;
    
    private Dictionary<string, UnityEvent> _events;
    private ConcurrentQueue<EventMarker> _pendingMarkers;
    
    /// <summary>
    /// Initializes the audio source with the parameters from the asset and sets up the event dictionary and pending marker queue. 
    /// Also checks if the asset is null and logs an error if so.
    /// </summary>
    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();

        try
        {
            if (asset == null) throw new Exception("AudioEventPlayer : Asset must not be null!");
            
            SetParameter(asset);

            _isPlaying = false;
            _startDspTime = 0;
            _markerIndex = 0;
            _loopTimer = 0;
            
            InitializeDictionary();
            _pendingMarkers = new ConcurrentQueue<EventMarker>();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    /// <summary>
    /// Checks if there are any pending markers to invoke and invokes them.
    ///  Also checks if the audio is still playing and updates the loop timer if the clip is set to loop and retrigger events.
    /// </summary>
    private void Update()
    {
        while (_pendingMarkers.TryDequeue(out var toInvoke))
        {
            toInvoke.onEvent.Invoke();
        }
        
        if (!_audioSource.isPlaying) _isPlaying = false;

        if (!asset.loop) return;
        if (!asset.retrigger) return;

        _loopTimer = AudioSettings.dspTime - _startDspTime;
        if (_loopTimer > asset.clip.length) // end of the loop
        {
            _startDspTime = AudioSettings.dspTime;
            _markerIndex = 0;
        }
    }

    /// <summary>
    /// Checks if the audio is playing and if so,
    /// checks if any markers should be triggered based on the elapsed time.
    ///  If a marker should be triggered, it is added to the pending marker queue.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="channels"></param>
    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (!_isPlaying) return;

        // Calculate how long the audio has been playing
        double elapsedTime = AudioSettings.dspTime - _startDspTime;

        // Check every marker to see if an event should be invoked
        while (_markerIndex < asset.markers.Count
               && asset.markers[_markerIndex].time <= elapsedTime)
        {
            EventMarker toInvoke = asset.markers[_markerIndex];
            _pendingMarkers.Enqueue(toInvoke);
            _markerIndex ++;
        }
    }




    /// <summary>
    /// Starts playing the audio clip and sets the start DSP time. 
    /// If the audio is already playing, it will be stopped and restarted.
    /// </summary>
    public void Play()
    {
        if (_isPlaying) Stop();
        
        double dspStartTime = AudioSettings.dspTime + Mathf.Epsilon;
        _audioSource.PlayScheduled(dspStartTime);
        _startDspTime = dspStartTime;
        _isPlaying = true;
        _markerIndex = 0;
    }


    /// <summary>
    /// Stops the audio and resets the playing state. 
    /// If the audio is not playing, this method does nothing.
    /// </summary>
    public void Stop()
    {
        _isPlaying = false;
        _audioSource.Stop();
    }


    /// <summary>
    /// Gets the UnityEvent associated with the given marker name. 
    /// If the marker name does not exist, this will throw a KeyNotFoundException.
    /// </summary>
    /// <param name="markerName"> The name of the marker to retrieve the event for. </param>
    /// <returns></returns>
    public UnityEvent GetEventByName(string markerName)
    {
        InitializeDictionary();
        return _events[markerName];
    }
    
    
    
    
    
    
    /// <summary>
    /// Sets the audio source parameters based on the given AudioEventAsset. 
    /// This includes the clip, volume, pitch, loop setting, spatial blend, and output audio mixer group.
    /// </summary>
    /// <param name="newAsset"> The AudioEventAsset to set the parameters from. </param>
    private void SetParameter(AudioEventAsset newAsset)
    {
        _audioSource.clip = newAsset.clip;
        _audioSource.volume = newAsset.volume;
        _audioSource.pitch = newAsset.pitch;
        _audioSource.loop = newAsset.loop;
        _audioSource.spatialBlend = newAsset.spatialBlend;
        _audioSource.outputAudioMixerGroup = newAsset.audioMixerGroup;
    }
    
    /// <summary>
    /// Initializes the event dictionary by iterating through the markers in the asset 
    /// and adding their names and associated UnityEvents to the dictionary. 
    /// If the dictionary is already initialized, this method does nothing.
    /// </summary>
    private void InitializeDictionary()
    {
        if (_events != null) return;
        
        _events = new Dictionary<string, UnityEvent>();
        foreach (var marker in asset.markers)
        {
            if (!_events.ContainsKey(marker.name))
            {
                _events.Add(marker.name, marker.onEvent);
            }
        }
    }
}

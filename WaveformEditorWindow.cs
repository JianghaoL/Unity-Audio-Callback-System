using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Interactive Audio Waveform Editor window for AudioEventAsset.
/// Provides waveform visualization, draggable/selectable event markers
/// with inline UnityEvent editing, audio preview with a playback cursor,
/// and zoom/scroll navigation drawn inside the waveform panel.
///
/// Architecture (all in one file for editor tooling convenience):
///   - WaveformEditorWindow : main EditorWindow — layout, lifecycle, coordination
///   - WaveformRenderer     : generates the waveform Texture2D from clip data
///   - MarkerInteraction    : draws markers, handles hover/drag/select/add input
///   - AudioPreview         : plays/stops AudioClip preview via reflection
/// </summary>
public class WaveformEditorWindow : EditorWindow
{
    // ─── Asset Binding ─────────────────────────────────────
    AudioEventAsset eventAsset;
    SerializedObject so;
    SerializedProperty markersProp;

    // ─── Waveform Cache ────────────────────────────────────
    Texture2D waveformTex;
    int cachedWidth;
    float cachedZoom;
    float cachedScroll;
    float cachedClipLength;

    // ─── Navigation ────────────────────────────────────────
    float zoom = 1f;          // 1x – 10x
    float scrollOffset = 0f;  // 0..maxScroll (normalised)

    // ─── Marker State ──────────────────────────────────────
    int draggedMarkerIndex = -1;
    int hoveredMarkerIndex = -1;
    int selectedMarkerIndex = -1;   // marker whose UnityEvents are shown
    const float MARKER_HIT_PX = 6f;

    // ─── Playback Cursor ───────────────────────────────────
    double playbackStartEditorTime;  // EditorApplication.timeSinceStartup when Play was pressed
    bool wasPlaying;                 // track transitions to reset timer
    float lastCursorTime = -1f;      // persisted cursor position after stopping

    // ─── Scroll for marker list and event editor ───────────
    Vector2 bottomScrollPos;

    // ─── Waveform Layout ───────────────────────────────────
    const int WAVEFORM_HEIGHT = 160;
    const int SCROLLBAR_HEIGHT = 16;

    // ─── UI Padding ────────────────────────────────────────
    const float PAD = 8f;   // horizontal/vertical padding from window edges

    // ─── Styles (lazy-init) ────────────────────────────────
    GUIStyle markerLabelStyle;
    GUIStyle headerStyle;
    GUIStyle selectedMarkerLabelStyle;

    // ════════════════════════════════════════════════════════
    //  Window Lifecycle
    // ════════════════════════════════════════════════════════

    [MenuItem("Tools/Audio Event Waveform Editor")]
    static void OpenWindow()
    {
        GetWindow<WaveformEditorWindow>("Audio Event Waveform Editor");
    }

    void OnEnable()
    {
        BindAsset(Selection.activeObject as AudioEventAsset);
    }

    void OnDisable()
    {
        AudioPreview.Stop();
    }

    void OnSelectionChange()
    {
        if (Selection.activeObject is AudioEventAsset sel && sel != eventAsset)
        {
            BindAsset(sel);
            Repaint();
        }
    }

    void Update()
    {
        // Continuously repaint while audio is playing so the cursor moves
        if (AudioPreview.IsPlaying())
            Repaint();
    }

    // ════════════════════════════════════════════════════════
    //  Asset Binding
    // ════════════════════════════════════════════════════════

    /// <summary>Binds the window to a new AudioEventAsset (or null).</summary>
    void BindAsset(AudioEventAsset asset)
    {
        eventAsset = asset;
        if (eventAsset != null)
        {
            so = new SerializedObject(eventAsset);
            markersProp = so.FindProperty("markers");
        }
        else
        {
            so = null;
            markersProp = null;
        }

        waveformTex = null;
        draggedMarkerIndex = -1;
        hoveredMarkerIndex = -1;
        selectedMarkerIndex = -1;
        lastCursorTime = -1f;
    }

    // ════════════════════════════════════════════════════════
    //  Main OnGUI
    // ════════════════════════════════════════════════════════

    void OnGUI()
    {
        EnsureStyles();

        if (eventAsset == null)
        {
            EditorGUILayout.HelpBox(
                "Select an AudioEventAsset in the Project window to begin editing.",
                MessageType.Info);
            return;
        }

        so.Update();

        // ── Outer padding area ──
        EditorGUILayout.BeginVertical();
        GUILayout.Space(PAD);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(PAD);
        EditorGUILayout.BeginVertical();

        DrawAssetHeader();
        DrawPlaybackAndZoomRow();
        DrawWaveformArea();
        DrawHorizontalScrollSlider();

        EditorGUILayout.Space(4);

        // Bottom panel: marker list + selected marker event editor (scrollable)
        bottomScrollPos = EditorGUILayout.BeginScrollView(bottomScrollPos);
        DrawMarkerList();
        DrawSelectedMarkerEvents();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
        GUILayout.Space(PAD);
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(PAD);
        EditorGUILayout.EndVertical();

        so.ApplyModifiedProperties();
    }

    // ════════════════════════════════════════════════════════
    //  GUI Sections
    // ════════════════════════════════════════════════════════

    void EnsureStyles()
    {
        if (markerLabelStyle == null)
        {
            markerLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Color.yellow },
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };
        }

        if (selectedMarkerLabelStyle == null)
        {
            selectedMarkerLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Color.cyan },
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };
        }

        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        }
    }

    /// <summary>Asset reference field + clip selector.</summary>
    void DrawAssetHeader()
    {
        EditorGUILayout.LabelField(eventAsset.name, headerStyle);
        EditorGUILayout.Space(2);

        EditorGUI.BeginChangeCheck();
        eventAsset.clip = (AudioClip)EditorGUILayout.ObjectField(
            "AudioClip", eventAsset.clip, typeof(AudioClip), false);
        if (EditorGUI.EndChangeCheck())
        {
            waveformTex = null;          // clip changed – force regeneration
            AudioPreview.Stop();
        }

        EditorGUILayout.Space(4);
    }

    /// <summary>
    /// Single row: Preview Audio button (left) + time readout + Zoom slider (right).
    /// </summary>
    void DrawPlaybackAndZoomRow()
    {
        if (eventAsset.clip == null) return;

        bool isPlaying = AudioPreview.IsPlaying();

        // Track playback start time on play-start transition
        if (isPlaying && !wasPlaying)
            playbackStartEditorTime = EditorApplication.timeSinceStartup;

        // When playback stops, freeze cursor at last known time
        if (!isPlaying && wasPlaying && eventAsset.clip != null)
        {
            float elapsed = (float)(EditorApplication.timeSinceStartup - playbackStartEditorTime);
            lastCursorTime = Mathf.Repeat(elapsed, eventAsset.clip.length);
        }

        wasPlaying = isPlaying;

        EditorGUILayout.BeginHorizontal(GUILayout.Height(28));

        // ── Preview button ──
        string label = isPlaying ? "\u25A0  Stop Preview" : "\u25B6  Preview Audio";
        if (GUILayout.Button(label, GUILayout.Height(24), GUILayout.Width(140)))
        {
            if (isPlaying)
            {
                // Freeze cursor before stopping
                float elapsed = (float)(EditorApplication.timeSinceStartup - playbackStartEditorTime);
                lastCursorTime = Mathf.Repeat(elapsed, eventAsset.clip.length);
                AudioPreview.Stop();
            }
            else
            {
                AudioPreview.Play(eventAsset.clip);
                playbackStartEditorTime = EditorApplication.timeSinceStartup;
                lastCursorTime = -1f;   // will be driven by live playback
            }
        }

        // ── Time readout ──
        if (isPlaying)
        {
            float elapsed = (float)(EditorApplication.timeSinceStartup - playbackStartEditorTime);
            float clipLen = eventAsset.clip.length;
            elapsed = Mathf.Repeat(elapsed, clipLen);
            EditorGUILayout.LabelField(
                $"{elapsed:F2}s / {clipLen:F2}s",
                EditorStyles.miniLabel, GUILayout.Width(110));
        }
        else if (lastCursorTime >= 0f)
        {
            float clipLen = eventAsset.clip.length;
            EditorGUILayout.LabelField(
                $"{lastCursorTime:F2}s / {clipLen:F2}s  (stopped)",
                EditorStyles.miniLabel, GUILayout.Width(150));
        }

        GUILayout.FlexibleSpace();

        // ── Zoom slider (right side) ──
        EditorGUILayout.LabelField("Zoom", GUILayout.Width(36));
        EditorGUI.BeginChangeCheck();
        zoom = GUILayout.HorizontalSlider(zoom, 1f, 10f, GUILayout.Width(130));
        if (EditorGUI.EndChangeCheck()) waveformTex = null;
        EditorGUILayout.LabelField($"{zoom:F1}x", GUILayout.Width(34));

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(2);
    }

    /// <summary>
    /// Horizontal scroll slider drawn directly below the waveform,
    /// inside the window and visually attached to the waveform area.
    /// </summary>
    void DrawHorizontalScrollSlider()
    {
        if (eventAsset.clip == null) return;

        float maxScroll = Mathf.Max(0f, 1f - 1f / zoom);
        // GUI.HorizontalScrollbar gives a thumb-sized scrollbar
        float contentWidth = position.width - PAD * 2;
        Rect sliderRect = GUILayoutUtility.GetRect(contentWidth, SCROLLBAR_HEIGHT);
        EditorGUI.BeginChangeCheck();
        float thumbSize = 1f / zoom;  // visible fraction
        scrollOffset = GUI.HorizontalScrollbar(
            sliderRect,
            Mathf.Clamp(scrollOffset, 0f, maxScroll),
            thumbSize,
            0f,
            1f);
        if (EditorGUI.EndChangeCheck()) waveformTex = null;
    }

    /// <summary>Waveform texture + time scale + marker overlay + playback cursor + input.</summary>
    void DrawWaveformArea()
    {
        if (eventAsset.clip == null)
        {
            EditorGUILayout.HelpBox("Assign an AudioClip above to display the waveform.", MessageType.None);
            return;
        }

        // ── Regenerate texture when parameters change ──
        int w = Mathf.Max(1, (int)(position.width - PAD * 2));
        if (waveformTex == null
            || cachedWidth != w
            || !Mathf.Approximately(cachedZoom, zoom)
            || !Mathf.Approximately(cachedScroll, scrollOffset)
            || !Mathf.Approximately(cachedClipLength, eventAsset.clip.length))
        {
            waveformTex = WaveformRenderer.Generate(
                eventAsset.clip, w, WAVEFORM_HEIGHT, zoom, scrollOffset);
            cachedWidth = w;
            cachedZoom = zoom;
            cachedScroll = scrollOffset;
            cachedClipLength = eventAsset.clip.length;
        }

        // ── Draw waveform ──
        float waveWidth = position.width - PAD * 2;
        Rect waveRect = GUILayoutUtility.GetRect(waveWidth, WAVEFORM_HEIGHT);
        GUI.DrawTexture(waveRect, waveformTex, ScaleMode.StretchToFill);

        // ── Time scale ticks ──
        DrawTimeScale(waveRect);

        // ── Playback cursor ──
        DrawPlaybackCursor(waveRect);

        // ── Markers: draw, hover, drag, select ──
        MarkerInteraction.DrawAndHandleDrag(
            waveRect, eventAsset, so, markersProp,
            zoom, scrollOffset, markerLabelStyle, selectedMarkerLabelStyle,
            ref draggedMarkerIndex, ref hoveredMarkerIndex,
            ref selectedMarkerIndex);

        // ── Click empty space to add marker ──
        MarkerInteraction.HandleAddMarker(
            waveRect, eventAsset, so, markersProp,
            zoom, scrollOffset, hoveredMarkerIndex, draggedMarkerIndex,
            ref selectedMarkerIndex);

        // Keep repainting while dragging for smooth feedback
        if (draggedMarkerIndex >= 0) Repaint();
    }

    /// <summary>
    /// Draws a playback position cursor on the waveform.
    /// While playing it tracks real-time position; after stopping
    /// it remains at the last playback position.
    /// </summary>
    void DrawPlaybackCursor(Rect waveRect)
    {
        float cursorTime;
        bool isLive = AudioPreview.IsPlaying();

        if (isLive)
        {
            float elapsed = (float)(EditorApplication.timeSinceStartup - playbackStartEditorTime);
            cursorTime = Mathf.Repeat(elapsed, eventAsset.clip.length);
        }
        else if (lastCursorTime >= 0f)
        {
            cursorTime = lastCursorTime;
        }
        else
        {
            return;  // no cursor to show
        }

        float clipLen  = eventAsset.clip.length;
        float visStart = scrollOffset * clipLen;
        float visDur   = clipLen / zoom;
        float nx       = (cursorTime - visStart) / visDur;

        if (nx < 0f || nx > 1f) return;  // cursor outside visible range

        float x = waveRect.x + nx * waveRect.width;

        // Cursor line colour: bright while playing, dimmed when stopped
        float alpha = isLive ? 0.95f : 0.55f;
        Handles.color = new Color(1f, 1f, 1f, alpha);
        Handles.DrawLine(new Vector3(x, waveRect.y), new Vector3(x, waveRect.yMax));
        Handles.DrawLine(new Vector3(x + 1, waveRect.y), new Vector3(x + 1, waveRect.yMax));

        // Small triangle at top
        Handles.color = isLive ? Color.white : new Color(0.8f, 0.8f, 0.8f, 0.6f);
        Vector3[] tri = new Vector3[]
        {
            new Vector3(x - 4, waveRect.y),
            new Vector3(x + 4, waveRect.y),
            new Vector3(x, waveRect.y + 7),
        };
        Handles.DrawAAConvexPolygon(tri);
    }

    /// <summary>Draws faint time labels along the bottom of the waveform.</summary>
    void DrawTimeScale(Rect waveRect)
    {
        float clipLen = eventAsset.clip.length;
        float visStart = scrollOffset * clipLen;
        float visDur   = clipLen / zoom;

        float tick = ChooseTickInterval(visDur);
        Handles.color = new Color(1f, 1f, 1f, 0.25f);

        float t = Mathf.Ceil(visStart / tick) * tick;
        while (t <= visStart + visDur)
        {
            float nx = (t - visStart) / visDur;
            float x  = waveRect.x + nx * waveRect.width;
            Handles.DrawLine(
                new Vector3(x, waveRect.yMax - 14),
                new Vector3(x, waveRect.yMax));
            GUI.Label(
                new Rect(x + 2, waveRect.yMax - 15, 50, 14),
                $"{t:F2}s", EditorStyles.miniLabel);
            t += tick;
        }
    }

    static float ChooseTickInterval(float duration)
    {
        float[] intervals = { 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 30f, 60f };
        foreach (float iv in intervals)
            if (duration / iv <= 14) return iv;
        return 60f;
    }

    /// <summary>
    /// Inspector-like list of markers with name, time, select, and delete.
    /// Clicking a row selects that marker for UnityEvent editing.
    /// </summary>
    void DrawMarkerList()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Event Markers", EditorStyles.boldLabel);

        if (markersProp.arraySize == 0)
        {
            EditorGUILayout.HelpBox(
                "No markers yet. Click on the waveform to add one.", MessageType.None);
            return;
        }

        // Clamp selected index in case markers were deleted
        if (selectedMarkerIndex >= markersProp.arraySize)
            selectedMarkerIndex = -1;

        for (int i = 0; i < markersProp.arraySize; i++)
        {
            var elem     = markersProp.GetArrayElementAtIndex(i);
            var nameProp = elem.FindPropertyRelative("name");
            var timeProp = elem.FindPropertyRelative("time");

            bool isSelected  = (i == selectedMarkerIndex);
            bool isHovered   = (i == hoveredMarkerIndex || i == draggedMarkerIndex);

            // Row background colour
            if (isSelected)
                GUI.backgroundColor = new Color(0.3f, 0.7f, 1f, 0.5f);
            else if (isHovered)
                GUI.backgroundColor = new Color(1f, 0.85f, 0.2f, 0.5f);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            // Select button / indicator
            string selectLabel = isSelected ? "\u25C6" : $"#{i}";
            if (GUILayout.Button(selectLabel, EditorStyles.miniButton, GUILayout.Width(28)))
            {
                selectedMarkerIndex = isSelected ? -1 : i;  // toggle
            }

            EditorGUILayout.PropertyField(nameProp, GUIContent.none, GUILayout.MinWidth(90));
            EditorGUILayout.PropertyField(timeProp, GUIContent.none, GUILayout.Width(70));

            if (GUILayout.Button("Delete", EditorStyles.miniButton, GUILayout.Width(52)))
            {
                Undo.RecordObject(eventAsset, "Delete Marker");
                if (selectedMarkerIndex == i) selectedMarkerIndex = -1;
                else if (selectedMarkerIndex > i) selectedMarkerIndex--;
                markersProp.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedProperties();
                GUIUtility.ExitGUI();   // list changed – safe exit
            }

            EditorGUILayout.EndHorizontal();
            GUI.backgroundColor = Color.white;
        }
    }

    /// <summary>
    /// When a marker is selected, draws its full details including
    /// the UnityEvent (onEvent) inline so the user can wire callbacks.
    /// </summary>
    void DrawSelectedMarkerEvents()
    {
        if (selectedMarkerIndex < 0 || selectedMarkerIndex >= markersProp.arraySize)
            return;

        var elem      = markersProp.GetArrayElementAtIndex(selectedMarkerIndex);
        var nameProp  = elem.FindPropertyRelative("name");
        var timeProp  = elem.FindPropertyRelative("time");
        var eventProp = elem.FindPropertyRelative("onEvent");

        string markerName = nameProp.stringValue;
        if (string.IsNullOrEmpty(markerName)) markerName = $"Marker {selectedMarkerIndex}";

        EditorGUILayout.Space(8);

        // ── Header ──
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(
            $"\u25C6  Selected Marker: {markerName}",
            EditorStyles.boldLabel);
        EditorGUILayout.Space(2);

        // Name and time fields (editable)
        EditorGUILayout.PropertyField(nameProp, new GUIContent("Name"));
        EditorGUILayout.PropertyField(timeProp, new GUIContent("Time (s)"));

        EditorGUILayout.Space(4);

        // ── UnityEvent editor ──
        if (eventProp != null)
        {
            EditorGUILayout.LabelField("UnityEvent (onEvent)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(eventProp, GUIContent.none, true);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "No 'onEvent' property found on this marker.", MessageType.Warning);
        }

        EditorGUILayout.EndVertical();
    }

    // ════════════════════════════════════════════════════════
    //  WAVEFORM RENDERER
    //  Generates a Texture2D showing the visible portion of
    //  the audio clip based on current zoom and scroll.
    // ════════════════════════════════════════════════════════

    static class WaveformRenderer
    {
        /// <summary>
        /// Generates a waveform texture for the visible window defined
        /// by <paramref name="zoom"/> and <paramref name="scrollNorm"/>.
        /// </summary>
        public static Texture2D Generate(
            AudioClip clip, int width, int height,
            float zoom, float scrollNorm)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

            // Background fill
            var bg = new Color(0.14f, 0.14f, 0.14f, 1f);
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;
            tex.SetPixels(pixels);

            // Centre-line
            int cy = height / 2;
            Color centreLine = new Color(0.3f, 0.3f, 0.3f, 1f);
            for (int x = 0; x < width; x++)
                tex.SetPixel(x, cy, centreLine);

            // Read sample data
            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            // Visible sample range
            int totalSamples   = samples.Length;
            int visibleSamples = Mathf.CeilToInt(totalSamples / zoom);
            int sampleStart    = Mathf.FloorToInt(scrollNorm * totalSamples);
            int sampleEnd      = Mathf.Min(totalSamples, sampleStart + visibleSamples);
            int visCount       = sampleEnd - sampleStart;
            if (visCount <= 0) { tex.Apply(); return tex; }

            float step = (float)visCount / width;

            Color waveColor = new Color(0.2f, 0.9f, 0.3f, 1f);
            for (int x = 0; x < width; x++)
            {
                int start = sampleStart + Mathf.FloorToInt(x * step);
                int end   = Mathf.Min(sampleStart + Mathf.CeilToInt((x + 1) * step), totalSamples);

                float min = 0f, max = 0f;
                for (int i = start; i < end; i++)
                {
                    float s = samples[i];
                    if (s < min) min = s;
                    if (s > max) max = s;
                }

                int yMin = Mathf.Clamp(Mathf.FloorToInt((min + 1f) * 0.5f * height), 0, height - 1);
                int yMax = Mathf.Clamp(Mathf.CeilToInt ((max + 1f) * 0.5f * height), 0, height - 1);

                for (int y = yMin; y <= yMax; y++)
                    tex.SetPixel(x, y, waveColor);
            }

            tex.Apply();
            return tex;
        }
    }

    // ════════════════════════════════════════════════════════
    //  MARKER INTERACTION
    //  Handles drawing of marker lines + labels, hover
    //  detection, drag-to-reposition, selection, and
    //  click-to-add.  All changes are wrapped in Undo calls.
    // ════════════════════════════════════════════════════════

    static class MarkerInteraction
    {
        /// <summary>
        /// Draws every visible marker as a vertical line with its name label,
        /// detects hover, processes drag, and handles selection on click.
        /// Selected markers are drawn with a distinct colour.
        /// </summary>
        public static void DrawAndHandleDrag(
            Rect waveRect,
            AudioEventAsset asset,
            SerializedObject so,
            SerializedProperty markersProp,
            float zoom, float scrollNorm,
            GUIStyle labelStyle,
            GUIStyle selectedLabelStyle,
            ref int draggedIndex,
            ref int hoveredIndex,
            ref int selectedIndex)
        {
            if (asset.clip == null || markersProp == null) return;

            float clipLen  = asset.clip.length;
            float visStart = scrollNorm * clipLen;
            float visDur   = clipLen / zoom;
            Event evt      = Event.current;

            hoveredIndex = -1;

            // ── Draw each marker & detect hover ──
            for (int i = 0; i < markersProp.arraySize; i++)
            {
                var elem    = markersProp.GetArrayElementAtIndex(i);
                float time  = elem.FindPropertyRelative("time").floatValue;
                string name = elem.FindPropertyRelative("name").stringValue;

                float nx = (time - visStart) / visDur;
                if (nx < -0.02f || nx > 1.02f) continue;  // off-screen

                float x = waveRect.x + nx * waveRect.width;

                // Determine marker visual state
                bool isSelected = (i == selectedIndex);
                bool isDragged  = (i == draggedIndex);

                Color lineColor;
                if (isDragged)       lineColor = Color.yellow;
                else if (isSelected) lineColor = Color.cyan;
                else                 lineColor = Color.red;

                // Marker line (drawn twice for thickness)
                Handles.color = lineColor;
                Handles.DrawLine(
                    new Vector3(x, waveRect.y),
                    new Vector3(x, waveRect.yMax));
                Handles.DrawLine(
                    new Vector3(x + 1, waveRect.y),
                    new Vector3(x + 1, waveRect.yMax));

                // Small diamond at bottom for selected marker
                if (isSelected)
                {
                    Handles.color = Color.cyan;
                    Vector3[] diamond = new Vector3[]
                    {
                        new Vector3(x, waveRect.yMax - 8),
                        new Vector3(x - 5, waveRect.yMax),
                        new Vector3(x, waveRect.yMax + 2),
                        new Vector3(x + 5, waveRect.yMax),
                    };
                    Handles.DrawAAConvexPolygon(diamond);
                }

                // Marker name label
                string displayName = string.IsNullOrEmpty(name) ? $"Marker {i}" : name;
                GUIStyle style = isSelected ? selectedLabelStyle : labelStyle;
                Vector2 sz = style.CalcSize(new GUIContent(displayName));
                Rect lblRect = new Rect(x + 4, waveRect.y + 2 + (i % 3) * 14, sz.x, sz.y);
                GUI.Label(lblRect, displayName, style);

                // Hover detection
                bool nearX = Mathf.Abs(evt.mousePosition.x - x) <= MARKER_HIT_PX;
                if (nearX && waveRect.Contains(evt.mousePosition))
                {
                    hoveredIndex = i;
                    EditorGUIUtility.AddCursorRect(
                        new Rect(x - MARKER_HIT_PX, waveRect.y,
                                 MARKER_HIT_PX * 2, waveRect.height),
                        MouseCursor.ResizeHorizontal);
                }
            }

            // ── Drag start (and select) ──
            if (evt.type == EventType.MouseDown && evt.button == 0 && hoveredIndex >= 0)
            {
                draggedIndex  = hoveredIndex;
                selectedIndex = hoveredIndex;  // selecting also starts on mousedown
                Undo.RecordObject(asset, "Move Marker");
                evt.Use();
            }

            // ── Drag update ──
            if (evt.type == EventType.MouseDrag && draggedIndex >= 0)
            {
                float nx      = (evt.mousePosition.x - waveRect.x) / waveRect.width;
                float newTime = Mathf.Clamp(visStart + nx * visDur, 0f, clipLen);

                markersProp.GetArrayElementAtIndex(draggedIndex)
                    .FindPropertyRelative("time").floatValue = newTime;
                so.ApplyModifiedProperties();
                evt.Use();
            }

            // ── Drag end ──
            if ((evt.type == EventType.MouseUp || evt.type == EventType.Ignore)
                && draggedIndex >= 0)
            {
                draggedIndex = -1;
                evt.Use();
            }
        }

        /// <summary>
        /// Left-click on empty waveform area (no marker hovered, no drag)
        /// creates a new marker at the clicked time position and selects it.
        /// </summary>
        public static void HandleAddMarker(
            Rect waveRect,
            AudioEventAsset asset,
            SerializedObject so,
            SerializedProperty markersProp,
            float zoom, float scrollNorm,
            int hoveredIndex, int draggedIndex,
            ref int selectedIndex)
        {
            if (asset.clip == null) return;
            if (hoveredIndex >= 0 || draggedIndex >= 0) return;

            Event evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 0) return;
            if (!waveRect.Contains(evt.mousePosition)) return;

            float clipLen  = asset.clip.length;
            float visStart = scrollNorm * clipLen;
            float visDur   = clipLen / zoom;
            float nx       = (evt.mousePosition.x - waveRect.x) / waveRect.width;
            float time     = Mathf.Clamp(visStart + nx * visDur, 0f, clipLen);

            Undo.RecordObject(asset, "Add Marker");
            int idx = markersProp.arraySize;
            markersProp.arraySize++;
            var newElem = markersProp.GetArrayElementAtIndex(idx);
            newElem.FindPropertyRelative("time").floatValue  = time;
            newElem.FindPropertyRelative("name").stringValue = $"Marker {idx}";
            so.ApplyModifiedProperties();

            selectedIndex = idx;  // auto-select newly added marker
            evt.Use();
        }
    }

    // ════════════════════════════════════════════════════════
    //  AUDIO PREVIEW
    //  Plays / stops AudioClip preview in the editor using
    //  reflection to access internal UnityEditor.AudioUtil.
    // ════════════════════════════════════════════════════════

    static class AudioPreview
    {
        static MethodInfo playClip;
        static MethodInfo stopClips;
        static MethodInfo isPlayingMethod;
        static bool initialised;

        static void Init()
        {
            if (initialised) return;
            initialised = true;

            Type audioUtil = typeof(AudioImporter).Assembly
                .GetType("UnityEditor.AudioUtil");
            if (audioUtil == null) return;

            playClip = audioUtil.GetMethod(
                "PlayPreviewClip",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { typeof(AudioClip), typeof(int), typeof(bool) },
                null);

            stopClips = audioUtil.GetMethod(
                "StopAllPreviewClips",
                BindingFlags.Static | BindingFlags.Public);

            isPlayingMethod = audioUtil.GetMethod(
                "IsPreviewClipPlaying",
                BindingFlags.Static | BindingFlags.Public);
        }

        public static void Play(AudioClip clip)
        {
            Init();
            playClip?.Invoke(null, new object[] { clip, 0, false });
        }

        public static void Stop()
        {
            Init();
            stopClips?.Invoke(null, null);
        }

        public static bool IsPlaying()
        {
            Init();
            if (isPlayingMethod != null)
                return (bool)isPlayingMethod.Invoke(null, null);
            return false;
        }
    }
}

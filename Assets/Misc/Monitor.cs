using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using UnityEngine.Video;
using Metavido.Decoder;

public sealed class Monitor : MonoBehaviour
{
    [System.Serializable]
    public struct ExposurePreset
    {
        public string label;
        public float exposure;
    }

    [field:SerializeField] public MetadataDecoder Decoder { get; set; }
    [field:SerializeField] public VideoPlayer Source { get; set; }
    [SerializeField] Volume _postProcessVolume = null;
    [SerializeField] Vector2 _distortionRange = new Vector2(-0.5f, 0.5f);
    [SerializeField] Vector2 _exposureRange = new Vector2(-3f, 3f);
    [SerializeField] ExposurePreset[] _exposurePresets = new ExposurePreset[]
    {
        new ExposurePreset { label = "Auto (0 EV)", exposure = 0f },
        new ExposurePreset { label = "Bright (+1.0 EV)", exposure = 1f },
        new ExposurePreset { label = "Overcast (+1.5 EV)", exposure = 1.5f },
        new ExposurePreset { label = "Indoor (-0.7 EV)", exposure = -0.7f },
        new ExposurePreset { label = "Night (-1.5 EV)", exposure = -1.5f }
    };

    VisualElement RootUI => GetComponent<UIDocument>().rootVisualElement;

    RenderTexture _frame;
    VolumeProfile _volumeProfile;
    LensDistortion _lensDistortion;
    ColorAdjustments _colorAdjustments;
    Slider _distortionSlider;
    Slider _exposureSlider;
    Button _distortionResetButton;
    Button _exposureResetButton;
    DropdownField _exposurePresetField;
    float _defaultDistortion;
    float _defaultExposure;
    bool _suppressUiEvents;
    const string ManualPresetLabel = "Manual";
    const float PresetMatchThreshold = 0.01f;

    string GetMetadataString()
    {
        var data = Decoder.Metadata;
        if (!data.IsValid) return "Loading...";
        return $"Position: {data.CameraPosition}\n" +
               $"Rotation: {data.CameraRotation.eulerAngles}\n" +
               $"Center:   {data.CenterShift}\n" +
               $"FoV:      {data.FieldOfView * Mathf.Rad2Deg:F2}\n" +
               $"Range:    {data.DepthRange}";
    }

    void Awake()
    {
        InitializeVolumeProfile();
    }

    void Start()
    {
        _frame = RenderTexture.GetTemporary(1920, 1080);
        var root = RootUI;
        root.Q<Label>("url-label").text = "Source: " + Source.url;
        root.Q("video-view").style.backgroundImage = Background.FromRenderTexture(_frame);
        SetupUiBindings(root);
    }

    void OnDestroy()
      => RenderTexture.ReleaseTemporary(_frame);

    void Update()
    {
       RootUI.Q<Label>("metadata-label").text = GetMetadataString();
       if (Source.texture != null) Graphics.Blit(Source.texture, _frame);
    }

    void InitializeVolumeProfile()
    {
        if (_postProcessVolume == null)
            _postProcessVolume = FindObjectOfType<Volume>();

        if (_postProcessVolume == null)
            return;

        if (_postProcessVolume.profile == null)
        {
            if (_postProcessVolume.sharedProfile != null)
                _postProcessVolume.profile = Instantiate(_postProcessVolume.sharedProfile);
            else
                _postProcessVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
        }

        _volumeProfile = _postProcessVolume.profile;

        if (_volumeProfile == null)
            return;

        if (!_volumeProfile.TryGet(out _lensDistortion))
            _lensDistortion = _volumeProfile.Add<LensDistortion>(true);

        if (!_volumeProfile.TryGet(out _colorAdjustments))
            _colorAdjustments = _volumeProfile.Add<ColorAdjustments>(true);

        _defaultDistortion = _lensDistortion.intensity.value;
        _defaultExposure = _colorAdjustments.postExposure.value;
    }

    void SetupUiBindings(VisualElement root)
    {
        _distortionSlider = root.Q<Slider>("distortion-slider");
        _exposureSlider = root.Q<Slider>("exposure-slider");
        _distortionResetButton = root.Q<Button>("distortion-reset");
        _exposureResetButton = root.Q<Button>("exposure-reset");
        _exposurePresetField = root.Q<DropdownField>("exposure-preset");

        if (_distortionSlider != null)
        {
            _distortionSlider.lowValue = _distortionRange.x;
            _distortionSlider.highValue = _distortionRange.y;
            _distortionSlider.SetValueWithoutNotify(_lensDistortion != null ? _lensDistortion.intensity.value : 0f);
            _distortionSlider.RegisterValueChangedCallback(OnDistortionSliderChanged);
            _distortionSlider.SetEnabled(_lensDistortion != null);
        }

        if (_distortionResetButton != null)
        {
            _distortionResetButton.clicked += () => ApplyDistortion(_defaultDistortion);
            _distortionResetButton.SetEnabled(_lensDistortion != null);
        }

        if (_exposureSlider != null)
        {
            _exposureSlider.lowValue = _exposureRange.x;
            _exposureSlider.highValue = _exposureRange.y;
            _exposureSlider.SetValueWithoutNotify(_colorAdjustments != null ? _colorAdjustments.postExposure.value : 0f);
            _exposureSlider.RegisterValueChangedCallback(OnExposureSliderChanged);
            _exposureSlider.SetEnabled(_colorAdjustments != null);
        }

        if (_exposureResetButton != null)
        {
            _exposureResetButton.clicked += () => ApplyExposure(_defaultExposure);
            _exposureResetButton.SetEnabled(_colorAdjustments != null);
        }

        if (_exposurePresetField != null)
        {
            var choices = new List<string>();
            foreach (var preset in _exposurePresets)
            {
                if (string.IsNullOrEmpty(preset.label))
                    continue;
                choices.Add(preset.label);
            }
            choices.Add(ManualPresetLabel);
            _exposurePresetField.choices = choices;
            _exposurePresetField.RegisterValueChangedCallback(OnExposurePresetChanged);

            var initialExposure = _colorAdjustments != null ? _colorAdjustments.postExposure.value : 0f;
            UpdateExposurePresetSelection(initialExposure);
            _exposurePresetField.SetEnabled(_colorAdjustments != null);
        }
    }

    void OnDistortionSliderChanged(ChangeEvent<float> evt)
    {
        if (_suppressUiEvents)
            return;

        ApplyDistortion(evt.newValue);
    }

    void OnExposureSliderChanged(ChangeEvent<float> evt)
    {
        if (_suppressUiEvents)
            return;

        ApplyExposure(evt.newValue);
    }

    void OnExposurePresetChanged(ChangeEvent<string> evt)
    {
        if (_suppressUiEvents)
            return;

        var presetLabel = evt.newValue;
        if (presetLabel == ManualPresetLabel)
            return;

        for (var i = 0; i < _exposurePresets.Length; i++)
        {
            var preset = _exposurePresets[i];
            if (preset.label != presetLabel)
                continue;

            _suppressUiEvents = true;
            ApplyExposure(preset.exposure);
            if (_exposureSlider != null)
                _exposureSlider.SetValueWithoutNotify(preset.exposure);
            _suppressUiEvents = false;
            break;
        }
    }

    void ApplyDistortion(float value)
    {
        if (_lensDistortion == null)
            return;

        value = Mathf.Clamp(value, _distortionRange.x, _distortionRange.y);
        _lensDistortion.intensity.value = value;

        if (_distortionSlider != null && !_suppressUiEvents)
        {
            _suppressUiEvents = true;
            _distortionSlider.SetValueWithoutNotify(value);
            _suppressUiEvents = false;
        }
    }

    void ApplyExposure(float value)
    {
        if (_colorAdjustments == null)
            return;

        value = Mathf.Clamp(value, _exposureRange.x, _exposureRange.y);
        _colorAdjustments.postExposure.value = value;

        if (_exposureSlider != null && !_suppressUiEvents)
        {
            _suppressUiEvents = true;
            _exposureSlider.SetValueWithoutNotify(value);
            _suppressUiEvents = false;
        }

        UpdateExposurePresetSelection(value);
    }

    void UpdateExposurePresetSelection(float exposure)
    {
        if (_exposurePresetField == null)
            return;

        var matchedLabel = ManualPresetLabel;
        foreach (var preset in _exposurePresets)
        {
            if (string.IsNullOrEmpty(preset.label))
                continue;

            if (Mathf.Abs(preset.exposure - exposure) < PresetMatchThreshold)
            {
                matchedLabel = preset.label;
                break;
            }
        }

        _suppressUiEvents = true;
        _exposurePresetField.SetValueWithoutNotify(matchedLabel);
        _suppressUiEvents = false;
    }
}

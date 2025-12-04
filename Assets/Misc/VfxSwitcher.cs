using UnityEngine;
using UnityEngine.VFX;

public sealed class VfxSwitcher : MonoBehaviour
{
    #region Scene object references

    [SerializeField] CameraController _controller = null;
    [SerializeField] VisualEffect[] _vfxList = null;
    [SerializeField] VisualEffect _proxyVfx = null;
    [SerializeField] VisualEffect _afterimageVfx = null;

    #endregion

    #region Public properties

    [field:SerializeField] public float Interval { get; set; } = 3;

    #endregion

    #region Private members

    Color _proxyColor;

    #endregion

    #region MonoBehaviour implementation

    async Awaitable Start()
    {
        _proxyColor = _proxyVfx.GetVector4("Line Color");

        // Ищем эффект с именем "Voxels" и включаем только его.
        VisualEffect voxels = null;

        foreach (var vfx in _vfxList)
        {
            if (vfx == null) continue;

            if (vfx.gameObject.name == "Voxels")
            {
                voxels = vfx;
                vfx.SetBool("Spawn", true);
            }
            else
            {
                vfx.SetBool("Spawn", false);
            }
        }

        // Если вдруг объект переименуют, на всякий случай включим первый ненулевой.
        if (voxels == null)
        {
            foreach (var vfx in _vfxList)
            {
                if (vfx == null) continue;
                voxels = vfx;
                voxels.SetBool("Spawn", true);
                break;
            }
        }

        // Больше никаких переключений не делаем.
    }

    void Update()
    {
        var zoom = _controller.ZoomParam;
        _proxyVfx.SetVector4("Line Color", _proxyColor * Mathf.Clamp01(zoom * 3));
        _afterimageVfx.SetBool("Spawn", zoom > 0.1f);
    }

    #endregion
}

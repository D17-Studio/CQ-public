using UnityEngine;

namespace VFXTest.Vfx
{
    /// <summary>
    /// 把请求里的 color 参数写到自己所在 ParticleSystem 的起始颜色上。
    /// 挂在组合特效中需要单独染色的粒子系统上，避免整体 tint 影响同特效里的其他系统。
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class VfxSelfColorSink : MonoBehaviour, IVfxParameterSink
    {
        [SerializeField] Color defaultColor = Color.white;

        ParticleSystem system;

        void Awake()
        {
            system = GetComponent<ParticleSystem>();
        }

        /// <summary>
        /// 由 VFX 运行时在播放前调用，把 color 参数写到本粒子系统的起始颜色上。
        /// </summary>
        public void ApplyParameters(VfxParamValues values)
        {
            if (system == null)
            {
                system = GetComponent<ParticleSystem>();
            }

            Color color = values.GetColor("color", defaultColor);
            var main = system.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color);
        }
    }
}

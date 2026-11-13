using UnityEngine;

namespace VFXTest.Vfx
{
    [DisallowMultipleComponent]
    public sealed class VfxEffectDefinition : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Stable cue id. Renaming is equivalent to replacing the effect.")]
        public string cueId = "fx_new_effect";

        public string displayName = "New Effect";
        public VfxChannel channel = VfxChannel.Gameplay;
        public VfxLifecycle lifecycle = VfxLifecycle.OneShot;

        [Tooltip("Seconds. For OneShot/Timed this is mandatory or computed at build time. Ignored by Sustained.")]
        public float duration = 1f;

        [Header("Budget & Limits")]
        public int priority;
        [Tooltip("0 means unlimited.")]
        public int maxConcurrent = 8;
        [Tooltip("Seconds between accepted plays of this cue. 0 disables the cooldown.")]
        public float cooldown;

        [Header("Capabilities")]
        [Tooltip("Only declared knobs are applied; undeclared requests are ignored.")]
        public VfxCapabilities capabilities = VfxCapabilities.Intensity | VfxCapabilities.Tint | VfxCapabilities.TimeScale;

        [Header("Accessibility")]
        public bool essential;

        [Header("Parameters")]
        public VfxParamDeclaration[] parameters = System.Array.Empty<VfxParamDeclaration>();

        void OnValidate()
        {
            duration = Mathf.Max(0f, duration);
            maxConcurrent = Mathf.Max(0, maxConcurrent);
            cooldown = Mathf.Max(0f, cooldown);
        }
    }
}

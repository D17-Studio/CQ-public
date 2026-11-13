using System;
using System.Collections.Generic;
using UnityEngine;

namespace VFXTest.Vfx
{
    public sealed class VfxHandle
    {
        internal VfxRuntime owner;
        internal VfxInstance instance;

        public bool IsValid => instance != null && instance.IsActive;

        public string CueId => instance != null ? instance.Definition.cueId : null;

        public void Stop()
        {
            owner?.Finish(this);
        }
    }

    public sealed class VfxRuntime : MonoBehaviour
    {
        const string CatalogResourceName = "VfxCatalog";
        const float ReducedMotionScale = 0.4f;

        static readonly int[] ChannelSortingOffsets = { -1000, 0, 500, 1000, 1500 };

        readonly List<VfxInstance> active = new List<VfxInstance>(64);
        readonly Dictionary<string, Stack<VfxInstance>> pools = new Dictionary<string, Stack<VfxInstance>>(StringComparer.Ordinal);
        readonly Dictionary<string, int> activeByCue = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, VfxEffectDefinition> definitions = new Dictionary<string, VfxEffectDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, float> lastSpawnTime = new Dictionary<string, float>(StringComparer.Ordinal);
        readonly HashSet<string> warnedKeys = new HashSet<string>(StringComparer.Ordinal);
        readonly List<VfxRuleEntry> ruleBuffer = new List<VfxRuleEntry>(8);

        Transform[] channelRoots;
        int playedCount;
        int rejectedCount;
        int unknownCueCount;
        int cooldownSuppressedCount;
        int preemptedCount;

        public VfxCatalog Catalog { get; private set; }

        public bool ReducedMotion { get; set; }

        static bool DevelopmentLogging => Application.isEditor || Debug.isDebugBuild;

        void Awake()
        {
            Catalog = Resources.Load<VfxCatalog>(CatalogResourceName);
            if (Catalog == null)
            {
                Debug.LogError("[VFX] Resources/VfxCatalog was not found. Check Assets/Common/VFX/Resources/VfxCatalog.asset.");
                return;
            }

            BuildDefinitions();
            CreateChannelRoots();
        }

        void BuildDefinitions()
        {
            definitions.Clear();
            var prefabs = Catalog.Prefabs;
            for (int i = 0; i < prefabs.Length; i++)
            {
                var prefab = prefabs[i];
                if (prefab == null)
                {
                    Debug.LogError($"[VFX] Catalog prefab at index {i} is null.");
                    continue;
                }

                var definition = prefab.GetComponent<VfxEffectDefinition>();
                if (definition == null)
                {
                    Debug.LogError($"[VFX] Prefab '{prefab.name}' has no VfxEffectDefinition.");
                    continue;
                }

                if (string.IsNullOrEmpty(definition.cueId))
                {
                    Debug.LogError($"[VFX] Prefab '{prefab.name}' has an empty cueId.");
                    continue;
                }

                if (definitions.ContainsKey(definition.cueId))
                {
                    Debug.LogError($"[VFX] Duplicate cueId '{definition.cueId}' on prefab '{prefab.name}'.");
                    continue;
                }

                definitions.Add(definition.cueId, definition);
            }
        }

        bool TryGetDefinition(string cueId, out VfxEffectDefinition definition)
        {
            if (string.IsNullOrEmpty(cueId))
            {
                definition = null;
                return false;
            }

            return definitions.TryGetValue(cueId, out definition);
        }

        void CreateChannelRoots()
        {
            int count = Enum.GetValues(typeof(VfxChannel)).Length;
            channelRoots = new Transform[count];
            var container = new GameObject("Channels");
            container.transform.SetParent(transform, false);

            for (int i = 0; i < count; i++)
            {
                var channelRoot = new GameObject(((VfxChannel)i).ToString());
                channelRoot.transform.SetParent(container.transform, false);
                channelRoots[i] = channelRoot.transform;
            }
        }

        public int Signal(VfxSignal signal)
        {
            if (signal == null || Catalog == null)
            {
                return 0;
            }

            Catalog.GetRules(signal.type, ruleBuffer);
            int played = 0;
            for (int i = 0; i < ruleBuffer.Count; i++)
            {
                var rule = ruleBuffer[i];
                if (!VfxRuleEvaluator.Matches(rule, signal))
                {
                    continue;
                }

                var request = new VfxRequest(rule.cueId);
                VfxRuleEvaluator.ApplyTo(request, rule, signal);
                if (PlayInternal(request, rule.cooldownOverride) != null)
                {
                    played++;
                }

                if (rule.exclusive)
                {
                    break;
                }
            }

            return played;
        }

        public VfxHandle Play(VfxRequest request)
        {
            return PlayInternal(request, -1f);
        }

        VfxHandle PlayInternal(VfxRequest request, float cooldownOverride)
        {
            if (request == null || string.IsNullOrEmpty(request.cueId))
            {
                return null;
            }

            if (Catalog == null || !TryGetDefinition(request.cueId, out var definition))
            {
                unknownCueCount++;
                WarnOnce("cue:" + request.cueId, $"[VFX] Unknown cue '{request.cueId}' was skipped.");
                return null;
            }

            if (definition == null || definition.gameObject == null)
            {
                unknownCueCount++;
                WarnOnce("prefab:" + request.cueId, $"[VFX] Cue '{request.cueId}' has no prefab.");
                return null;
            }

            float cooldown = cooldownOverride >= 0f ? cooldownOverride : definition.cooldown;
            if (cooldown > 0f && lastSpawnTime.TryGetValue(request.cueId, out var lastTime) &&
                Time.time - lastTime < cooldown)
            {
                cooldownSuppressedCount++;
                return null;
            }

            int priority = request.priority ?? definition.priority;
            if (!TryReserveConcurrentSlot(request.cueId, definition, priority))
            {
                rejectedCount++;
                return null;
            }

            WarnUnsupported(request, definition);
            ValidateParamOverrides(request, definition);

            var channel = request.channel ?? definition.channel;
            if (channelRoots == null || (int)channel < 0 || (int)channel >= channelRoots.Length)
            {
                channel = VfxChannel.Gameplay;
            }

            var vfxInstance = AcquireInstance(definition, channelRoots[(int)channel]);
            if (vfxInstance == null)
            {
                rejectedCount++;
                return null;
            }

            float reducedScale = ReducedMotion && !definition.essential ? ReducedMotionScale : 1f;
            vfxInstance.Play(request, channel, reducedScale);

            var handle = new VfxHandle { owner = this, instance = vfxInstance };
            vfxInstance.Handle = handle;
            active.Add(vfxInstance);
            activeByCue.TryGetValue(request.cueId, out var count);
            activeByCue[request.cueId] = count + 1;
            lastSpawnTime[request.cueId] = Time.time;
            playedCount++;
            return handle;
        }

        bool TryReserveConcurrentSlot(string cueId, VfxEffectDefinition definition, int priority)
        {
            if (definition.maxConcurrent <= 0)
            {
                return true;
            }

            if (!activeByCue.TryGetValue(cueId, out var count) || count < definition.maxConcurrent)
            {
                return true;
            }

            VfxInstance victim = null;
            int victimPriority = int.MaxValue;
            for (int i = 0; i < active.Count; i++)
            {
                var candidate = active[i];
                if (candidate.Definition.cueId != cueId || candidate.Definition.essential || candidate.Definition.priority >= priority)
                {
                    continue;
                }

                if (candidate.Definition.priority < victimPriority)
                {
                    victimPriority = candidate.Definition.priority;
                    victim = candidate;
                }
            }

            if (victim == null)
            {
                return false;
            }

            preemptedCount++;
            FinishInstance(victim);
            return true;
        }

        VfxInstance AcquireInstance(VfxEffectDefinition definition, Transform channelRoot)
        {
            if (!pools.TryGetValue(definition.cueId, out var pool))
            {
                pool = new Stack<VfxInstance>(8);
                pools.Add(definition.cueId, pool);
            }

            if (pool.Count > 0)
            {
                return pool.Pop();
            }

            return VfxInstance.Create(definition, channelRoot);
        }

        void Update()
        {
            if (active.Count == 0)
            {
                return;
            }

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var vfxInstance = active[i];
                vfxInstance.Tick(Time.deltaTime);
                if (vfxInstance.IsDone)
                {
                    FinishInstance(vfxInstance);
                }
            }
        }

        internal void Finish(VfxHandle handle)
        {
            if (handle == null || handle.instance == null)
            {
                return;
            }

            FinishInstance(handle.instance);
        }

        void FinishInstance(VfxInstance vfxInstance)
        {
            if (vfxInstance == null || !vfxInstance.IsActive)
            {
                return;
            }

            active.Remove(vfxInstance);
            if (activeByCue.TryGetValue(vfxInstance.Definition.cueId, out var count))
            {
                activeByCue[vfxInstance.Definition.cueId] = Mathf.Max(0, count - 1);
            }

            if (vfxInstance.Handle != null)
            {
                vfxInstance.Handle.instance = null;
                vfxInstance.Handle = null;
            }

            vfxInstance.Release();
            if (!pools.TryGetValue(vfxInstance.Definition.cueId, out var pool))
            {
                pool = new Stack<VfxInstance>(8);
                pools.Add(vfxInstance.Definition.cueId, pool);
            }

            pool.Push(vfxInstance);
        }

        public void StopAll()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                FinishInstance(active[i]);
            }
        }

        public VfxStats GetStats()
        {
            int pooled = 0;
            foreach (var pair in pools)
            {
                pooled += pair.Value.Count;
            }

            return new VfxStats
            {
                active = active.Count,
                pooled = pooled,
                played = playedCount,
                rejected = rejectedCount,
                unknownCues = unknownCueCount,
                cooldownSuppressed = cooldownSuppressedCount,
                preempted = preemptedCount
            };
        }

        void WarnUnsupported(VfxRequest request, VfxEffectDefinition definition)
        {
            if (!DevelopmentLogging)
            {
                return;
            }

            if (!Mathf.Approximately(request.intensity, 1f) && !Supports(definition, VfxCapabilities.Intensity))
            {
                WarnOnce("cap:intensity:" + definition.cueId,
                    $"[VFX] Cue '{definition.cueId}' does not declare the Intensity capability; the value is ignored.");
            }

            if (request.tint.HasValue && !Supports(definition, VfxCapabilities.Tint))
            {
                WarnOnce("cap:tint:" + definition.cueId,
                    $"[VFX] Cue '{definition.cueId}' does not declare the Tint capability; the color is ignored.");
            }

            if (!Mathf.Approximately(request.opacity, 1f) && !Supports(definition, VfxCapabilities.Opacity))
            {
                WarnOnce("cap:opacity:" + definition.cueId,
                    $"[VFX] Cue '{definition.cueId}' does not declare the Opacity capability; the value is ignored.");
            }

            if (!Mathf.Approximately(request.timeScale, 1f) && !Supports(definition, VfxCapabilities.TimeScale))
            {
                WarnOnce("cap:timescale:" + definition.cueId,
                    $"[VFX] Cue '{definition.cueId}' does not declare the TimeScale capability; the value is ignored.");
            }
        }

        void ValidateParamOverrides(VfxRequest request, VfxEffectDefinition definition)
        {
            if (!DevelopmentLogging || request.parameters == null)
            {
                return;
            }

            for (int i = 0; i < request.parameters.Count; i++)
            {
                var parameter = request.parameters[i];
                if (!VfxParamResolver.IsDeclared(definition.parameters, parameter.name, out var declaration))
                {
                    WarnOnce("param:unknown:" + definition.cueId + ":" + parameter.name,
                        $"[VFX] Undeclared parameter '{parameter.name}' for cue '{definition.cueId}' is ignored.");
                }
                else if (declaration.type != parameter.type)
                {
                    WarnOnce("param:type:" + definition.cueId + ":" + parameter.name,
                        $"[VFX] Parameter '{parameter.name}' on cue '{definition.cueId}' expects {declaration.type}, got {parameter.type}.");
                }
            }
        }

        static bool Supports(VfxEffectDefinition definition, VfxCapabilities capability)
        {
            return (definition.capabilities & capability) == capability;
        }

        void WarnOnce(string key, string message)
        {
            if (warnedKeys.Add(key))
            {
                Debug.LogWarning(message);
            }
        }

        internal static int GetChannelSortingOffset(VfxChannel channel)
        {
            int index = (int)channel;
            return index >= 0 && index < ChannelSortingOffsets.Length ? ChannelSortingOffsets[index] : 0;
        }
    }

    sealed class VfxInstance
    {
        GameObject root;
        Transform channelRoot;
        ParticleSystem[] systems;
        ParticleSystemRenderer[] renderers;
        IVfxParameterSink[] sinks;
        ParticleSystem.MinMaxGradient[] originalStartColors;
        float[] originalStartSizes;
        float[] originalStartSpeeds;
        float[] originalRates;
        float[] originalSimulationSpeeds;
        int[] originalSortingOrders;
        float elapsed;
        float duration;
        Quaternion fixedWorldRotation;
        Quaternion followRotationOffset;
        Vector3 followLocalPosition;
        bool active;

        public VfxEffectDefinition Definition { get; private set; }

        public VfxHandle Handle { get; set; }

        public bool IsActive => active;

        public bool IsDone { get; private set; }

        public static VfxInstance Create(VfxEffectDefinition definition, Transform channelRoot)
        {
            var clone = UnityEngine.Object.Instantiate(definition.gameObject, channelRoot);
            clone.name = definition.cueId + " (pooled)";
            var vfxInstance = new VfxInstance
            {
                Definition = definition,
                root = clone,
                channelRoot = channelRoot,
                systems = clone.GetComponentsInChildren<ParticleSystem>(true),
                renderers = clone.GetComponentsInChildren<ParticleSystemRenderer>(true)
            };

            var behaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);
            var foundSinks = new List<IVfxParameterSink>(2);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IVfxParameterSink sink)
                {
                    foundSinks.Add(sink);
                }
            }

            vfxInstance.sinks = foundSinks.ToArray();
            vfxInstance.CaptureOriginals();
            clone.SetActive(false);
            return vfxInstance;
        }

        void CaptureOriginals()
        {
            int count = systems.Length;
            originalStartColors = new ParticleSystem.MinMaxGradient[count];
            originalStartSizes = new float[count];
            originalStartSpeeds = new float[count];
            originalRates = new float[count];
            originalSimulationSpeeds = new float[count];

            for (int i = 0; i < count; i++)
            {
                var system = systems[i];
                var main = system.main;
                originalStartColors[i] = main.startColor;
                originalStartSizes[i] = main.startSizeMultiplier;
                originalStartSpeeds[i] = main.startSpeedMultiplier;
                originalSimulationSpeeds[i] = main.simulationSpeed;
                originalRates[i] = system.emission.rateOverTimeMultiplier;
            }

            originalSortingOrders = new int[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                originalSortingOrders[i] = renderers[i].sortingOrder;
            }
        }

        public void Play(VfxRequest request, VfxChannel channel, float reducedIntensityScale)
        {
            active = true;
            IsDone = false;
            elapsed = 0f;
            duration = Definition.lifecycle == VfxLifecycle.Sustained ? float.PositiveInfinity : ResolveDuration();

            root.SetActive(true);
            var transformRef = root.transform;
            transformRef.SetParent(channelRoot, false);
            transformRef.SetPositionAndRotation(request.position, request.rotation);
            fixedWorldRotation = request.rotation;

            followRotationOffset = Quaternion.identity;
            followLocalPosition = Vector3.zero;
            if (request.follow != null && request.attachMode != VfxAttachMode.WorldFixed)
            {
                followLocalPosition = request.follow.InverseTransformPoint(request.position);
                followRotationOffset = Quaternion.Inverse(request.follow.rotation) * request.rotation;
                AttachMode = request.attachMode;
                Follow = request.follow;
            }
            else
            {
                AttachMode = VfxAttachMode.WorldFixed;
                Follow = null;
            }

            ApplySorting(channel);
            ApplyCapabilities(request, reducedIntensityScale);

            if (sinks.Length > 0)
            {
                var values = VfxParamResolver.Resolve(Definition.parameters, request.parameters);
                for (int i = 0; i < sinks.Length; i++)
                {
                    sinks[i].ApplyParameters(values);
                }
            }

            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Clear(true);
                systems[i].Play(true);
            }
        }

        VfxAttachMode AttachMode { get; set; }

        Transform Follow { get; set; }

        float ResolveDuration()
        {
            if (Definition.duration > 0f)
            {
                return Definition.duration;
            }

            float longest = 0f;
            for (int i = 0; i < systems.Length; i++)
            {
                var main = systems[i].main;
                if (main.loop)
                {
                    continue;
                }

                float candidate = main.duration + main.startLifetime.constantMax;
                if (candidate > longest)
                {
                    longest = candidate;
                }
            }

            return longest > 0.05f ? Mathf.Min(longest, 30f) : 2f;
        }

        void ApplySorting(VfxChannel channel)
        {
            int offset = VfxRuntime.GetChannelSortingOffset(channel);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sortingOrder = originalSortingOrders[i] + offset;
            }
        }

        void ApplyCapabilities(VfxRequest request, float reducedIntensityScale)
        {
            var capabilities = Definition.capabilities;
            bool allowIntensity = (capabilities & VfxCapabilities.Intensity) == VfxCapabilities.Intensity;
            bool allowTint = (capabilities & VfxCapabilities.Tint) == VfxCapabilities.Tint;
            bool allowOpacity = (capabilities & VfxCapabilities.Opacity) == VfxCapabilities.Opacity;
            bool allowTimeScale = (capabilities & VfxCapabilities.TimeScale) == VfxCapabilities.TimeScale;

            float intensity = allowIntensity
                ? Mathf.Clamp(request.intensity * reducedIntensityScale, 0.05f, 4f)
                : 1f;
            float timeScale = allowTimeScale ? Mathf.Clamp(request.timeScale, 0f, 4f) : 1f;
            Color tint = allowTint && request.tint.HasValue ? request.tint.Value : Color.white;
            float opacity = allowOpacity ? Mathf.Clamp01(request.opacity) : 1f;
            bool recolor = (allowTint && request.tint.HasValue) || (allowOpacity && !Mathf.Approximately(opacity, 1f));

            for (int i = 0; i < systems.Length; i++)
            {
                var main = systems[i].main;
                main.startSizeMultiplier = originalStartSizes[i] * intensity;
                main.startSpeedMultiplier = originalStartSpeeds[i] * Mathf.Lerp(1f, intensity, 0.5f);
                main.simulationSpeed = originalSimulationSpeeds[i] * timeScale;

                var emission = systems[i].emission;
                emission.rateOverTimeMultiplier = originalRates[i] * intensity;

                var gradient = originalStartColors[i];
                if (recolor)
                {
                    MultiplyGradient(ref gradient, tint, opacity);
                }

                main.startColor = gradient;
            }
        }

        static void MultiplyGradient(ref ParticleSystem.MinMaxGradient gradient, Color tint, float opacity)
        {
            switch (gradient.mode)
            {
                case ParticleSystemGradientMode.Color:
                    gradient.color = MultiplyColor(gradient.color, tint, opacity);
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    gradient.colorMin = MultiplyColor(gradient.colorMin, tint, opacity);
                    gradient.colorMax = MultiplyColor(gradient.colorMax, tint, opacity);
                    break;
            }
        }

        static Color MultiplyColor(Color source, Color tint, float opacity)
        {
            return new Color(source.r * tint.r, source.g * tint.g, source.b * tint.b,
                source.a * tint.a * opacity);
        }

        public void Tick(float deltaTime)
        {
            if (!active)
            {
                return;
            }

            if (Follow != null)
            {
                if (AttachMode != VfxAttachMode.WorldFixed)
                {
                    var rootTransform = root.transform;
                    rootTransform.position = Follow.TransformPoint(followLocalPosition);
                    if (AttachMode == VfxAttachMode.FollowFull)
                    {
                        rootTransform.rotation = Follow.rotation * followRotationOffset;
                    }
                    else
                    {
                        rootTransform.rotation = fixedWorldRotation;
                    }
                }
            }

            if (float.IsPositiveInfinity(duration))
            {
                return;
            }

            elapsed += deltaTime;
            if (elapsed >= duration)
            {
                IsDone = true;
            }
        }

        public void Release()
        {
            if (root != null)
            {
                for (int i = 0; i < systems.Length; i++)
                {
                    systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }

                root.transform.SetParent(channelRoot, false);
                root.SetActive(false);
            }

            active = false;
            IsDone = false;
            Follow = null;
            AttachMode = VfxAttachMode.WorldFixed;
            Handle = null;
        }
    }
}

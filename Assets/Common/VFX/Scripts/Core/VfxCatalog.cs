using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VFXTest.Vfx
{
    [CreateAssetMenu(menuName = "VFX/VFX Catalog", fileName = "VfxCatalog")]
    public sealed class VfxCatalog : ScriptableObject
    {
        [Tooltip("特效 prefab 清单。每个 prefab 根节点必须挂 VfxEffectDefinition，运行时直接读组件数据。")]
        [SerializeField] GameObject[] prefabs = Array.Empty<GameObject>();
        [SerializeField] VfxRuleEntry[] rules = Array.Empty<VfxRuleEntry>();

        Dictionary<string, List<VfxRuleEntry>> rulesByEvent;

        public GameObject[] Prefabs => prefabs ?? Array.Empty<GameObject>();

        public VfxRuleEntry[] Rules => rules ?? Array.Empty<VfxRuleEntry>();

        public void Configure(GameObject[] newPrefabs, VfxRuleEntry[] newRules)
        {
            prefabs = newPrefabs ?? Array.Empty<GameObject>();
            rules = newRules ?? Array.Empty<VfxRuleEntry>();
            rulesByEvent = null;
        }

        public void GetRules(string eventType, List<VfxRuleEntry> buffer)
        {
            buffer.Clear();
            EnsureRulesIndex();
            if (!string.IsNullOrEmpty(eventType) && rulesByEvent.TryGetValue(eventType, out var list))
            {
                buffer.AddRange(list);
            }
        }

        void EnsureRulesIndex()
        {
            if (rulesByEvent == null)
            {
                RebuildRulesIndex();
            }
        }

        void RebuildRulesIndex()
        {
            rulesByEvent = new Dictionary<string, List<VfxRuleEntry>>(StringComparer.Ordinal);
            foreach (var rule in Rules)
            {
                if (rule == null || string.IsNullOrEmpty(rule.eventType))
                {
                    continue;
                }

                if (!rulesByEvent.TryGetValue(rule.eventType, out var list))
                {
                    list = new List<VfxRuleEntry>();
                    rulesByEvent.Add(rule.eventType, list);
                }

                list.Add(rule);
            }

            var eventTypes = new List<string>(rulesByEvent.Keys);
            for (int i = 0; i < eventTypes.Count; i++)
            {
                var eventType = eventTypes[i];
                // OrderByDescending is stable: equal priorities keep declaration order.
                rulesByEvent[eventType] = rulesByEvent[eventType].OrderByDescending(rule => rule.priority).ToList();
            }
        }
    }
}

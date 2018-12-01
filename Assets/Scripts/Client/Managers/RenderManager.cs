﻿using System.Collections.Generic;
using Core;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Assertions;

namespace Client
{
    public class RenderManager : SingletonGameObject<RenderManager>
    {
        [SerializeField, UsedImplicitly] private Sprite defaultSpellIcon;

        private WorldManager worldManager;

        public Sprite DefaultSpellIcon => defaultSpellIcon;

        private Dictionary<Unit, UnitRenderer> UnitRenderers { get; } = new Dictionary<Unit, UnitRenderer>();

        public void Initialize(WorldManager worldManager)
        {
            this.worldManager = worldManager;

            SpellManager.Instance.EventSpellCast += OnSpellCast;
            SpellManager.Instance.EventSpellDamageDone += OnSpellDamageDone;

            Player localPlayer = MapManager.Instance.FindMap(1).FindMapEntity<Player>(worldManager.LocalPlayerId);
            localPlayer.GetComponentInChildren<UnitRenderer>().Initialize(localPlayer);
            localPlayer.GetComponent<WarcraftController>().Initialize(localPlayer);
            FindObjectOfType<WarcraftCamera>().Target = localPlayer.transform;
            UnitRenderers.Add(localPlayer, localPlayer.GetComponentInChildren<UnitRenderer>());
        }

        public void Deinitialize()
        {
            SpellManager.Instance.EventSpellDamageDone -= OnSpellDamageDone;
            SpellManager.Instance.EventSpellCast -= OnSpellCast;

            foreach (var unitRendererRecord in UnitRenderers)
                unitRendererRecord.Value.Deinitialize();

            UnitRenderers.Clear();
        }

        public void DoUpdate(int deltaTime)
        {
            foreach (var unitEntry in UnitRenderers)
                unitEntry.Value.DoUpdate(deltaTime);
        }

        private void OnSpellDamageDone(Unit caster, Unit target, int damage, bool isCrit)
        {
            if (!UnitRenderers.ContainsKey(target))
                return;

            if (caster.Guid == worldManager.LocalPlayerId)
            {
                GameObject damageEvent = Instantiate(Resources.Load("Prefabs/UI/DamageEvent")) as GameObject;
                Assert.IsNotNull(damageEvent, "damageEvent != null");
                // damageEvent.GetComponent<UnitDamageUIEvent>().Initialize(damage, UnitRenderers[target], isCrit, ArenaManager.PlayerInterface);
            }
        }

        private void OnSpellCast(Unit target, SpellInfo spellInfo)
        {
            GameObject spellRenderer = spellInfo.VisualSettings.FindEffect(SpellVisualEntry.UsageType.Cast);

            if (spellRenderer != null)
                Instantiate(spellRenderer, target.Position, Quaternion.identity);
        }
    }
}
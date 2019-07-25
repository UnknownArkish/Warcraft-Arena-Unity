﻿using System.Collections.Generic;
using Client.Spells;
using Common;
using UnityEngine;

namespace Client
{
    public sealed partial class UnitRenderer
    {
        private class AuraEffectController : IVisibleAuraHandler
        {
            private class SpellVisualAuraState
            {
                private IEffectEntity EffectEntity { get; }
                private long PlayId { get; }

                public SpellVisualAuraState(long playId, IEffectEntity effectEntity)
                {
                    PlayId = playId;
                    EffectEntity = effectEntity;
                }

                public void Fade()
                {
                    EffectEntity.Fade(PlayId);
                }

                public void Stop()
                {
                    EffectEntity.Stop(PlayId);
                }

                public void Replay()
                {
                    EffectEntity.Replay(PlayId);
                }
            }

            private bool isDetaching;
            private UnitRenderer unitRenderer;
            private readonly Dictionary<int, List<IVisibleAura>> aurasByAuraId = new Dictionary<int, List<IVisibleAura>>();
            private readonly Dictionary<int, SpellVisualAuraState> effectByAuraId = new Dictionary<int, SpellVisualAuraState>();

            public void HandleAttach(UnitRenderer unitRenderer)
            {
                this.unitRenderer = unitRenderer;
                unitRenderer.Unit.FindBehaviour<AuraControllerClient>().AddHandler(this);
            }

            public void HandleDetach()
            {
                isDetaching = true;
                unitRenderer.Unit.FindBehaviour<AuraControllerClient>().RemoveHandler(this);
                unitRenderer = null;

                Assert.IsTrue(aurasByAuraId.Count == 0);
                Assert.IsTrue(effectByAuraId.Count == 0);
                isDetaching = false;
            }

            public void AuraApplied(IVisibleAura visibleAura)
            {
                aurasByAuraId.Insert(visibleAura.AuraId, visibleAura);

                if (effectByAuraId.ContainsKey(visibleAura.AuraId))
                    return;

                if (!unitRenderer.rendering.AuraVisualSettingsById.TryGetValue(visibleAura.AuraId, out AuraVisualSettings auraVisualSettings))
                    return;

                if (auraVisualSettings.EffectSettings == null)
                    return;

                Vector3 effectDirection = Vector3.ProjectOnPlane(unitRenderer.transform.forward, Vector3.up);
                Quaternion effectRotation = Quaternion.LookRotation(effectDirection);
                IEffectEntity newEffect = auraVisualSettings.EffectSettings.PlayEffect(unitRenderer.transform.position, effectRotation, out long playId);
                if (newEffect != null)
                {
                    unitRenderer.TagContainer.ApplyPositioning(newEffect, auraVisualSettings);
                    effectByAuraId[visibleAura.AuraId] = new SpellVisualAuraState(playId, newEffect);
                }
            }

            public void AuraUnapplied(IVisibleAura visibleAura)
            {
                aurasByAuraId.Delete(visibleAura.AuraId, visibleAura);

                if (aurasByAuraId.ContainsKey(visibleAura.AuraId) || !effectByAuraId.TryGetValue(visibleAura.AuraId, out SpellVisualAuraState visualToRemove))
                    return;

                if (isDetaching)
                    visualToRemove.Stop();
                else
                    visualToRemove.Fade();

                effectByAuraId.Remove(visibleAura.AuraId);
            }

            public void AuraRefreshed(IVisibleAura visibleAura)
            {
                if (effectByAuraId.TryGetValue(visibleAura.AuraId, out SpellVisualAuraState activeState))
                    activeState.Replay();
            }
        }
    }
}
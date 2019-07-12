﻿using System.Collections.Generic;
using System.Linq;
using Common;
using JetBrains.Annotations;
using UdpKit;
using UnityEngine;

using EventHandler = Common.EventHandler;

namespace Core
{
    public abstract class Unit : WorldEntity
    {
        public new class CreateToken : WorldEntity.CreateToken
        {
            public DeathState DeathState;
            public bool FreeForAll;
            public int FactionId;

            public override void Read(UdpPacket packet)
            {
                base.Read(packet);

                DeathState = (DeathState)packet.ReadInt();
                FactionId = packet.ReadInt();
                FreeForAll = packet.ReadBool();
            }

            public override void Write(UdpPacket packet)
            {
                base.Write(packet);

                packet.WriteInt((int)DeathState);
                packet.WriteInt(FactionId);
                packet.WriteBool(FreeForAll);
            }

            public void Attached(Unit unit)
            {
                unit.deathState = DeathState;
                unit.faction = unit.Balance.FactionsById[FactionId];

                unit.EntityState.DeathState = (int) DeathState;
                unit.EntityState.Faction.FreeForAll = FreeForAll;
                unit.EntityState.Faction.Id = FactionId;

                unit.OnFactionChanged();
            }
        }

        [SerializeField, UsedImplicitly, Header(nameof(Unit)), Space(10)]
        private CapsuleCollider unitCollider;
        [SerializeField, UsedImplicitly]
        private WarcraftController controller;
        [SerializeField, UsedImplicitly]
        private UnitAttributeDefinition unitAttributeDefinition;
        [SerializeField, UsedImplicitly]
        private UnitMovementDefinition unitMovementDefinition;

        private FactionDefinition faction;
        private UnitFlags unitFlags;
        private DeathState deathState;
        private AuraInterruptFlags auraInterruptFlags;
        private ulong targetId;
        
        private CreateToken createToken;
        private EntityAttributeInt health;
        private EntityAttributeInt maxHealth;
        private EntityAttributeInt mana;
        private EntityAttributeInt maxMana;
        private EntityAttributeInt level;
        private EntityAttributeInt spellPower;
        private EntityAttributeFloat modHaste;
        private EntityAttributeFloat modRangedHaste;
        private EntityAttributeFloat modSpellHaste;
        private EntityAttributeFloat modRegenHaste;
        private EntityAttributeFloat critPercentage;
        private EntityAttributeFloat rangedCritPercentage;
        private EntityAttributeFloat spellCritPercentage;

        private readonly Dictionary<UnitMoveType, float> speedRates = new Dictionary<UnitMoveType, float>();
        private readonly Dictionary<AuraStateType, List<AuraApplication>> auraApplicationsByAuraState = new Dictionary<AuraStateType, List<AuraApplication>>();
        private readonly Dictionary<AuraEffectType, List<AuraEffect>> auraEffectsByAuraType = new Dictionary<AuraEffectType, List<AuraEffect>>();
        private readonly Dictionary<int, List<Aura>> ownedAurasById = new Dictionary<int, List<Aura>>();
        private readonly Dictionary<int, List<AuraApplication>> auraApplicationsByAuraId = new Dictionary<int, List<AuraApplication>>();
        private readonly List<AuraApplication> interruptableAuraApplications = new List<AuraApplication>();
        private readonly List<AuraApplication> auraApplications = new List<AuraApplication>();
        private readonly List<Aura> ownedAuras = new List<Aura>();

        private ThreatManager ThreatManager { get; set; }
        private UnitState UnitState { get; set; }

        internal IReadOnlyList<AuraApplication> AuraApplications => auraApplications;
        internal WarcraftController Controller => controller;

        public Unit Target { get; private set; }
        public SpellCast SpellCast { get; private set; }
        public IUnitState EntityState { get; private set; }
        public SpellHistory SpellHistory { get; private set; }
        public CapsuleCollider UnitCollider => unitCollider;
        public PlayerControllerDefinition ControllerDefinition => controller.ControllerDefinition;

        public int Health => health.Value;
        public int MaxHealth => maxHealth.Value;
        public int BaseMana => mana.Base;
        public int Mana => mana.Value;
        public int MaxMana => maxMana.Value;
        public int SpellPower => spellPower.Value;
        public float HealthRatio => maxHealth.Value > 0 ? (float)Health / MaxHealth : 0.0f;
        public bool HasFullHealth => health.Value == maxHealth.Value;
        public float HealthPercent => 100.0f * HealthRatio;

        public float ModHaste => modHaste.Value;
        public float ModRangedHaste => modRangedHaste.Value;
        public float ModSpellHaste => modSpellHaste.Value;
        public float ModRegenHaste => modRegenHaste.Value;
        public float CritPercentage => critPercentage.Value;
        public float RangedCritPercentage => rangedCritPercentage.Value;
        public float SpellCritPercentage => spellCritPercentage.Value;

        public bool IsMovementBlocked => HasState(UnitState.Root) || HasState(UnitState.Stunned);
        public bool IsAlive => deathState == DeathState.Alive;
        public bool IsDead => deathState == DeathState.Dead;
        public bool IsControlledByPlayer => this is Player;
        public bool IsStopped => !HasState(UnitState.Moving);
        public bool IsFreeForAll => EntityState.Faction.FreeForAll;

        public bool HealthBelowPercent(int percent) => health.Value < CountPercentFromMaxHealth(percent);
        public bool HealthAbovePercent(int percent) => health.Value > CountPercentFromMaxHealth(percent);
        public bool HealthAbovePercentHealed(int percent, int healAmount) => health.Value + healAmount > CountPercentFromMaxHealth(percent);
        public bool HealthBelowPercentDamaged(int percent, int damageAmount) => health.Value - damageAmount < CountPercentFromMaxHealth(percent);
        public long CountPercentFromMaxHealth(int percent) => maxHealth.Value.CalculatePercentage(percent);
        public long CountPercentFromCurrentHealth(int percent) => health.Value.CalculatePercentage(percent);
        public float GetSpeed(UnitMoveType type) => speedRates[type] * unitMovementDefinition.BaseSpeedByType(type);
        public float GetSpeedRate(UnitMoveType type) => speedRates[type];
        public float GetPowerPercent(SpellResourceType type) => GetMaxPower(type) > 0 ? 100.0f * GetPower(type) / GetMaxPower(type) : 0.0f;
        public int GetPower(SpellResourceType type) => mana.Value;
        public int GetMaxPower(SpellResourceType type) => maxMana.Value;

        [UsedImplicitly]
        protected override void Awake()
        {
            base.Awake();

            health = new EntityAttributeInt(this, unitAttributeDefinition.BaseHealth, int.MaxValue, EntityAttributes.Health);
            maxHealth = new EntityAttributeInt(this, unitAttributeDefinition.BaseMaxHealth, int.MaxValue, EntityAttributes.MaxHealth);
            mana = new EntityAttributeInt(this, unitAttributeDefinition.BaseMana, int.MaxValue, EntityAttributes.Power);
            maxMana = new EntityAttributeInt(this, unitAttributeDefinition.BaseMaxMana, int.MaxValue, EntityAttributes.MaxPower);
            level = new EntityAttributeInt(this, 1, int.MaxValue, EntityAttributes.Level);
            spellPower = new EntityAttributeInt(this, unitAttributeDefinition.BaseSpellPower, int.MaxValue, EntityAttributes.SpellPower);
            modHaste = new EntityAttributeFloat(this, 1.0f, float.MaxValue, EntityAttributes.ModHaste);
            modRangedHaste = new EntityAttributeFloat(this, 1.0f, float.MaxValue, EntityAttributes.ModRangedHaste);
            modSpellHaste = new EntityAttributeFloat(this, 1.0f, float.MaxValue, EntityAttributes.ModSpellHaste);
            modRegenHaste = new EntityAttributeFloat(this, 1.0f, float.MaxValue, EntityAttributes.ModRegenHaste);
            critPercentage = new EntityAttributeFloat(this, unitAttributeDefinition.CritPercentage, float.MaxValue, EntityAttributes.CritPercentage);
            rangedCritPercentage = new EntityAttributeFloat(this, 1.0f, unitAttributeDefinition.RangedCritPercentage, EntityAttributes.RangedCritPercentage);
            spellCritPercentage = new EntityAttributeFloat(this, 1.0f, unitAttributeDefinition.SpellCritPercentage, EntityAttributes.SpellCritPercentage);

            faction = Balance.DefaultFaction;
        }

        public override void Attached()
        {
            base.Attached();

            EntityState = entity.GetState<IUnitState>();

            foreach (UnitMoveType moveType in StatUtils.UnitMoveTypes)
                speedRates[moveType] = 1.0f;

            createToken = (CreateToken)entity.AttachToken;
            createToken.Attached(this);

            if (!IsOwner)
            {
                EntityState.AddCallback(nameof(EntityState.DeathState), OnDeathStateChanged);
                EntityState.AddCallback(nameof(EntityState.Health), OnHealthStateChanged);
                EntityState.AddCallback(nameof(EntityState.TargetId), OnTargetIdChanged);
                EntityState.AddCallback(nameof(EntityState.Faction), OnFactionChanged);
            }

            ThreatManager = new ThreatManager(this);
            MovementInfo.Attached(EntityState, this);

            SpellHistory = new SpellHistory(this);
            SpellCast = new SpellCast(this);

            SetMap(WorldManager.FindMap(1));

            WorldManager.UnitManager.EventEntityDetach += OnEntityDetach;
            WorldManager.UnitManager.Attach(this);
        }

        public override void Detached()
        {
            // called twice on client (from Detached Photon callback and manual in UnitManager.Dispose)
            // if he needs to instantly destroy current world and avoid any events
            if (!IsValid)
                return;

            WorldManager.UnitManager.EventEntityDetach -= OnEntityDetach;

            SpellHistory.Detached();
            SpellCast.Detached();

            EntityState.RemoveAllCallbacks();

            ResetMap();

            ThreatManager.Detached();
            WorldManager.UnitManager.Detach(this);
            MovementInfo.Detached();

            createToken = null;

            base.Detached();
        }

        internal override void DoUpdate(int deltaTime)
        {
            base.DoUpdate(deltaTime);

            SpellHistory.DoUpdate(deltaTime);
            Controller.DoUpdate();

            for (int i = 0; i < ownedAuras.Count; i++)
            {
                Aura auraToUpdate = ownedAuras[i];
                if (auraToUpdate.Updated)
                    continue;

                auraToUpdate.DoUpdate(deltaTime);

                if (auraToUpdate.IsExpired)
                    RemoveOwnedAura(auraToUpdate, AuraRemoveMode.Expired);

                if (i >= ownedAuras.Count || auraToUpdate != ownedAuras[i])
                    i = 0;
            }

            for (int i = 0; i < ownedAuras.Count; i++)
                ownedAuras[i].LateUpdate();
        }

        internal void HandleSpawn()
        {
            ModifyDeathState(DeathState.Alive);

            SetHealth(MaxHealth);
        }

        internal void UpdateTarget(ulong newTargetId = UnitUtils.NoTargetId, Unit newTarget = null, bool updateState = false)
        {
            targetId = newTarget?.Id ?? newTargetId;
            Target = newTarget ?? WorldManager.UnitManager.Find(targetId);

            if (updateState)
                EntityState.TargetId = Target?.BoltEntity.NetworkId ?? default;

            EventHandler.ExecuteEvent(this, GameEvents.UnitTargetChanged);
        }

        public bool IsHostileTo(Unit unit)
        {
            if (unit == this)
                return false;

            if (unit.IsFreeForAll && IsFreeForAll)
                return true;

            return faction.HostileFactions.Contains(unit.faction);
        }

        public bool IsFriendlyTo(Unit unit)
        {
            if (unit == this)
                return true;

            if (unit.IsFreeForAll && IsFreeForAll)
                return false;

            return faction.FriendlyFactions.Contains(unit.faction);
        }

        #region Attribute Handling

        internal void AddState(UnitState state) { UnitState |= state; }

        internal bool HasState(UnitState state) { return (UnitState & state) != 0; }

        internal void RemoveState(UnitState state) { UnitState &= ~state; }

        internal void SetFlag(UnitFlags flag) => unitFlags |= flag;

        internal void RemoveFlag(UnitFlags flag) => unitFlags &= ~flag;

        internal bool HasFlag(UnitFlags flag) => (unitFlags & flag) == flag;

        internal void AddFlag(MovementFlags f) { MovementInfo.AddMovementFlag(f); }

        internal void RemoveFlag(MovementFlags f) { MovementInfo.RemoveMovementFlag(f); }

        internal bool HasFlag(MovementFlags f) { return MovementInfo.HasMovementFlag(f); }
        
        internal int ModifyHealth(int delta)
        {
            return SetHealth(Health + delta);
        }

        internal int SetHealth(int value)
        {
            int delta = health.Set(Mathf.Clamp(value, 0, maxHealth.Value));
            EntityState.Health = health.Value;
            return delta;
        }

        internal void SetPowerType(SpellResourceType spellResource) { }

        internal void SetPower(SpellResourceType spellResource, int value) { }

        internal void SetMaxPower(SpellResourceType spellResource, int value) { }

        internal int ModifyPower(SpellResourceType spellResource, int value) { return 0; }

        internal int ModifyPowerPercent(SpellResourceType spellResource, float percent, bool apply = true) { return 0; }

        internal void UpdateSpeed(UnitMoveType type)
        {
            int mainSpeedMod = 0;
            float stackBonus = 1.0f;
            float nonStackBonus = 1.0f;

            switch (type)
            {
                // only apply debuffs
                case UnitMoveType.RunBack:
                    break;
                case UnitMoveType.Run:
                    mainSpeedMod = /*GetMaxPositiveAuraModifier(SPELL_AURA_MOD_INCREASE_SPEED)*/0;
                    stackBonus = /*GetTotalAuraMultiplier(SPELL_AURA_MOD_SPEED_ALWAYS)*/0;
                    nonStackBonus += /*GetMaxPositiveAuraModifier(SPELL_AURA_MOD_SPEED_NOT_STACK) / 100.0f*/0;
                    break;
                default:
                    Debug.LogErrorFormat("Characters::UpdateSpeed: Unsupported move type - {0}", type);
                    return;
            }

            // now we ready for speed calculation
            float speed = Mathf.Max(nonStackBonus, stackBonus);
            if (mainSpeedMod != 0)
                speed *= mainSpeedMod;

            switch (type)
            {
                case UnitMoveType.Run:
                    // Normalize speed by 191 aura SPELL_AURA_USE_NORMAL_MOVEMENT_SPEED if need #TODO
                    int normalization/* = GetMaxPositiveAuraModifier(SPELL_AURA_USE_NORMAL_MOVEMENT_SPEED)*/ = 0;
                    if (normalization > 0)
                    {
                        // Use speed from aura
                        float maxSpeed = normalization / unitMovementDefinition.BaseSpeedByType(type);
                        if (speed > maxSpeed)
                            speed = maxSpeed;
                    }

                    // force minimum speed rate @ aura 437 SPELL_AURA_MOD_MINIMUM_SPEED_RATE
                    int minSpeedModRate = /*GetMaxPositiveAuraModifier(SPELL_AURA_MOD_MINIMUM_SPEED_RATE)*/0;
                    if (minSpeedModRate != 0)
                    {
                        float minSpeed = minSpeedModRate / unitMovementDefinition.BaseSpeedByType(type);
                        if (speed < minSpeed)
                            speed = minSpeed;
                    }
                    break;
            }

            // Apply strongest slow aura mod to speed
            int slow = /*GetMaxNegativeAuraModifier(SPELL_AURA_MOD_DECREASE_SPEED)*/0;
            if (slow != 0)
                speed *= slow;

            float minSpeedMod = /*(float)GetMaxPositiveAuraModifier(SPELL_AURA_MOD_MINIMUM_SPEED)*/0;
            if (minSpeedMod > 0)
            {
                float minSpeed = minSpeedMod / 100.0f;
                if (speed < minSpeed)
                    speed = minSpeed;
            }

            SetSpeedRate(type, speed);
        }

        internal void SetSpeed(UnitMoveType type, float newValue)
        {
            SetSpeedRate(type, newValue / unitMovementDefinition.BaseSpeedByType(type));
        }

        internal void SetSpeedRate(UnitMoveType type, float rate)
        {
            if (rate < 0)
                rate = 0.0f;

            speedRates[type] = rate;
        }

        internal void StopMoving() { }

        internal void SetControlled(bool apply, UnitState state) { }

        #endregion

        #region Spell Handling

        internal SpellCastResult CastSpell(SpellInfo spellInfo, SpellCastingOptions castOptions)
        {
            Spell spell = new Spell(this, spellInfo, castOptions);

            SpellCastResult castResult = spell.Prepare();
            if (castResult != SpellCastResult.Success)
            {
                WorldManager.SpellManager.Remove(spell);
                return castResult;
            }

            switch (spell.ExecutionState)
            {
                case SpellExecutionState.Casting:
                    SpellCast.HandleSpellCast(spell, SpellCast.HandleMode.Started);
                    break;
                case SpellExecutionState.Processing:
                    return castResult;
                case SpellExecutionState.Completed:
                    return castResult;
            }
            
            return SpellCastResult.Success;
        }

        internal int DamageBySpell(SpellCastDamageInfo damageInfoInfo)
        {
            Unit victim = damageInfoInfo.Target;
            if (victim == null || !victim.IsAlive)
                return 0;

            EventHandler.ExecuteEvent(EventHandler.GlobalDispatcher, GameEvents.SpellDamageDone, this, victim, damageInfoInfo.Damage, damageInfoInfo.HitInfo == HitType.CriticalHit);

            return DealDamage(victim, damageInfoInfo.Damage);
        }

        internal int HealBySpell(Unit target, SpellInfo spellInfo, int healAmount, bool critical = false)
        {
            return DealHeal(target, healAmount);
        }

        internal int DealDamage(Unit target, int damageAmount)
        {
            if (damageAmount < 1)
                return 0;

            int healthValue = target.Health;
            if (healthValue <= damageAmount)
            {
                Kill(target);
                return healthValue;
            }

            return target.ModifyHealth(-damageAmount);
        }

        internal int DealHeal(Unit target, int healAmount)
        {
            if(healAmount < 1)
                return 0;

            return target.ModifyHealth(healAmount);
        }

        internal void Kill(Unit victim)
        {
            if (victim.Health <= 0)
                return;

            victim.SetHealth(0);
            victim.ModifyDeathState(DeathState.Dead);
        }

        internal void ModifyDeathState(DeathState newState)
        {
            if (deathState == newState)
                return;

            EntityState.DeathState = (int)(createToken.DeathState = deathState = newState);

            if (IsDead && SpellCast.IsCasting)
                SpellCast.Cancel();
        }

        internal int CalculateSpellDamageTaken(SpellCastDamageInfo damageInfoInfo, int damage, SpellInfo spellInfo)
        {
            if (damage < 0)
                return 0;

            Unit victim = damageInfoInfo.Target;
            if (victim == null || !victim.IsAlive)
                return 0;

            SpellSchoolMask damageSchoolMask = damageInfoInfo.SchoolMask;

            if (damage > 0)
            {
                int absorb = damageInfoInfo.Absorb;
                int resist = damageInfoInfo.Resist;
                CalcAbsorbResist(victim, damageSchoolMask, SpellDamageType.Direct, damage, ref absorb, ref resist, spellInfo);
                damageInfoInfo.Absorb = absorb;
                damageInfoInfo.Resist = resist;
                damage -= damageInfoInfo.Absorb + damageInfoInfo.Resist;
            }
            else
                damage = 0;

            return damageInfoInfo.Damage = damage;
        }
        
        internal SpellMissType SpellHitResult(Unit victim, SpellInfo spellInfo, bool canReflect = false)
        {
            // Check for immune
            /*if (victim->IsImmunedToSpell(spellInfo))
                return SPELL_MISS_IMMUNE;*/

            // All positive spells can`t miss
            if (spellInfo.IsPositive() && !IsHostileTo(victim)) // prevent from affecting enemy by "positive" spell
                return SpellMissType.None;

            // Check for immune
            /*if (victim->IsImmunedToDamage(spellInfo))
                return SPELL_MISS_IMMUNE;*/

            if (this == victim)
                return SpellMissType.None;

            // Try victim reflect spell
            /*if (CanReflect)
            {
                int32 reflectchance = victim->GetTotalAuraModifier(SPELL_AURA_REFLECT_SPELLS);
                    Unit::AuraEffectList const& mReflectSpellsSchool = victim->GetAuraEffectsByType(SPELL_AURA_REFLECT_SPELLS_SCHOOL);
                for (Unit::AuraEffectList::const_iterator i = mReflectSpellsSchool.begin(); i != mReflectSpellsSchool.end(); ++i)
                    if ((*i)->GetMiscValue() & spellInfo->GetSchoolMask())
                        reflectchance += (*i)->GetAmount();
                if (reflectchance > 0 && roll_chance_i(reflectchance))
                {
                    // Start triggers for remove charges if need (trigger only for victim, and mark as active spell)
                    ProcDamageAndSpell(victim, PROC_FLAG_NONE, PROC_FLAG_TAKEN_SPELL_MAGIC_DMG_CLASS_NEG, PROC_EX_REFLECT, 1, BASE_ATTACK, spellInfo);
                    return SPELL_MISS_REFLECT;
                }
            }*/

            /*switch (spellInfo->DmgClass)
            {
                case SPELL_DAMAGE_CLASS_RANGED:
                case SPELL_DAMAGE_CLASS_MELEE:
                    return MeleeSpellHitResult(victim, spellInfo);
                case SPELL_DAMAGE_CLASS_NONE:
                    return SPELL_MISS_NONE;
                case SPELL_DAMAGE_CLASS_MAGIC:
                    return MagicSpellHitResult(victim, spellInfo);
            }*/
            return SpellMissType.None;
        }

        internal float GetSpellMinRangeForTarget(Unit target, SpellInfo spellInfo)
        {
            if (Mathf.Approximately(spellInfo.MinRangeFriend, spellInfo.MinRangeHostile))
                return spellInfo.GetMinRange(false);
            if (target == null)
                return spellInfo.GetMinRange(true);
            return spellInfo.GetMinRange(!IsHostileTo(target));
        }

        internal float GetSpellMaxRangeForTarget(Unit target, SpellInfo spellInfo)
        {
            if (Mathf.Approximately(spellInfo.MaxRangeFriend, spellInfo.MaxRangeHostile))
                return spellInfo.GetMaxRange(false);
            if (target == null)
                return spellInfo.GetMaxRange(true);
            return spellInfo.GetMaxRange(!IsHostileTo(target));
        }

        internal void ModifyAuraState(AuraStateType flag, bool apply) { }

        internal bool HasAuraState(AuraStateType flag, SpellInfo spellProto = null, Unit caster = null) { return false; }

        internal Unit GetMagicHitRedirectTarget(Unit victim, SpellInfo spellProto) { return null; }

        internal Unit GetMeleeHitRedirectTarget(Unit victim, SpellInfo spellProto = null) { return null; }

        internal int SpellBaseDamageBonusDone(SpellSchoolMask schoolMask) { return 0; }

        internal int SpellBaseDamageBonusTaken(SpellSchoolMask schoolMask) { return 0; }

        internal int SpellDamageBonusDone(Unit victim, SpellInfo spellProto, int damage, SpellDamageType damagetype, SpellEffectInfo effect, uint stack = 1) { return 0; }

        internal float SpellDamagePctDone(Unit victim, SpellInfo spellProto, SpellDamageType damagetype) { return 0.0f; }

        internal void ApplySpellMod(SpellInfo spellInfo, SpellModifierType modifierType, ref int value) { }

        internal void ApplySpellMod(SpellInfo spellInfo, SpellModifierType modifierType, ref float value) { }

        internal int SpellDamageBonusTaken(Unit caster, SpellInfo spellProto, int damage, SpellDamageType damagetype, SpellEffectInfo effect, uint stack = 1)
        {
            return damage;
        }

        internal int SpellBaseHealingBonusDone(SpellSchoolMask schoolMask) { return 0; }

        internal int SpellBaseHealingBonusTaken(SpellSchoolMask schoolMask) { return 0; }

        internal uint SpellHealingBonusDone(Unit victim, SpellInfo spellProto, uint healamount, SpellDamageType damagetype, SpellEffectInfo effect, uint stack = 1) { return 0; }

        internal uint SpellHealingBonusTaken(Unit caster, SpellInfo spellProto, uint healamount, SpellDamageType damagetype, SpellEffectInfo effect, uint stack = 1) { return 0; }

        internal float SpellHealingPercentDone(Unit victim, SpellInfo spellProto) { return 0.0f; }

        internal bool IsSpellBlocked(Unit victim, SpellInfo spellProto, WeaponAttackType attackType = WeaponAttackType.BaseAttack) { return false; }

        internal bool IsSpellCrit(Unit victim, SpellInfo spellProto, SpellSchoolMask schoolMask, WeaponAttackType attackType = WeaponAttackType.BaseAttack) { return false; }

        internal float GetUnitSpellCriticalChance(Unit victim, SpellInfo spellProto, SpellSchoolMask schoolMask, WeaponAttackType attackType = WeaponAttackType.BaseAttack) { return 0.0f; }

        internal int SpellCriticalHealingBonus(SpellInfo spellProto, int damage, Unit victim) { return 0; }

        internal void ApplySpellDispelImmunity(SpellInfo spellProto, SpellDispelType spellDispelType, bool apply) { }

        internal bool IsImmunedToDamage(SpellSchoolMask meleeSchoolMask) { return false; }

        internal bool IsImmunedToDamage(SpellInfo spellProto) { return false; }

        internal bool IsImmuneToSpell(SpellInfo spellInfo, Unit caster) { return false; }

        internal bool IsImmuneToAura(AuraInfo auraInfo, Unit caster) { return false; }

        internal bool IsImmuneToAuraEffect(AuraEffectInfo auraEffect, Unit caster) { return false; }

        internal uint CalcSpellResistance(Unit victim, SpellSchoolMask schoolMask, SpellInfo spellProto) { return 0; }

        internal void CalcAbsorbResist(Unit victim, SpellSchoolMask schoolMask, SpellDamageType damagetype, int damage, ref int absorb, ref int resist, SpellInfo spellProto = null) { }

        internal void CalcHealAbsorb(Unit victim, SpellInfo spellProto, ref int healAmount, ref int absorb) { }

        internal float ApplyEffectModifiers(SpellInfo spellProto, int effectIndex, float value)
        {
            //var modOwner = this;
            /*modOwner->ApplySpellMod(spellProto->Id, SPELLMOD_ALL_EFFECTS, value);
            switch (effect_index)
            {
                case 0:
                    modOwner->ApplySpellMod(spellProto->Id, SPELLMOD_EFFECT1, value);
                    break;
                case 1:
                    modOwner->ApplySpellMod(spellProto->Id, SPELLMOD_EFFECT2, value);
                    break;
                case 2:
                    modOwner->ApplySpellMod(spellProto->Id, SPELLMOD_EFFECT3, value);
                    break;
                case 3:
                    modOwner->ApplySpellMod(spellProto->Id, SPELLMOD_EFFECT4, value);
                    break;
                case 4:
                    modOwner->ApplySpellMod(spellProto->Id, SPELLMOD_EFFECT5, value);
                    break;
            }*/
            return value;
        }

        internal int CalcSpellDuration(SpellInfo spellProto) { return 0; }

        internal int ModSpellDuration(SpellInfo spellInfo, Unit target, int duration) { return duration; }

        internal void ModSpellCastTime(SpellInfo spellProto, ref int castTime, Spell spell = null) { }

        internal void ModSpellDurationTime(SpellInfo spellProto, ref int castTime, Spell spell = null) { }

        #endregion

        #region Aura Handling

        internal Aura FindOwnedAura(int spellId, ulong casterId, Aura exceptAura = null)
        {
            if (ownedAurasById.TryGetValue(spellId, out List<Aura> ownedAuraList))
                foreach (Aura aura in ownedAuraList)
                    if (aura.CasterId == casterId && exceptAura != aura)
                        return aura;

            return null;
        }

        internal void AddOwnedAura(Aura aura)
        {
            ownedAuras.Add(aura);
            ownedAurasById.Insert(aura.Info.Id, aura);

            RemoveNonStackableAuras(aura);
        }

        internal void ApplyAuraApplication(AuraApplication auraApplication)
        {
            Aura aura = auraApplication.Aura;

            RemoveNonStackableAuras(aura);

            if (auraApplication.IsRemoved)
                return;

            HandleStateContainingAura(auraApplication, true);
            HandleInterruptableAura(auraApplication, true);
            aura.RegisterForTarget(this, auraApplication);

            if (aura.Info.StateType != AuraStateType.None)
                ModifyAuraState(aura.Info.StateType, true);

            for (int i = 0; i < aura.EffectsInfos.Count; i++)
                if (auraApplication.EffectsToApply.HasBit(i) && !auraApplication.IsRemoved)
                    auraApplication.HandleEffect(i, true);

            if (!auraApplication.IsRemoved)
            {
                auraApplications.Add(auraApplication);
                auraApplicationsByAuraId.Insert(aura.Info.Id, auraApplication);
            }
        }

        internal void UnapplyAuraApplication(AuraApplication auraApplication, AuraRemoveMode removeMode)
        {
            Aura aura = auraApplication.Aura;

            auraApplicationsByAuraId.Delete(aura.Info.Id, auraApplication);
            auraApplications.Remove(auraApplication);

            HandleInterruptableAura(auraApplication, false);
            HandleStateContainingAura(auraApplication, false);
            aura.UnregisterForTarget(this, auraApplication);
            auraApplication.Remove(removeMode);

            for (int i = 0; i < aura.EffectsInfos.Count; i++)
                if (auraApplication.AppliedEffectMask.HasBit(i))
                    auraApplication.HandleEffect(i, false);

            Assert.IsTrue(auraApplication.AppliedEffectMask == 0);
        }

        private void HandleInterruptableAura(AuraApplication auraApplication, bool added)
        {
            if (!auraApplication.Aura.Info.HasInterruptFlags)
                return;

            if (added)
            {
                interruptableAuraApplications.Add(auraApplication);
                auraInterruptFlags |= auraApplication.Aura.Info.InterruptFlags;
            }
            else
            {
                interruptableAuraApplications.Remove(auraApplication);

                auraInterruptFlags = 0;
                foreach (AuraApplication interruptableAura in interruptableAuraApplications)
                    auraInterruptFlags |= interruptableAura.Aura.Info.InterruptFlags;
            }
        }

        private void HandleStateContainingAura(AuraApplication auraApplication, bool added)
        {
            AuraStateType stateType = auraApplication.Aura.Info.StateType;
            if (stateType == AuraStateType.None)
                return;

            if (added)
            {
                auraApplicationsByAuraState.Insert(stateType, auraApplication);

                ModifyAuraState(stateType, true);
            }
            else
            {
                auraApplicationsByAuraState.Delete(stateType, auraApplication);

                ModifyAuraState(stateType, auraApplicationsByAuraState.ContainsKey(stateType));
            }
        }

        private void HandleAuraEffectRegistration(AuraEffect auraEffect, bool added)
        {
            if (added)
                auraEffectsByAuraType.Insert(auraEffect.EffectInfo.AuraEffectType, auraEffect);
            else
                auraEffectsByAuraType.Delete(auraEffect.EffectInfo.AuraEffectType, auraEffect);
        }

        private void RemoveNonStackableAuras(Aura aura)
        {
            for (int i = AuraApplications.Count - 1; i >= 0; i--)
                if (!AuraApplications[i].Aura.CanStackWith(aura))
                    RemoveAura(AuraApplications[i], AuraRemoveMode.Default);
        }

        private void RemoveOwnedAura(Aura aura, AuraRemoveMode removeMode)
        {
            ownedAuras.Remove(aura);

            aura.Remove(removeMode);
        }

        internal void RemoveAura(AuraApplication application, AuraRemoveMode mode)
        {
            if (!application.IsRemoved)
            {
                UnapplyAuraApplication(application, mode);

                if (application.Aura.Owner == this)
                    RemoveOwnedAura(application.Aura, mode);
            }
        }

        internal void RemoveAura(Aura aura, AuraRemoveMode mode)
        {
            if (aura.IsRemoved && aura.ApplicationsByTargetId.TryGetValue(Id, out AuraApplication auraApplication))
                RemoveAura(auraApplication, mode);
        }

        internal bool HasAuraType(AuraEffectType auraEffectType)
        {
            return auraEffectsByAuraType.ContainsKey(auraEffectType);
        }

        #endregion

        private void OnEntityDetach(Unit entity)
        {
            if (targetId == entity.Id || Target == entity)
                UpdateTarget(updateState: true);
        }

        private void OnDeathStateChanged()
        {
            deathState = (DeathState)EntityState.DeathState;
        }

        private void OnHealthStateChanged()
        {
            SetHealth(EntityState.Health);
        }

        private void OnTargetIdChanged()
        {
            UpdateTarget(EntityState.TargetId.PackedValue);
        }

        private void OnFactionChanged()
        {
            faction = Balance.FactionsById[EntityState.Faction.Id];

            EventHandler.ExecuteEvent(this, GameEvents.UnitFactionChanged);
        }
    }
}

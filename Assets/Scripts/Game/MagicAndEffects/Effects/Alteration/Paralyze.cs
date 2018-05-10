﻿// Project:         Daggerfall Tools For Unity
// Copyright:       Copyright (C) 2009-2018 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Gavin Clayton (interkarma@dfworkshop.net)
// Contributors:    
// 
// Notes:
//

using UnityEngine;
using DaggerfallConnect;
using DaggerfallWorkshop.Game.Entity;

namespace DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects
{
    /// <summary>
    /// Paralyze
    /// </summary>
    public class Paralyze : IncumbentEffect
    {
        const string textDatabase = "ClassicEffects";

        public override void SetProperties()
        {
            properties.Key = "Paralyze";
            properties.ClassicKey = MakeClassicKey(0, 255);
            properties.GroupName = TextManager.Instance.GetText("ClassicEffects", "paralyze");
            properties.SubGroupName = string.Empty;
            properties.SpellMakerDescription = DaggerfallUnity.Instance.TextProvider.GetRSCTokens(1502);
            properties.SpellBookDescription = DaggerfallUnity.Instance.TextProvider.GetRSCTokens(1202);
            properties.SupportDuration = true;
            properties.SupportChance = true;
            properties.AllowedTargets = EntityEffectBroker.TargetFlags_Other;
            properties.AllowedElements = EntityEffectBroker.ElementFlags_All;
            properties.MagicSkill = DFCareer.MagicSkills.Alteration;
            properties.DurationCosts = MakeEffectCosts(28, 100);
            properties.ChanceCosts = MakeEffectCosts(28, 100);
        }

        public override void Start(EntityEffectManager manager, DaggerfallEntityBehaviour caster = null)
        {
            base.Start(manager, caster);
            PlayerAggro();
        }

        public override void MagicRound()
        {
            base.MagicRound();
            StartParalyzation();
        }

        public override void Resume(EntityEffectManager.EffectSaveData_v1 effectData, EntityEffectManager manager, DaggerfallEntityBehaviour caster = null)
        {
            base.Resume(effectData, manager, caster);
            StartParalyzation();
            ShowPlayerParalyzed();
        }

        public override void End()
        {
            base.End();
            StopParalyzation();
        }

        protected override void BecomeIncumbent()
        {
            ShowPlayerParalyzed();
        }

        protected override bool IsLikeKind(IncumbentEffect other)
        {
            return (other is Paralyze);
        }

        protected override void AddState(IncumbentEffect incumbent)
        {
            // Stack my rounds onto incumbent
            incumbent.RoundsRemaining += RoundsRemaining;
            ShowPlayerParalyzed();
        }

        void ShowPlayerParalyzed()
        {
            // Output "You are paralyzed." if the host manager is player
            if (manager.EntityBehaviour == GameManager.Instance.PlayerEntityBehaviour)
                DaggerfallUI.AddHUDText(TextManager.Instance.GetText(textDatabase, "youAreParalyzed"), 1.5f);
        }

        void StartParalyzation()
        {
            // Get peered entity gameobject
            DaggerfallEntityBehaviour entityBehaviour = GetPeeredEntityBehaviour(manager);
            if (!entityBehaviour)
                return;

            entityBehaviour.Entity.IsParalyzed = true;
            PlayerAggro();
        }

        void StopParalyzation()
        {
            // Get peered entity gameobject
            DaggerfallEntityBehaviour entityBehaviour = GetPeeredEntityBehaviour(manager);
            if (!entityBehaviour)
                return;

            entityBehaviour.Entity.IsParalyzed = false;
        }
    }
}

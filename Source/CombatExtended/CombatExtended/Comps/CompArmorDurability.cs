﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;
using System.Xml;
using UnityEngine;
using Verse.AI;

namespace CombatExtended
{
    /// The file has a few classes but they are all extremely closely related to splitting them to files would only hinder working on it.
     
    public class ERAComponent : IExposable
    {
        public BodyPartDef part;

        public float armor;

        public float damageTreshold;

        public float APTreshold;

        public bool triggered = false;

        public CompProperties_Fragments frags;

        public List<DamageDef> ignoredDmgDefs;

        public CompFragments fragComp => new CompFragments() { props = frags };

        public void LoadDataFromXmlCustom(XmlNode xmlRoot)
        {
            foreach (XmlNode node in xmlRoot.ChildNodes)
            {
                switch (node.Name.ToLower())
                {
                    case "armor":
                        armor = ParseHelper.ParseFloat(node.InnerText);
                        break;
                    case "damagetreshold":
                        damageTreshold = ParseHelper.ParseFloat(node.InnerText);
                        break;
                    case "aptreshold":
                        damageTreshold = ParseHelper.ParseFloat(node.InnerText);
                        break;
                    case "part":
                        DirectXmlCrossRefLoader.RegisterObjectWantsCrossRef(this, "part", node.InnerText, null, null);
                        break;
                    case "triggered":
                        triggered = ParseHelper.ParseBool(node.InnerText);
                        break;
                    case "frags":
                        frags = new CompProperties_Fragments() { fragments = new List<ThingDefCountClass>() };

                        foreach (XmlNode node2 in node.ChildNodes)
                        {
                            if (node2.Name == "fragments")
                            {
                                foreach (XmlNode node3 in node2.ChildNodes)
                                {
                                    ThingDefCountClass count = new ThingDefCountClass();

                                    count.LoadDataFromXmlCustom(node3);

                                    frags.fragments.Add(count);
                                }
                            }
                            if (node2.Name == "fragSpeedFactor")
                            {
                                frags.fragSpeedFactor = ParseHelper.ParseFloat(node2.InnerText);
                            }
                        }
                        break;
                    case "ignoreddmgdefs":
                        ignoredDmgDefs = new List<DamageDef>();

                        foreach (XmlNode node2 in node.ChildNodes)
                        {
                            DirectXmlCrossRefLoader.RegisterListWantsCrossRef(ignoredDmgDefs, node2.InnerText);
                        }
                        break;
                }
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref triggered, "triggered");
        }
    }

    public class CompArmorDurability : ThingComp
    {
        public List<ERAComponent> ERA => this.parent.def.GetModExtension<ERAModExt>().ERA;

        public MechArmorDurabilityExt durabilityProps => this.parent.def.GetModExtension<MechArmorDurabilityExt>();

        public float maxDurability => durabilityProps.Durability;

        public float curDurability;
        public float curDurabilityPercent => (float)Math.Round(curDurability / maxDurability, 2);

        public bool regens = false;

        public int timer;

        public override void CompTick()
        {
            if (regens)
            {
                timer++;

                if (timer == durabilityProps.RegenInterval)
                {
                    if (curDurability < maxDurability)
                        curDurability += Mathf.Min(durabilityProps.RegenValue, maxDurability - curDurability);
                }
            }
            base.CompTick();
        }

        public override void PostPostMake()
        {
            curDurability = maxDurability;
            regens = durabilityProps.Regenerates;
            base.PostPostMake();
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look<bool>(ref regens, "regens", false);
            base.PostExposeData();
        }

        public override void PostPreApplyDamage(DamageInfo dinfo, out bool absorbed)
        {
            base.PostPreApplyDamage(dinfo, out absorbed);
            curDurability -= dinfo.Amount;
            if (curDurability < 0)
            {
                curDurability = 0;
            }
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (durabilityProps.Repairable)
            {
                yield return new FloatMenuOption("Fix natural armor", delegate
                {
                    selPawn.jobs.StartJob(new Job(
                        CE_JobDefOf.RepairNaturalArmor,
                        this.parent,
                        Find.CurrentMap.listerThings.AllThings.Find(x => x.def == durabilityProps.RepairIngredients.First().thingDef && x.stackCount >= durabilityProps.RepairIngredients.First().count)
                        ), JobCondition.InterruptForced);
                });
            }
        }
    }

    public class ERAModExt : DefModExtension
    {
        public List<ERAComponent> ERA;
    }

    public class MechArmorDurabilityExt : DefModExtension
    {
        public float Durability;

        public bool Regenerates;

        public float RegenInterval;

        public float RegenValue;

        public bool Repairable;

        public List<ThingDefCountClass> RepairIngredients;

        public int RepairTime;

        public float RepairValue;

        public bool CanOverHeal;

        public float MaxOverHeal;
    }

    public class JobDriver_RepairNaturalArmor : JobDriver
    {
        Pawn actor => GetActor();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            bool canReachTargetC = TargetC.Thing == null;

            if (!canReachTargetC)
                canReachTargetC = actor.CanReserveAndReach(TargetC, PathEndMode.ClosestTouch, Danger.Some);
            return actor.CanReserveAndReach(TargetA, PathEndMode.ClosestTouch, Danger.Some)
                && actor.CanReserveAndReach(TargetB, PathEndMode.ClosestTouch, Danger.Some)
                && (canReachTargetC)
                ;
        }

        public override IEnumerable<Toil> MakeNewToils()
        {
            var natArmor = (TargetA.Thing as ThingWithComps).TryGetComp<CompArmorDurability>();

            var TargetBThing = TargetB.Thing;

            var TargetCThing = TargetC.Thing;

            yield return Toils_Goto.GotoCell(TargetB.Cell, PathEndMode.ClosestTouch);

            yield return Toils_Haul.TakeToInventory(TargetIndex.B, natArmor.durabilityProps.RepairIngredients.First().count);

            if (TargetCThing != null)
            {
                yield return Toils_Goto.GotoCell(TargetB.Cell, PathEndMode.ClosestTouch);

                yield return Toils_Haul.TakeToInventory(TargetIndex.B, natArmor.durabilityProps.RepairIngredients.Last().count);
            }

            yield return Toils_Goto.GotoThing(TargetIndex.A, TargetThingA.Position);

            var toilWait = Toils_General.Wait(natArmor.durabilityProps.RepairTime).WithProgressBarToilDelay(TargetIndex.A);

            toilWait.AddFinishAction(
                delegate 
                {
                    TargetBThing.Destroy();
                    if (TargetCThing != null)
                    {
                        TargetCThing.Destroy();
                    }
                    natArmor.curDurability += natArmor.durabilityProps.RepairValue;
                    if (natArmor.durabilityProps.CanOverHeal)
                    {
                        if (natArmor.curDurability > natArmor.durabilityProps.MaxOverHeal + natArmor.maxDurability)
                        {
                            natArmor.curDurability = natArmor.maxDurability + natArmor.durabilityProps.MaxOverHeal;
                        }
                        else
                        {
                            natArmor.curDurability += natArmor.durabilityProps.RepairValue;
                        }
                       
                    }
                    else
                    {
                        if (natArmor.curDurability > natArmor.maxDurability)
                        {
                            natArmor.curDurability = natArmor.maxDurability;
                        }
                        else
                        {
                            natArmor.curDurability += natArmor.durabilityProps.RepairValue;
                        }
                    }
                       
                });

            yield return toilWait;

        }
    }
}

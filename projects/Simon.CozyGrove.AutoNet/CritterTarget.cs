using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System;
using System.Collections;

namespace Simon.CozyGrove.AutoNet
{
    // Critter discovery (with new-critter filter) and the catch/collect sequence.
    public partial class MyMod
    {
        private Critter FindNearestCritter(AvatarController avatar)
        {
            var critters = UnityEngine.Object.FindObjectsOfType<Critter>();
            Critter bestCritter = null;
            float bestDistSq = float.MaxValue;
            Vector3 avatarPos = avatar.transform.position;

            CollectionsState playerCollections = null;
            if (_findNewCritterOnly)
                playerCollections = Il2Cpp.Main.Instance?.GameState?.PlayerCollections;

            foreach (var critter in critters)
            {
                if (critter == null || !critter.gameObject.activeInHierarchy) continue;

                if (_findNewCritterOnly)
                {
                    if (playerCollections == null)
                    {
                        MelonLogger.Warning("[NewCritter] PlayerCollections not available — filter skipped.");
                    }
                    else
                    {
                        string caughtItemId = critter.config?.caughtItem;
                        if (string.IsNullOrEmpty(caughtItemId)) continue;
                        if (!playerCollections.CanBeDonated(new ConfigID(caughtItemId))) continue;
                    }
                }

                float distSq = (critter.transform.position - avatarPos).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestCritter = critter;
                }
            }
            return bestCritter;
        }

        private IEnumerator HandleTarget(AvatarController avatar, Critter target)
        {
            // Walk to target if too far
            float dist = Vector3.Distance(avatar.transform.position, target.transform.position);
            if (dist > CatchDistance)
            {
                // Follow critter while walking
                Func<Vector3> trackFunc = () => {
                    if (target != null && target.gameObject.activeInHierarchy)
                        return target.transform.position;
                    return avatar.transform.position;
                };

                avatar.WalkToPosition(target.transform.position, true, false, CatchDistance * 0.8f, trackFunc);

                float timeout = CatchTimeout;
                yield return null; // Wait 1 frame so walk action is registered
                while (target != null && target.gameObject.activeInHierarchy &&
                       Vector3.Distance(avatar.transform.position, target.transform.position) > CatchDistance && timeout > 0)
                {
                    if (!_isActive) yield break;

                    // If avatar stopped but critter moved away, break rather than stalling 10 seconds
                    if (!avatar.actionsController.HasAnyActions()) break;

                    if (!avatar.speechBubble.isShown) ShowBark(avatar, "AutoNet: ON");

                    timeout -= Time.deltaTime;
                    yield return null;
                }
            }

            // Verify target still valid after walking
            if (target == null || !target.gameObject.activeInHierarchy) yield break;

            // If still too far (critter moved away), abort and let the coroutine pick a fresh target
            if (Vector3.Distance(avatar.transform.position, target.transform.position) > CatchDistance)
                yield break;

            // Ensure net is equipped before swinging
            if (!HasNetEquipped(avatar))
            {
                TryEquipNet(avatar);
                yield return new WaitForSeconds(0.5f);
                if (!HasNetEquipped(avatar)) yield break;
            }

            // Interact using the high-level method if possible, otherwise manual throw
            var harvestable = target.GetComponent<HarvestableItem>();
            if (harvestable != null)
            {
                harvestable.Interact(avatar, null, null);
            }
            else
            {
                Vector3 throwForce = (target.transform.position - avatar.transform.position).normalized * 5f;
                var catchAction = new AvatarActionCritterCatching(avatar, avatar.activeItem, throwForce);
                avatar.actionsController.Add(catchAction.Cast<IAvatarAction>());
            }

            // Wait for action to finish then collect doobers
            yield return new WaitForSeconds(1.5f);
            while (IsAvatarBusy(avatar)) yield return null;
            yield return new WaitForSeconds(0.5f);

            var doobers = UnityEngine.Object.FindObjectsOfType<Doober>();
            foreach (var doober in doobers)
            {
                if (doober != null && doober.gameObject.activeInHierarchy)
                    doober.Pickup();
            }
        }
    }
}

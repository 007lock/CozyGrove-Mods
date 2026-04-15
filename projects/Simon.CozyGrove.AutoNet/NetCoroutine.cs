using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System.Collections;

namespace Simon.CozyGrove.AutoNet
{
    // Main coroutine loop: scouts for critters, bids, locks, and delegates to HandleTarget.
    public partial class MyMod
    {
        private IEnumerator AutoNetCoro()
        {
            while (true)
            {
                if (!_isActive || !_isInGameScene)
                {
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                var avatar = GetAvatar();
                if (avatar == null)
                {
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                if (IsAvatarBusy(avatar))
                {
                    AutonomyManager.ClearBid(avatar, "AutoNet");
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                // Ensure net is equipped while idle
                if (!HasNetEquipped(avatar))
                {
                    TryEquipNet(avatar);
                    yield return new WaitForSeconds(1.0f);
                    continue;
                }

                _targetCritter = FindNearestCritter(avatar);
                if (_targetCritter != null)
                {
                    float dist = Vector3.Distance(avatar.transform.position, _targetCritter.transform.position);
                    AutonomyManager.UpdateBid(avatar, "AutoNet", dist);

                    yield return new WaitForSeconds(0.2f);

                    if (avatar == null || !avatar.gameObject.activeInHierarchy ||
                        IsAvatarBusy(avatar) ||
                        !AutonomyManager.IsMyBidLowest(avatar, "AutoNet", dist))
                    {
                        AutonomyManager.ClearBid(avatar, "AutoNet");
                        continue;
                    }

                    AutonomyManager.ClearBid(avatar, "AutoNet");
                    AutonomyManager.AcquireLock(avatar, "AutoNet");

                    yield return HandleTarget(avatar, _targetCritter);

                    AutonomyManager.ReleaseLock(avatar, "AutoNet");
                }
                else
                {
                    AutonomyManager.ClearBid(avatar, "AutoNet");
                }

                yield return new WaitForSeconds(0.3f);
            }
        }
    }
}

using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Linq;

namespace Simon.CozyGrove.AutoNet
{
    public class MyMod : MelonMod
    {
        private float _checkTimer = 0f;
        private const float CheckInterval = 0.5f;
        private bool _isActive = false;
        
        // State machine enum
        private enum AutoNetState
        {
            Idle,
            WalkingToCritter,
            ThrowingNet,
            Collecting
        }

        private AutoNetState _currentState = AutoNetState.Idle;
        private Critter _targetCritter = null;
        private float _stateTimer = 0f;
        private IAvatarAction _currentCatchAction = null;

        // Configuration
        private const float CatchDistance = 2.0f; // Distance from critter to trigger net throw
        private const float CatchTimeout = 10f; // Max time to spend trying to catch one critter
        private const float CollectDelay = 1.0f; // Time to wait for doobers to spawn before collecting

        public override void OnUpdate()
        {
            if (SceneManager.GetActiveScene().name != "Game") return;

            // Toggle logic
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.T))
            {
                _isActive = !_isActive;
                if (!_isActive)
                {
                    ResetState();
                }
                MelonLogger.Msg($"AutoNet is now {(_isActive ? "ON" : "OFF")}");
            }

            if (!_isActive) return;

            var avatar = UnityEngine.Object.FindObjectOfType<AvatarController>();
            if (avatar == null) return;

            switch (_currentState)
            {
                case AutoNetState.Idle:
                    UpdateIdle(avatar);
                    break;
                case AutoNetState.WalkingToCritter:
                    UpdateWalkingToCritter(avatar);
                    break;
                case AutoNetState.ThrowingNet:
                    UpdateThrowingNet(avatar);
                    break;
                case AutoNetState.Collecting:
                    UpdateCollecting(avatar);
                    break;
            }
        }

        private void ResetState()
        {
            _currentState = AutoNetState.Idle;
            _targetCritter = null;
            _currentCatchAction = null;
            _stateTimer = 0f;
        }

        private void UpdateIdle(AvatarController avatar)
        {
            _checkTimer += Time.deltaTime;
            if (_checkTimer < CheckInterval) return;
            _checkTimer = 0f;

            // Find nearest active critter
            var critters = UnityEngine.Object.FindObjectsOfType<Critter>();
            Critter bestCritter = null;
            float bestDistSq = float.MaxValue;
            Vector3 avatarPos = avatar.transform.position;

            foreach (var critter in critters)
            {
                if (critter != null && critter.gameObject.activeInHierarchy)
                {
                    float distSq = (critter.transform.position - avatarPos).sqrMagnitude;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestCritter = critter;
                    }
                }
            }

            if (bestCritter != null)
            {
                _targetCritter = bestCritter;
                _currentState = AutoNetState.WalkingToCritter;
                _stateTimer = 0f;
                MelonLogger.Msg($"Found critter, tracking...");
                
                // Track func for WalkToPosition
                Func<Vector3> trackFunc = new Func<Vector3>(() => 
                {
                    if (_targetCritter != null && _targetCritter.gameObject.activeInHierarchy)
                        return _targetCritter.transform.position;
                    return avatar.transform.position;
                });

                // Tell avatar to walk towards it
                avatar.WalkToPosition(_targetCritter.transform.position, true, false, CatchDistance * 0.8f, trackFunc);
            }
        }

        private void UpdateWalkingToCritter(AvatarController avatar)
        {
            _stateTimer += Time.deltaTime;

            if (_targetCritter == null || !_targetCritter.gameObject.activeInHierarchy || _stateTimer > CatchTimeout)
            {
                MelonLogger.Msg($"Lost critter or timed out while walking.");
                ResetState();
                return;
            }

            float dist = Vector3.Distance(avatar.transform.position, _targetCritter.transform.position);
            if (dist <= CatchDistance)
            {
                MelonLogger.Msg($"In range ({dist:F1}m), throwing net!");
                ThrowNetAtCritter(avatar, _targetCritter);
            }
        }

        private void ThrowNetAtCritter(AvatarController avatar, Critter critter)
        {
            // Use active net or find one in inventory
            var activeItem = avatar.activeItem;
            if (activeItem == null || !activeItem.isNet)
            {
                // Search slots in PlayerInventory (which inherits from InventoryState)
                Item netItem = null;
                var slots = avatar.inventory.slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (slot != null && slot.item != null && slot.item.isNet)
                    {
                        netItem = slot.item;
                        break;
                    }
                }

                if (netItem != null)
                {
                    MelonLogger.Msg("Equipping net from inventory...");
                    avatar.inventory.UseItem(netItem, false);
                    activeItem = netItem;
                }
                else
                {
                    MelonLogger.Error("No net found in hands or inventory!");
                    ResetState();
                    return;
                }
            }

            // Estimate throw force
            Vector3 throwForce = (critter.transform.position - avatar.transform.position).normalized * 5f;

            // Instantiate and queue the catch action
            var catchAction = new AvatarActionCritterCatching(avatar, activeItem, throwForce);
            
            // Note: If you get a conversion error here, it's likely an Il2Cpp mismatch. 
            // In most MelonLoader environments, casting via .Cast<IAvatarAction>() is safer.
            avatar.actionsController.Add(catchAction.Cast<IAvatarAction>());
            
            _currentCatchAction = catchAction.Cast<IAvatarAction>();
            _currentState = AutoNetState.ThrowingNet;
            _stateTimer = 0f;
        }

        private void UpdateThrowingNet(AvatarController avatar)
        {
            _stateTimer += Time.deltaTime;

            // Check if the current action is still our catch action
            var current = avatar.actionsController.GetCurrent();
            
            // In Il2Cpp, comparing objects directly (current == _currentCatchAction) is usually fine
            // but we also check if current is null (action finished).
            bool isStillRunning = (current != null && current.Pointer == _currentCatchAction.Pointer);

            if (!isStillRunning || _stateTimer > 8f) 
            {
                MelonLogger.Msg("Catch action finished, collecting rewards...");
                _currentState = AutoNetState.Collecting;
                _stateTimer = 0f;
            }
        }

        private void UpdateCollecting(AvatarController avatar)
        {
            _stateTimer += Time.deltaTime;

            // Wait for doobers to spawn
            if (_stateTimer < CollectDelay) return;

            var doobers = UnityEngine.Object.FindObjectsOfType<Doober>();
            int collected = 0;
            foreach (var doober in doobers)
            {
                if (doober != null && doober.gameObject.activeInHierarchy)
                {
                    // Pickup is often internal/private in dump but unhollower makes it public or we use it anyway
                    doober.Pickup();
                    collected++;
                }
            }

            if (collected > 0)
                MelonLogger.Msg($"Auto-collected {collected} doober(s)");

            ResetState();
        }
    }
}

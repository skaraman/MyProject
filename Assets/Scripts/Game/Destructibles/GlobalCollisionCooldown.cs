using System;
using UnityEngine;

/// <summary>
/// Applies a short, pair-scoped cooldown to solid contacts between two
/// kinematic bodies. The cooldown is deliberately separate from trigger hit
/// processing so combat callbacks remain immediate.
/// </summary>
[DisallowMultipleComponent]
public sealed class GlobalCollisionCooldown : MonoBehaviour
{
    public const float DefaultCooldownSeconds = 0.01f;

    private const int MaxTrackedPairs = 128;

    [Tooltip("How long to ignore this kinematic collider pair after a solid contact")]
    [Min(0f)]
    public float cooldownDuration = DefaultCooldownSeconds;

    private struct CooldownEntry
    {
        public Collider2D first;
        public Collider2D second;
        public float expiresAt;
        public bool restoreCollision;
    }

    private static readonly CooldownEntry[] entries = new CooldownEntry[MaxTrackedPairs];
    private static readonly Action processExpiredCallback = ProcessExpired;
    private static int entryCount;
    private static bool expiryRunnerRegistered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Array.Clear(entries, 0, entries.Length);
        entryCount = 0;
        expiryRunnerRegistered = false;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryApply(collision, cooldownDuration);
    }

    /// <summary>
    /// Applies the default cooldown to a collision observed by one of the
    /// gameplay collision paths. This lets kinematic pairs be covered even
    /// when neither object carries this optional component.
    /// </summary>
    public static bool TryApply(Collision2D collision)
    {
        return TryApply(collision, DefaultCooldownSeconds);
    }

    /// <summary>
    /// Applies a pair cooldown only when both colliders belong to different
    /// kinematic rigidbodies. Returns true when the pair was accepted.
    /// </summary>
    public static bool TryApply(Collision2D collision, float durationSeconds)
    {
        if (collision == null || durationSeconds <= 0f)
        {
            return false;
        }

        var first = collision.collider;
        var second = collision.otherCollider;
        if (first == null || second == null || !first.enabled || !second.enabled)
        {
            return false;
        }

        var firstBody = first.attachedRigidbody;
        var secondBody = second.attachedRigidbody;
        if (firstBody == null || secondBody == null ||
            firstBody == secondBody ||
            firstBody.bodyType != RigidbodyType2D.Kinematic ||
            secondBody.bodyType != RigidbodyType2D.Kinematic)
        {
            return false;
        }

        var now = Time.unscaledTime;
        PruneExpired(now);

        for (var i = 0; i < entryCount; i++)
        {
            ref var entry = ref entries[i];
            if (!IsSamePair(entry.first, entry.second, first, second))
            {
                continue;
            }

            var expiresAt = now + durationSeconds;
            if (expiresAt > entry.expiresAt)
            {
                entry.expiresAt = expiresAt;
            }
            return true;
        }

        var slot = FindFreeSlot();
        var restoreCollision = !Physics2D.GetIgnoreCollision(first, second);
        Physics2D.IgnoreCollision(first, second, true);
        entries[slot] = new CooldownEntry
        {
            first = first,
            second = second,
            expiresAt = now + durationSeconds,
            restoreCollision = restoreCollision
        };
        if (slot == entryCount)
        {
            entryCount++;
        }

        EnsureExpiryRunner();
        return true;
    }

    private static bool IsSamePair(Collider2D first, Collider2D second, Collider2D candidateFirst, Collider2D candidateSecond)
    {
        return (first == candidateFirst && second == candidateSecond) ||
               (first == candidateSecond && second == candidateFirst);
    }

    private static int FindFreeSlot()
    {
        if (entryCount < entries.Length)
        {
            return entryCount;
        }

        // The array is intentionally bounded. If all slots are occupied,
        // replace the pair that will expire first rather than growing during
        // a contact burst.
        var oldestIndex = 0;
        var oldestExpiry = entries[0].expiresAt;
        for (var i = 1; i < entries.Length; i++)
        {
            if (entries[i].expiresAt < oldestExpiry)
            {
                oldestIndex = i;
                oldestExpiry = entries[i].expiresAt;
            }
        }

        RestoreEntry(oldestIndex);
        return oldestIndex;
    }

    private static void EnsureExpiryRunner()
    {
        if (expiryRunnerRegistered || !Application.isPlaying)
        {
            return;
        }

        RuntimeUpdateHub.Register(
            90,
            "RuntimeUpdateHub.KinematicCollisionCooldown",
            processExpiredCallback
        );
        expiryRunnerRegistered = true;
    }

    private static void ProcessExpired()
    {
        if (entryCount == 0)
        {
            if (expiryRunnerRegistered)
            {
                RuntimeUpdateHub.Unregister(processExpiredCallback);
                expiryRunnerRegistered = false;
            }
            return;
        }

        PruneExpired(Time.unscaledTime);
        if (entryCount == 0 && expiryRunnerRegistered)
        {
            RuntimeUpdateHub.Unregister(processExpiredCallback);
            expiryRunnerRegistered = false;
        }
    }

    private static void PruneExpired(float now)
    {
        for (var i = entryCount - 1; i >= 0; i--)
        {
            if (entries[i].first != null &&
                entries[i].second != null &&
                now < entries[i].expiresAt)
            {
                continue;
            }

            RestoreEntry(i);
            var lastIndex = entryCount - 1;
            entries[i] = entries[lastIndex];
            entries[lastIndex] = default;
            entryCount = lastIndex;
        }
    }

    private static void RestoreEntry(int index)
    {
        var entry = entries[index];
        if (!entry.restoreCollision || entry.first == null || entry.second == null)
        {
            return;
        }

        Physics2D.IgnoreCollision(entry.first, entry.second, false);
    }
}

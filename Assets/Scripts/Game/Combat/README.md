# Hit and Hurt Box System

This document describes the simplified hit and hurt box system for combat interactions.

## Overview

The system follows these basic principles:
- **HitBox2D** detects contact with HurtBox2D and forwards the hit.
- **HurtBox2D** validates the hit, invokes OnHit with context, and can trigger DestructionManager.
- Characters and enemies have one HurtBox2D and one or more HitBox2D under "HBOXES".
- HitBoxes and HurtBoxes exist on the same layer.

## Components

### HitBox2D

The HitBox detects collisions with HurtBoxes and forwards them to HurtBox2D.

**Key Features:**
- Detects collisions with HurtBox2D components
- Forwards contacts to `HurtBox2D.ReceiveHit`
- Optional `hitId` for filtering special hits
- Prevents self-hits via `ignoreSameRoot`
- Optionally prevents hitting the same HurtBox multiple times via `hitEachHurtBoxOnce`
- Optional cooldown window between hits via `hitCooldown`

**Inspector Properties:**
- `ignoreSameRoot`: If true, ignores contacts with colliders on the same root object (prevents self-hits)
- `hitEachHurtBoxOnce`: If true, each HurtBox2D can only be hit once while this component is enabled
- `hitCooldown`: Minimum time in seconds between successful hits (0 disables cooldown)
- `hitId`: Optional identifier for filtering hit reactions (attack name/type)

### HurtBox2D

The HurtBox receives hits from HitBoxes, validates them, and runs game logic.

**Key Features:**
- Receives hits via the `ReceiveHit()` method (called by HitBox2D)
- Validates that both components are active
- Optional attacker filtering via `ignoreEnemyHitBoxes`
- Invokes `OnHit` with HitBox2D context
- Can trigger `DestructionManager.LaunchRandom` after a validated hit

**Inspector Properties:**
- `ignoreEnemyHitBoxes`: If true, ignores hits from hitboxes under an EnemyInfo (useful for enemies)
- `OnHit`: UnityEvent called on a validated hit (HitBox2D arg)
- `launchRandomOnHit`: If true, calls `DestructionManager.LaunchRandom`

## Setup Instructions

### For Characters/Enemies:

1. **Root GameObject** - Enemies should have `EnemyInfo` and `DestructionManager`
2. **HurtBox** - Add a single `HurtBox2D` component with a Collider2D
3. **HitBoxes Parent** - Create a child GameObject named "HBOXES" to contain all HitBoxes
4. **HitBoxes** - Add `HitBox2D` components with Collider2D to child objects under "HBOXES"
5. **Filtering** - Set `hitId` on HitBoxes and branch in your OnHit handler if needed
6. **Enemy Filter** - Enable `ignoreEnemyHitBoxes` on enemy HurtBoxes if enemies should not hit each other

Example hierarchy:
```
Enemy (EnemyInfo + DestructionManager)
├── HurtBox (HurtBox2D + Collider2D)
└── HBOXES
    ├── HitBox1 (HitBox2D + Collider2D, hitId="Light")
    └── HitBox2 (HitBox2D + Collider2D, hitId="Heavy")
```

### Layer Configuration:

- Ensure HitBoxes and HurtBoxes are on the same layer
- Configure Unity's Physics2D collision matrix to allow this layer to collide with itself

## Usage Examples

### Example 1: Basic Hit Detection

```csharp
public class EnemyHealth : MonoBehaviour {
    private HurtBox2D hurtBox;

    void Awake() {
        hurtBox = GetComponentInChildren<HurtBox2D>();
        hurtBox.OnHit.AddListener(OnHit);
    }

    void OnHit(HitBox2D hitBox) {
        Debug.Log($"Hit id: {hitBox.hitId}");
    }
}
```

### Example 2: Enemy or Hit Specific Reactions (Code)

```csharp
public class EnemySpecialHit : MonoBehaviour {
    public void OnHitSpecial(HitBox2D hitBox) {
        var info = GetComponentInParent<EnemyInfo>();
        if (info != null && info.enemyType == "Imp" && hitBox.hitId == "Heavy") {
            Debug.Log("Imp heavy hit!");
        }
    }
}
```

## Migration from Old System

Removed:
- `HitManager` and `IHitManager` (no manager layer now)
- `HitContactResponder` (logic lives in HurtBox2D)
- `HitBox2D.OnHit` event (use `HurtBox2D.OnHit` with HitBox2D arg instead)
- Older clash/contact events and layer fields

## Design Rationale

1. **Single Responsibility**: HitBoxes detect; HurtBoxes validate and react.
2. **Fewer Components**: Reduced to two runtime scripts for hits.
3. **Simple Filtering**: Use `hitId` and EnemyInfo in your OnHit handler.
4. **Per-Enemy Logic**: Handle it in your OnHit handler or per-enemy script.
5. **Built-in Destruction**: LaunchRandom is handled inside HurtBox2D when enabled.

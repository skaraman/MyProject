# Hit and Hurt Box System

This document describes the simplified hit and hurt box system for combat interactions.

## Overview

The system follows these basic principles:
- **HitBox** - A box that sends a contact of "HitTrue" to a general manager with the details of itself and the collider it hit
- **HurtBox** - A box that can receive hits and will validate that the HitBox contact is true
- Characters and enemies have one HurtBox and one or more HitBoxes on a parent object called "HBOXES"
- Character and enemy boxes exist on the same layer

## Components

### HitBox2D

The HitBox is responsible for detecting collisions with HurtBoxes and notifying relevant parties.

**Key Features:**
- Detects collisions with HurtBox2D components
- Sends hit contacts to a general manager (IHitManager) if one exists in parent hierarchy
- Invokes local OnHit event for direct listeners
- Prevents self-hits via `ignoreSameRoot` option
- Optionally prevents hitting the same HurtBox multiple times via `hitEachHurtBoxOnce`

**Inspector Properties:**
- `ignoreSameRoot`: If true, ignores contacts with colliders on the same root object (prevents self-hits)
- `hitEachHurtBoxOnce`: If true, each HurtBox2D can only be hit once while this component is enabled
- `OnHit`: UnityEvent called when this hitbox makes contact with a HurtBox2D

### HurtBox2D

The HurtBox receives hits from HitBoxes and validates them.

**Key Features:**
- Receives hits via the `ReceiveHit()` method (called by HitBox2D)
- Validates that the hit is legitimate (both components are active)
- Invokes OnHit event when a validated hit is received

**Inspector Properties:**
- `OnHit`: UnityEvent called when this hurtbox is hit by a HitBox2D and validates the hit

### IHitManager (Interface)

An interface for components that need to be notified of hit contacts.

**Methods:**
- `OnHitContact(HitBox2D hitBox, HurtBox2D hurtBox)`: Called when a HitBox makes contact with a HurtBox

### HitManager (Component)

A basic implementation of IHitManager that can be attached to characters and enemies.

**Features:**
- Implements IHitManager interface
- Provides a UnityEvent that fires when any HitBox under this manager makes contact with a HurtBox
- Can be used to centralize hit logic for a character or enemy

**Inspector Properties:**
- `OnHitContact`: UnityEvent called when any HitBox under this manager makes contact with a HurtBox

## Setup Instructions

### For Characters/Enemies:

1. **Root GameObject** - Add a `HitManager` component to the character/enemy root GameObject
2. **HurtBox** - Add a single `HurtBox2D` component with a Collider2D to represent the damageable area
3. **HitBoxes Parent** - Create a child GameObject named "HBOXES" to contain all HitBoxes
4. **HitBoxes** - Add `HitBox2D` components with Collider2D to child objects under "HBOXES" for attack areas

Example hierarchy:
```
Character (HitManager)
├── HurtBox (HurtBox2D + Collider2D)
└── HBOXES
    ├── HitBox1 (HitBox2D + Collider2D)
    ├── HitBox2 (HitBox2D + Collider2D)
    └── HitBox3 (HitBox2D + Collider2D)
```

### Layer Configuration:

- Ensure HitBoxes and HurtBoxes are on the same layer
- Configure Unity's Physics2D collision matrix to allow this layer to collide with itself

## Usage Examples

### Example 1: Basic Hit Detection

```csharp
// On the HitManager component (attached to character root)
public class MyCharacterController : MonoBehaviour {
    void Start() {
        var hitManager = GetComponent<HitManager>();
        hitManager.OnHitContact.AddListener(OnMyHitContact);
    }

    void OnMyHitContact(HitBox2D hitBox, HurtBox2D hurtBox) {
        Debug.Log($"My HitBox hit a HurtBox!");
        // Apply damage, effects, etc.
    }
}
```

### Example 2: Responding to Being Hit

```csharp
// On the HurtBox component
public class MyCharacterHealth : MonoBehaviour {
    void Start() {
        var hurtBox = GetComponent<HurtBox2D>();
        hurtBox.OnHit.AddListener(OnGotHit);
    }

    void OnGotHit(HitBox2D hitBox) {
        Debug.Log($"I was hit by a HitBox!");
        // Take damage, play effects, etc.
    }
}
```

### Example 3: Custom Hit Manager

```csharp
// Custom implementation of IHitManager
public class CustomCombatManager : MonoBehaviour, IHitManager {
    public void OnHitContact(HitBox2D hitBox, HurtBox2D hurtBox) {
        // Custom hit logic
        var attacker = hitBox.GetComponentInParent<Character>();
        var defender = hurtBox.GetComponentInParent<Character>();
        
        // Calculate damage, apply effects, etc.
    }
}
```

## Migration from Old System

The old system had the following properties that have been removed:

### Removed from HitBox2D:
- `hittableLayers`: Removed - all boxes should be on the same layer
- `OnHitAny`: Removed - use `OnHit` instead
- `OnClash`: Removed - HitBoxes only interact with HurtBoxes now
- `OnClashAny`: Removed - HitBoxes only interact with HurtBoxes now

### Removed from HurtBox2D:
- `contactLayers`: Removed - all boxes should be on the same layer
- `ignoreSameRoot`: Removed - handled by HitBox2D now
- `OnHurtContact`: Removed - HurtBoxes don't detect HurtBox-to-HurtBox contacts
- `OnHitBoxContact`: Removed - HurtBoxes receive hits via ReceiveHit() only
- Collision detection methods: Removed - HurtBoxes are passive receivers now

## Design Rationale

### Why this simplification?

1. **Single Responsibility**: HitBoxes handle detection, HurtBoxes handle receiving. Clear separation of concerns.
2. **Reduced Complexity**: Removed redundant collision detection in HurtBox
3. **Centralized Management**: Hit contacts flow through a manager for easier system-wide handling
4. **Same Layer**: Simplifies Unity physics setup - no complex layer mask configuration needed
5. **Validation**: HurtBox validates hits, allowing for custom logic (invincibility, shields, etc.)

### When to use direct events vs. manager?

- **Direct Events** (OnHit on HitBox/HurtBox): Use for local, immediate responses (particles, sounds)
- **Manager Events** (HitManager.OnHitContact): Use for game logic (damage calculation, score tracking)

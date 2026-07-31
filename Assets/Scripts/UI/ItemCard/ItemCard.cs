using System;
using UnityEngine;

namespace Esperanza.UI
{
    public class ItemCard : MonoBehaviour
    {
        [Serializable]
        public sealed class TierMeterVisuals
        {
            public SpriteWithNormals bar;
            public SpriteWithNormals fill;
            public SpriteWithNormals knob;
        }

        const string BackgroundName = "Background";
        const string CategoryTextName = "CategoryText";
        const string ItemNameTextName = "ItemNameText";
        const string ItemIconName = "ItemIcon";
        const string SoulMeterName = "SoulMeter";
        const string BoostMeterName = "BoostMeter";
        const string BarName = "Bar";
        const string FillName = "Fill";
        const string KnobName = "Knob";
        const string StatsName = "Stats";

        [Header("Tier Visuals")]
        [SerializeField] SpriteWithNormals tierBackground;
        [SerializeField] TierMeterVisuals soulMeter = new TierMeterVisuals();
        [SerializeField] TierMeterVisuals boostMeter = new TierMeterVisuals();
        [SerializeField] Transform statsRoot;

        [Header("Backgrounds & Frames")]
        public SpriteRenderer backgroundImage;
        public SpriteRenderer innerBackgroundImage;
        public SpriteRenderer frameOuterImage;
        public SpriteRenderer frameInnerImage;
        public SpriteRenderer nameBackingImage;
        public SpriteRenderer imageBackingImage;
        public SpriteRenderer embellishmentImage;

        [Header("Meters")]
        public SpriteRenderer soulMeterFill;
        public SpriteRenderer boostMeterFill;

        [Header("Item Icon")]
        public SpriteRenderer itemIconImage;
        [SerializeField] SpriteWithNormals itemIconSprite;

        [Header("Text Fields")]
        public FontText categoryText;
        public FontText itemNameText;
        public FontText itemSlotText;
        
        [Header("Stats Texts")]
        public FontText damageText;
        public FontText attackSpeedText;
        public FontText durabilityText;

        void Awake()
        {
            ResolveAuthoredReferences();
        }

        public void SetupGear(GearItem gearItem, SpriteWithNormals itemIconSource)
        {
            ResolveAuthoredReferences();
            if (gearItem == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            ApplyTier(gearItem);
            ApplyBoostStats(gearItem);

            if (categoryText != null)
            {
                categoryText.content = gearItem.slot ?? "";
                categoryText.Generate();
            }
            if (itemNameText != null)
            {
                itemNameText.content = gearItem.name ?? "";
                itemNameText.Generate();
            }
            if (itemSlotText != null)
            {
                itemSlotText.content = gearItem.slot ?? "";
                itemSlotText.Generate();
            }
            ApplyItemIcon(itemIconSource);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        void ResolveAuthoredReferences()
        {
            soulMeter ??= new TierMeterVisuals();
            boostMeter ??= new TierMeterVisuals();
            if (tierBackground == null)
            {
                tierBackground = GetNamedComponent<SpriteWithNormals>(transform, BackgroundName);
            }
            ResolveMeterReferences(soulMeter, SoulMeterName);
            ResolveMeterReferences(boostMeter, BoostMeterName);
            if (statsRoot == null)
            {
                statsRoot = FindDirectChild(transform, StatsName);
            }
            if (categoryText == null)
            {
                categoryText = FindNamedFontText(CategoryTextName);
            }
            if (itemNameText == null)
            {
                itemNameText = FindNamedFontText(ItemNameTextName);
            }
            if (itemIconImage == null)
            {
                var icon = FindNamedTransform(ItemIconName);
                itemIconImage = icon != null ? icon.GetComponent<SpriteRenderer>() : null;
                itemIconSprite = icon != null ? icon.GetComponent<SpriteWithNormals>() : null;
            }
            else if (itemIconSprite == null)
            {
                itemIconSprite = itemIconImage.GetComponent<SpriteWithNormals>();
            }
        }

        void ApplyItemIcon(SpriteWithNormals source)
        {
            if (itemIconImage == null)
            {
                return;
            }

            var sourceRenderer = source != null ? source.GetComponent<SpriteRenderer>() : null;
            if (source == null || sourceRenderer == null)
            {
                itemIconImage.enabled = false;
                return;
            }

            if (itemIconSprite != null)
            {
                itemIconSprite.SetLibraryName(source.libraryName);
                itemIconSprite.SetLabelPrefix(source.labelPrefix);
                itemIconSprite.SetAnimation(source.category);
                itemIconSprite.SetIsAnimation(source.IsAnimation);
                itemIconSprite.SetDoNotRender(false);
                itemIconSprite.ForceUpdateSpriteAndNormal(source.LastRequestedFrame);
            }
            else
            {
                itemIconImage.sprite = sourceRenderer.sprite;
            }

            itemIconImage.color = sourceRenderer.color;
            itemIconImage.flipX = sourceRenderer.flipX;
            itemIconImage.flipY = sourceRenderer.flipY;
            itemIconImage.enabled = true;
        }

        void ApplyTier(GearItem gearItem)
        {
            var tier = gearItem != null && gearItem.TryGetTier(out var parsedTier)
                ? parsedTier
                : ItemTier.Basic;
            var tierName = tier.ToString();

            ApplyTierSprite(tierBackground, tierName);
            ApplyMeterTier(soulMeter, tier, tierName);
            ApplyMeterTier(boostMeter, tier, tierName);
        }

        static void ApplyMeterTier(TierMeterVisuals meter, ItemTier tier, string tierName)
        {
            if (meter == null)
            {
                return;
            }

            ApplyTierSprite(meter.bar, tierName);
            ApplyTierSprite(meter.fill, tierName);

            var hasKnob = tier == ItemTier.Basic ||
                tier == ItemTier.Magic ||
                tier == ItemTier.Legend;
            if (meter.knob != null)
            {
                meter.knob.gameObject.SetActive(hasKnob);
                if (hasKnob)
                {
                    ApplyTierSprite(meter.knob, tierName);
                }
            }
        }

        static void ApplyTierSprite(SpriteWithNormals sprite, string tierName)
        {
            if (sprite == null)
            {
                return;
            }

            sprite.SetAnimation(tierName);
            sprite.ForceUpdateSpriteAndNormal();
        }

        void ApplyBoostStats(GearItem gearItem)
        {
            if (statsRoot == null)
            {
                return;
            }

            var tierCapacity = gearItem != null && gearItem.TryGetTier(out var tier)
                ? ItemTierRules.GetBoostCount(tier)
                : 0;
            var boostCount = gearItem != null && gearItem.boosts != null
                ? gearItem.boosts.Count
                : 0;
            var visibleCount = Mathf.Min(tierCapacity, boostCount);

            for (var statNumber = 1; statNumber <= 8; statNumber++)
            {
                var row = FindDirectChild(statsRoot, "text" + statNumber);
                if (row == null) {
                    continue;
                }

                var isVisible = statNumber <= visibleCount;
                row.gameObject.SetActive(isVisible);
                if (!isVisible) {
                    continue;
                }

                var boost = gearItem.boosts[statNumber - 1];
                var statName = boost != null && !string.IsNullOrWhiteSpace(boost.statName)
                    ? boost.statName.Trim().ToUpperInvariant()
                    : "";
                var statLabel = Abbreviations.all.TryGetValue(statName, out var label)
                    ? label
                    : statName;
                var value = boost != null ? boost.value : 0f;
                var rowText = row.GetComponent<FontText>();
                if (rowText != null) {
                    rowText.content = statName + " (" + statLabel + ") +" + value.ToString("0.##");
                    rowText.Generate();
                }
            }
        }

        void ResolveMeterReferences(TierMeterVisuals meter, string meterName)
        {
            if (meter == null)
            {
                return;
            }

            var meterRoot = FindDirectChild(transform, meterName);
            var barRoot = FindDirectChild(meterRoot, BarName);
            if (meter.bar == null && barRoot != null)
            {
                meter.bar = barRoot.GetComponent<SpriteWithNormals>();
            }
            if (meter.fill == null)
            {
                meter.fill = GetNamedComponent<SpriteWithNormals>(barRoot, FillName);
            }
            if (meter.knob == null)
            {
                meter.knob = GetNamedComponent<SpriteWithNormals>(barRoot, KnobName);
            }
        }

        static T GetNamedComponent<T>(Transform parent, string objectName) where T : Component
        {
            var child = FindDirectChild(parent, objectName);
            return child != null ? child.GetComponent<T>() : null;
        }

        static Transform FindDirectChild(Transform parent, string objectName)
        {
            if (parent == null)
            {
                return null;
            }

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child != null && child.name == objectName)
                {
                    return child;
                }
            }

            return null;
        }

        FontText FindNamedFontText(string parentName)
        {
            var texts = GetComponentsInChildren<FontText>(includeInactive: true);
            for (var i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text != null && text.transform.parent != null &&
                    text.transform.parent.name == parentName)
                {
                    return text;
                }
            }

            return null;
        }

        Transform FindNamedTransform(string objectName)
        {
            var transforms = GetComponentsInChildren<Transform>(includeInactive: true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i] != null && transforms[i].name == objectName)
                {
                    return transforms[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Populates the UI elements of the item card.
        /// </summary>
        public void SetupCard(
            string category, 
            string itemName, 
            string itemSlot, 
            Sprite itemIcon,
            int damageMin, 
            int damageMax, 
            float attackSpeed, 
            int durabilityCurrent, 
            int durabilityMax, 
            float soulPercent, 
            float boostPercent)
        {
            if (categoryText != null) categoryText.content = category;
            if (itemNameText != null) itemNameText.content = itemName;
            if (itemSlotText != null) itemSlotText.content = itemSlot;
            
            if (itemIconImage != null && itemIcon != null)
            {
                itemIconImage.sprite = itemIcon;
                itemIconImage.enabled = true;
            }
            else if (itemIconImage != null)
            {
                itemIconImage.enabled = false;
            }

            if (damageText != null) damageText.content = $"Damage: {damageMin} - {damageMax}";
            if (attackSpeedText != null) attackSpeedText.content = $"Attack Speed: {attackSpeed:F2}";
            if (durabilityText != null) durabilityText.content = $"Durability: {durabilityCurrent} / {durabilityMax}";

            if (soulMeterFill != null) soulMeterFill.transform.localScale = new Vector3(1, soulPercent, 1);
            if (boostMeterFill != null) boostMeterFill.transform.localScale = new Vector3(1, boostPercent, 1);
        }
    }
}

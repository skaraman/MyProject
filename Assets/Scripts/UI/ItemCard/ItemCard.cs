using UnityEngine;

namespace Esperanza.UI
{
    public class ItemCard : MonoBehaviour
    {
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

        [Header("Text Fields")]
        public FontText categoryText;
        public FontText itemNameText;
        public FontText itemSlotText;
        
        [Header("Stats Texts")]
        public FontText damageText;
        public FontText attackSpeedText;
        public FontText durabilityText;

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

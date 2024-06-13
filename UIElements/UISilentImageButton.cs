﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;
using Terraria.UI.Chat;

namespace RecipeBrowser.UIElements
{
	// A bit confusing, don't use.
	class UIBadgedSilentImageButton : UISilentImageButton
	{
		internal bool drawX = false;
		public UIBadgedSilentImageButton(Asset<Texture2D> texture, string hoverText) : base(texture, hoverText) {
		}

		protected override void DrawSelf(SpriteBatch spriteBatch) {
			base.DrawSelf(spriteBatch);
			if (drawX) {
				CalculatedStyle dimensions = base.GetDimensions();
				//ChatManager.DrawColorCodedStringWithShadow(spriteBatch, Main.fontItemStack, "X", dimensions.Position() + new Vector2(14f, 10f), Color.LightSalmon, 0f, Vector2.Zero, new Vector2(0.7f));
				var r = dimensions.ToRectangle();
				r.Inflate(-2, -2);
				spriteBatch.Draw(TextureAssets.Cd.Value, r, null, Color.White, 0, Vector2.Zero, SpriteEffects.None, 0);
			}
		}
	}

	class UISilentImageButton : UIElement
	{
		private Asset<Texture2D> _texture;
		private float _visibilityActive = 1f;
		private float _visibilityHovered = .9f;
		private float _visibilityInactive = 0.8f; // or color? same thing?

		public bool selected;
		internal string hoverText;

		public UISilentImageButton(Asset<Texture2D> texture, string hoverText) {
			this._texture = texture;
			this.Width.Set((float)this._texture.Value.Width, 0f);
			this.Height.Set((float)this._texture.Value.Height, 0f);
			this.hoverText = hoverText;

			base.Recalculate();
		}

		public void SetImage(Asset<Texture2D> texture) {
			this._texture = texture;
			this.Width.Set((float)this._texture.Value.Width, 0f);
			this.Height.Set((float)this._texture.Value.Height, 0f);

			base.Recalculate();
		}

		protected override void DrawSelf(SpriteBatch spriteBatch) {
			if (selected) {
				var r = GetDimensions().ToRectangle();
				r.Inflate(0, 0);
				//spriteBatch.Draw(UIElements.UIRecipeSlot.selectedBackgroundTexture, r, Color.White);
				spriteBatch.Draw(TextureAssets.InventoryBack14.Value, r, Color.White);
			}

			CalculatedStyle dimensions = base.GetDimensions();
			spriteBatch.Draw(this._texture.Value, dimensions.Position(), Color.White * (selected ? _visibilityActive : (IsMouseHovering ? _visibilityHovered : this._visibilityInactive)));
			if (IsMouseHovering) {
				// Main.hoverItemName = hoverText;
				if (!string.IsNullOrWhiteSpace(hoverText))
					Terraria.ModLoader.UI.UICommon.TooltipMouseText(hoverText);
			}

			if (this == SharedUI.instance.ObtainableFilter.button && IsMouseHovering) {
				if(RecipeBrowser.instance.concurrentTasks.Count > 0)
					Terraria.ModLoader.UI.UICommon.TooltipMouseText($"{hoverText}\n{RecipeBrowser.instance.concurrentTasks.Count} recipes remain to be calculated");
				//spriteBatch.DrawString(FontAssets.MouseText.Value, UISystem.Instance.concurrentTasks.Count + "", dimensions.Position(), Color.White);
			}
		}

		public override void MouseOver(UIMouseEvent evt) {
			base.MouseOver(evt);
			//Main.PlaySound(12, -1, -1, 1, 1f, 0f);
		}

		//public void SetVisibility(float whenActive, float whenInactive)
		//{
		//	this._visibilityActive = MathHelper.Clamp(whenActive, 0f, 1f);
		//	this._visibilityInactive = MathHelper.Clamp(whenInactive, 0f, 1f);
		//}
	}
}

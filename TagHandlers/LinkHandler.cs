﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Text;
using Terraria.UI.Chat;
using Newtonsoft.Json.Linq;
using Terraria;
using ReLogic.Graphics;
using System.Net;
using System.IO;
using System;
using System.Diagnostics;

namespace RecipeBrowser.TagHandlers
{
	public class LinkTagHandler : ITagHandler
	{
		private class LinkSnippet : TextSnippet
		{
			private string url;

			public LinkSnippet(string url, string text)
				: base(text, Color.LightBlue, 1f)
			{
				this.CheckForHover = true;
				this.url = url;
			}

			public override void OnHover()
			{
				// Main.hoverItemName = url;
				if (!string.IsNullOrWhiteSpace(url))
					Terraria.ModLoader.UI.UICommon.TooltipMouseText(url);
			}

			public override void OnClick()
			{
				//Process.Start(url);
				string realLink = "https://" + url;
				Utils.OpenToURL(realLink);
			}

			public override Color GetVisibleColor()
			{
				return Color;
			}
		}

		TextSnippet ITagHandler.Parse(string text, Color baseColor, string options)
		{
			return new LinkTagHandler.LinkSnippet(options, text);
		}

		public static string GenerateTag(string url, string text)
		{
			// TODO: Make some sort of escape for ":" or fix Utils.OpenToURL needing https://
			return $"[l/{url}:{text}]";
		}
	}
}

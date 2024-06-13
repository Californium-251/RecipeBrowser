﻿using RecipeBrowser.TagHandlers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace RecipeBrowser
{
	internal class RecipePathTester
	{
		//internal static IdDictionary Search;
		internal static bool print = false;
		internal static bool printResults = false;
		//internal static bool printResultCounts = true;
		//internal static bool calculateAll = false;
		//internal static int calculateAllRepeat = 1;
		//internal static int inventoryChoice = 9; // doubles as test case.
		//internal static int TestRecipeCraftItem = -1;
		internal static bool thousandExtra = false;
	}

	public readonly record struct TileTypeMinPickPair(int tileType, int minPick);

	public record class ShopEntry(int npcType, NPCShop.Entry entry);

	internal static class RecipePath
	{
		internal static bool sourceInventory = true;
		internal static bool sourceBanks = false;
		internal static bool sourceChests = false;
		internal static bool sourceMagicStorage = false;

		internal static bool extendedCraft = false;

		internal static bool allowLoots = false;
		// seenBefore, killedBefore --> Damaged before? Keep track separately for non-banner npc? Use BestiaryUICollectionInfo.UnlockState maybe

		// Simplify logic by only doing terrain tiles.
		internal static bool allowMineables = false;
		internal static Dictionary<int, List<TileTypeMinPickPair>> mineables; // ItemID to List(TileID, minpick)....are there even any examples of 2 tiles dropping 1 item besides wood?
		//internal static Dictionary<int, int> mineableWalls; // ItemID to WallIDs
		internal static MethodInfo GetPickaxeDamageMethodInfo;

		internal static bool allowBugNetables = false;
		internal static Dictionary<int, int> bugNetables; // Item to NPC mapping
		//internal static Dictionary<int, int> bugNetablesSeen; // Item to NPC mapping, only seen NPC
		// TODO: Lavaproof Bug Net

		internal static bool allowMissingStations = false;

		internal static bool allowPurchasable = true;
		internal static Dictionary<int, List<ShopEntry>> purchasable; // Map Item to shop entries (NPCType, NPCShop.Entry). For faster lookup.

		// internal static HashSet<int> harvestable;

		// internal static bool enforceRecipeAvailable = false;

		//internal static bool allowMissingItems = false; // Allow missing but Seen items....items of mysterious origin? Harvested and Fished for now.
		//internal static int numberAllowedMissingItems = 1; // By count? no, ID? probably.
		//internal static bool limitMissingItemsToSeenItemChecklist = false;

		// internal static bool isCraftableOptimization = false; 
		//internal static bool enforceTiles = true; // Main.LocalPlayer.adjTile[tileID]    and  lava/honey/water?
		//internal static bool shortCircitIngredients = true; // 40 vs 49. Is it correct though?   --> 1000 extra: 281 vs 333
		//internal static int loopLimit = 17; // 12 has no one reaching limit..

		internal static void Refresh(bool complete = false) // refresh in-game when needed. Null out during unload.
		{
			purchasable = null;
			if (complete)
			{
				recipeDictionary = null;
				allowLoots = false;
				allowMissingStations = false;
				allowPurchasable = false;
			}
		}

		//internal static bool useRecipeDictionary = true; // Much faster.
		internal static Dictionary<int, List<Recipe>> recipeDictionary;
		internal static void InitializeRecipeDictionary()
		{
			recipeDictionary = new Dictionary<int, List<Recipe>>();
			for (int i = 0; i < Recipe.numRecipes; i++)
			{
				Recipe recipe = Main.recipe[i];
				List<Recipe> recipeList;
				if (!recipeDictionary.TryGetValue(recipe.createItem.type, out recipeList))
					recipeDictionary.Add(recipe.createItem.type, recipeList = new List<Recipe>());
				recipeList.Add(recipe);
			}
			// This removes all troublesome ingredients.
			// Ingredients from alchemy mods that just convert to ingredients used in a ton of recipes.
			var toRemove = new List<int>();
			foreach (var recipeKVP in recipeDictionary) {
				if (recipeKVP.Value.Count > 15)
					toRemove.Add(recipeKVP.Key);
			}
			toRemove.ForEach((key) => recipeDictionary.Remove(key));
		}

		internal static void Adjust(this Dictionary<int, int> d, int key, int adjustment)
		{
			int currentCount;
			d.TryGetValue(key, out currentCount);
			d[key] = currentCount + adjustment;
			if (d[key] <= 0)
				d.Remove(key);
		}

		internal static void PrepareGetCraftPaths()
		{
			//RecipePathTester.Search = ItemID.Search;
			if (recipeDictionary == null)
				InitializeRecipeDictionary();

			// Rather than simulating mining tile and tracking items, calculate placetile and styles of items and see if they exist in world?...nah.
			//HashSet<int> a = new HashSet<int>();
			//for (int i = 0; i < Main.maxTilesX; i++)
			//{
			//	for (int j = 0; j < Main.maxTilesY; j++)
			//	{
			//		if(Main.tile[i, j] != null)
			//		{
			//			a.Add(Main.tile[i, j].type);
			//		}
			//	}
			//}

			if (purchasable == null && allowPurchasable)
			{
				purchasable = new Dictionary<int, List<ShopEntry>>();
				foreach (var shop in NPCShopDatabase.AllShops.OfType<NPCShop>()) {
					foreach (var entry in shop.Entries) {
						List<ShopEntry> shopList;
						if (!purchasable.TryGetValue(entry.Item.type, out shopList))
							purchasable.Add(entry.Item.type, shopList = new List<ShopEntry>());
						shopList.Add(new ShopEntry(shop.NpcType, entry));
					}
				}
			}

			if (bugNetables == null && allowBugNetables) {
				bugNetables = new Dictionary<int, int>();
				for (int i = 1; i < ItemLoader.ItemCount; i++) {
					Item testItem = ContentSamples.ItemsByType[i];
					if (testItem.makeNPC > 0) {
						bugNetables[i] = testItem.makeNPC;
					}
				}
			}

			if (mineables == null && allowMineables) {
				// should mineables include drops from grass and other tiles that break on swing? Main.tileCut
				GetPickaxeDamageMethodInfo = typeof(Player).GetMethod("GetPickaxeDamage", BindingFlags.Instance | BindingFlags.NonPublic);
				mineables = new Dictionary<int, List<TileTypeMinPickPair>>();
				for (int i = 1; i < ItemLoader.ItemCount; i++) {
					Item testItem = ContentSamples.ItemsByType[i];
					// Want to tell user to mine Silver ore, not Silver bar. If user placed 1 silver bar, we'd want to forget that if mined so the recommendation is mine silver ore again. --> !Main.tileFrameImportant
					// Tiles with different styles complicate things, and aren't usually ingredients, but just furniture. Also Bars tile would require tracking each tile+style pair, which would make seenTiles more complicated. Not really needed.
					// Jungle Spores, for example, aren't covered by current logic.
					if (testItem.createTile > -1 && !Main.tileFrameImportant[testItem.createTile]) {
						// maybe only care about TileID.Sets.Ores?
						if (Main.tileCut[testItem.createTile]) {
							// Cobweb is in this category.
							List<TileTypeMinPickPair> tileMinPickPairs;
							if (!mineables.TryGetValue(i, out tileMinPickPairs))
								mineables.Add(i, tileMinPickPairs = new List<TileTypeMinPickPair>());
							tileMinPickPairs.Add(new(testItem.createTile, -1));
						}
						else {
							Tile tile = Main.tile[0, 0];
							tile.HasTile = true;
							tile.TileType = (ushort)testItem.createTile;
							for (int pickPower = 5; pickPower < 300; pickPower += 5) {
								int hitBufferIndex = Main.LocalPlayer.hitTile.HitObject(0, 0, 1);
								// private int GetPickaxeDamage(int x, int y, int pickPower, int hitBufferIndex, Tile tileTarget)
								int pickDamage = (int)GetPickaxeDamageMethodInfo.Invoke(Main.LocalPlayer, new object[] { 0, 0, pickPower, hitBufferIndex, tile });
								if (pickDamage > 0) {
									List<TileTypeMinPickPair> tileMinPickPairs;
									if (!mineables.TryGetValue(i, out tileMinPickPairs))
										mineables.Add(i, tileMinPickPairs = new List<TileTypeMinPickPair>());
									tileMinPickPairs.Add(new(testItem.createTile, pickPower));
									break;
								}
							}
						}
					}

					// TODO: This probably should be a separate dictionary, but the value of the dictionary is currently unused anyway.
					/*if (testItem.createWall > -1) {
						List<int> tiles;
						if (!mineables.TryGetValue(i, out tiles))
							mineables.Add(i, tiles = new List<int>());
						tiles.Add(testItem.createWall);
						//mineables[i] = testItem.createTile;
					}*/
				}
				// Trees don't currently count towards wood, maybe it should, manually?
				//mineables.Add(ItemID.Wood, new List<int>() { TileID.Trees, TileID.WoodBlock });
				//mineables.Add(ItemID.SilverOre, new List<int>() { TileID.Silver });
				//mineables.Add(ItemID.Obsidian, new List<int>() { TileID.Obsidian });
			}
			//loots.Add(ItemID.Gel);
			//loots.Add(ItemID.CopperBar);
		}

		// TODO: GetCraftPaths but without a Recipe? Just an item? Buy/Loot
		// Bestiary Option? All New Items you can craft if you farm this npc?
		internal static List<CraftPath> GetCraftPaths(Recipe recipe, CancellationToken token, bool single)
		{
			//Main.NewText("GetCraftPaths");
			
			//if(harvestable == null)
			//{
			//	// prevent network spam?
			//	harvestable = new HashSet<int>();
			//	for (int i = 0; i < Main.maxTilesX; i++)
			//	{
			//		for (int j = 0; j < Main.maxTilesY; j++)
			//		{

			//		}
			//	}
			//}

			//if (RecipePath.isCraftableOptimization)
			//{
			//	//if (ItemCatalogueUI.instance.craftResults == null)
			//	throw new Exception();
			//}


			// TODO: Track money? or just use inventory items. Bank money easily query-able.
			// Special Currencies?

			var haveItems = CalculateHaveItems();
			List<CraftPath> paths = new List<CraftPath>();
			CraftPath craftPath = new CraftPath(recipe, haveItems); // Push. Can't pop. // not calling ConsumeResources(recipeNode); 
			if (RecipePathTester.print)
				craftPath.Print();
			FindCraftPaths(paths, craftPath, token, single);

			//watch.Stop();
			//var elapsedMs = watch.ElapsedMilliseconds;
			//if (elapsedMs > 1000)
			//{
			//	StringBuilder sb = new StringBuilder();
			//	foreach (var item in recipe.requiredItem)
			//	{
			//		if (!item.IsAir)
			//			sb.Append(ItemTagHandler.GenerateTag(item));
			//	}
			//	sb.Append("-->");
			//	sb.Append(ItemTagHandler.GenerateTag(recipe.createItem));

			//	Main.NewText(elapsedMs + ": " + sb.ToString());
			//}

			if (!allowMissingStations)
			{
				for (int i = paths.Count - 1; i >= 0; i--)
				{
					// imkSushisMod caused thousands of paths to be culled here. Cull earlier or limit paths count. Investigate more.
					if (paths[i].root.GetAllChildrenPreOrder().OfType<CraftPath.RecipeNode>().Any(x => x.recipe.requiredTile.Any(tile => tile > -1 && !RecipeBrowserPlayer.seenTiles[tile])))
						paths.RemoveAt(i);
				}
			}
			return paths;
		}

		private static Dictionary<int, int> CalculateHaveItems()
		{
			// TODO: cache, return clone instead.
			Dictionary<int, int> haveItems = new Dictionary<int, int>(); // Have items can't have already used items in final recipe.
			if (sourceInventory)
			{
				for (int i = 0; i < 59; i++)
				{
					Item item = Main.LocalPlayer.inventory[i];
					if (i == 58)
						item = Main.mouseItem;
					//}
					//foreach (var item in Main.LocalPlayer.inventory)
					//{
					if (!item.IsAir)
					{
						int currentCount;
						haveItems.TryGetValue(item.type, out currentCount);
						haveItems[item.type] = currentCount + item.stack;
					}
				}
			}
			if (RecipePathTester.thousandExtra)
				for (int i = 1500; i < 2500; i++)
				{
					int currentCount;
					haveItems.TryGetValue(i, out currentCount);
					haveItems[i] = currentCount + 10;
				}
			return haveItems;
		}

		// Multithreading to prevent lag?
		private static void FindCraftPaths(List<CraftPath> paths, CraftPath inProgress, CancellationToken token, bool single)
		{
			if (single && paths.Count > 0)
				return;

			//if(token.CanBeCanceled && watch.ElapsedMilliseconds > 1000)
			//{
			//	Main.NewText("timed out" + watch.ElapsedMilliseconds);
			//	return;
			//}
			if (token.IsCancellationRequested) {
				inProgress.Print();
				return;
			}
			// Some notion of limiting depth of tree might help.
			// TODO
			// Limit by Total steps (TODO: steps only, not HAves)
			int count = inProgress.root.GetAllChildrenPreOrder().Count(); //.OfType<CraftPath.RecipeNode>()
			if (count > 20) {
				return;
			}

			// Current will always be an unfulfilled
			CraftPath.UnfulfilledNode current = inProgress.GetCurrent();

			if (current == null)
			{
				paths.Add(inProgress.Clone()); // Clone probably.
				return;
			}

			// Assume current is UnfulfulledItem class...could change later. Kill boss, other conditions maybe? think more about this.
			var ViableIngredients = current.item;
			var neededStack = current.stack;

			// reduce VialbleIngredients to only items not seen above.

			current.CheckParentsForRecipeLoopViaIngredients(ViableIngredients);

			if (ViableIngredients.Count == 0)
			{
				//Console.WriteLine();
				return;
			}

			// if(VialbleIngredients.length == 1) optimization?
			//List<Recipe> recipes;
			//if (!recipeDictionary.TryGetValue(needItem.Key, out recipes))
			//	recipes = new List<Recipe>();

			// Find all Recipes that can fulfill current, which might be a RecipeGroup
			var recipeOptions = recipeDictionary.Where(x => ViableIngredients.Contains(x.Key)).SelectMany(x => x.Value).ToList(); // inefficient maybe?

			foreach (var recipe in recipeOptions)
			{
				// Better to do this before.
				//if (current.CheckParentsForRecipeLoop(recipe))
				//	continue;

				//if(inProgress.root.recipe.)

				if (inProgress.root is CraftPath.RecipeNode)
				{
					var rootRecipe = (inProgress.root as CraftPath.RecipeNode).recipe;
					if (recipe.requiredItem.Any(x => x.type == rootRecipe.createItem.type && x.stack >= rootRecipe.createItem.stack))
					{
						continue; // Prevents Wood->Platform->Wood loops. Other code checks similar loops but didn't catch these. TODO: Think about RecipeGroups.
					}
				}

				// RecipeAvailable here.
				// Allow missing tiles?
				// If createItem/ingredients exists in Tree?

				int craftMultiple = (neededStack - 1) / recipe.createItem.stack + 1;

				// Push recipe consumes Items while populating Children with Unfulfilled or Fulfilled.
				var recipeNode = inProgress.Push(current, recipe, craftMultiple);

				if (RecipePathTester.print)
					inProgress.Print();
				int pathsBefore = paths.Count;
				FindCraftPaths(paths, inProgress, token, single); // Handles everything from here recursively.
				int pathsAfter = paths.Count;
				// Pop restores Unfulfilled and restores consumed Items.
				inProgress.Pop(current, recipeNode);

				//if (pathsAfter > 5) {
				//	return;
				//}

				// 3 Iron Ore 6 Lead Ore problem...if paths same size, try with craftMultiple of 1 maybe?
				if (pathsBefore == pathsAfter) // and craftMultiple < some number for performance.
				{
					// And 1 is possible?
				}
			}

			if (allowPurchasable)
			{
				// TODO recipe groups.
				var buyAble = ViableIngredients.Intersect(purchasable.Keys);
				if (buyAble.Count() > 0)
				{
					// TODO: Check conditions.
					// TODO: Check NPC in world, if encountered before, show since respawn possible.
					// TODO: Find NPC selling for the least.
					// TODO: Support Custom currency
					// TODO: If Can afford. 
					// TODO: Auto-purchase button, like craft button.
					// TODO: Take into account incomplete already owned items?
					// UnfulfilledNode should probably be nested under HaveItemNode, or merged together.

					// Find first buyable item from ViableIngredients
					foreach (var purchaseItem in buyAble) {
						var shopEntries = purchasable[purchaseItem];
						foreach (var shopEntry in shopEntries) {
							// If TownNPC hasn't been seen, skip.
							if (!RecipePath.NPCUnlocked(shopEntry.npcType))
								continue;

							// Toggleable? If support showing conditions, allow this to be toggleable.
							if (!shopEntry.entry.ConditionsMet())
								continue;

							if (shopEntry.entry.Item.shopSpecialCurrency != -1)
								continue; // Support later.

							// Make a single entry for buying? (which price to show?)

							int price = shopEntry.entry.Item.shopCustomPrice ?? shopEntry.entry.Item.value;

							// just pass in ShopEntry instead?
							CraftPath.BuyItemNode buyItemNode = new CraftPath.BuyItemNode(purchaseItem, neededStack, price, shopEntry.npcType, current.ChildNumber, current.parent, current.craftPath);
							inProgress.Push(current, buyItemNode);
							if (RecipePathTester.print)
								inProgress.Print();
							FindCraftPaths(paths, inProgress, token, single);
							inProgress.Pop(current, buyItemNode);
						}
					}
				}
			}

			if (allowLoots) // multithread issue if allowLoots checked after.
			{
				//if (VialbleIngredients.Intersect(loots).Any())
				var lootable = ViableIngredients.Intersect(LootCache.instance.lootInfos.Keys); // TODO recipe groups. --> For loop??
				if (lootable.Count() > 0)
				{
					bool encountered = false;

					// Only checks 1 item in Group. Fix later.
					var npcs = LootCache.instance.lootInfos[lootable.First()];
					foreach (var npc in npcs) {
						if(NPCUnlocked(npc)) {
							encountered = true;
							break;
						}
					}

					if (encountered)
					{
						CraftPath.LootItemNode lootItemNode = new CraftPath.LootItemNode(lootable.First(), neededStack, current.ChildNumber, current.parent, current.craftPath);
						inProgress.Push(current, lootItemNode);
						if (RecipePathTester.print)
							inProgress.Print();
						FindCraftPaths(paths, inProgress, token, single);
						inProgress.Pop(current, lootItemNode);
					}
				}
			}

			if (allowMineables && mineables != null) {
				int pickPower = Main.LocalPlayer.GetBestPickaxe()?.pick ?? -1; // if no pick in inventory, -1 should prevent all mining, except Main.tileCut results
				var mineableItems = ViableIngredients.Intersect(mineables.Keys);
				if (mineableItems.Count() > 0) {
					foreach (var mineableItem in mineableItems) {
						List<TileTypeMinPickPair> tileMinPickPairs = mineables[mineableItem];
						foreach ((int tileType, int minPick) in tileMinPickPairs) {
							if (!RecipeBrowserPlayer.seenTiles[tileType])
								continue;
							if (pickPower < minPick)
								continue;

							CraftPath.MineItemNode lootItemNode = new CraftPath.MineItemNode(mineableItem, neededStack, current.ChildNumber, current.parent, current.craftPath);
							inProgress.Push(current, lootItemNode);
							if (RecipePathTester.print)
								inProgress.Print();
							FindCraftPaths(paths, inProgress, token, single);
							inProgress.Pop(current, lootItemNode);
						}
					}
				}
			}

			if (allowBugNetables && bugNetables != null) {
				var bugnetable = ViableIngredients.Intersect(bugNetables.Keys);

				foreach (var bugItem in bugnetable) {
					if (NPCUnlocked(bugNetables[bugItem])) {
						CraftPath.BugNetItemNode lootItemNode = new CraftPath.BugNetItemNode(bugItem, neededStack, bugNetables[bugItem], current.ChildNumber, current.parent, current.craftPath);
						inProgress.Push(current, lootItemNode);
						if (RecipePathTester.print)
							inProgress.Print();
						FindCraftPaths(paths, inProgress, token, single);
						inProgress.Pop(current, lootItemNode);
						// TODO: Snail statue: do we want to show multiple paths for capturing "any snail"? Hide unseen npc in BugNetItemNode?
						// Could change this to: Any Snail by capturing snail, glowsnail maybe? Do other nodes do similar, or is that not how I handle groups..?
						// Hint at other missing npc, or keep them hidden unless cheat mode enabled?
						break;
					}
				}
			}

			// Other sources?
			// Fishing?
			// Mining/Harvesting?
			// Extractinator
			// Chests
			// Statue
			// Traveling Shop
			// ItemChecklist? If not any of the others, I must have had it once before.
			// Use item results of Shimmer transformation and decrafting (conditional?) in craft path calculations.

			// returning will result in Popping.
		}

		internal static bool NPCUnlocked(int npcid) {
			return Main.BestiaryDB.FindEntryByNPCID(npcid).UIInfoProvider.GetEntryUICollectionInfo().UnlockState > Terraria.GameContent.Bestiary.BestiaryEntryUnlockState.NotKnownAtAll_0;
		}

		internal static bool ItemFullyResearched(int itemID) {
			// Call if (Main.GameModeInfo.IsJourneyMode) before calling. Not sure the behavior if called in normal modes
			if (Main.LocalPlayerCreativeTracker.ItemSacrifices.TryGetSacrificeNumbers(itemID, out var amountWeHave, out var amountNeededTotal) && amountWeHave >= amountNeededTotal) {
				return true;
			}
			return false;

			// GetSacrificeCount doesn't work, it doesn't check alternate itemids
			// Terraria.GameContent.Creative.CreativeUI.GetSacrificeCount(validItemID, out bool fullyResearched);
		}

		// TODO: Crafting Station requirements, IsAvailable
		//private static void FindCraftPaths(List<CraftPath> paths, CraftPath inProgress, Recipe current, int recipeMultiple, Dictionary<int, int> haveItems, Dictionary<int, int> needItems)
		//{
		//	//Push main recipe
		//	//	push R 1
		//	//		??
		//	//	pop R1


		//	//pop main recipe

		public static IEnumerable<IEnumerable<T>> CartesianProduct<T>(this IEnumerable<IEnumerable<T>> sequences)
		{
			IEnumerable<IEnumerable<T>> result = new[] { Enumerable.Empty<T>() };
			foreach (var sequence in sequences)
			{
				var localSequence = sequence;
				result = result.SelectMany(
				  _ => localSequence,
				  (seq, item) => seq.Concat(new[] { item })
				);
			}
			return result;
		}
	}

	//internal class CraftPathOld
	//{
	//	internal List<Recipe> recipes = new List<Recipe>();
	//	internal List<int> craftMultiple = new List<int>();

	//	internal CraftPath GetClone()
	//	{
	//		var clone = new CraftPath();
	//		clone.recipes = new List<Recipe>(recipes);
	//		clone.craftMultiple = new List<int>(craftMultiple);
	//		return clone;
	//	}

	//	//internal bool HasLoop()
	//	//{

	//	//}
	//}


	// Allow missing 
	// -- Lootable Items from seen NPC
	// -- Buyable Items from seen town NPC
	// -- Crafting stations?
	// -- Recipe Available blocked.
	internal class CraftPath
	{
		internal class CraftPathNode
		{
			internal CraftPathNode parent;
			internal int ChildNumber = -1;
			internal CraftPathNode[] children;
			internal CraftPath craftPath;

			public CraftPathNode(int childNumber, CraftPathNode parent, CraftPath craftPath)
			{
				ChildNumber = childNumber;
				this.parent = parent;
				this.craftPath = craftPath;

				if (parent != null)
					parent.children[ChildNumber] = this;
			}

			bool Fulfilled => false;

			internal UnfulfilledNode FindUnfulfilled()
			{
				if (this is UnfulfilledNode)
					return (UnfulfilledNode)this;
				if (children != null) // TODO: Initilize to 0? Leave as null?
					foreach (var item in children)
					{
						UnfulfilledNode child = item.FindUnfulfilled();
						if (child != null)
							return child;
					}
				return null;
			}

			internal void Print(int indent)
			{
				if (!RecipePathTester.print && !RecipePathTester.printResults)
					return;
				RecipeBrowser.instance.Logger.Info(new String(' ', indent) + ToString());
				if (children != null) // TODO: Initilize to 0? Leave as null?
					foreach (var item in children)
					{
						if (item == null)
							RecipeBrowser.instance.Logger.Info(new String(' ', indent + 4) + "null");
						else
							item.Print(indent + 4);
					}
			}

			public virtual string ToUITextString()
			{
				return ToString();
			}

			public override string ToString()
			{
				return base.ToString();
			}

			internal virtual CraftPathNode Clone()
			{
				CraftPathNode clone = (CraftPathNode)this.MemberwiseClone();
				clone.parent = null;
				clone.craftPath = null;
				clone.children = children?.Select(x => x?.Clone()).ToArray();
				return clone;
			}

			internal virtual void ConsumeResources(CraftPath path)
			{
				// Do Consumption here

				if (children != null)
					foreach (var item in children)
					{
						item.ConsumeResources(path);
					}
			}

			internal virtual void UnConsumeResources(CraftPath path)
			{
				// Do Consumption here

				if (children != null)
					foreach (var item in children)
					{
						item.UnConsumeResources(path);
					}
			}

			public IEnumerable<CraftPathNode> GetAllChildrenPreOrder()
			{
				yield return this;
				if (children != null)
					foreach (var child in children)
					{
						foreach (var subchild in child.GetAllChildrenPreOrder())
						{
							yield return subchild;
						}
					}
			}

			//GetAllLeafPreOrder

			// push and pop to consume haveItem efficiently?
		}

		// Terminal node
		internal class HaveItemNode : CraftPathNode
		{
			internal int itemid;
			internal int stack;
			public HaveItemNode(int itemid, int stack, int ChildNumber, CraftPathNode parent, CraftPath craftPath) : base(ChildNumber, parent, craftPath)
			{
				this.itemid = itemid;
				this.stack = stack;
			}

			internal override void ConsumeResources(CraftPath path)
			{
				ConsumeItems(path);

				base.ConsumeResources(path);
			}

			internal override void UnConsumeResources(CraftPath path)
			{
				UnConsumeItems(path);

				base.UnConsumeResources(path);
			}

			internal void ConsumeItems(CraftPath path)
			{
				path.haveItems.Adjust(itemid, -stack);
			}
			internal void UnConsumeItems(CraftPath path)
			{
				path.haveItems.Adjust(itemid, stack);
			}

			public override string ToString()
			{
				return $"Have: {Lang.GetItemNameValue(itemid)} ({stack})";
			}

			public override string ToUITextString()
			{
				return $"Have: {ItemHoverFixTagHandler.GenerateTag(itemid, stack, null, true)}";
			}
		}

		internal class HaveItemsNode : CraftPathNode
		{
			internal RecipeGroup recipeGroup;
			internal List<Tuple<int, int>> listOfItems;

			public HaveItemsNode(RecipeGroup recipeGroup, List<Tuple<int, int>> listOfItems, int ChildNumber, CraftPathNode parent, CraftPath craftPath) : base(ChildNumber, parent, craftPath)
			{
				this.recipeGroup = recipeGroup;
				this.listOfItems = listOfItems;
			}

			internal override void ConsumeResources(CraftPath path)
			{
				ConsumeItems(path);

				base.ConsumeResources(path);
			}

			internal override void UnConsumeResources(CraftPath path)
			{
				UnConsumeItems(path);

				base.UnConsumeResources(path);
			}

			internal void ConsumeItems(CraftPath path)
			{
				foreach (var item in listOfItems)
					path.haveItems.Adjust(item.Item1, -item.Item2);
			}
			internal void UnConsumeItems(CraftPath path)
			{
				foreach (var item in listOfItems)
					path.haveItems.Adjust(item.Item1, item.Item2);
			}

			public override string ToString()
			{
				int count = listOfItems.Sum(x => x.Item2);

				return $"{ItemHoverFixTagHandler.GenerateTag(recipeGroup.IconicItemId, count, recipeGroup.GetText(), false)} ({string.Concat(listOfItems.Select(x => ItemHoverFixTagHandler.GenerateTag(x.Item1, x.Item2, null, true)))})";
				// return $"Haves: {string.Join(", ", listOfItems.Select(x => $"{Lang.GetItemNameValue(x.Item1)} ({x.Item2})"))}";
			}
		}

		internal class BuyItemNode : CraftPathNode
		{
			// TODO: storeID
			int itemid;
			int stack;
			int price;
			int npcID;

			internal int TotalPrice => price * stack;

			public BuyItemNode(int itemid, int stack, int price, int npcID, int ChildNumber, CraftPathNode parent, CraftPath craftPath) : base(ChildNumber, parent, craftPath)
			{
				this.itemid = itemid;
				this.stack = stack;
				this.price = price;
				this.npcID = npcID;
			}

			internal override void ConsumeResources(CraftPath path)
			{
				ConsumeMoney(path);

				base.ConsumeResources(path);
			}

			internal override void UnConsumeResources(CraftPath path)
			{
				UnConsumeMoney(path);

				base.UnConsumeResources(path);
			}

			internal void ConsumeMoney(CraftPath path)
			{
				// TODO. For now assume infinite money.
				//path.haveItems.Adjust(itemid, -stack);
			}
			internal void UnConsumeMoney(CraftPath path)
			{
				//path.haveItems.Adjust(itemid, stack);
			}

			public override string ToString()
			{
				return $"Buy: {Lang.GetItemNameValue(itemid)} ({stack}) from ??";
			}

			public override string ToUITextString()
			{
				// TODO: Show a UIRecipeInfoRightAligned for the ShopEntry Conditions. Also show small red x over currently dead Merchant.
				return $"[image/tPurchase:RecipeBrowser/Images/sortValue]: {ItemHoverFixTagHandler.GenerateTag(itemid, stack)} from [npc/head:{npcID}] for {GetTotalCostAsTags(price * stack)}";
				// TODO: TownNPC Head instead of 
				// Each NPC: return $"[image/tPurchase:RecipeBrowser/Images/sortValue]: {ItemHoverFixTagHandler.GenerateTag(itemid, stack)} from {string.Concat(RecipePath.purchasable2[itemid].Select(x => $"[npc/head:{x.npcType}]"))};";
			}

			internal static string GetTotalCostAsTags(int totalPrice) {
				StringBuilder sb = new StringBuilder();
				int platinum = totalPrice / 1000000;
				int gold = (totalPrice % 1000000) / 10000;
				int silver = (totalPrice % 10000) / 100;
				int copper = totalPrice % 100;

				if (platinum != 0)
					sb.Append(ItemHoverFixTagHandler.GenerateTag(ItemID.PlatinumCoin, platinum));
				if (gold != 0)
					sb.Append(ItemHoverFixTagHandler.GenerateTag(ItemID.GoldCoin, gold));
				if (silver != 0)
					sb.Append(ItemHoverFixTagHandler.GenerateTag(ItemID.SilverCoin, silver));
				if (copper != 0)
					sb.Append(ItemHoverFixTagHandler.GenerateTag(ItemID.CopperCoin, copper));
				return sb.ToString();
			}
		}

		internal class LootItemNode : CraftPathNode
		{
			// TODO: storeID
			int itemid;
			int stack;
			public LootItemNode(int itemid, int stack, int ChildNumber, CraftPathNode parent, CraftPath craftPath) : base(ChildNumber, parent, craftPath)
			{
				this.itemid = itemid;
				this.stack = stack;
			}

			public override string ToString()
			{
				return $"Farm: {Lang.GetItemNameValue(itemid)} ({stack}) from {string.Join(", ", LootCache.instance.lootInfos[itemid].Select(x => Lang.GetNPCNameValue(x)))}";
			}

			public override string ToUITextString()
			{
				var npcs = LootCache.instance.lootInfos[itemid]; // Only checks 1 item in Group. Fix later.
				List<int> encountered = new List<int>();
				foreach (var npc in npcs)
				{
					// Normally we only show loot from encountered npc
					if (RecipePath.NPCUnlocked(npc)) {
						encountered.Add(npc);
					}
					// TODO: icon for "and other unknown NPC"?
				}
				return $"[image/s0.8,v2,tFarm:RecipeBrowser/Images/sortDamage] {ItemHoverFixTagHandler.GenerateTag(itemid, stack)} from {string.Concat(encountered.Select(x => $"[npc:{x}]"))}";

				//[image/tMissing Tiles[i;{ItemID.MythrilAnvil}]:
				//return $"[image/tFarm:RecipeBrowser/Images/sortDamage] {ItemHoverFixTagHandler.GenerateTag(itemid, stack)} from {string.Concat(RecipePath.loots[itemid].Select(x => $"[npc:{x}]"))}";
				//return $"Farm: {ItemHoverFixTagHandler.GenerateTag(itemid, stack)} from {string.Concat(RecipePath.loots[itemid].Select(x => $"[npc:{x}]"))}";
			}

		}

		internal class MineItemNode : CraftPathNode
		{
			int itemid;
			int stack;
			public MineItemNode(int itemid, int stack, int ChildNumber, CraftPathNode parent, CraftPath craftPath) : base(ChildNumber, parent, craftPath) {
				this.itemid = itemid;
				this.stack = stack;
			}

			public override string ToString() {
				return $"Mine: {Lang.GetItemNameValue(itemid)} ({stack})";
			}

			public override string ToUITextString() {
				// Pass in tile? make Tile chat tag? Probably not needed, tile and item sprites are similar enough.
				return $"[image/s0.8,v2,tMine:RecipeBrowser/Images/sortPick] > {ItemHoverFixTagHandler.GenerateTag(itemid, stack)} from the world";
			}
		}

		internal class BugNetItemNode : CraftPathNode
		{
			// TODO: storeID
			int itemid;
			int stack;
			int npcid;
			public BugNetItemNode(int itemid, int stack, int npcid, int ChildNumber, CraftPathNode parent, CraftPath craftPath) : base(ChildNumber, parent, craftPath) {
				this.itemid = itemid;
				this.stack = stack;
				this.npcid = npcid;
			}

			public override string ToString() {
				return $"Bug Net: {Lang.GetItemNameValue(itemid)} ({stack}) from {Lang.GetNPCNameValue(npcid)}";
			}

			public override string ToUITextString() {
				return $"[image/s0.8,v2,tBug Net:RecipeBrowser/Images/bugNet] > {ItemHoverFixTagHandler.GenerateTag(itemid, stack)} by capturing [npc:{npcid}]";
			}
		}

		internal class JourneyDuplicateItemNode : CraftPathNode
		{
			internal int itemid;
			internal int stack;
			public JourneyDuplicateItemNode(int itemid, int stack, int ChildNumber, CraftPathNode parent, CraftPath craftPath) : base(ChildNumber, parent, craftPath) {
				this.itemid = itemid;
				this.stack = stack;
			}

			public override string ToString() {
				return $"Duplicate: {Lang.GetItemNameValue(itemid)} ({stack})";
			}

			public override string ToUITextString() {
				return $"[image/s0.8,v2,tDuplicate:RecipeBrowser/Images/duplicateOff] > {ItemHoverFixTagHandler.GenerateTag(itemid, stack, null, true)}";
			}
		}

		internal class UnfulfilledNode : CraftPathNode
		{
			internal RecipeGroup recipeGroup;
			internal HashSet<int> item;
			internal int stack;

			public UnfulfilledNode(int item, int stack, int ChildNumber, CraftPathNode parent, CraftPath craftPath) : base(ChildNumber, parent, craftPath)
			{
				this.stack = stack;
				this.item = new HashSet<int>() { item };
			}

			public UnfulfilledNode(RecipeGroup recipeGroup, int stack, int ChildNumber, CraftPathNode parent, CraftPath craftPath) : base(ChildNumber, parent, craftPath)
			{
				this.recipeGroup = recipeGroup;
				this.item = recipeGroup.ValidItems;
				this.stack = stack;

				// recipeGroup.ContainsItem probably faster than iterating over item?
			}

			public override string ToString()
			{
				return $"Need: { string.Join(", ", item.Select(x => $"{Lang.GetItemNameValue(x)} ({stack})"))}";
			}

			internal void CheckParentsForRecipeLoopViaIngredients(HashSet<int> vialbleIngredients)
			{
				var p = parent;
				while (p != null)
				{
					RecipeNode r = p as RecipeNode; // Technically this shouldn't ever fail...
					if (r != null)
					{
						// TODO: 100Wood <- Bundle, Bundle <- Wood+Duplicator problem. Stack size matters? issue.
						if (vialbleIngredients.Contains(r.recipe.createItem.type))
						{
							//Console.WriteLine();
						}

						if (r.recipe.createItem.type == 9 && (r.craftPath.root as RecipeNode).recipe.createItem.type == 9)
						{
							//Console.WriteLine(); // 2629 living wood plat
						}

						vialbleIngredients.Remove(r.recipe.createItem.type);
						p = p.parent;
					}
					else if (p is HaveItemNode) // 
					{
						//Console.WriteLine();
						p = p.parent;
					}
					else if (p is HaveItemsNode)
					{
						//Console.WriteLine();
						p = p.parent;
					}
					else
					{
						throw new Exception("How is a parent not a recipe?");
					}
				}
			}

			internal bool CheckParentsForRecipeLoop(Recipe recipe)
			{
				// Yeah, wait until later is a better idea.
				var p = parent;
				while (p != null)
				{
					RecipeNode r = p as RecipeNode; // Technically this shouldn't ever fail...
					if (r != null)
					{
						if (r.recipe.createItem.type == recipe.createItem.type && r.recipe.createItem.stack != recipe.createItem.stack)
							throw new Exception("Found a stack size problem craft path!");
						// If any create item matches an above create item
						if (r.recipe.createItem.type == recipe.createItem.type) // is there a Recipe Group problem here? below?
							return true;
						p = p.parent;
					}
					else if (p is HaveItemNode || p is HaveItemsNode) // is this OK?
					{
						p = p.parent;
					}
					else
					{
						throw new Exception("How is a parent not a recipe?");
					}
				}
				return false;

				// TODO: Ignore recipe group items

				// I removed RecipeGroup items. Remember to check this later when actualizing it.
				// I could just check Result->Result and bypass any recipe group problem
				// 100Wood <- Bundle, Bundle <- Wood+Duplicator problem. Stack size matters?
				//HashSet<int> items = new HashSet<int>(recipe.requiredItem.Where(x => !x.IsAir).Select(x => x.type));
				//List<int> groups = RecipePath.GetAcceptedGroups(recipe);
				//foreach (var groupid in groups)
				//{
				//	items.Remove(RecipeGroup.recipeGroups[groupid].ValidItems[RecipeGroup.recipeGroups[groupid].IconicItemIndex]);
				//}

				//var p = parent;
				//while (p != null)
				//{
				//	RecipeNode r = p as RecipeNode;
				//	if (r != null)
				//	{
				//		// If any create item matches an above create item
				//		if (p.recipe.createItem.type == recipe.createItem.type) // is there a Recipe Group problem here? below?
				//			return true;
				//		// Vs
				//		if (RecipePath.shortCircitIngredients && recipe.requiredItem.Any(x => x.type == p.recipe.createItem.type)) // TODO: Don't short circuit if parent is creating wood but recipe requires any wood...?
				//			return true;
				//		// If any ingredient matches a parent create item
				//		// OLD
				//		//if (p.recipe == recipe)
				//		//	return true;
				//	}
				//	p = p.parent;
				//}
				//return false;
			}
		}

		// Change to CraftNode? KillNode -> Farm these NPC for this item
		internal class RecipeNode : CraftPathNode
		{
			internal Recipe recipe;
			internal int multiplier;

			public RecipeNode(Recipe recipe, int multiplier, int ChildNumber, CraftPathNode parent, CraftPath craftPath) : base(ChildNumber, parent, craftPath)
			{
				this.recipe = recipe;
				this.multiplier = multiplier;
				children = new CraftPathNode[recipe.requiredItem.Count(x => !x.IsAir)];
				
				for (int i = 0; i < children.Length; i++) // For Each Ingredient.
				{
					bool itemIsRecipeGroupItem = false;
					foreach (var groupid in recipe.acceptedGroups)
					{
						// 6 wood, 4 shadewood works for 10 any wood.

						// multiplier assumes all same Item in ItemGroup used for all Recipes.
						if (recipe.requiredItem[i].type == RecipeGroup.recipeGroups[groupid].IconicItemId)
						{
							bool foundValidItem = false;
							bool foundPartialItem = false;
							foreach (var validItemID in RecipeGroup.recipeGroups[groupid].ValidItems)
							{
								if (Main.GameModeInfo.IsJourneyMode) {
									if (RecipePath.ItemFullyResearched(validItemID)) {
										children[i] = new JourneyDuplicateItemNode(validItemID, recipe.requiredItem[i].stack * multiplier, i, this, craftPath);
										foundValidItem = true;
										break;
									}
								}

								if (craftPath.haveItems.ContainsKey(validItemID) && craftPath.haveItems[validItemID] >= recipe.requiredItem[i].stack * multiplier)
								{
									// Any Wood on left, Wood on Right problem. Wood could be consumed before Wood node, when ShadeWood would be better option.
									children[i] = new HaveItemNode(validItemID, recipe.requiredItem[i].stack * multiplier, i, this, craftPath);
									foundValidItem = true;
									break;
								}
								else if (craftPath.haveItems.ContainsKey(validItemID))
								{
									foundPartialItem = true;
								}
							}
							if (!foundValidItem && foundPartialItem)
							{
								List<Tuple<int, int>> listOfItems = new List<Tuple<int, int>>();
								int remaining = recipe.requiredItem[i].stack * multiplier;
								foreach (var validItemID in RecipeGroup.recipeGroups[groupid].ValidItems)
								{
									if (remaining > 0 && craftPath.haveItems.ContainsKey(validItemID))
									{
										int taken = Math.Min(remaining, craftPath.haveItems[validItemID]);
										listOfItems.Add(new Tuple<int, int>(validItemID, taken));
										remaining -= taken;
									}
								}
								children[i] = new HaveItemsNode(RecipeGroup.recipeGroups[groupid], listOfItems, i, this, craftPath);
								if (remaining > 0)
								{
									children[i].children = new CraftPathNode[1];
									children[i].children[0] = new UnfulfilledNode(RecipeGroup.recipeGroups[groupid], remaining, 0, children[i], craftPath);
								}
							}
							else if (!foundValidItem)
								children[i] = new UnfulfilledNode(RecipeGroup.recipeGroups[groupid], recipe.requiredItem[i].stack * multiplier, i, this, craftPath);
							itemIsRecipeGroupItem = true;
							break;
						}
					}
					// Does it make more sense to nest these, or add more children slots? Hm, Children match up to recipe ingredient index.... Make a BranchNode?
					if (!itemIsRecipeGroupItem)
					{
						if (Main.GameModeInfo.IsJourneyMode) {
							if (RecipePath.ItemFullyResearched(recipe.requiredItem[i].type)) {
								children[i] = new JourneyDuplicateItemNode(recipe.requiredItem[i].type, recipe.requiredItem[i].stack * multiplier, i, this, craftPath);
								continue;
							}
						}

						// Recipe Groups can have stacks-size different inputs if needed. Ignore for now and handle: 10 wood needed, 9 wood held and 2 platforms held.

						if (craftPath.haveItems.ContainsKey(recipe.requiredItem[i].type) && craftPath.haveItems[recipe.requiredItem[i].type] >= recipe.requiredItem[i].stack * multiplier)
						{
							// Potential problem: Recipe with multiple of same item. Or Item and ItemGroup that share.
							// Could implement consumed flag and attempt to consume immediately. 
							children[i] = new HaveItemNode(recipe.requiredItem[i].type, recipe.requiredItem[i].stack * multiplier, i, this, craftPath);
						}
						else
						{
							if (craftPath.haveItems.ContainsKey(recipe.requiredItem[i].type))
							{
								int remainder = recipe.requiredItem[i].stack * multiplier - craftPath.haveItems[recipe.requiredItem[i].type];
								children[i] = new HaveItemNode(recipe.requiredItem[i].type, craftPath.haveItems[recipe.requiredItem[i].type], i, this, craftPath);
								children[i].children = new CraftPathNode[1];
								children[i].children[0] = new UnfulfilledNode(recipe.requiredItem[i].type, remainder, 0, children[i], craftPath);
							}
							else
								children[i] = new UnfulfilledNode(recipe.requiredItem[i].type, recipe.requiredItem[i].stack * multiplier, i, this, craftPath); // assign current?
						}
					}
					// TODO: Assign CraftPath.Current to 1st or last unfulfilled
					// TODO: If Loot and Shop and Missing disabled, check 
					// if (RecipePath.isCraftableOptimization && !ItemCatalogueUI.instance.craftResults[item.Key])
				}
			}

			public override string ToString()
			{
				return "Recipe...";
				//return $"Recipe: {RecipePathTester.Search.GetName(recipe.createItem.type)} ({recipe.createItem.stack}): {string.Join(", ", recipe.requiredItem.Where(x => !x.IsAir).Select(x => $"{RecipePathTester.Search.GetName(x.type)} ({x.stack})"))} x {multiplier}";
			}
		}

		internal Dictionary<int, int> haveItems;
		// Shouldn't be removed, should be RecipeNode for now.
		internal CraftPathNode root;
		//internal List<Recipe> recipes = new List<Recipe>();
		//internal List<int> craftMultiple = new List<int>();
		internal UnfulfilledNode current;
		//		internal static CraftPath 

		public CraftPath(Recipe root, Dictionary<int, int> haveItems)
		{
			this.haveItems = haveItems;
			// 1 for now.
			current = null;
			this.root = new RecipeNode(root, 1, -1, null, this);
			// current = (UnfulfilledNode)this.root.children[0];
			ConsumeResources(this.root);
		}

		// Swaps out an unfulfilled Node with a real node?
		internal RecipeNode Push(UnfulfilledNode current, Recipe recipe, int needed)
		{
			this.current = null;
			RecipeNode recipeNode = new RecipeNode(recipe, needed, current.ChildNumber, current.parent, current.craftPath);

			//current.parent.children[current.ChildNumber] = recipeNode;
			// Swap out Edges
			//recipeNode.parent = current.parent;
			//recipeNode.ChildNumber = current.ChildNumber;
			current.parent = null;
			current.ChildNumber = -1;
			//this.current = null;

			// Consume
			ConsumeResources(recipeNode); // consumes items from haveItems

			return recipeNode;
		}

		private void ConsumeResources(CraftPathNode craftPathNode)
		{
			craftPathNode.ConsumeResources(this);

			//BuyItemNode buyItemNode = craftPathNode as BuyItemNode;
			//if (buyItemNode != null)
			//{
			//	buyItemNode.ConsumeMoney(this);
			//}
			//if (craftPathNode.children != null)
			//	foreach (var item in craftPathNode.children)
			//	{
			//		HaveItemNode haveItemNode = item as HaveItemNode;
			//		if (haveItemNode != null)
			//		{
			//			haveItemNode.ConsumeItems(this);
			//		}
			//	}
		}

		internal void Push(UnfulfilledNode current, CraftPathNode buyItemNode)
		{
			this.current = null;

			current.parent = null;
			current.ChildNumber = -1;

			// Consume
			ConsumeResources(buyItemNode); // consumes money from savings
		}

		internal void Pop(UnfulfilledNode current, CraftPathNode recipeNode)
		{
			// Swap out Edges
			this.current = current;
			current.parent = recipeNode.parent;
			current.ChildNumber = recipeNode.ChildNumber;
			current.parent.children[current.ChildNumber] = current;
			//Utils.Swap(ref )
			recipeNode.parent = null;
			recipeNode.ChildNumber = -1; // invalid, should be GCed.

			// UnConsume
			UnConsumeResources(recipeNode); // unconsumes items from haveItems
		}

		private void UnConsumeResources(CraftPathNode craftPathNode)
		{
			craftPathNode.UnConsumeResources(this);

			//BuyItemNode buyItemNode = recipeNode as BuyItemNode;
			//if (buyItemNode != null)
			//{
			//	buyItemNode.ConsumeMoney(this);
			//}
			//if (recipeNode.children != null)
			//	foreach (var item in recipeNode.children)
			//	{
			//		HaveItemNode haveItemNode = item as HaveItemNode;
			//		if (haveItemNode != null)
			//		{
			//			haveItemNode.UnConsumeItems(this);
			//		}
			//	}
		}

		internal UnfulfilledNode GetCurrent()
		{
			if (current == null || current.ChildNumber == -1) // or is parentless?
			{
				// Make sure added RecipeNodes assign current.
				current = root.FindUnfulfilled();
			}
			return current;
		}

		internal void Print()
		{
			if (!RecipePathTester.print)
				return;
			RecipeBrowser.instance.Logger.Info("Printing CraftPath");
			root.Print(0);
		}

		internal CraftPath Clone()
		{
			// CraftPath is continued to be used, so we need a clone to prevent corruption of reference types.
			CraftPath clone = (CraftPath)this.MemberwiseClone();
			clone.root = root.Clone();
			return clone;
		}
	}
}

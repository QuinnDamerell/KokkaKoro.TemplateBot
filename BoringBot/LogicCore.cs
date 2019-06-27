using GameCommon;
using GameCommon.Buildings;
using GameCommon.Protocol;
using GameCommon.Protocol.ActionOptions;
using GameCommon.StateHelpers;
using KokkaKoroBotHost;
using KokkaKoroBotHost.ActionOptions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace KokkaKoroBot
{
    class LogicCore
    {
        IBotInterface m_bot;
        RandomGenerator m_random = new RandomGenerator();

        int buildingsPurchased = 0;
        public LogicCore(IBotInterface handler)
        {
            m_bot = handler;
        }

        public Task Setup()
        {
            // This is a good time to setup the bot and pre-load any async items that might take a while to load.
            // Once the game starts asking us for action requests, there's a turn time limit we must respect.

            // To avoid making the function async (because we don't need it) we will return this task.
            // Remove this and make the function async if you need to await things.

            return Task.CompletedTask;
        }

        public Task Cleanup()
        {
            // This will be called when the bot is shutting down regardless if the game is complete or not.
            // If you want to write anything out to disk, now is a good time.

            return Task.CompletedTask;
        }

        public Task OnGameUpdate(GameStateUpdate<object> update)
        {
            // OnGameUpdate fires when just about anything changes in the game. This might be coins added to a user because of a building,
            // cards being swapped, etc. Your bot doesn't need to pay attention to these updates if you don't wish, when your bot needs to make
            // an action OnGameActionRequested will be called with the current game state and a list of possible actions.

            return Task.CompletedTask;
        }

        private int HowManyDiceToRoll(StateHelper stateHelper)
        {
            return 1;
        }

        public async Task OnGameActionRequested(GameActionRequest actionRequest, StateHelper stateHelper)
        {
            // OnGameActionRequested is called when the bot actually needs to take an action. Below is an example of how this can
            // be done and what events your bot will need to handle.
            // 
            // To see all of the actions your must handle, look at GameActionType.
            //
            // actionRequest.State is the root of the state object for the game. This holds all things like players, coin amounts,
            // what building are in the marketplace, states of the current turn, etc. 
            // Essentially, this object is everything you would see on the table when playing the game.
            //
            // The statehelper is a very useful tool that will answer many current state questions. The state helper takes a perspective user 
            // when it's created, that it will use as a default player if no player is given.
            // For example, the Player.GetPlayerName function takes an option playerIndex. If not given, it will return your name.
            //
            // There are 4 modules to the state helper. Each helper has functions specific the to topic.
            //     Player
            //         ex. GetPlayer(), GetNumberOfLandmarksOwned(), GetMaxRollsAllowed(), CanHaveExtraTurn()
            //     Marketplace
            //         ex. GetMaxBuildingsInGame(), GetBuiltBuildingsInCurrentGame(), GetBuildingTypesBuildableInMarketplace()
            //     CurrentTurn
            //         ex. CanRollOrReRoll(), GetPossibleActions(), CanTakeAction()
            //     BuildingRules
            //         This helper holds all of the building types and the rules of them. ex. BuildingRules[buildingIndex].GetColor()
            // 

            // Always roll if it's an option.
            if (actionRequest.PossibleActions.Contains(GameActionType.RollDice))
            {
                // How many dice can we roll?
                int maxDiceCount = stateHelper.Player.GetMaxCountOfDiceCanRoll();

                // Can we re-roll
                int rollsSoFar = stateHelper.GetState().CurrentTurnState.Rolls;
                bool canReRoll = stateHelper.Player.GetMaxRollsAllowed() < rollsSoFar;

                // If we can't reroll, auto commit the dice. Otherwise don't, so we can reroll if we want.
                Logger.Log(Log.Info, "Rolling the dice!");
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateRollDiceAction(1, true));
                if (!result.Accepted)
                {
                    await Shutdown("failed to roll dice.", result.Error);
                }
                else
                {
                    Logger.Log(Log.Info, "Done");
                }
                return;
            }

            if (actionRequest.PossibleActions.Contains(GameActionType.BuildBuilding))
            {
                // Get all building that are in the marketplace currently.
                List<int> buildable = stateHelper.Marketplace.GetBuildingTypesBuildableInMarketplace();

                // Filter it down to only buildings we can afford.
                List<int> affordable = stateHelper.Player.FilterBuildingIndexesWeCanAfford(buildable);

                int buildingIndex = -1;
                if (affordable.Contains(BuildingRules.ShoppingMall))
                {
                    buildingIndex = BuildingRules.ShoppingMall;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.AmusementPark))
                {
                    buildingIndex = BuildingRules.AmusementPark;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.TrainStation))
                {
                    buildingIndex = BuildingRules.TrainStation;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.RadioTower))
                {
                    buildingIndex = BuildingRules.RadioTower;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.ConvenienceStore))
                {
                    buildingIndex = BuildingRules.ConvenienceStore;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.Bakery))
                {
                    buildingIndex = BuildingRules.Bakery;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.WheatField))
                {
                    buildingIndex = BuildingRules.WheatField;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.Ranch))
                {
                    buildingIndex = BuildingRules.Ranch;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.Forest))
                {
                    buildingIndex = BuildingRules.Forest;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.Cafe))
                {
                    buildingIndex = BuildingRules.Cafe;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.CheeseFactory) && stateHelper.Player.GetTotalProductionTypeBuilt(EstablishmentProduction.Cattle) > 2)
                {
                    buildingIndex = BuildingRules.CheeseFactory;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.FurnitureFactory) && stateHelper.Player.GetTotalProductionTypeBuilt(EstablishmentProduction.Gear) > 3)
                {
                    buildingIndex = BuildingRules.FurnitureFactory;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.Mine))
                {
                    buildingIndex = BuildingRules.Mine;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.AppleOrchard))
                {
                    buildingIndex = BuildingRules.AppleOrchard;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.FarmersMarket) && stateHelper.Player.GetTotalProductionTypeBuilt(EstablishmentProduction.Wheat) > 3)
                {
                    buildingIndex = BuildingRules.FarmersMarket;
                    buildingsPurchased = 0;
                }
                else if (affordable.Contains(BuildingRules.FamilyRestaurant))
                {
                    buildingIndex = BuildingRules.FamilyRestaurant;
                    buildingsPurchased = 0;
                }

                // Randomly pick one.
                if (buildingIndex == -1)
                {
                    buildingIndex = affordable[m_random.RandomInt(0, affordable.Count - 1)];
                    buildingsPurchased += 1;
                }

                Logger.Log(Log.Info, $"Requesting to build {stateHelper.BuildingRules[buildingIndex].GetName()}...");
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateBuildBuildingAction(buildingIndex));
                if (!result.Accepted)
                {
                    await Shutdown("failed to build building.", result.Error);
                }
                else
                {
                    Logger.Log(Log.Info, $"We just bought {stateHelper.BuildingRules[buildingIndex].GetName()}!");
                }
                return;
            }

            if (actionRequest.PossibleActions.Contains(GameActionType.EndTurn))
            {
                // If we can't roll the dice or build a building, we must not have enough funds.
                // Just end the turn.

                Logger.Log(Log.Info, "There's nothing to do, requesting turn end...");
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateEndTurnAction());
                if (!result.Accepted)
                {
                    // If random bot fails, it instantly shuts down.
                    await Shutdown("failed to end our turn.", result.Error);
                }
                else
                {
                    Logger.Log(Log.Info, $"We have {stateHelper.Player.GetPlayer().Coins} coins and can't buy anything, so we ended the turn.");
                }
                return;
            }

            if (actionRequest.PossibleActions.Contains(GameActionType.TvStationPayout))
            {
                // Our Tv Station activated! Let's take 5 coins from a player at random.
                GamePlayer randomPlayer = GetRandomPlayer(stateHelper);

                Logger.Log(Log.Info, $"Our Tv Station was activated, let's take coins from player {randomPlayer.Name}!");
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateTvStationPayoutAction(randomPlayer.PlayerIndex));
                if (!result.Accepted)
                {
                    // If random bot fails, it instantly shuts down.
                    await Shutdown("failed to respond to tv station payout.", result.Error);
                }
                else
                {
                    Logger.Log(Log.Info, $"We taken coins from player {randomPlayer.Name} for our tv station!");
                }
                return;
            }

            if (actionRequest.PossibleActions.Contains(GameActionType.BusinessCenterSwap))
            {
                // Our Business Center activated! Let's randomly pick a player and building to swap.
                GamePlayer randomPlayer = GetRandomPlayer(stateHelper);
                BuildingBase ourBuilding = GetRandomOwnedNonMajorBuidling(stateHelper, null);
                BuildingBase theirBuilding = GetRandomOwnedNonMajorBuidling(stateHelper, randomPlayer.PlayerIndex);

                GameActionResponse result;
                if (randomPlayer == null || ourBuilding == null || theirBuilding == null)
                {
                    // If there aren't any building we can use, skip the action.
                    Logger.Log(Log.Info, $"Our Business Center was activated, but there weren't the correct building to swap. So we will skip!");
                    result = await m_bot.SendAction(GameAction<object>.CreateBusinessCenterSwapAction(0, 0, 0, true));
                }
                else
                {
                    Logger.Log(Log.Info, $"Our Business Center was activated, swap our {ourBuilding.GetName()} for {randomPlayer.Name}'s {theirBuilding.GetName()}!");
                    result = await m_bot.SendAction(GameAction<object>.CreateBusinessCenterSwapAction(randomPlayer.PlayerIndex, ourBuilding.GetBuildingIndex(), theirBuilding.GetBuildingIndex()));
                }

                if (!result.Accepted)
                {
                    // If random bot fails, it instantly shuts down.
                    await Shutdown("failed to respond to business center swap.", result.Error);
                }
                else
                {
                    Logger.Log(Log.Info, $"Business center swap done!");
                }
                return;
            }

            Logger.Log(Log.Error, $"Hmm, we were asked for an action but didn't know what to do with...");
            foreach (GameActionType type in actionRequest.PossibleActions)
            {
                Logger.Log(Log.Error, $"  ... {type.ToString()}");
                await Shutdown("received an unknown action.", null);
            }
        }

        /// <summary>
        /// Returns a random player that's not us.
        /// </summary>
        /// <param name="stateHelper"></param>
        /// <returns></returns>
        private GamePlayer GetRandomPlayer(StateHelper stateHelper)
        {
            if (stateHelper.Player.GetPlayerCount() < 2)
            {
                return null;
            }
            // Start with our player index
            int playerIndex = stateHelper.Player.GetPlayer().PlayerIndex;

            // Pick a random number until we don't have our index.
            while (playerIndex == stateHelper.Player.GetPlayer().PlayerIndex)
            {
                playerIndex = m_random.RandomInt(0, stateHelper.Player.GetPlayerCount() - 1);
            }
            return stateHelper.GetState().Players[playerIndex];
        }

        /// <summary>
        /// Returns a random non major building, if any exists.
        /// </summary>
        /// <param name="stateHelper"></param>
        /// <param name="playerIndex"></param>
        /// <returns></returns>
        private BuildingBase GetRandomOwnedNonMajorBuidling(StateHelper stateHelper, int? playerIndex = null)
        {
            GamePlayer p = stateHelper.Player.GetPlayer(playerIndex);

            // Build a list of building the player owns and matches our filter.
            List<int> buildingIndex = new List<int>();
            for (int b = 0; b < stateHelper.BuildingRules.GetCountOfUniqueTypes(); b++)
            {
                BuildingBase building = stateHelper.BuildingRules[b];
                if (p.OwnedBuildings[b] > 0)
                {
                    if (building.GetEstablishmentColor() != EstablishmentColor.Landmark && building.GetEstablishmentColor() != EstablishmentColor.Purple)
                    {
                        buildingIndex.Add(b);
                    }
                }
            }

            // Make sure there are buildings.
            if (buildingIndex.Count == 0)
            {
                return null;
            }

            // Now get a random int index into the build index array, and get the building.
            return stateHelper.BuildingRules[buildingIndex[m_random.RandomInt(0, buildingIndex.Count - 1)]];
        }

        /// <summary>
        /// Helps us shutdown.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private async Task Shutdown(string message, GameError e)
        {
            Logger.Log(Log.Error, $"That's not good...");
            Logger.Log(Log.Error, $"   ...we failed to {message}");
            Logger.Log(Log.Error, $"   ...because {(e != null ? e.Message : "")}");
            Logger.Log(Log.Error, $"   ...time to give up and shutdown!");

            // Send the forfeit command to the game host since we don't know what to do.
            await m_bot.SendAction(GameAction<object>.CreateForfeitAction());

            // Disconnect the bot.
            await m_bot.Disconnect();
        }
    }
}

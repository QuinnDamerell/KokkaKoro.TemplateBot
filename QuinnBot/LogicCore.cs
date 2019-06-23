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
    class RoundInfo
    {
        public int MoneyAtStart = 0;
        public int MoneyAtBuild = 0;
        public int BuildingBoughtCost = 0;
    }

    class LogicCore
    {
        IBotInterface m_bot;
        RandomGenerator m_random = new RandomGenerator();
        List<RoundInfo> m_roundInfo = new List<RoundInfo>();

        public LogicCore(IBotInterface handler)
        {
            m_bot = handler;
        }

        public Task Setup()
        {
            return Task.CompletedTask;
        }

        public Task Cleanup()
        {
            return Task.CompletedTask;
        }

        public Task OnGameUpdate(GameStateUpdate<object> update)
        {
            return Task.CompletedTask;
        }

        public async Task OnGameActionRequested(GameActionRequest actionRequest, StateHelper stateHelper)
        {
            GameState state = stateHelper.GetState();
             // Always roll if it's an option.
            if (actionRequest.PossibleActions.Contains(GameActionType.RollDice))
            {
                if(m_roundInfo.Count <= state.CurrentTurnState.RoundNumber)
                {
                    m_roundInfo.Add(new RoundInfo() { MoneyAtStart = stateHelper.Player.GetPlayer().Coins });
                }

                // How many dice can we roll?
                //int maxDiceCount = stateHelper.Player.GetMaxCountOfDiceCanRoll();

                //// Can we re-roll
                //int rollsSoFar = stateHelper.GetState().CurrentTurnState.Rolls;
                //bool canReRoll = stateHelper.Player.GetMaxRollsAllowed() < rollsSoFar;

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
                // Update the amount of coins we have now.
                m_roundInfo[state.CurrentTurnState.RoundNumber].MoneyAtBuild = stateHelper.Player.GetPlayer().Coins;

                BuildingBase toBuy = GetBuildingToBuild(stateHelper);
                
                if (toBuy != null)
                {
                    // Note the cost.
                    m_roundInfo[state.CurrentTurnState.RoundNumber].BuildingBoughtCost = toBuy.GetBuildCost();

                    Logger.Log(Log.Info, $"Requesting to build {toBuy.GetName()}...");
                    GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateBuildBuildingAction(toBuy.GetBuildingIndex()));
                    if (!result.Accepted)
                    {
                        await Shutdown("failed to build building.", result.Error);
                    }
                    else
                    {
                        Logger.Log(Log.Info, $"We just bought {toBuy.GetName()}!");
                    }
                    return;
                }
                else
                {
                    Logger.Log(Log.Info, $"We are not going to build this turn.");
                }
            }

            if (actionRequest.PossibleActions.Contains(GameActionType.TvStationPayout))
            {
                // Our Tv Station activated!
                // Take coins from the player who's the closest to winning, but make sure they have 5 coins.
                // If we can't find anyone with 5 coins, just take anything.
                GamePlayer player = FindPlayerClosestToWinning(stateHelper, 5);
                if (player == null)
                {
                    player = FindPlayerClosestToWinning(stateHelper, 0);
                }

                Logger.Log(Log.Info, $"Our Tv Station was activated, let's take coins from player {player.Name}!");
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateTvStationPayoutAction(player.PlayerIndex));
                if (!result.Accepted)
                {
                    // If random bot fails, it instantly shuts down.
                    await Shutdown("failed to respond to tv station payout.", result.Error);
                }
                else
                {
                    Logger.Log(Log.Info, $"We taken coins from player {player.Name} for our tv station!");
                }
                return;
            }

            if (actionRequest.PossibleActions.Contains(GameActionType.BusinessCenterSwap))
            {
                // Our Business Center activated! Let's randomly pick a player and building to swap.

                // Let's take a card from the player how is winning right now.
                GamePlayer leadingPlayer = FindPlayerClosestToWinning(stateHelper);
                // Find our worst card
                BuildingBase ourWorst = GetWorstOrBestBuildingOwned(stateHelper, false, stateHelper.Player.GetPlayer().PlayerIndex);
                BuildingBase theirBest = GetWorstOrBestBuildingOwned(stateHelper, true, leadingPlayer.PlayerIndex);

                GameActionResponse result;
                if (ourWorst == null || theirBest == null)
                {
                    // Skip this action
                    Logger.Log(Log.Info, $"Our Business Center was activated, but there weren't the correct building to swap. So we will skip!");
                    result = await m_bot.SendAction(GameAction<object>.CreateBusinessCenterSwapAction(0, 0, 0, true));
                }
                else
                {
                    // Make the action.
                    Logger.Log(Log.Info, $"Our Business Center was activated, swap our {ourWorst.GetName()} for {leadingPlayer.Name}'s {theirBest.GetName()}!");
                    result = await m_bot.SendAction(GameAction<object>.CreateBusinessCenterSwapAction(leadingPlayer.PlayerIndex, ourWorst.GetBuildingIndex(), theirBest.GetBuildingIndex()));
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

            Logger.Log(Log.Error, $"Hmm, we were asked for an action but didn't know what to do with...");
            foreach (GameActionType type in actionRequest.PossibleActions)
            {
                Logger.Log(Log.Error, $"  ... {type.ToString()}");
                await Shutdown("received an unknown action.", null);
            }
        }

        private BuildingBase GetBuildingToBuild(StateHelper stateHelper)
        {
            // First, Check to see if we can build a landmark.
            BuildingBase toBuy = GetLandmarkIfWeCanBuildIt(stateHelper);
            if(toBuy != null)
            {
                return toBuy;
            }

            // Next, check if we are going to wait to build a marketplace building.
            if (CheckISWaitingToGetLandmarks(stateHelper))
            {
                return null;
            }

            // Get all building that are in the marketplace currently.
            List<int> buildable = stateHelper.Marketplace.GetBuildingTypesBuildableInMarketplace();

            // Filter to buildings we can afford and don't have the limit of.
            List<int> affordable = stateHelper.Player.FilterBuildingIndexesWeCanAfford(buildable);

            // Remove the train station, since it's a landmark we might be able to afford but we don't want to build.
            if (affordable.Contains(BuildingRules.TrainStation))
            {
                affordable.Remove(BuildingRules.TrainStation);
            }

            // Try to get a building under 6 that will smooth out how many buildings we own.
            toBuy = GetSmoothedBuilding(stateHelper, affordable, 6);
            if(toBuy != null)
            {
                Logger.Log(Log.Info, "We can buy a smooth building under 6.");
                return toBuy;
            }

            // Now try to get the best building to buy under 6.
            GamePlayer us = stateHelper.Player.GetPlayer();
            toBuy = GetBestOrWorstBuilding(stateHelper, affordable, true, false, null, 6);
            if(toBuy != null)
            {
                Logger.Log(Log.Info, "We can build a building under 6.");
                return toBuy;
            }

            // If not, try to get a building higher than 6.
            toBuy = GetBestOrWorstBuilding(stateHelper, affordable, true, false, null, null);
            if (toBuy != null)
            {
                Logger.Log(Log.Info, "We can build a building higher 6.");
                return toBuy;
            }

            Logger.Log(Log.Info, $"We couldn't find a building to make");
            return null;
        }

        private BuildingBase GetLandmarkIfWeCanBuildIt(StateHelper stateHelper)
        {
            GamePlayer us = stateHelper.Player.GetPlayer();
            if (stateHelper.Player.CanBuildBuilding(BuildingRules.RadioTower))
            {
                Logger.Log(Log.Info, "We can build a radio tower!");
                return stateHelper.BuildingRules[BuildingRules.RadioTower];
            }
            if (stateHelper.Player.CanBuildBuilding(BuildingRules.AmusementPark))
            {
                Logger.Log(Log.Info, "We can build a amusement park!");
                return stateHelper.BuildingRules[BuildingRules.AmusementPark];
            }
            if (stateHelper.Player.CanBuildBuilding(BuildingRules.ShoppingMall))
            {
                Logger.Log(Log.Info, "We can build a shopping mall!");
                return stateHelper.BuildingRules[BuildingRules.ShoppingMall];
            }
            // Since we only roll one dice, don't buy train station until we have the other buildings.
            if(stateHelper.Player.GetNumberOfLandmarksOwned() >= 2 && stateHelper.Player.CanBuildBuilding(BuildingRules.TrainStation))
            {
                Logger.Log(Log.Info, "We can build a train station!");
                return stateHelper.BuildingRules[BuildingRules.TrainStation];
            }
            return null;
        }

        private bool CheckISWaitingToGetLandmarks(StateHelper stateHelper)
        {
            double averageIncomeLast3Turns = 0;
            double added = 0;
            for(int i = m_roundInfo.Count - 1; i >= 0; i--)
            {
                if (added >= 3)
                {
                    break;
                }
                added++;

                int lastRoundValue = 0;
                int buildingCostLastRound = 0;
                if(i - 1 >= 0)
                {
                    lastRoundValue = m_roundInfo[i - 1].MoneyAtBuild;
                    buildingCostLastRound = m_roundInfo[i - 1].BuildingBoughtCost;
                }
                int diffFromLastTurn = m_roundInfo[i].MoneyAtBuild - lastRoundValue + buildingCostLastRound;
                averageIncomeLast3Turns += diffFromLastTurn;
            }
            averageIncomeLast3Turns = (averageIncomeLast3Turns / added);
            Logger.Log(Log.Info, $"We made an average of {averageIncomeLast3Turns} coins in the last 3 turns");

            // For these landmarks, we want to hold buying to make money if we are making money quickly enough.
            GamePlayer us = stateHelper.Player.GetPlayer();
            if (us.OwnedBuildings[BuildingRules.ShoppingMall] == 0 && averageIncomeLast3Turns > 4)
            {
                Logger.Log(Log.Info, $"We are holding buying a Shopping Mall.");
                return true;
            }
            if (us.OwnedBuildings[BuildingRules.AmusementPark] == 0 && averageIncomeLast3Turns > 8)
            {
                Logger.Log(Log.Info, $"We are holding buying a Amusement Park.");
                return true;
            }
            if (us.OwnedBuildings[BuildingRules.RadioTower] == 0 && averageIncomeLast3Turns > 10)
            {
                Logger.Log(Log.Info, $"We are holding buying a Radio Tower.");
                return true;
            }
            return false;
        }

        private BuildingBase GetSmoothedBuilding(StateHelper stateHelper, List<int> buildable, int? activationLimit)
        {
            List<int> smooth = GetMissingSmoothBuildings(stateHelper, buildable, activationLimit);
            if(smooth.Count == 0)
            {
                return null;
            }
            return GetBestOrWorstBuilding(stateHelper, smooth, true, false);
        }

        private List<int> GetMissingSmoothBuildings(StateHelper stateHelper, List<int> buildable, int? activationLimit)
        {
            GamePlayer p = stateHelper.Player.GetPlayer();
            List<int> list = new List<int>();
            int builtlimit = 1;
            while (builtlimit < 6 && list.Count == 0)
            {
                foreach(int b in buildable)
                {
                    BuildingBase bb = stateHelper.BuildingRules[b];
                    (int min, int max) = bb.GetActivationRange();
                    if (!activationLimit.HasValue || max <= activationLimit.Value)
                    {
                        if (p.OwnedBuildings[b] < builtlimit)
                        {
                            list.Add(b);
                        }
                    }
                }
                builtlimit++;
            }
            return list;
        }

        private GamePlayer FindPlayerClosestToWinning(StateHelper stateHelper, int minCoinAmount = 0)
        {
            GamePlayer currentPlayerIndex = null;
            int currentPlayerRank = -1;
            foreach(GamePlayer p in stateHelper.GetState().Players)
            {
                if(p.PlayerIndex == stateHelper.Player.GetPlayer().PlayerIndex)
                {
                    continue;
                }
                int rank = p.Coins;
                rank += (GetLandmarksOwned(stateHelper, p.PlayerIndex) * 10);
                if(rank > currentPlayerRank && p.Coins >= minCoinAmount)
                {
                    currentPlayerRank = rank;
                    currentPlayerIndex = p;
                }
            }
            return currentPlayerIndex;
        }

        private int GetLandmarksOwned(StateHelper stateHelper, int playerIndex)
        {
            GamePlayer player = stateHelper.Player.GetPlayer(playerIndex);
            return player.OwnedBuildings[BuildingRules.AmusementPark] + player.OwnedBuildings[BuildingRules.TrainStation] + player.OwnedBuildings[BuildingRules.RadioTower] + player.OwnedBuildings[BuildingRules.ShoppingMall];
        }

        private BuildingBase GetBestOrWorstBuilding(StateHelper stateHelper, List<int> buildingIndexes, bool best, bool excludeMajorAndPurple, int? maxPrice = null, int? maxDiceActivation = null, EstablishmentColor? colorFilter = null)
        {
            BuildingBase retB = null;
            int currentRank = best ? 100 : -100;
            foreach (int bi in buildingIndexes)
            {
                BuildingBase b = stateHelper.BuildingRules[bi];

                // Filter non major
                if(excludeMajorAndPurple && IsMajorOrPurple(stateHelper, b.GetBuildingIndex()))
                {
                    continue;
                }
                if(maxPrice.HasValue && b.GetBuildCost() > maxPrice.Value)
                {
                    continue;
                }
                if(colorFilter.HasValue && b.GetEstablishmentColor() != colorFilter.Value)
                {
                    continue;
                }
                (int minAct, int maxAct) = b.GetActivationRange();
                if(maxDiceActivation.HasValue && maxAct > maxDiceActivation.Value)
                {
                    continue;
                }
                int rank = GetBuildingRank(b.GetBuildingIndex());
                int rankDiff = rank - currentRank;
                if ((best && rankDiff < 0) || (!best && rankDiff > 0))
                {
                    retB = b;
                    currentRank = rank;
                }
            }
            return retB;
        }

        private List<int> GetBuildingIndexesOwned(StateHelper stateHelper, int? playerIndex = null)
        {
            GamePlayer p = stateHelper.Player.GetPlayer(playerIndex);
            List<int> indexes = new List<int>();
            for (int b = 0; b < p.OwnedBuildings.Count; b++)
            {
                if (p.OwnedBuildings[b] > 0)
                {
                    indexes.Add(b);
                }
            }
            return indexes;
        }

        private BuildingBase GetWorstOrBestBuildingOwned(StateHelper stateHelper, bool best, int playerIndex, bool onlyNonMajorEstablishment = true)
        {
            List<int> bi = GetBuildingIndexesOwned(stateHelper, playerIndex);
            return GetBestOrWorstBuilding(stateHelper, bi, best, onlyNonMajorEstablishment, 100);
        }

        private bool IsMajorOrPurple(StateHelper stateHelper, int buildingIndex)
        {
            return stateHelper.BuildingRules[buildingIndex].GetEstablishmentColor() == EstablishmentColor.Purple
                || stateHelper.BuildingRules[buildingIndex].GetEstablishmentColor() == EstablishmentColor.Landmark;
        }

        private int GetBuildingRank(int buildingIndex)
        {
            // Towards the front are better.
            int[] s_ranks =
            {
                BuildingRules.ShoppingMall,
                BuildingRules.RadioTower,
                BuildingRules.TrainStation,
                BuildingRules.AmusementPark,
                BuildingRules.Mine,
                BuildingRules.FurnitureFactory,
                BuildingRules.Forest,
                BuildingRules.Stadium,
                BuildingRules.TvStation,
                BuildingRules.AppleOrchard,
                BuildingRules.FamilyRestaurant,
                BuildingRules.CheeseFactory,
                BuildingRules.FarmersMarket,
                BuildingRules.BusinessCenter,
                BuildingRules.Ranch,
                BuildingRules.WheatField,
                BuildingRules.ConvenienceStore,
                BuildingRules.Cafe,
                BuildingRules.Bakery,
            };
            for(int i = 0; i < s_ranks.Length; i++)
            {
                if(s_ranks[i] == buildingIndex)
                {
                    return i;
                }
            }
            Logger.Log(Log.Warn, $"We failed to find the building index {buildingIndex} in the building ranks");
            return 20;
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

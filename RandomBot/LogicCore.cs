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

            // RANDOM BOT DOESN'T NEED NO SETUP!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!

            return Task.CompletedTask;
        }

        public Task Cleanup()
        {
            // This will be called when the bot is shutting down regardless if the game is complete or not.
            // If you want to write anything out to disk, now is a good time.

            // THERE IS NO CLEANING RANDOM BOTTTTTTTT!!!!!!!!!!!!!!!!!!!!

            return Task.CompletedTask;
        }

        public Task OnGameUpdate(GameStateUpdate<object> update)
        {
            // OnGameUpdate fires when just about anything changes in the game. This might be coins added to a user because of a building,
            // cards being swapped, etc. Your bot doesn't need to pay attention to these updates if you don't wish, when your bot needs to make
            // an action OnGameActionRequested will be called with the current game state and a list of possible actions.

            // RANDOM BOT DOESN'T GIVE A S$$$ ABOUT OTHER PLAYERS!

            return Task.CompletedTask;
        }

        public async Task OnGameActionRequested(GameActionRequest actionRequest, StateHelper stateHelper)
        {
            // Always roll if it's an option.
            if (actionRequest.PossibleActions.Contains(GameActionType.RollDice))
            {
                // If we can roll the dice.... LET'S DO IT!

                // Always roll ALL THE DICE
                int maxDiceCount = stateHelper.Player.GetMaxDiceCountCanRoll();

                // Check if we have another roll.
                int rollsSoFar = stateHelper.GetState().CurrentTurnState.Rolls;
                bool canReRoll = stateHelper.Player.GetMaxRollsAllowed() < rollsSoFar;

                // If we can't reroll, auto commit the dice. Otherwise don't, so we can reroll if we want.
                Logger.Log(Log.Info, "Requesting a dice roll! BIG MONEY NO WHAMMIES...");
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateRollDiceAction(maxDiceCount, !canReRoll));
                if (!result.Accepted)
                {
                    // If random bot fails, it instantly shuts down.
                    await Shutdown("failed to roll dice.", result.Error);
                }
                else
                {
                    Logger.Log(Log.Info, "Trust the dice gods, we roll the dice and commit!");
                }
                return;
            }

            if (actionRequest.PossibleActions.Contains(GameActionType.BuildBuilding))
            {
                // WE ARE A BIG ROLLER... let's build.

                // Get all building that are in the marketplace currently.
                List<int> buildable = stateHelper.Marketplace.GetBuildingTypesBuildableInMarketplace();

                // Filter it down to only buildings we can afford.
                List<int> affordable = stateHelper.Player.FilterBuildingIndexsWeCanAfford(buildable);

                // Randomly pick one.
                int buildingIndex = affordable[m_random.RandomInt(0, affordable.Count - 1)];

                // IF WE BUILD IT...
                Logger.Log(Log.Info, $"Requesting to build {stateHelper.BuildingRules[buildingIndex].GetName()}...");
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateBuildBuildingAction(buildingIndex));
                if (!result.Accepted)
                {
                    // If random bot fails, it instantly shuts down.
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

                // End it!
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

            if(actionRequest.PossibleActions.Contains(GameActionType.TvStationPayout))
            {
                // Our Tv Station activated! Let's take 5 coins from a player at random.
                GamePlayer randomPlayer = GetRandomPlayer(stateHelper);

                // DO IT!!!
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

            if(actionRequest.PossibleActions.Contains(GameActionType.BusinessCenterSwap))
            {
                // Our Business Center activated! Let's randomly pick a player and building to swap.
                GamePlayer randomPlayer = GetRandomPlayer(stateHelper);
                BuildingBase ourBuilding = GetRandomOwnedBuidling(stateHelper, null, true);
                BuildingBase theirBuilding = GetRandomOwnedBuidling(stateHelper, randomPlayer.PlayerIndex, true);

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
                    result = await m_bot.SendAction(GameAction<object>.CreateBusinessCenterSwapAction(randomPlayer.PlayerIndex, ourBuilding.GetBuldingIndex(), theirBuilding.GetBuldingIndex()));
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
            foreach(GameActionType type in actionRequest.PossibleActions)
            {
                Logger.Log(Log.Error, $"  ... {type.ToString()}");
            }
        }

        private GamePlayer GetRandomPlayer(StateHelper stateHelper)
        {
            if(stateHelper.Player.GetPlayerCount() < 2)
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

        private BuildingBase GetRandomOwnedBuidling(StateHelper stateHelper, int? playerIndex = null, bool onlyNormalBuildings = true)
        {
            GamePlayer p = stateHelper.Player.GetPlayerFromIndex(playerIndex);

            // Build a list of building the player owns and matches our filter.
            List<int> buildingIndex = new List<int>();
            for(int b = 0; b < stateHelper.BuildingRules.GetCountOfUniqueTypes(); b++)
            {
                BuildingBase building = stateHelper.BuildingRules[b];
                if(p.OwnedBuildings[b] > 0)
                {
                    if (!onlyNormalBuildings || (building.GetEstablishmentColor() != EstablishmentColor.Landmark&& building.GetEstablishmentColor() != EstablishmentColor.Purple))
                    {
                        buildingIndex.Add(b);
                    }
                }
            }

            // Make sure there are buildings.
            if(buildingIndex.Count == 0)
            {
                return null;
            }

            return stateHelper.BuildingRules[m_random.RandomInt(0, stateHelper.BuildingRules.GetCountOfUniqueTypes() - 1)];
        }

        private async Task Shutdown(string message, GameError e)
        {
            Logger.Log(Log.Error, $"That's not good...");
            Logger.Log(Log.Error, $"   ... we failed to {message} ...");
            Logger.Log(Log.Error, $"   ... because {e.Message} ...");
            Logger.Log(Log.Error, $"   ... time to give up and shutdown!");
            await m_bot.Disconnect();
        }        
    }
}

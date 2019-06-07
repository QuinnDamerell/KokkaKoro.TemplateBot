using GameCommon;
using GameCommon.Protocol;
using GameCommon.Protocol.ActionOptions;
using GameCommon.StateHelpers;
using KokkaKoroBotHost;
using KokkaKoroBotHost.ActionOptions;
using System;
using System.Collections.Generic;
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

                // We will set the flag to commit this dice roll, we trust in the random gods.
                // This means that rather than the server sending us the result and us committing it, it will be done automatically.
                Logger.Log(Log.Info, "Requesting a dice roll! BIG MONEY NO WHAMMIES...");
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateRollDiceAction(maxDiceCount, true));
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

            Logger.Log(Log.Error, $"Hmm, we were asked for an action but didn't know what to do with...");
            foreach(GameActionType type in actionRequest.PossibleActions)
            {
                Logger.Log(Log.Error, $"  ... {type.ToString()}");
            }
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

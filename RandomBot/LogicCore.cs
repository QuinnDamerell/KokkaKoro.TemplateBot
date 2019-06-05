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
        BuildingList m_buildingList;
        IBotInterface m_bot;

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
            // When we get the game start, build the building list object.
            // The building object list gives us the rules for each building.
            if(update.Type == StateUpdateType.GameStart)
            {
                m_buildingList = new BuildingList(update.State.Mode);
            }
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

                // Commit this dice roll, we trust in the random gods and don't need to see the results.
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateRollDiceAction(maxDiceCount, true));
                if (!result.Accepted)
                {
                    // If random bot fails, it instantly shuts down.
                    await Shutdown("failed to roll dice.", result.Error);
                }
                else
                {
                    Logger.Info("Trust the dice gods, we roll the dice and commit!");
                    return;
                }
            }

            if(actionRequest.PossibleActions.Contains(GameActionType.BuyBuilding))
            {

            }

            Logger.Info($"Hmm, we were asked for an action but didn't know what to do with...");
            foreach(GameActionType type in actionRequest.PossibleActions)
            {
                Logger.Info($"  ...{type.ToString()}");
            }
        }

        public Task OnGameUpdate(GameStateUpdate<object> update)
        {
            // OnGameUpdate fires when just about anything changes in the game. This might be coins added to a user because of a building,
            // cards being swapped, etc. Your bot doesn't need to pay attention to these updates if you don't wish, when your bot needs to make
            // an action OnGameActionRequested will be called with the current game state and a list of possible actions.
            return Task.CompletedTask;
        }

        private async Task Shutdown(string message, GameError e)
        {
            Logger.Error($"That's not good...");
            Logger.Error($"   ... we failed to {message} ...");
            Logger.Error($"   ... because {e.Message} ...");
            Logger.Error($"   ... time to give up and shutdown!");
            await m_bot.Disconnect();
        }        
    }
}

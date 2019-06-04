using GameCommon;
using GameCommon.Protocol;
using GameCommon.Protocol.ActionOptions;
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

        public async Task OnGameActionRequested(GameActionRequest actionRequest, StateHelper stateHelper)
        {
            // OnGameActionRequested fires when your bot actually needs to do something. In the request, you will find the entire game state (what you would normally see on the table)
            // and a list of possible actions. Some actions have options that you need to provide when taking them, things like how many dice to roll, or which building you would like to buy.

            // You will get many OnGameActionRequested per turn.
            // When you take an action by calling `SendAction` two things can happen.
            //    Action Accepted - You can leave the `OnGameActionRequested` and it will be called again for the next decision you must make this turn.
            //    Action Not Accepted - You should try to recover and submit the action again. The game will not progress until the action is accepted. If you don't get an accepted action 
            //                          before the game turn timeout you will lose your turn.

            if (actionRequest.PossibleActions.Contains(GameActionType.RollDice))
            {
                // If we are asked to roll the dice, we need to tell the service how many dice we want to roll.
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateRollDiceAction(DiceCount.OneDice));
                if (!result.Accepted)
                {
                    // If the action isn't accepted, the bot should try to correct and send the action again until result.WasTakenOnPlayersTurn returns false.
                    // After the turn timeout if the bot fails to submit a action, the turn will be skipped.
                    Logger.Info($"Our roll dice action wasn't accepted. Error Type: {result.Error.Type}; Can try again? {result.Error.CanTryAgain}; Error: {result.Error.Message}");

                    bool cantRecover = true;
                    if (cantRecover)
                    {
                        // Request to terminate the game
                        // TODO

                        // If we can't recover our state, calling disconnect will shutdown the bot.
                        //await m_bot.Disconnect();
                    }
                }
                else
                {
                    Logger.Info($"We rolled the dice!");
                }
            }
            else
            {
                Logger.Info($"We were asked to do an action we don't know how to! {actionRequest.PossibleActions}");
            }
        }

        public Task OnGameUpdate(GameStateUpdate update)
        {
            // OnGameUpdate fires when just about anything changes in the game. This might be coins added to a user because of a building,
            // cards being swapped, etc. Your bot doesn't need to pay attention to these updates if you don't wish, when your bot needs to make
            // an action OnGameActionRequested will be called with the current game state and a list of possible actions.
            return Task.CompletedTask;
        }

        private void HandleDiceRoll()
        {

        }
    }
}

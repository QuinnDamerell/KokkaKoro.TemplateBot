using GameCommon.Protocol;
using KokkaKoroBotHost;
using KokkaKoroBotHost.ActionOptions;
using KokkaKoroBotHost.ActionResponses;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KokkaKoroBot
{
    class MyBot : BotHost
    {
        public override Task OnGameJoined()
        {
            Logger.Info($"OnGameJoined called.");

            // To avoid making the function async (because we don't need it) we will return this task.
            // Remove this and make the function async if you need to await things.
            return Task.CompletedTask;
        }

        public override Task OnGameUpdate(GameUpdate update)
        {
            Logger.Info($"Game update! {update.UpdateText}");

            // OnGameUpdate fires when just about anything changes in the game. This might be coins added to a user because of a building,
            // cards being swapped, etc. Your bot doesn't need to pay attention to these updates if you don't wish, when your bot needs to make
            // an action OnGameActionRequested will be called with the current game state and a list of possible actions.

            return Task.CompletedTask;
        }

        public override async Task OnGameActionRequested(GameActionRequest actionRequest)
        {
            Logger.Info($"OnGameActionRequested!");

            // OnGameActionRequested fires when your bot actually needs to do something. In the request, you will find the entire game state (what you would normally see on the table)
            // and a list of possible actions. Some actions have options that you need to provide when taking them, things like how many dice to roll, or which building you would like to buy.

            if(actionRequest.PossibleActions.Contains(GameActionType.RollDice))
            {
                // If we are asked to roll the dice, we need to tell the service how many dice we want to roll.
                SendActionResult result = await SendAction(GameAction<object>.CreateRollDiceAction(1));
                if(!result.WasAccepted)
                {
                    // If the action isn't accepted, the bot should try to correct and send the action again until result.WasTakenOnPlayersTurn returns false.
                    // After the turn timeout if the bot fails to submit a action, the turn will be skipped.
                    Logger.Info($"Our roll dice action wasn't accepted. Was our turn? {result.WasTakenOnPlayersTurn}, Error: {result.ErrorIfNotSuccessful}");
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

        public override Task OnDisconnected(string reason, bool isClean, Exception e)
        {
            Logger.Info($"OnDisconnected called. Reason: {reason}, Exception: {(e == null ? "" : e.Message)}, isClean: {isClean}");

            return Task.CompletedTask;
        }

        #region Advance Functions

        // Function call pattern
        //
        // 1) OnSetup
        // 2) OnConnecting
        // 3) OnConnected
        // 4) 

        public override Task<OnSetupResponse> OnSetup(OnSetupOptions options)
        {
            // OnSetup is called first when the BotHost is figuring out how to run.
            //
            // If `options.IsHosted` is set to true, this bot is running on the server and won't
            // take any configurations. This is because the game is already setup for it to run.
            //
            // If `options.IsHosted` is false, the bot is ruining as a remote player. This can either
            // against the public service or a local server. To connect to a local server, the bot should 
            // set the `LocalServerPort` port in `OnSetupResponse`.
            Logger.Info($"OnSetup called; Is running hosted? {options.IsHosted}");

            return Task.FromResult(new OnSetupResponse()
            {
                // Set this if you want to connect to a local server.
                // (only respected if the bot isn't running in hosted mode).
                LocalServerPort = 27699,
                // If this bot is running remotely, you must supply a user name.
                // (only respected if the bot isn't running in hosted mode).
                UserName = "MyFirstBot",
                // If this bot is running remotely, you must supply a passcode.
                // (only respected if the bot isn't running in hosted mode).
                Passcode = "IamARobot"
            });
        }

        // Called when the bot is connecting to the service.
        public override Task OnConnecting()
        {
            Logger.Info($"OnConnecting called.");
            return Task.CompletedTask;
        }

        // Called when the bot has connected.
        public override Task OnConnected()
        {
            Logger.Info($"OnConnecting called.");
            return Task.CompletedTask;
        }

        // Only called on remote bots, this allows them to create or join a game.
        public override async Task<OnGameConfigureResponse> OnRemoteBotGameConfigure()
        {
            // Using the service SDK, you can call list bots. Example:
            List<ServiceProtocol.Common.KokkaKoroBot> bots = await KokkaKoroService.ListBots();

            // But we know we want to beat the test bot.
            List<string> botNames = new List<string>();
            botNames.Add("TestBot");

            // We want to make a new game that auto starts and has the test bot to play against.

            return OnGameConfigureResponse.CreateNewGame("MyTestBotGame", botNames, true);           
        }

        public override Task OnUnhandledException(string callbackName, Exception e)
        {
            Logger.Info($"OnUnhandledException. The bot will be terminated. Callback Name: {callbackName}, Exception: {e.Message}");
            return Task.CompletedTask;
        }

        #endregion
    }
}

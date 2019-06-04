using GameCommon;
using GameCommon.Protocol;
using KokkaKoroBotHost;
using KokkaKoroBotHost.ActionOptions;
using KokkaKoroBotHost.ActionResponses;
using ServiceProtocol.Common;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KokkaKoroBot
{
    // This interface just mocks the BotHost functions to give the logic core a
    // more clean view of the world.
    public interface IBotInterface
    {
        // Sends an action. Throws if fails!
        Task<GameActionResponse> SendAction(GameAction<object> action);

        // Disconnects the bot (shuts it down)
        Task Disconnect();
    }

    class MyBot : BotHost, IBotInterface
    {
        // To keep our logic more abstract, we will create a new object to contain it.
        LogicCore m_logicCore;

        public MyBot()
        {
            // Setup our logic core.
            m_logicCore = new LogicCore(this);
        }

        // This interface gives a clean view of the bot functions to the logic core.
        async Task<GameActionResponse> IBotInterface.SendAction(GameAction<object> action)
        {
            return await SendAction(action);
        }

        // This interface gives a clean view of the bot functions to the logic core.
        async Task IBotInterface.Disconnect()
        {
            await Disconnect();
        }

        public override async Task OnConnected()
        {
            // Called when the bot has connected to the service.
            Logger.Info($"OnConnecting called.");

            // OnConnected is a good time to do async setup work that might take some time.
            // Once the game is joined and we start getting action requests, there's a turn time limit we must finish the turn in or it will be lost.
            // But we can block OnConnected for up to the game time limit (defaults to 1h) with no problems.
            await m_logicCore.Setup();
        }

        public override Task OnGameJoined()
        {
            // Called when the game has been joined.
            Logger.Info($"OnGameJoined called.");

            return Task.CompletedTask;
        }

        public override async Task OnGameStateUpdate(GameStateUpdate update)
        {
            // OnGameUpdate fires when just about anything changes in the game. This might be coins added to a user because of a building,
            // cards being swapped, etc. Your bot doesn't need to pay attention to these updates if you don't wish, when your bot needs to make
            // an action OnGameActionRequested will be called with the current game state and a list of possible actions.
            Logger.Info($"Game update! {update.Type} : {update.Reason}");

            await m_logicCore.OnGameUpdate(update);
        }

        public override async Task OnGameActionRequested(GameActionRequest actionRequest)
        {
            // OnGameActionRequested fires when your bot actually needs to do something. In the request, you will find the entire game state (what you would normally see on the table)
            // and a list of possible actions. Some actions have options that you need to provide when taking them, things like how many dice to roll, or which building you would like to buy.
            Logger.Info($"OnGameActionRequested!");

            // This state helper object is useful to validate state questions.
            StateHelper stateHelper = actionRequest.State.GetStateHelper(GetCurrentUserName());

            await m_logicCore.OnGameActionRequested(actionRequest, stateHelper);
        }

        public override async Task OnDisconnected(string reason, bool isClean, Exception optionalException)
        {
            // Called when the bot is disconnected from the service either intentionally or unintentionally.
            Logger.Info($"OnDisconnected called. Reason: {reason}, Exception: {(optionalException == null ? "None" : optionalException.Message)}, isClean: {isClean}");

            // Now is a good time to cleanup, because for some reason (good or bad) the bot has disconnected and will be shutdown.
            await m_logicCore.Cleanup();
        }

        #region Advance Functions

        // Function call pattern
        //
        // 1) OnSetup
        // 2) OnConnecting
        // 3) OnConnected
        // 3.1) OnRemoteBotGameConfigure - Only for non hosted bots
        // 4) OnGameJoined
        // 5) Loop
        //    5.1) OnGameUpdate
        //    5.2) OnGameActionRequested
        // 6) OnDisconnected
        // Anytime) OnUnhandledException

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

        public override Task OnConnecting()
        {
            // Called when the bot is connecting to the service.
            Logger.Info($"OnConnecting called.");

            return Task.CompletedTask;
        }

        public override async Task<OnGameConfigureResponse> OnRemoteBotGameConfigure()
        {
            // Only called on remote bots, not for hosted bots.
            //
            // Hosted bots are passed their information, like user name, and what game to join. 
            // But remote bots just act like external players. So when they start, you have to either join a game that's already
            // been created, or create a new game. 
            //
            // This function simply gives you a nice interface to create or join a game. It's also possible to use the
            // `KokkaKoroService` object directly to call the service directly.
            // Note! All KokkaKoroService throw exceptions if there is an unexpected failure.

            // Example: List all games on the service
            // List<KokkaKoroGame> games = await KokkaKoroService.ListGames();

            // Example: Using the service SDK, you can call list bots:
            List<ServiceProtocol.Common.KokkaKoroBot> bots = await KokkaKoroService.ListBots();

            // But we know we want to beat the TestBot.
            List<string> botNames = new List<string>();
            botNames.Add("TestBot");

            // We want to make a new game that auto starts and has the test bot to play against.
            return OnGameConfigureResponse.CreateNewGame("MyTestBotGame", botNames, true);

            // Or if we used the service to create a game, we could join it like this.
            // return OnGameConfigureResponse.JoinGame(gameId);
        }

        public override Task OnUnhandledException(string callbackName, Exception e)
        {
            // Called when one of the bot functions let an exception leak back to the bot host.
            Logger.Info($"OnUnhandledException. The bot will be terminated. Callback Name: {callbackName}, Exception: {e.Message}");
            return Task.CompletedTask;
        }

        #endregion
    }
}

using GameCommon;
using GameCommon.Protocol;
using GameCommon.StateHelpers;
using KokkaKoro;
using KokkaKoroBotHost.ActionOptions;
using KokkaKoroBotHost.ActionResponses;
using ServiceProtocol.Common;
using ServiceProtocol.Requests;
using ServiceProtocol.Responses;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;

namespace KokkaKoroBotHost
{
    class HostedBotArgs
    {
        public string LocalServerAddress;
        public string UserName;
        public string Passcode;
        public string GamePassword;
        public Guid GameId;
    }

    public class SendActionResult
    {
        // Indicates if the action was committed successfully.
        public bool WasAccepted;

        // Indicates if the action was attempted to be taken on the players turn or not.
        public bool WasTakenOnPlayersTurn;

        // If not, this string will indicate why.
        public string ErrorIfNotSuccessful;
    }

    public abstract class BotHost
    {
        // 
        //  Public Overwrite Functions.
        //

        // Called initial to figure out how the bot is being ran.
        // There are two configurations
        //    Hosted Bot - Running on the server, the bot has little control. (the game is already setup for it)
        //    Remote Player - The bot is either playing on the service or a local server, it's responsible for setting up the game.
        public abstract Task<OnSetupResponse> OnSetup(OnSetupOptions options);

        // Called when we are connecting to the service.
        public abstract Task OnConnecting();

        // Called when we are connected to the service.
        public abstract Task OnConnected();

        // Called only for remote bots. This function allows the bot to create a new game, add bots, and join it, or join an existing game.
        public abstract Task<OnGameConfigureResponse> OnRemoteBotGameConfigure();

        // Called when the game has been joined
        public abstract Task OnGameJoined();

        // Fires when the game the bot has connected to has a state update.
        // This doesn't mean the bot must respond, but it can watch updates to know what's happening on other's turns.
        public abstract Task OnGameStateUpdate(GameStateUpdate<object> update);

        // Fires when the game the bot has connected wants the bot to decide on an action.
        // This will fire as part of the bot's turn, the argument object has details on the actions that can be preformed.
        public abstract Task OnGameActionRequested(GameActionRequest actionRequest);

        // Called when the bot is disconnected for some reason.
        // The exception is optional, so it might be null.
        public abstract Task OnDisconnected(string reason, bool isClean, Exception optionalException);

        // Called when there is an unhanded exception from the bot.
        // The program will exit after the function returns.
        // The exception is optional, so it might be null.
        public abstract Task OnUnhandledException(string callbackName, Exception optionalException);

        // 
        // Public functions
        //

        // Runs the bot and blocks the current thread until it's complete.
        public void Run()
        {
            if(m_hasRan)
            {
                return;
            }
            m_hasRan = true;

            // We want to block this thread while we do our async work.
            AutoResetEvent are = new AutoResetEvent(false);
            Task.Run(async () =>
            {
                try
                {
                    await RunInternal();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"RunInternal Exception: {e.Message}");
                }
                are.Set();
            });
            are.WaitOne();
        }

        // Disconnects the bot and terminates it.
        public async Task Disconnect()
        {
            // Tell the bot we are disconnecting.
            await FireDisconnect("Client invoked", true, null);

            // Kill the connection to the service.
            if(KokkaKoroService != null)
            {
                await KokkaKoroService.Disconnect();
            }

            // Let the run function exit.
            try
            {
                m_waitHandle.Release();
            }
            catch {}
        }

        //
        // Protected vars that the sub class is allowed to use.
        //

        // Allows the client access to the service SDK to make calls.
        protected Service KokkaKoroService;

        protected Guid GetJoinedGameId()
        {
            return m_connectedGameId;
        }

        protected string GetCurrentUserName()
        {
            return m_userName;
        }

        protected async Task<GameActionResponse> SendAction(GameAction<object> action)
        {
            string error = null;
            SendGameActionResponse response = null;
            try
            {
                response = await KokkaKoroService.SendGameAction(new SendGameActionOptions() { Action = action, GameId = m_connectedGameId });
                if(response.Response == null)
                {
                    throw new Exception("An empty response was sent back.");
                }
            }
            catch(Exception e)
            {
                error = $"Exception thrown while making call, message: {e.Message}";
            }

            if(!String.IsNullOrWhiteSpace(error))
            {
                return GameActionResponse.CreateError(GameError.Create(null, ErrorTypes.LocalError, error, false));
            }
            else if(response.Response == null || (!response.Response.Accepted && response.Response.Error == null) || (!response.Response.Accepted && String.IsNullOrWhiteSpace(response.Response.Error.Message)))
            {
                return GameActionResponse.CreateError(GameError.Create(null, ErrorTypes.LocalError, "Result failed but has no error object.", false));
            }
            else
            {
                return response.Response;
            }
        }

        //
        // Private vars.
        //

        // The handle the run function will wait on. When this is set the run function will exit.
        SemaphoreSlim m_waitHandle = new SemaphoreSlim(0, 1);

        // If this is a hosted bot, this will not be null and will hold the vars.
        HostedBotArgs m_hostedArgs;

        // The user name we logged in as. This way we can find who we are in the games.
        string m_userName;

        // Once we join a game, set the connected game id so 
        // we can filter out game updates.
        Guid m_connectedGameId;

        // A guard to make sure we only fire disconnected once.
        bool m_hasFiredDisconnect = false;

        // Indicates if the bot has ran before, since we can only run once.
        bool m_hasRan = false;
               
        private async Task RunInternal()
        {
            // Make a service. Remember this can only be used for a single connection.
            Service kokkaKoroService = new Service();

            // Add our handlers.
            kokkaKoroService.OnDisconnected += KokkaKoroService_OnDisconnected;
            kokkaKoroService.OnGameUpdates += KokkaKoroService_OnGameUpdates;

            // Connect
            if (!await ConnectAndLogin(kokkaKoroService))
            {
                return;
            }

            // After we connect and login, set the service to the bot has access to it.
            KokkaKoroService = kokkaKoroService;

            // Configure the game we will connect to.
            (Guid gameId, string gamePassword, bool autoStart, bool success) = await ConfigureGame(kokkaKoroService); 
            if(!success)
            {
                return;
            }

            // Set the gameId of the game we are trying to connect to so we can filter out it's updates.
            m_connectedGameId = gameId;

            // Now join the game.
            if (!await JoinGame(kokkaKoroService, gameId, gamePassword))
            {
                return;
            }

            // Start the game if we were told to.
            if(autoStart && !await StartGame(kokkaKoroService, gameId, gamePassword))
            {
                return;
            }

            // Wait on the handle to exit.
            // Once this is set, we should return so Run() returns.
            await m_waitHandle.WaitAsync();
        }

        // Fired when there is a new game update for any game this user is connected to.
        private async Task KokkaKoroService_OnGameUpdates(List<GameLog> newLogItems)
        {
            // Handle each game log event.
            foreach(GameLog log in newLogItems)
            {
                if(log.GameId.Equals(m_connectedGameId))
                {
                    if(log.StateUpdate != null)
                    {
                        // Fire on connecting.
                        try { await OnGameStateUpdate(log.StateUpdate); }
                        catch (Exception e) { await FireOnUnhandledException("OnGameStateUpdate", e); return; }
                    }
                    else if(log.ActionRequest != null)
                    {
                        // Only fire action requested for this player. 
                        // This logic can be changed so the bot can observe the server asking all players for actions.
                        StateHelper stateHelper = log.ActionRequest.State.GetStateHelper(GetCurrentUserName());
                        if(stateHelper.CurrentTurn.IsMyTurn())
                        {
                            // Fire the action request.
                            try { await OnGameActionRequested(log.ActionRequest); }
                            catch (Exception e) { await FireOnUnhandledException("OnGameActionRequested", e); return; }
                        }                       
                    }
                }
            }
        }

        // Fires when the websocket closes. We only care if this isn't client invoked.
        private async Task KokkaKoroService_OnDisconnected(bool isClientInvoked)
        {
            await FireDisconnect("Lost websocket connection.", isClientInvoked, null);
        }

        private async Task<bool> ConnectAndLogin(Service kokkaKoroService)
        {
            // First, look for environment vars.
            // These will be passed by the server if it's running hosted.
            FindEnvVars();

            // Now based on the state, call the bot setup.
            OnSetupResponse setup = null;
            try { setup = await OnSetup(new OnSetupOptions() { IsHosted = IsHostedBot() });
            }catch(Exception e)
            { await FireOnUnhandledException("OnSetup", e); return false; }

            int? localPort = null;
            if (IsHostedBot())
            {
                // For hosted bots, use the local address given by the service.
                try
                {
                    int pos = m_hostedArgs.LocalServerAddress.LastIndexOf(":");
                    localPort = int.Parse(m_hostedArgs.LocalServerAddress.Substring(pos + 1));
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to parse local server address. {m_hostedArgs.LocalServerAddress}, msg {e.Message}");
                    return false;
                }       
            }
            else
            {
                // If this is a remote player, check if the bot has a local port they want to connect to.
                localPort = setup.LocalServerPort.Value;
            }           

            // Fire on connecting.
            try { await OnConnecting(); }
            catch (Exception e) { await FireOnUnhandledException("OnConnecting", e); return false; }

            // Try to connect to the service.
            try
            {
                await kokkaKoroService.ConnectAsync(localPort);
            }
            catch(Exception e)
            {
                await FireDisconnect("Failed to connect to server.", false, e);
                return false;
            }

            // And login
            string userName = IsHostedBot() ? m_hostedArgs.UserName : setup.UserName;
            string passcode = IsHostedBot() ? m_hostedArgs.Passcode : setup.Passcode;
            try
            {
                await kokkaKoroService.Login(new LoginOptions() { User = new KokkaKoroUser() { UserName = userName, Passcode = passcode } });
            }
            catch(Exception e)
            {
                await FireDisconnect("Failed to login.", false, e);
                return false;
            }

            // Once we have logged in, set the user name we used.
            m_userName = userName;

            return true;
        }

        private async Task<(Guid, string, bool, bool)> ConfigureGame(Service kokkaKoroService)
        {
            // If this is a hosted bot, we already know what game we want.
            if(IsHostedBot())
            {
                return (m_hostedArgs.GameId, m_hostedArgs.Passcode, false, true);
            }

            // If this is a remote bot, ask the client what they want.
            OnGameConfigureResponse gameConfig = null;
            // Now based on the state, call the bot setup.
            try
            {
                gameConfig = await OnRemoteBotGameConfigure();
                if(gameConfig == null)
                {
                    throw new Exception("No game config returned");
                }                
            }
            catch (Exception e)
            {
                await FireOnUnhandledException("OnRemovteBotGameConfigure", e);
                return (Guid.Empty, null, false, false);
            }

            Guid gameId;
            if(gameConfig.ConfigType == GameConfigureType.CreateNewGame)
            {
                // If we need to create a game, do it now.
                KokkaKoroGame newGame = null;
                try
                {
                    newGame = await kokkaKoroService.CreateGame(new CreateGameOptions() { GameName = gameConfig.GameName, Password = gameConfig.GamePassword });
                }
                catch (Exception e)
                {
                    await FireDisconnect("Failed create game.", false, e);
                    return (Guid.Empty, null, false, false);
                }

                // Add any bots requested.
                foreach(string botName in gameConfig.NewGameBotNames)
                {
                    try
                    {
                        newGame = await kokkaKoroService.AddBotToGame(new AddHostedBotOptions() { BotName = botName, InGameName = botName, GameId = newGame.Id, Password= gameConfig.GamePassword });
                    }
                    catch (Exception e)
                    {
                        await FireDisconnect("Failed to add bot.", false, e);
                        return (Guid.Empty, null, false, false);
                    }
                }

                gameId = newGame.Id;
            }
            else
            {
                gameId = gameConfig.JoinGameId;
            }

            return (gameId, gameConfig.GamePassword, gameConfig.ShouldAutoStartGame, true);  
        }

        private async Task<bool> JoinGame(Service kokkaKoroService, Guid gameId, string password)
        {
            // Try to connect to the service.
            try
            {                
                await kokkaKoroService.JoinGame(new JoinGameOptions() { GameId = gameId, Password = password });
            }
            catch (Exception e)
            {
                await FireDisconnect("Failed to join game.", false, e);
                return false;
            }

            // Fire on connecting.
            try { await OnGameJoined(); }
            catch (Exception e) { await FireOnUnhandledException("OnGameJoined", e); return false; }

            return true;
        }

        private async Task<bool> StartGame(Service kokkaKoroService, Guid gameId, string password)
        {
            // Try to connect to the service.
            try
            {
                await kokkaKoroService.StartGame(new StartGameOptions() { GameId = gameId, Password = password });
            }
            catch (Exception e)
            {
                await FireDisconnect("Failed to start game.", false, e);
                return false;
            }
            return true;
        }

        private async Task FireDisconnect(string reason, bool isClean, Exception e)
        {
            if(m_hasFiredDisconnect)
            {
                return;
            }
            m_hasFiredDisconnect = true;

            try { await OnDisconnected(reason, isClean, e); }
            catch (Exception ex) { await OnUnhandledException("OnDisconnect", ex); }

            // If we fired disconnect, call this disconnect as well to make sure the bot shutsdown.
            // Note this would make a loop, but m_hasFiredDisconnect protects us.
            await Disconnect();
        }

        private async Task FireOnUnhandledException(string callbackName, Exception e)
        {
            try { await OnUnhandledException(callbackName, e); }
            catch { }
            // Always exit the Run() function after this is called.
            m_waitHandle.Release();
        }

        public bool IsHostedBot()
        {
            return m_hostedArgs != null;
        }

        private void FindEnvVars()
        {
            const string c_userNameKey = "UserName";
            const string c_userPasscodeKey = "Passcode";
            const string c_gameIdKey = "GameId";
            const string c_gamePasswordKey = "GamePassword";
            const string c_localServiceAddress = "LocalServiceAddress";
            string localServerAddress = Environment.GetEnvironmentVariable(c_localServiceAddress, EnvironmentVariableTarget.Process);
            string userName = Environment.GetEnvironmentVariable(c_userNameKey, EnvironmentVariableTarget.Process);
            string passcode = Environment.GetEnvironmentVariable(c_userPasscodeKey, EnvironmentVariableTarget.Process);
            string gameId = Environment.GetEnvironmentVariable(c_gameIdKey, EnvironmentVariableTarget.Process);
            // Note that gamePassword might not be set, that indicates there is no password.
            string gamePassword = Environment.GetEnvironmentVariable(c_gamePasswordKey, EnvironmentVariableTarget.Process);
            if (!String.IsNullOrWhiteSpace(localServerAddress) && !String.IsNullOrWhiteSpace(userName) && !String.IsNullOrWhiteSpace(passcode) && !String.IsNullOrWhiteSpace(gameId))
            {
                Guid output;
                if(Guid.TryParse(gameId, out output))
                {
                    m_hostedArgs = new HostedBotArgs()
                    {
                        LocalServerAddress = localServerAddress,
                        GameId = output,
                        GamePassword = gamePassword,
                        Passcode = passcode,
                        UserName = userName,
                    };
                }
            }
        }
    }
}

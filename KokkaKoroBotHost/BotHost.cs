using KokkaKoro;
using KokkaKoroBotHost.ActionOptions;
using KokkaKoroBotHost.ActionResponses;
using ServiceProtocol.Common;
using ServiceProtocol.Requests;
using System;
using System.Collections;
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

    public abstract class BotHost
    {
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
        public abstract Task<OnGameConfigureResponse> OnRemovteBotGameConfigure();

        public abstract Task OnDisconnected(string reason, bool isClean, Exception e);

        public abstract Task OnUnhandledException(string callbackName, Exception e);

        // Called when the game has been joined
        public abstract Task OnGameJoined();

        // Allows the client access to the service SDK to make calls.
        protected Service KokkaKoroService;

        // Class vars
        HostedBotArgs m_hostedArgs;
        bool m_hasFiredDisconnect = false;

        public void Run()
        {
            // We want to block this thread while we do our async work.
            AutoResetEvent are = new AutoResetEvent(false);
            Task.Run(async () =>
            {
                try
                {
                    await RunInternal();
                }
                catch(Exception e)
                {
                    Console.WriteLine($"RunInternal Exception: {e.Message}");
                }
                are.Set();
            });
            are.WaitOne();
        }

        private async Task RunInternal()
        {
            // Make a service. Remember this can only be used for a single connection.
            Service kokkaKoroService = new Service();
            KokkaKoroService = kokkaKoroService;

            // Connect
            if (!await ConnectAndLogin(kokkaKoroService))
            {
                return;
            }

            // Configure the game we will connect to.
            (Guid gameId, string gamePassword, bool autoStart, bool success) = await ConfigureGame(kokkaKoroService); 
            if(!success)
            {
                return;
            }

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

            for(int i = 0; i < 60; i++)
            {
                Console.WriteLine("Sleeping...");
                Thread.Sleep(1000);
            }
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
            { await FireOnUnhandledException("OnSetup", e); }

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
            catch (Exception e) { await FireOnUnhandledException("OnConnecting", e); }

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
                gameConfig = await OnRemovteBotGameConfigure();
                if(gameConfig == null)
                {
                    throw new Exception("No game config returned");
                }                
            }
            catch (Exception e)
            { await FireOnUnhandledException("OnRemovteBotGameConfigure", e); }

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
            catch (Exception e) { await FireOnUnhandledException("OnGameJoined", e); }

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
        }

        private async Task FireOnUnhandledException(string callbackName, Exception e)
        {
            try { await OnUnhandledException(callbackName, e); }
            catch { }
            Environment.Exit(1);
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

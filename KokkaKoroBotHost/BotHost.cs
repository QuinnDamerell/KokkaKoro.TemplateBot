﻿using KokkaKoro;
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
        public string UserName;
        public string Passcode;
        public Guid GameId;
    }

    public abstract class BotHost
    {
        // Called inital to figure out how the bot is being ran.
        // There are two configurations
        //    Hosted Bot - Running on the server, the bot has little control. (the game is already setup for it)
        //    Remote Player - The bot is either playing on the service or a local server, it's responsible for setting up the game.
        public abstract Task<OnSetupResponse> OnSetup(OnSetupOptions options);

        // Called when we are connecting to the service.
        public abstract Task OnConnecting();

        // Called when we are connected to the service.
        public abstract Task OnConnected();

        public abstract Task OnDisconnected(string reason, bool isClean, Exception e);

        public abstract Task OnUnhandledException(string callbackName, Exception e);

        // Called when the game has been joined
        public abstract void OnGameJoined();

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
            // Make a service. Remember this can only be used for a single connnection.
            Service kokkaKoroService = new Service();

            // Connect
            if(!await ConnectAndLogin(kokkaKoroService))
            {
                return;
            }

            // TODO let the bot handle game setup if not remote.


        }

        private async Task<bool> ConnectAndLogin(Service kokkaKoroService)
        {
            // First, look for enviroment vars.
            // These will be passed by the server if it's running hosted.
            FindEnvVars();

            // Now based on the state, call the bot setup.
            OnSetupResponse setup = null;
            try { setup = await OnSetup(new OnSetupOptions() { IsHosted = IsHostedBot() });
            }catch(Exception e)
            { await FireOnUnhandledException("OnSetup", e); }

            // Now try to connect.
            int? localPort = null;
            if(!IsHostedBot() && setup != null && setup.LocalServerPort.HasValue)
            {
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
            string userName = IsHostedBot() ? m_hostedArgs.UserName : "";
            string passcode = IsHostedBot() ? m_hostedArgs.Passcode : "";
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
            string userName = Environment.GetEnvironmentVariable(c_userNameKey, EnvironmentVariableTarget.Process);
            string passcode = Environment.GetEnvironmentVariable(c_userPasscodeKey, EnvironmentVariableTarget.Process);
            string gameId = Environment.GetEnvironmentVariable(c_gameIdKey, EnvironmentVariableTarget.Process);
            if(!String.IsNullOrWhiteSpace(userName) && !String.IsNullOrWhiteSpace(passcode) && !String.IsNullOrWhiteSpace(gameId))
            {
                Guid output;
                if(Guid.TryParse(gameId, out output))
                {
                    m_hostedArgs = new HostedBotArgs()
                    {
                        GameId = output,
                        Passcode = passcode,
                        UserName = userName
                    };
                }
            }
        }
    }
}
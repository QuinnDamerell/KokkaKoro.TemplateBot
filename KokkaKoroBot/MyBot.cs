﻿using KokkaKoroBotHost;
using KokkaKoroBotHost.ActionOptions;
using KokkaKoroBotHost.ActionResponses;
using System;
using System.Threading.Tasks;

namespace KokkaKoroBot
{
    class MyBot : BotHost
    {
        // Called when the bot is connecting to the service.
        public override Task OnConnecting()
        {
            Logger.Info($"OnConnecting called.");

            // To avoid making the function async (because we don't need it, we will do this).
            // Remove this and make the function async if you need to await things.
            return Task.CompletedTask;
        }

        // Called when the bot has connected.
        public override async Task OnConnected()
        {
            Logger.Info($"OnConnecting called.");
        }

        public override async Task OnDisconnected(string reason, bool isClean, Exception e)
        {
            Logger.Info($"OnDisconnected called. Reason: {reason}, Exception: {(e == null ? "" : e.Message)}, isClean: {isClean}");
        }

        public override async Task OnUnhandledException(string callbackName, Exception e)
        {
            Logger.Info($"OnUnhandledException. The bot will be terminated. Callback Name: {callbackName}, Exception: {e.Message}");
        }

        public override async Task OnGameJoined()
        {
            //throw new NotImplementedException();
        }

        #region Advance Functions

        // Function call pattern
        //
        // 1) OnSetup
        // 2) OnConnecting
        // 3) OnConnected
        // 4) 

        public override async Task<OnSetupResponse> OnSetup(OnSetupOptions options)
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

            return new OnSetupResponse()
            {
                // Set this if you want to connect to a local server.
                // (only respected if the bot isn't running in hosted mode).
                // LocalServerPort = <port>
            };
        }



        #endregion
    }
}

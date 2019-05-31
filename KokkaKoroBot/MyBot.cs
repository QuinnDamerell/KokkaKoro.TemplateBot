using KokkaKoroBotHost;
using KokkaKoroBotHost.ActionOptions;
using KokkaKoroBotHost.ActionResponses;
using System;

namespace KokkaKoroBot
{
    class MyBot : BotHost
    {
        // Called when the bot is connecting to the service.
        public override void OnConnecting()
        {
            Logger.Info($"OnConnecting called.");
        }

        // Called when the bot has connected.
        public override void OnConnected()
        {
            Logger.Info($"OnConnecting called.");
        }

   

        public override void OnGameJoined()
        {
            throw new NotImplementedException();
        }

        #region Advance Functions

        // Function call patern
        //
        // 1) OnSetup
        // 2) OnConnecting
        // 3) OnConnected
        // 4) 

        public override OnSetupResponse OnSetup(OnSetupOptions options)
        {
            // OnSetup is called first when the BotHost is figuring out how to run.
            //
            // If `options.IsHosted` is set to true, this bot is running on the server and won't
            // take any configurations. This is because the game is already setup for it to run.
            //
            // If `options.IsHosted` is false, the bot is runing as a remote player. This can either
            // aginst the public service or a local server. To connect to a local server, the bot should 
            // set the `LocalServerPort` port in `OnSetupResponse`.
            Logger.Info($"OnSetup called; Is running hosted? {options.IsHosted}");

            return new OnSetupResponse()
            {
                // Set this if you want to connect to a local server.
                // (only respsected if the bot isn't running in hosted mode).
                // LocalServerPort = <port>
            };
        }

        #endregion
    }
}

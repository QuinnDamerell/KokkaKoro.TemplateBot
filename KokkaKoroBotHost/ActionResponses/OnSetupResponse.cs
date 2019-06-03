using System;
using System.Collections.Generic;
using System.Text;

namespace KokkaKoroBotHost.ActionResponses
{
    public class OnSetupResponse
    {
        // Only respected if the bot is running as a remote player! Hosted bots will overwrite this.
        // The user name to sign into the service with. If you don't have one, just make one up and the account will be
        // created for you.
        public string UserName;

        // Only respected if the bot is running as a remote player! Hosted bots will overwrite this.
        // Passcode that matches the username.
        public string Passcode;

        // Only respected if the bot is running as a remote player!
        // Optional - Add this port if you want to connect to a local server.
        // If not set, the bot will connect to the public service.
        public int? LocalServerPort;
    }
}

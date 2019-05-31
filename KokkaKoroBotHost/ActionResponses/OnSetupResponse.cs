using System;
using System.Collections.Generic;
using System.Text;

namespace KokkaKoroBotHost.ActionResponses
{
    public class OnSetupResponse
    {
        // Only respected if the bot is running as a remote player!
        // Optional - Add this port if you want to connect to a local server.
        // If not set, the bot will connect to the public service.
        public int? LocalServerPort;
    }
}

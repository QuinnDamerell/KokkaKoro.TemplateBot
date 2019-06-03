using System;
using System.Collections.Generic;
using System.Text;

namespace KokkaKoroBotHost.ActionResponses
{
    public enum GameConfigureType
    {
        CreateNewGame,
        JoinExistingGame
    }

    public class OnGameConfigureResponse
    {
        // Set by the bot, allows them to join an existing game or create a new game.
        public GameConfigureType ConfigType;

        // Common between both types, this allows the client to auto start the game when it's done configuring it.
        public bool ShouldAutoStartGame;

        // For join commands, this is the game password (if there is one). For create commands, this sets a game password.
        public string GamePassword;

        //
        // Join Game Config Options
        //

        // If the client want's to join an existing game, this should be the GameId they want to join.
        public Guid JoinGameId;

        //
        // Create Game Config Options
        //

        // A fun name for the game.
        public string GameName;

        // A list of bot names to be added to the new game. Adding the same name multiple times will
        // make that bot show up multiple times.
        public List<string> NewGameBotNames = new List<string>();

        //
        // Helpers
        //
        public static OnGameConfigureResponse CreateNewGame(string gameName, List<string> botNames, bool autoStart = true, string password = null)
        {
            return new OnGameConfigureResponse() { ConfigType = GameConfigureType.CreateNewGame, GameName = gameName, NewGameBotNames = botNames, ShouldAutoStartGame = autoStart, GamePassword = password };
        }

        public static OnGameConfigureResponse JoinGame(Guid gameId, bool autoStart = true, string password = null)
        {
            return new OnGameConfigureResponse() { ConfigType = GameConfigureType.JoinExistingGame, JoinGameId = gameId, ShouldAutoStartGame = autoStart, GamePassword = password };
        }
    }
}

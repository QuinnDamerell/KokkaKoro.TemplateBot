using GameCommon;
using GameCommon.Buildings;
using GameCommon.Protocol;
using GameCommon.StateHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KokkaKoroBot
{

    public static class ListExtensions
    {
        public static T Max<T, TCompare>(this IEnumerable<T> collection, Func<T, TCompare> func) where TCompare : IComparable<TCompare>
        {
            T maxItem = collection.First();
            TCompare maxValue = func(maxItem);
            foreach (var item in collection)
            {
                TCompare temp = func(item);
                if (maxItem == null || temp.CompareTo(maxValue) > 0)
                {
                    maxValue = temp;
                    maxItem = item;
                }
            }
            return maxItem;
        }
    }

    class LogicCore
    {
        static int s_maxBuyLookAhead = 3;
        GameState[] m_lookAheadStates;
        GameState m_currentRollState;
        (int, float) m_lastRoll;

        static Dictionary<int, List<(int, float)>> s_probabilities = new Dictionary<int, List<(int,float)>>
        {
            { 1, new List<(int,float)>{(1, 1 / 6f), (2, 1 / 6f), (3, 1 / 6f), (4, 1 / 6f), (5, 1 / 6f), (6, 1 / 6f)} },
            { 2, new List<(int,float)>{(2, 1 / 36f), (3, 2 / 36f), (4, 3 / 36f), (5, 4 / 36f), (6, 5 / 36f), (7, 6 / 36f), (8, 5 / 36f), (9, 4 / 36f), (10, 3 / 36f), (11, 2 / 36f), (12, 1 / 36f)} },
        };

        static List<float> s_turnPlayerValueSetAsides = new List<float>{0,0,0,0,0,0,0,0,0,0,0,0,0};
        static List<float> s_perspectivePlayerValueSetAsides = new List<float> { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };


        IBotInterface m_bot;
        RandomGenerator m_random = new RandomGenerator();

        public LogicCore(IBotInterface handler)
        {
            m_bot = handler;
        }

        public Task Setup()
        {
            // This is a good time to setup the bot and pre-load any async items that might take a while to load.
            // Once the game starts asking us for action requests, there's a turn time limit we must respect.

            // To avoid making the function async (because we don't need it) we will return this task.
            // Remove this and make the function async if you need to await things.
            return Task.CompletedTask;
        }

        public Task Cleanup()
        {
            // This will be called when the bot is shutting down regardless if the game is complete or not.
            // If you want to write anything out to disk, now is a good time.
            return Task.CompletedTask;
        }

        public Task OnGameUpdate(GameStateUpdate<object> update)
        {
            // OnGameUpdate fires when just about anything changes in the game. This might be coins added to a user because of a building,
            // cards being swapped, etc. Your bot doesn't need to pay attention to these updates if you don't wish, when your bot needs to make
            // an action OnGameActionRequested will be called with the current game state and a list of possible actions.
            return Task.CompletedTask;
        }

        public async Task OnGameActionRequested(GameActionRequest actionRequest, StateHelper stateHelper)
        {
            EnsureLookAheadStateInitialized(stateHelper);

            // Always roll if it's an option.
            if (actionRequest.PossibleActions.Contains(GameActionType.RollDice))
            {
                // If we can roll the dice.... LET'S DO IT!
                var diceInfo = NumberOfDicePlayerShouldRoll(stateHelper.Player.GetPlayer().PlayerIndex, stateHelper);
                int diceCount = diceInfo.Item1;
                m_lastRoll = diceInfo;

                Logger.Log(Log.Info, $"Requesting a dice roll for {diceCount} dice! BIG MONEY NO WHAMMIES...");
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateRollDiceAction(diceCount, false));
                if (!result.Accepted)
                {
                    // If random bot fails, it instantly shuts down.
                    await Shutdown("failed to roll dice.", result.Error);
                }
                else
                {
                    Logger.Log(Log.Info, "Trust the dice gods, we roll the dice and commit!");
                }
                return;
            }

            if (actionRequest.PossibleActions.Contains(GameActionType.CommitDiceResult))
            {
                int diceSum = 0;
                foreach (int r in stateHelper.GetState().CurrentTurnState.DiceResults)
                {
                    diceSum += r;
                }

                if ((stateHelper.CurrentTurn.CanRollOrReRoll()) &&
                    (ValueOfRollForPlayer(diceSum, stateHelper.Player.GetPlayer().PlayerIndex, stateHelper.Player.GetPlayer().PlayerIndex, stateHelper) < m_lastRoll.Item2))
                {
                    Logger.Log(Log.Info, "The Gods hath been unkind. Reroll incoming.");

                    GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateRollDiceAction(m_lastRoll.Item1, false));
                    if (!result.Accepted)
                    {
                        await Shutdown("failed to commit dice.", result.Error);
                    }
                    else
                    {
                        Logger.Log(Log.Info, "Done");
                    }
                }
                else
                {

                    GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateCommitDiceResultAction());
                    if (!result.Accepted)
                    {
                        await Shutdown("failed to commit dice.", result.Error);
                    }
                    else
                    {
                        Logger.Log(Log.Info, "Commited Dice Roll!");
                    }
                }
                return;
            }

            if (actionRequest.PossibleActions.Contains(GameActionType.BuildBuilding))
            {
                Logger.Log(Log.Info, $"****** [BRIBOT] deciding what to build with {stateHelper.Player.GetPlayer().Coins} coins!");
                // WE ARE A BIG ROLLER... let's build.
                int buildingIndex = FindBestBuildingToBuild(stateHelper, s_maxBuyLookAhead).Item1;

                if (buildingIndex != -1)
                {
                    // IF WE BUILD IT...
                    Logger.Log(Log.Info, $"Requesting to build {stateHelper.BuildingRules[buildingIndex].GetName()}...");
                    GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateBuildBuildingAction(buildingIndex));
                    if (!result.Accepted)
                    {
                        // If bot fails, it instantly shuts down.
                        await Shutdown("failed to build building.", result.Error);
                    }
                    else
                    {
                        // ... THEY WILL COME!
                        Logger.Log(Log.Info, $"****** [BRIBOT] just bought {stateHelper.BuildingRules[buildingIndex].GetName()}!");
                    }
                    return;
                }
                else
                {
                    // Don't build anything.
                    Logger.Log(Log.Info, $"Choosing not to build anything this turn...");
                    GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateEndTurnAction());
                    if (!result.Accepted)
                    {
                        // If bot fails, it instantly shuts down.
                        await Shutdown("failed to build building.", result.Error);
                    }
                    else
                    {
                        Logger.Log(Log.Info, $"****** [BRIBOT] just chose not to build anything.");
                    }
                    return;
                }
            }

            if (actionRequest.PossibleActions.Contains(GameActionType.EndTurn))
            {
                // If we can't roll the dice or build a building, we must not have enough funds.
                // Just end the turn.

                // End it!
                Logger.Log(Log.Info, "There's nothing to do, requesting turn end...");
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateEndTurnAction());
                if (!result.Accepted)
                {
                    // If random bot fails, it instantly shuts down.
                    await Shutdown("failed to end our turn.", result.Error);
                }
                else
                {
                    Logger.Log(Log.Info, $"We have {stateHelper.Player.GetPlayer().Coins} coins and can't buy anything, so we ended the turn.");
                }
                return;
            }

            if(actionRequest.PossibleActions.Contains(GameActionType.TvStationPayout))
            {
                // Our Tv Station activated! Let's take 5 coins from the player with the most coins.
                // As they say, GREED IS GOOD!
                List<GamePlayer> players = new List<GamePlayer>(stateHelper.GetState().Players);
                players.Remove(stateHelper.GetState().Players[stateHelper.Player.GetPlayer().PlayerIndex]);
                GamePlayer playerToStealFrom = players.Max(t => t.Coins);

                // DO IT!!!
                Logger.Log(Log.Info, $"Our Tv Station was activated, let's take coins from player {playerToStealFrom.Name}!");
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateTvStationPayoutAction(playerToStealFrom.PlayerIndex));
                if (!result.Accepted)
                {
                    // If random bot fails, it instantly shuts down.
                    await Shutdown("failed to respond to tv station payout.", result.Error);
                }
                else
                {
                    Logger.Log(Log.Info, $"CHA-CHING BA-BY. We have taken coins from player {playerToStealFrom.Name} for our tv station!");
                }
                return;
            }

            if(actionRequest.PossibleActions.Contains(GameActionType.BusinessCenterSwap))
            {
                // Our Business Center activated! Let's randomly pick a player and building to swap.
                GamePlayer randomPlayer = GetRandomPlayer(stateHelper);
                BuildingBase ourBuilding = GetRandomOwnedNonMajorBuidling(stateHelper, null);
                BuildingBase theirBuilding = GetRandomOwnedNonMajorBuidling(stateHelper, randomPlayer.PlayerIndex);

                GameActionResponse result;
                if (randomPlayer == null || ourBuilding == null || theirBuilding == null)
                {
                    // If there aren't any building we can use, skip the action.
                    Logger.Log(Log.Info, $"Our Business Center was activated, but there weren't the correct building to swap. So we will skip!");
                    result = await m_bot.SendAction(GameAction<object>.CreateBusinessCenterSwapAction(0, 0, 0, true));
                }
                else
                {
                    Logger.Log(Log.Info, $"Our Business Center was activated, swap our {ourBuilding.GetName()} for {randomPlayer.Name}'s {theirBuilding.GetName()}!");
                    result = await m_bot.SendAction(GameAction<object>.CreateBusinessCenterSwapAction(randomPlayer.PlayerIndex, ourBuilding.GetBuildingIndex(), theirBuilding.GetBuildingIndex()));
                }
              
                if (!result.Accepted)
                {
                    // If random bot fails, it instantly shuts down.
                    await Shutdown("failed to respond to business center swap.", result.Error);
                }
                else
                {
                    Logger.Log(Log.Info, $"Business center swap done!");
                }
                return;
            }

            Logger.Log(Log.Error, $"Hmm, we were asked for an action but didn't know what to do with...");
            foreach(GameActionType type in actionRequest.PossibleActions)
            {
                Logger.Log(Log.Error, $"  ... {type.ToString()}");
                await Shutdown("received an unknown action.", null);
            }
        }

        private void EnsureLookAheadStateInitialized(StateHelper stateHelper)
        {
            if (m_lookAheadStates?.Length == s_maxBuyLookAhead + 1)
            {
                return;
            }

            m_lookAheadStates = new GameState[s_maxBuyLookAhead + 1];
            for (int i = 0; i < m_lookAheadStates.Length; i++)
            {
                m_lookAheadStates[i] = CreateDefaultGameState(stateHelper.GetState());
            }

            m_currentRollState = CreateDefaultGameState(stateHelper.GetState());
        }

        private GameState CreateDefaultGameState(GameState gameState)
        {
            GameState copiedState = new GameState();
            copiedState.Mode = gameState.Mode;
            BuildingRules buildingRules = new BuildingRules(gameState.Mode);
            foreach (var player in gameState.Players)
            {
                GamePlayer copiedPlayer = new GamePlayer();
                copiedPlayer.PlayerIndex = player.PlayerIndex;
                copiedPlayer.UserName = player.UserName;

                for (int i = 0; i < buildingRules.GetCountOfUniqueTypes(); i++)
                {
                    copiedPlayer.OwnedBuildings.Add(0);
                }

                copiedState.Players.Add(copiedPlayer);
            }

            copiedState.Market = Marketplace.Create(buildingRules);
            return copiedState;
        }

        private (int, float) FindBestBuildingToBuild(StateHelper originalStateHelper, int turnLookahead)
        {
            // Logger.Log(Log.Info, $"!!! - FindBestBuildingToBuild TurnLookAhead: {turnLookahead}");
            // Okay so the way to find the best building to build is the one that gives the biggest increase in round expected value.
            // The round expected value is calculated by finding the max single roll expected value for us (very similar to getting the number of dice to roll)
            // As well as getting the expected value for our own coins on other players' turns. This does NOT consider other player's buying decisions.
            List <(int, float)> buildingExpectedValues = new List<(int, float)>();

            // Get all building that are in the marketplace currently.
            List<int> buildable = originalStateHelper.Marketplace.GetBuildingTypesBuildableInMarketplace();

            // Filter it down to only buildings we can afford.
            List<int> affordable = originalStateHelper.Player.FilterBuildingIndexesWeCanAfford(buildable);

            foreach (var building in affordable.OrderBy(t => originalStateHelper.BuildingRules.Get(t).GetEstablishmentColor() == EstablishmentColor.Landmark ? 0 : 1).Append(-1))
            {
                float expectedValue = 0;

                // First thing first, lets create a copy of the StateHelper to not muck with real things.
                var stateCopy = DeepCopyGameState(originalStateHelper.GetState(), m_lookAheadStates[turnLookahead]);

                if (building != -1)
                {
                    // Only try to buy the building if we can...
                    if (stateCopy.Players[originalStateHelper.Player.GetPlayer().PlayerIndex].OwnedBuildings[building] >= originalStateHelper.Marketplace.GetMaxBuildingsPerPlayer(building))
                    {
                        continue;
                    }

                    // Pretend to add the card in question to the owned buildings.
                    stateCopy.Players[originalStateHelper.Player.GetPlayer().PlayerIndex].OwnedBuildings[building]++;
                    stateCopy.Market.AvailableBuildable[building]--;
                    stateCopy.Players[originalStateHelper.Player.GetPlayer().PlayerIndex].Coins -= originalStateHelper.BuildingRules.Get(building).GetBuildCost();
                }

                StateHelper stateHelper = stateCopy.GetStateHelper(originalStateHelper.Player.GetPlayerUserName(originalStateHelper.Player.GetPlayer().PlayerIndex));

                // easy mode check first, if this card makes us win. That is the most valueable. Also losing is the least valueable.
                var winner = stateHelper.Player.CheckForWinner();
                if (winner != null)
                {
                    if (winner.PlayerIndex == originalStateHelper.Player.GetPlayer().PlayerIndex)
                    {
                        return (building, Single.PositiveInfinity);
                    }
                    else
                    {
                        return (building, Single.NegativeInfinity);
                    }
                }


                // Lets figure out the round expected value by simulating each player doing their optimal number of dice and seeing the value from our perspective.
                var valuePerspectiveIndex = stateHelper.Player.GetPlayer().PlayerIndex;
                float roundValue = 0;
                foreach (var player in stateHelper.GetState().Players)
                {
                    var maxExpected = MaxRollExpectedValueForPlayer(player.PlayerIndex, valuePerspectiveIndex, stateHelper);
                    roundValue += maxExpected.Item2;

                    // TODO: insert something for other players buying strategy here. We could mini compute theirs, random or something. 
                }

                expectedValue += roundValue;

                if (turnLookahead > 0)
                {
                    // Pretend a round went buy and its time for another buying decision.
                    stateCopy.Players[originalStateHelper.Player.GetPlayer().PlayerIndex].Coins += (int)(Math.Floor(roundValue));
                    var lookAheadBest = FindBestBuildingToBuild(stateHelper, turnLookahead - 1);

                    expectedValue += lookAheadBest.Item2;
                }

                if (!Single.IsPositiveInfinity(expectedValue))
                {
                    buildingExpectedValues.Add((building, expectedValue));
                }
                else
                {
                    return (building, expectedValue);
                }

                if (building != -1)
                {
                    // Logger.Log(Log.Info, $"**** Building: {stateHelper.BuildingRules.Get(building).GetName()} Expected Value: {maxExpected.Item2}");
                }
                else
                {
                    // Logger.Log(Log.Info, $"**** Building: DON'T BUY Expected Value: {maxExpected.Item2}");
                }
            }

            var bestBuilding = buildingExpectedValues.Max(t => t.Item2);
            if (bestBuilding.Item1 != -1)
            {
                // Logger.Log(Log.Info, $"!!! Best Building: {originalStateHelper.BuildingRules.Get(bestBuilding.Item1).GetName()} Expected Value: {bestBuilding.Item2}");
            }
            else
            {
                // Logger.Log(Log.Info, $"!!! Best Building: DON'T BUY Expected Value: {bestBuilding.Item2}");
            }
            
            return bestBuilding;
        }

        private GameState DeepCopyGameState(GameState gameState)
        {
            return DeepCopyGameState(gameState, CreateDefaultGameState(gameState));
        }

        private GameState DeepCopyGameState(GameState gameState, GameState copiedState)
        {
            copiedState.CurrentTurnState.PlayerIndex = gameState.CurrentTurnState.PlayerIndex;

            for (int i = 0; i < gameState.Players.Count; i++)
            {
                GamePlayer player = gameState.Players[i];
                GamePlayer copiedPlayer = copiedState.Players[i];
                copiedPlayer.Coins = player.Coins;

                for (int j = 0; j < player.OwnedBuildings.Count; j++)
                {
                    copiedPlayer.OwnedBuildings[j] = player.OwnedBuildings[j];
                }
            }

            for (int i = 0; i < gameState.Market.AvailableBuildable.Count; i++)
            {
                copiedState.Market.AvailableBuildable[i] = gameState.Market.AvailableBuildable[i];
            }

            return copiedState;
        }

        private (int,float) MaxRollExpectedValueForPlayer(int turnPlayerIndex, int perspectivePlayerIndex, StateHelper stateHelper)
        {
            List<(int, (float, float))> expectedValues = new List<(int, (float, float))>();
            for (int numDice = 1; numDice <= stateHelper.Player.GetMaxCountOfDiceCanRoll(turnPlayerIndex); numDice++)
            {
                float turnPlayerExpectedValue = 0;
                float perspectivePlayerExpectedValue = 0;
                foreach (var entry in s_probabilities[numDice])
                {
                    var value = ValueOfRollForPlayer(entry.Item1, turnPlayerIndex, turnPlayerIndex, stateHelper);
                    s_turnPlayerValueSetAsides[entry.Item1] = value;
                    turnPlayerExpectedValue += entry.Item2 * value;

                    var perspectiveValue = (turnPlayerIndex != perspectivePlayerIndex) ? ValueOfRollForPlayer(entry.Item1, turnPlayerIndex, perspectivePlayerIndex, stateHelper) : turnPlayerExpectedValue;
                    perspectivePlayerExpectedValue += entry.Item2 * perspectiveValue;
                    s_perspectivePlayerValueSetAsides[entry.Item1] = value;
                    // Logger.Log(Log.Info, $"Player: {playerIndex} Entry: {entry} Value: {value}");
                }

                float finalTurnPlayerExpectedValue = 0;
                float finalPerspectivePlayerExpectedValue = 0;

                // Check re-roll. Only handles a single re-roll though.
                if (stateHelper.Player.GetMaxRollsAllowed(turnPlayerIndex) > 1)
                {
                    float rerollTurnPlayerExpectedValue = 0;
                    float rerollPerspectivePlayerExpectedValue = 0;
                    foreach (var entry in s_probabilities[numDice])
                    {
                        // Assume re roll is taken whenever a roll value is less than the expected value (unlucky).
                        // The new value of that re roll is the single roll expected value because its just a straight re roll.
                        var value = s_turnPlayerValueSetAsides[entry.Item1];
                        rerollTurnPlayerExpectedValue += entry.Item2 * (value < turnPlayerExpectedValue ? turnPlayerExpectedValue : value);

                        rerollPerspectivePlayerExpectedValue += entry.Item2 * (value < turnPlayerExpectedValue ? perspectivePlayerExpectedValue : s_perspectivePlayerValueSetAsides[entry.Item1]);

                        // Logger.Log(Log.Info, $"Player: {playerIndex} Entry: {entry} Value: {value}");
                    }

                    finalTurnPlayerExpectedValue = rerollTurnPlayerExpectedValue;
                    finalPerspectivePlayerExpectedValue = rerollPerspectivePlayerExpectedValue;
                }
                else
                {
                    finalTurnPlayerExpectedValue = turnPlayerExpectedValue;
                    finalPerspectivePlayerExpectedValue = perspectivePlayerExpectedValue;
                }

                // Check extra turns. (Only considers making the same value again and again, not that the extra turn also allows buying more things.
                if (stateHelper.Player.CanHaveExtraTurn(turnPlayerIndex) )
                {
                    finalTurnPlayerExpectedValue *= 1.2f; // 1.2 might seem random but its actually [sum 1/6^n, n=0 to infinity].
                    finalPerspectivePlayerExpectedValue *= 1.2f;
                }

                expectedValues.Add((numDice, (finalTurnPlayerExpectedValue, finalPerspectivePlayerExpectedValue)));

                // Logger.Log(Log.Info, $"Player: {playerIndex} NumDice: {numDice} Final Expected Value: {expectedValue}");
            }

            var maxValue = expectedValues.Max(t => t.Item2.Item1);
        
            if (perspectivePlayerIndex == turnPlayerIndex)
            {
                return (maxValue.Item1, maxValue.Item2.Item1);
            }
            else
            {
                return (maxValue.Item1, maxValue.Item2.Item2);
            }
        }

        private (int, float) NumberOfDicePlayerShouldRoll(int playerIndex, StateHelper stateHelper)
        {
            //In order to determine how many dice a player "should" roll, lets figure out the expected value
            // of each number of dice and choose the best (obvi).
            return MaxRollExpectedValueForPlayer(playerIndex, playerIndex, stateHelper);
        }

        private float ValueOfRollForPlayer(int diceValue, int turnPlayerIndex, int valuePlayerIndex, StateHelper originalStateHelper)
        {
            // First thing first, lets create a copy of the StateHelper to not muck with real things.
            var stateCopy = DeepCopyGameState(originalStateHelper.GetState(), m_currentRollState);

            // Change the current turn to pretend the player in question is going.
            // Pretend the desired dice value was rolled.
            stateCopy.CurrentTurnState.PlayerIndex = turnPlayerIndex;
            stateCopy.CurrentTurnState.DiceResults = new List<int> { diceValue };

            StateHelper stateHelper = stateCopy.GetStateHelper(originalStateHelper.Player.GetPlayerUserName(turnPlayerIndex));

            // Okay now we go through and activate all the cards in much the same way as the game engine itself would to get a new game state. The difference is that
            // cards that require actions are skipped (mostly).

            // Code livingly ripped from GameEngine EarnIncome Phase.

            // 
            // REDS
            //
            // First, starting with the active player, in reverse order we need to settle red cards.
            // Each player in reverse order should get the full amount from the player for all their red cards before moving on.
            // Red cards don't activate on the current player.

            // Start with the current player - 1;
            int redPlayerIndex = turnPlayerIndex - 1;
            while (true)
            {
                // If we roll under, go back to the highest player.
                if (redPlayerIndex < 0)
                {
                    redPlayerIndex = stateHelper.GetState().Players.Count - 1;
                }

                // When we get back to the current player break. We don't need to execute reds on the current player.
                if (redPlayerIndex == turnPlayerIndex)
                {
                    break;
                }

                // Execute all red buildings for this player.
                ExecuteBuildingColorIncomeForPlayer(stateHelper, redPlayerIndex, EstablishmentColor.Red);

                // Move to the next.
                redPlayerIndex--;
            }

            //
            // BLUES
            //
            // Next, settle any blue cards from any players.
            // Player order doesn't matter.
            for (int bluePlayerIndex = 0; bluePlayerIndex < stateHelper.GetState().Players.Count; bluePlayerIndex++)
            {
                ExecuteBuildingColorIncomeForPlayer(stateHelper, bluePlayerIndex, EstablishmentColor.Blue);
            }

            //
            // GREENS
            //
            // Green cards only execute on the active player's turn
            ExecuteBuildingColorIncomeForPlayer(stateHelper, turnPlayerIndex, EstablishmentColor.Green);

            //
            // PURPLE
            //
            // Purple cards only execute on the active player's turn.
            // Purple cards may result in actions we need to ask the player about.
            ExecuteBuildingColorIncomeForPlayer(stateHelper, turnPlayerIndex, EstablishmentColor.Purple);

            // At this point, all the buildings activated for all the players. The way to then calculate value is to determine how many coins the player
            // earned and how many coins other players lost (effectively a coin gained to the player although this could be tweaked for AOE vs Single Target DPS).
            int value = 0;
            for (int incomePlayerIndex = 0; incomePlayerIndex < stateHelper.GetState().Players.Count; incomePlayerIndex++)
            {
                if (incomePlayerIndex == valuePlayerIndex)
                {
                    value += (stateHelper.Player.GetPlayer(incomePlayerIndex).Coins - originalStateHelper.Player.GetPlayer(incomePlayerIndex).Coins);
                }
                else
                {
                    value += (originalStateHelper.Player.GetPlayer(incomePlayerIndex).Coins - stateHelper.Player.GetPlayer(incomePlayerIndex).Coins);
                }
            }

            return value;
        }

        private void ExecuteBuildingColorIncomeForPlayer(StateHelper stateHelper, int playerIndex, EstablishmentColor color)
        {
            List<GameLog> log = new List<GameLog>();

            // Get the sum of the roll.
            int diceSum = 0;
            foreach (int r in stateHelper.GetState().CurrentTurnState.DiceResults)
            {
                diceSum += r;
            }

            // Figure out if this player is the current player.
            bool isActivePlayer = playerIndex == stateHelper.GetState().CurrentTurnState.PlayerIndex;

            // Look for any of their buildings that activate.
            for (int buildingIndex = 0; buildingIndex < stateHelper.BuildingRules.GetCountOfUniqueTypes(); buildingIndex++)
            {
                // Check if the building activates.
                BuildingBase building = stateHelper.BuildingRules[buildingIndex];
                if (building.GetEstablishmentColor() == color && building.IsDiceInRange(diceSum))
                {
                    // Active the card if this is the active player or if the card activates on other player's turns.
                    if ((isActivePlayer || building.ActivatesOnOtherPlayersTurns()))
                    {
                        // Execute for every building the player has.
                        int built = stateHelper.Player.GetBuiltCount(buildingIndex, playerIndex);
                        for (int i = 0; i < built; i++)
                        {
                            // This building should activate.
                            var activation = building.GetActivation();
                            activation.Activate(log, stateHelper.GetState(), stateHelper, buildingIndex, playerIndex);

                            if (activation.GetAction() == GameActionType.TvStationPayout)
                            {
                                List<GamePlayer> players = new List<GamePlayer>(stateHelper.GetState().Players);
                                players.Remove(stateHelper.GetState().Players[playerIndex]);
                                GamePlayer playerToStealFrom = players.Max(t => t.Coins);

                                int coinsToSteal = stateHelper.Player.GetMaxTakeableCoins(5, playerToStealFrom.PlayerIndex);
                                playerToStealFrom.Coins -= coinsToSteal;
                                stateHelper.Player.GetPlayer(playerIndex).Coins += coinsToSteal;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns a random player that's not us.
        /// </summary>
        /// <param name="stateHelper"></param>
        /// <returns></returns>
        private GamePlayer GetRandomPlayer(StateHelper stateHelper)
        {
            if(stateHelper.Player.GetPlayerCount() < 2)
            {
                return null;
            }
            // Start with our player index
            int playerIndex = stateHelper.Player.GetPlayer().PlayerIndex;

            // Pick a random number until we don't have our index.
            while (playerIndex == stateHelper.Player.GetPlayer().PlayerIndex)
            {
                playerIndex = m_random.RandomInt(0, stateHelper.Player.GetPlayerCount() - 1);
            }
            return stateHelper.GetState().Players[playerIndex];
        }

        /// <summary>
        /// Returns a random non major building, if any exists.
        /// </summary>
        /// <param name="stateHelper"></param>
        /// <param name="playerIndex"></param>
        /// <returns></returns>
        private BuildingBase GetRandomOwnedNonMajorBuidling(StateHelper stateHelper, int? playerIndex = null)
        {
            GamePlayer p = stateHelper.Player.GetPlayer(playerIndex);

            // Build a list of building the player owns and matches our filter.
            List<int> buildingIndex = new List<int>();
            for(int b = 0; b < stateHelper.BuildingRules.GetCountOfUniqueTypes(); b++)
            {
                BuildingBase building = stateHelper.BuildingRules[b];
                if(p.OwnedBuildings[b] > 0)
                {
                    if (building.GetEstablishmentColor() != EstablishmentColor.Landmark && building.GetEstablishmentColor() != EstablishmentColor.Purple)
                    {
                        buildingIndex.Add(b);
                    }
                }
            }

            // Make sure there are buildings.
            if(buildingIndex.Count == 0)
            {
                return null;
            }

            // Now get a random int index into the build index array, and get the building.
            return stateHelper.BuildingRules[buildingIndex[m_random.RandomInt(0, buildingIndex.Count - 1)]];
        }

        /// <summary>
        /// Helps us shutdown.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        private async Task Shutdown(string message, GameError e)
        {
            Logger.Log(Log.Error, $"That's not good...");
            Logger.Log(Log.Error, $"   ...we failed to {message}");
            Logger.Log(Log.Error, $"   ...because {(e != null ? e.Message : "")}");
            Logger.Log(Log.Error, $"   ...time to give up and shutdown!");

            // Send the forfeit command to the game host since we don't know what to do.
            await m_bot.SendAction(GameAction<object>.CreateForfeitAction());

            // Disconnect the bot.
            await m_bot.Disconnect();
        }        
    }
}

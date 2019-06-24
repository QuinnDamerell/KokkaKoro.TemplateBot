using GameCommon;
using GameCommon.Buildings;
using GameCommon.Protocol;
using GameCommon.Protocol.ActionOptions;
using GameCommon.StateHelpers;
using KokkaKoroBotHost;
using KokkaKoroBotHost.ActionOptions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using System.ArrayExtensions;

// Lovingly ripped from https://stackoverflow.com/questions/129389/how-do-you-do-a-deep-copy-of-an-object-in-net-c-specifically/11308879#11308879
namespace System
{
    public static class ObjectExtensions
    {
        private static readonly MethodInfo CloneMethod = typeof(Object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

        public static bool IsPrimitive(this Type type)
        {
            if (type == typeof(String)) return true;
            return (type.IsValueType & type.IsPrimitive);
        }

        public static Object Copy(this Object originalObject)
        {
            return InternalCopy(originalObject, new Dictionary<Object, Object>(new ReferenceEqualityComparer()));
        }
        private static Object InternalCopy(Object originalObject, IDictionary<Object, Object> visited)
        {
            if (originalObject == null) return null;
            var typeToReflect = originalObject.GetType();
            if (IsPrimitive(typeToReflect)) return originalObject;
            if (visited.ContainsKey(originalObject)) return visited[originalObject];
            if (typeof(Delegate).IsAssignableFrom(typeToReflect)) return null;
            var cloneObject = CloneMethod.Invoke(originalObject, null);
            if (typeToReflect.IsArray)
            {
                var arrayType = typeToReflect.GetElementType();
                if (IsPrimitive(arrayType) == false)
                {
                    Array clonedArray = (Array)cloneObject;
                    clonedArray.ForEach((array, indices) => array.SetValue(InternalCopy(clonedArray.GetValue(indices), visited), indices));
                }

            }
            visited.Add(originalObject, cloneObject);
            CopyFields(originalObject, visited, cloneObject, typeToReflect);
            RecursiveCopyBaseTypePrivateFields(originalObject, visited, cloneObject, typeToReflect);
            return cloneObject;
        }

        private static void RecursiveCopyBaseTypePrivateFields(object originalObject, IDictionary<object, object> visited, object cloneObject, Type typeToReflect)
        {
            if (typeToReflect.BaseType != null)
            {
                RecursiveCopyBaseTypePrivateFields(originalObject, visited, cloneObject, typeToReflect.BaseType);
                CopyFields(originalObject, visited, cloneObject, typeToReflect.BaseType, BindingFlags.Instance | BindingFlags.NonPublic, info => info.IsPrivate);
            }
        }

        private static void CopyFields(object originalObject, IDictionary<object, object> visited, object cloneObject, Type typeToReflect, BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy, Func<FieldInfo, bool> filter = null)
        {
            foreach (FieldInfo fieldInfo in typeToReflect.GetFields(bindingFlags))
            {
                if (filter != null && filter(fieldInfo) == false) continue;
                if (IsPrimitive(fieldInfo.FieldType)) continue;
                var originalFieldValue = fieldInfo.GetValue(originalObject);
                var clonedFieldValue = InternalCopy(originalFieldValue, visited);
                fieldInfo.SetValue(cloneObject, clonedFieldValue);
            }
        }
        public static T Copy<T>(this T original)
        {
            return (T)Copy((Object)original);
        }
    }

    public class ReferenceEqualityComparer : EqualityComparer<Object>
    {
        public override bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }
        public override int GetHashCode(object obj)
        {
            if (obj == null) return 0;
            return obj.GetHashCode();
        }
    }

    namespace ArrayExtensions
    {
        public static class ArrayExtensions
        {
            public static void ForEach(this Array array, Action<Array, int[]> action)
            {
                if (array.LongLength == 0) return;
                ArrayTraverse walker = new ArrayTraverse(array);
                do action(array, walker.Position);
                while (walker.Step());
            }
        }

        internal class ArrayTraverse
        {
            public int[] Position;
            private int[] maxLengths;

            public ArrayTraverse(Array array)
            {
                maxLengths = new int[array.Rank];
                for (int i = 0; i < array.Rank; ++i)
                {
                    maxLengths[i] = array.GetLength(i) - 1;
                }
                Position = new int[array.Rank];
            }

            public bool Step()
            {
                for (int i = 0; i < Position.Length; ++i)
                {
                    if (Position[i] < maxLengths[i])
                    {
                        Position[i]++;
                        for (int j = 0; j < i; j++)
                        {
                            Position[j] = 0;
                        }
                        return true;
                    }
                }
                return false;
            }
        }
    }

}


namespace KokkaKoroBot
{

    class LogicCore
    {
        static int s_maxBuyLookAhead = 3;

        // TODO: figure out re-rolls
        static Dictionary<int, List<(int, float)>> s_probabilities = new Dictionary<int, List<(int,float)>>
        {
            { 1, new List<(int,float)>{(1, 1 / 6f), (2, 1 / 6f), (3, 1 / 6f), (4, 1 / 6f), (5, 1 / 6f), (6, 1 / 6f)} },
            { 2, new List<(int,float)>{(2, 1 / 36f), (3, 2 / 36f), (4, 3 / 36f), (5, 4 / 36f), (6, 5 / 36f), (7, 6 / 36f), (8, 5 / 36f), (9, 4 / 36f), (10, 3 / 36f), (11, 2 / 36f), (12, 1 / 36f)} },
        };

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
            // Always roll if it's an option.
            if (actionRequest.PossibleActions.Contains(GameActionType.RollDice))
            {
                // If we can roll the dice.... LET'S DO IT!

                int diceCount = NumberOfDicePlayerShouldRoll(stateHelper.Player.GetPlayer().PlayerIndex, stateHelper);

                // Check if we have another roll.
                bool canReRoll = stateHelper.GetState().CurrentTurnState.Rolls < stateHelper.Player.GetMaxRollsAllowed();

                Logger.Log(Log.Info, $"Requesting a dice roll for {diceCount} dice! BIG MONEY NO WHAMMIES...");
                GameActionResponse result = await m_bot.SendAction(GameAction<object>.CreateRollDiceAction(diceCount, true));
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
                GamePlayer playerToStealFrom = players.OrderByDescending(t => t.Coins).First();

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
                var stateCopy = DeepCopyGameState(originalStateHelper.GetState());

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

                // easy mode check first, if this card makes us win. That is the most valueable.
                if (stateHelper.Player.CheckForWinner()?.PlayerIndex == originalStateHelper.Player.GetPlayer().PlayerIndex)
                {
                    return (building, Single.PositiveInfinity);
                }


                // Lets do this lazy mode to start and not consider other player's turns at all yet.
                var maxExpected = MaxRollExpectedValueForPlayer(stateHelper.Player.GetPlayer().PlayerIndex, stateHelper);
                expectedValue += maxExpected.Item2;

                if (turnLookahead > 0)
                {
                    // Pretend a round went buy and its time for another buying decision.
                    stateCopy.Players[originalStateHelper.Player.GetPlayer().PlayerIndex].Coins += (int)(Math.Floor(maxExpected.Item2));
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

            var bestBuilding = buildingExpectedValues.OrderByDescending(t => t.Item2).First();
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
            return gameState.Copy();
        }

        private (int,float) MaxRollExpectedValueForPlayer(int playerIndex, StateHelper stateHelper)
        {
            List<(int, float)> expectedValues = new List<(int, float)>();
            for (int numDice = 1; numDice <= stateHelper.Player.GetMaxCountOfDiceCanRoll(playerIndex); numDice++)
            {
                float expectedValue = 0;
                foreach (var entry in s_probabilities[numDice])
                {
                    var value = ValueOfRollForPlayer(entry.Item1, playerIndex, stateHelper);
                    expectedValue += entry.Item2 * value;

                    // Logger.Log(Log.Info, $"Player: {playerIndex} Entry: {entry} Value: {value}");
                }
                expectedValues.Add((numDice, expectedValue));
                // Logger.Log(Log.Info, $"Player: {playerIndex} NumDice: {numDice} Final Expected Value: {expectedValue}");
            }

            return expectedValues.OrderByDescending(t => t.Item2).First();
        }

        private int NumberOfDicePlayerShouldRoll(int playerIndex, StateHelper stateHelper)
        {
            if (stateHelper.Player.GetMaxCountOfDiceCanRoll(playerIndex) <= 1)
            {
                // Well that was surprisingly easy. Can't do anything else so no decision.
                return 1;
            }
            else
            {
                // Well shoot. This is a bit more complicated. In order to determine how many dice a player "should" roll, lets figure out the expected value
                // of each number of dice and choose the best (obvi).
                return MaxRollExpectedValueForPlayer(playerIndex, stateHelper).Item1;
            }
        }

        private float ValueOfRollForPlayer(int diceValue, int playerIndex, StateHelper originalStateHelper)
        {
            // First thing first, lets create a copy of the StateHelper to not muck with real things.
            var stateCopy = DeepCopyGameState(originalStateHelper.GetState());

            // Change the current turn to pretend the player in question is going.
            // Pretend the desired dice value was rolled.
            stateCopy.CurrentTurnState.PlayerIndex = playerIndex;
            stateCopy.CurrentTurnState.DiceResults = new List<int> { diceValue };

            StateHelper stateHelper = stateCopy.GetStateHelper(originalStateHelper.Player.GetPlayerUserName(playerIndex));

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
            int redPlayerIndex = playerIndex - 1;
            while (true)
            {
                // If we roll under, go back to the highest player.
                if (redPlayerIndex < 0)
                {
                    redPlayerIndex = stateHelper.GetState().Players.Count - 1;
                }

                // When we get back to the current player break. We don't need to execute reds on the current player.
                if (redPlayerIndex == playerIndex)
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
            ExecuteBuildingColorIncomeForPlayer(stateHelper, playerIndex, EstablishmentColor.Green);

            //
            // PURPLE
            //
            // Purple cards only execute on the active player's turn.
            // Purple cards may result in actions we need to ask the player about.
            ExecuteBuildingColorIncomeForPlayer(stateHelper, playerIndex, EstablishmentColor.Purple);

            // At this point, all the buildings activated for all the players. The way to then calculate value is to determine how many coins the player
            // earned and how many coins other players lost (effectively a coin gained to the player although this could be tweaked for AOE vs Single Target DPS).

            int value = 0;
            for (int incomePlayerIndex = 0; incomePlayerIndex < stateHelper.GetState().Players.Count; incomePlayerIndex++)
            {
                if (incomePlayerIndex == playerIndex)
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

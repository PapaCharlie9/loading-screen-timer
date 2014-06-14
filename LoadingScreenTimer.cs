/* LoadingScreenTimer.cs

Copyright 2014, by PapaCharlie9

Permission is hereby granted, free of charge, to any person or organization
obtaining a copy of the software and accompanying documentation covered by
this license (the "Software") to use, reproduce, display, distribute,
execute, and transmit the Software, and to prepare derivative works of the
Software, and to permit third-parties to whom the Software is furnished to
do so, without restriction.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE, TITLE AND NON-INFRINGEMENT. IN NO EVENT
SHALL THE COPYRIGHT HOLDERS OR ANYONE DISTRIBUTING THE SOFTWARE BE LIABLE
FOR ANY DAMAGES OR OTHER LIABILITY, WHETHER IN CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.

*/

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections;
using System.Net;
using System.Web;
using System.Data;
using System.Threading;
using System.Timers;
using System.Diagnostics;
using System.ComponentModel;
using System.Reflection;
using System.Xml;
using System.Windows.Forms;

using PRoCon.Core;
using PRoCon.Core.Plugin;
using PRoCon.Core.Plugin.Commands;
using PRoCon.Core.Players;
using PRoCon.Core.Players.Items;
using PRoCon.Core.Battlemap;
using PRoCon.Core.Maps;


namespace PRoConEvents
{

/* Aliases */

using EventType = PRoCon.Core.Events.EventType;
using CapturableEvent = PRoCon.Core.Events.CapturableEvents;

/* Main Class */

public class LoadingScreenTimer : PRoConPluginAPI, IPRoConPluginInterface
{
/* Enums */

public enum GameVersion { BF3, BF4 };

public enum MessageType { Warning, Error, Exception, Normal, Debug };
        
public enum PluginState { Disabled, JustEnabled, Active, Error, Reconnected };
    
public enum GameState { RoundEnding, RoundStarting, Deploying, Playing, Warmup, Unknown };

public enum ChatScope {Global, Team, Squad, Player};

public enum LoadedEvent {OnFirstTeamChange, OnFirstSpawn};

public enum LogLevel {None, Limited, All};

// General
private bool fIsEnabled;
private bool fAborted = false;
private int fServerUptime = -1;
private bool fServerCrashed = false; // because fServerUptime >  fServerInfo.ServerUptime
private DateTime fEnabledTimestamp = DateTime.MinValue;
private DateTime fLastVersionCheckTimestamp = DateTime.MinValue;
private String fHost;
private String fPort;
private DateTime fRoundOverTimestamp = DateTime.MinValue;
private DateTime fRoundStartTimestamp = DateTime.Now;
private DateTime fLastServerInfoTimestamp = DateTime.MinValue;
private DateTime fLevelLoadTimestamp = DateTime.MinValue;
private double fTotalLoadLevelSeconds = 0;
private double fTotalLoadLevelRounds = 0;
private int fTaskId;
private String fTaskScheduled;
private DateTime fTaskTimestamp = DateTime.MinValue;

private PluginState fPluginState;
private GameState fGameState;
private CServerInfo fServerInfo;

// Settings support
private Dictionary<int, Type> fEasyTypeDict = null;
private Dictionary<int, Type> fBoolDict = null;
private Dictionary<int, Type> fListStrDict = null;

// Settings
public int MinimumPlayers;
public int MaximumLoadingSeconds;
public LoadedEvent LoadSucceededEvent;
public String TimeExpiredCommand;
//public bool EnableDebugLogging;
public LogLevel DebugLoggingLevel;
public int DebugLevel = 0; // hidden


/* Constructor */

public LoadingScreenTimer() {
    /* Private members */
    fIsEnabled = false;
    fAborted = false;
    fPluginState = PluginState.Disabled;
    fGameState = GameState.Unknown;
    fServerInfo = null;
    fServerUptime = 0;
    fServerCrashed = false;
    fRoundOverTimestamp = DateTime.MinValue;
    fRoundStartTimestamp = DateTime.Now;
    fLastServerInfoTimestamp = DateTime.MinValue;
    fLevelLoadTimestamp = DateTime.MinValue;
    fTotalLoadLevelSeconds = 0;
    fTotalLoadLevelRounds = 0;
    fTaskId = 100;
    fTaskScheduled = null;
    fTaskTimestamp = DateTime.MinValue;

    fEasyTypeDict = new Dictionary<int, Type>();
    fEasyTypeDict.Add(0, typeof(int));
    fEasyTypeDict.Add(1, typeof(Int16));
    fEasyTypeDict.Add(2, typeof(Int32));
    fEasyTypeDict.Add(3, typeof(Int64));
    fEasyTypeDict.Add(4, typeof(float));
    fEasyTypeDict.Add(5, typeof(long));
    fEasyTypeDict.Add(6, typeof(String));
    fEasyTypeDict.Add(7, typeof(string));
    fEasyTypeDict.Add(8, typeof(double));

    fBoolDict = new Dictionary<int, Type>();
    fBoolDict.Add(0, typeof(Boolean));
    fBoolDict.Add(1, typeof(bool));

    fListStrDict = new Dictionary<int, Type>();
    fListStrDict.Add(0, typeof(String[]));
    
    fEnabledTimestamp = DateTime.MinValue;
    fLastVersionCheckTimestamp = DateTime.MinValue;
    
    /* Settings */

    MinimumPlayers = 4;
    MaximumLoadingSeconds = 60;
    LoadSucceededEvent = LoadedEvent.OnFirstTeamChange;
    TimeExpiredCommand = "mapList.runNextRound";
    DebugLoggingLevel = LogLevel.None;
}



public String GetPluginName() {
    return "Loading Screen Timer";
}

public String GetPluginVersion() {
    return "1.0.0.0";
}

public String GetPluginAuthor() {
    return "PapaCharlie9";
}

public String GetPluginWebsite() {
    return "https://github.com/PapaCharlie9/loading-screen-timer";
}




/* ======================== SETTINGS ============================= */




public List<CPluginVariable> GetDisplayPluginVariables() {


    List<CPluginVariable> lstReturn = new List<CPluginVariable>();

    try {

        /* ===== SECTION 9 - Debug Settings ===== */
        
        lstReturn.Add(new CPluginVariable("Minimum Players", MinimumPlayers.GetType(), MinimumPlayers));

        lstReturn.Add(new CPluginVariable("Maximum Loading Seconds", MaximumLoadingSeconds.GetType(), MaximumLoadingSeconds));

        String var_name = "Load Succeeded Event";

        String var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(LoadedEvent))) + ")";
        
        lstReturn.Add(new CPluginVariable(var_name, var_type, Enum.GetName(typeof(LoadedEvent), LoadSucceededEvent)));

        lstReturn.Add(new CPluginVariable("Time Expired Command", TimeExpiredCommand.GetType(), TimeExpiredCommand));

        var_name = "Debug Logging Level";

        var_type = "enum." + var_name + "(" + String.Join("|", Enum.GetNames(typeof(LogLevel))) + ")";
        
        lstReturn.Add(new CPluginVariable(var_name, var_type, Enum.GetName(typeof(LogLevel), DebugLoggingLevel)));

    } catch (Exception e) {
        ConsoleException(e);
    }

    return lstReturn;
}

public List<CPluginVariable> GetPluginVariables() {
    List<CPluginVariable> lstReturn = null;
    try {
        lstReturn = GetDisplayPluginVariables();
    } catch (Exception) {
        if (lstReturn == null) lstReturn = new List<CPluginVariable>();
    }
    return lstReturn;
}

public void SetPluginVariable(String strVariable, String strValue) {

    if (fIsEnabled) DebugWrite(strVariable + " <- " + strValue, 6);

    try {
        String tmp = strVariable;
        int pipeIndex = strVariable.IndexOf('|');
        if (pipeIndex >= 0) {
            pipeIndex++;
            tmp = strVariable.Substring(pipeIndex, strVariable.Length - pipeIndex);
        }

        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        String propertyName = Regex.Replace(tmp, @"[^a-zA-Z_0-9]", String.Empty);
        
        FieldInfo field = this.GetType().GetField(propertyName, flags);
        
        Type fieldType = null;


        if (field != null) {
            fieldType = field.GetValue(this).GetType();

            if (strVariable.Contains("Event")) {
                fieldType = typeof(LoadedEvent);
                try {
                    LoadSucceededEvent = (LoadedEvent)Enum.Parse(fieldType, strValue);
                } catch (Exception e) {
                    ConsoleException(e);
                }
            } else if (strVariable.Contains("Debug")) {
                fieldType = typeof(LogLevel);
                try {
                    DebugLoggingLevel = (LogLevel)Enum.Parse(fieldType, strValue);
                } catch (Exception e) {
                    ConsoleException(e);
                }
            } else if (fEasyTypeDict.ContainsValue(fieldType)) {
                field.SetValue(this, TypeDescriptor.GetConverter(fieldType).ConvertFromString(strValue));
            } else if (fListStrDict.ContainsValue(fieldType)) {
                /*
                if (DebugLevel >= 8) ConsoleDebug("String array " + propertyName + " <- " + strValue);
                field.SetValue(this, CPluginVariable.DecodeStringArray(strValue));
                if (propertyName == "Whitelist") {
                    UpdateWhitelistModel();

                } else if (propertyName == "DisperseEvenlyList") {
                    MergeWithFile(DisperseEvenlyList, fSettingDisperseEvenlyList); // clears fSettingDispersEvenlyList
                    SetDispersalListGroups();
                    AssignGroups();
                } else if (propertyName == "FriendsList") {
                    MergeWithFile(FriendsList, fSettingFriendsList); // clears fSettingFriendsList
                    SetFriends();
                }
                */
            } else if (fBoolDict.ContainsValue(fieldType)) {
                //if (fIsEnabled) DebugWrite(propertyName + " strValue = " + strValue, 6);
                if (Regex.Match(strValue, "true", RegexOptions.IgnoreCase).Success) {
                    field.SetValue(this, true);
                } else {
                    field.SetValue(this, false);
                }
            } else {
                if (DebugLevel >= 8) ConsoleDebug("Unknown var " + propertyName + " with type " + fieldType);
            }
        }
    } catch (System.Exception e) {
        ConsoleException(e);
    } finally {
        switch (DebugLoggingLevel) {
            case LogLevel.None: DebugLevel = 0; break;
            case LogLevel.Limited: DebugLevel = 3; break;
            case LogLevel.All: DebugLevel = 10; break;
            default: break;
        }
        /*

        // Validate all values and correct if needed
        ValidateSettings(strVariable,  strValue);

        // Handle show in log commands
        if (!String.IsNullOrEmpty(ShowCommandInLog)) {
            CommandToLog(ShowCommandInLog);
            ShowCommandInLog = String.Empty;
        }

        // Handle risky settings
        if (!EnableRiskyFeatures) {
            if (EnableTicketLossRateLogging) {
                ConsoleWarn("^8Setting ^bEnable Ticket Loss Rate Logging^n to False. This is an experimental setting and you have not enabled risky settings.");
                EnableTicketLossRateLogging = false;
            }
        }
        */
    }
}







/* ======================== OVERRIDES ============================= */










public void OnPluginLoaded(String strHostName, String strPort, String strPRoConVersion) {
    fHost = strHostName;
    fPort = strPort;

    this.RegisterEvents(this.GetType().Name, 
        "OnVersion",
        "OnServerInfo",
        //"OnPlayerKilled",
        "OnPlayerSpawned",
        "OnPlayerTeamChange",
        "OnPlayerSquadChange",
        "OnGlobalChat",
        "OnTeamChat",
        "OnSquadChat",
        "OnRoundOverPlayers",
        "OnRoundOver",
        "OnRoundOverTeamScores",
        "OnLevelLoaded",
        "OnEndRound",
        "OnRunNextLevel",
        "OnResponseError"
    );
}


public void OnPluginEnable() {
    fIsEnabled = true;
    fPluginState = PluginState.JustEnabled;
    fGameState = GameState.Unknown;
    fEnabledTimestamp = DateTime.Now;
    fRoundOverTimestamp = DateTime.MinValue;
    fRoundStartTimestamp = DateTime.Now;

    ConsoleWrite("^b^2Enabled!^0^n Version = " + GetPluginVersion(), 0);
    DebugWrite("^b^3State = " + fPluginState, 6);
    DebugWrite("^b^3Game state = " + fGameState, 6);

    ServerCommand("serverInfo");
}


public void OnPluginDisable() {
    fIsEnabled = false;

    try {
        fEnabledTimestamp = DateTime.MinValue;
        
        if (fTaskScheduled != null) {
            ConsoleWrite("^bDisabling, removing tasks ...^n", 0);
            StopTasks();
        }

        fPluginState = PluginState.Disabled;
        fGameState = GameState.Unknown;
        DebugWrite("^b^3State = " + fPluginState, 6);
        DebugWrite("^b^3Game state = " + fGameState, 6);
    } catch (Exception e) {
        ConsoleException(e);
    }
    ConsoleWrite("^1^bDisabled!", 0);
}


public override void OnVersion(String type, String ver) {
    if (!fIsEnabled) return;
    
    DebugWrite("^5Got ^bOnVersion^n: " + type + " " + ver, 7);
}


public override void OnPlayerSquadChange(String soldierName, int teamId, int squadId) {
    if (!fIsEnabled) return;

    if (fGameState == GameState.Playing && squadId == 0) return;
    
    DebugWrite("^5Got OnPlayerSquadChange^n: " + soldierName + " " + teamId + " " + squadId, 7);

}


public override void OnPlayerTeamChange(String soldierName, int teamId, int squadId) {
    if (!fIsEnabled) return;
    
    DebugWrite("^5Got OnPlayerTeamChange: " + soldierName + " " + teamId + " " + squadId, 6);

    if (fPluginState == PluginState.Disabled || fPluginState == PluginState.Error) return;

    try {
        int totalPlayers = TotalPlayerCount();
        if (fGameState == GameState.Unknown || fGameState == GameState.Warmup) {
            bool wasUnknown = (fGameState == GameState.Unknown);
            fGameState = (totalPlayers < 4) ? GameState.Warmup : GameState.Playing;
            if (wasUnknown || fGameState == GameState.Playing) DebugWrite("OnPlayerTeamChange: ^b^3Game state = " + fGameState, 6); 
        } else if (fGameState == GameState.RoundStarting) {
            // First team change after level loaded may indicate successful load
            DebugWrite(":::::::::::::::::::::::::::::::::::: ^b^1First team change detected^0^n ::::::::::::::::::::::::::::::::::::", 3);

            fGameState = (totalPlayers < 4) ? GameState.Warmup :GameState.Deploying;

            if (LoadSucceededEvent == LoadedEvent.OnFirstTeamChange) {
                ConsoleWrite("^b^2LEVEL LOADED SUCCESSFULLY!", 0);
                StopTasks();
                UpdateLoadScreenDuration();
            }

        }
    } catch (Exception e) {
        ConsoleException(e);
    }
}



public override void OnPlayerSpawned(String soldierName, Inventory spawnedInventory) {
    if (!fIsEnabled) return;
    
    DebugWrite("^5Got OnPlayerSpawned: ^n" + soldierName, 8);
    
    try {
        int totalPlayers = TotalPlayerCount();
        if (fGameState == GameState.Unknown || fGameState == GameState.Warmup) {
            bool wasUnknown = (fGameState == GameState.Unknown);
            fGameState = (totalPlayers < 4) ? GameState.Warmup : GameState.Playing;
            if (wasUnknown || fGameState == GameState.Playing) DebugWrite("OnPlayerSpawned: ^b^3Game state = " + fGameState, 6); 
        } else if (fGameState == GameState.Deploying) {
            // First spawn after Level Loaded is the official start of a round
            DebugWrite(":::::::::::::::::::::::::::::::::::: ^b^1First spawn detected^0^n ::::::::::::::::::::::::::::::::::::", 3);

            fGameState = (totalPlayers < 4) ? GameState.Warmup : GameState.Playing;
            DebugWrite("OnPlayerSpawned: ^b^3Game state = " + fGameState, 6);

            if (LoadSucceededEvent == LoadedEvent.OnFirstSpawn) {
                ConsoleWrite("LEVEL LOADED SUCCESSFULLY!", 0);
                StopTasks();
                UpdateLoadScreenDuration();
            }

        }
    
        if (fPluginState == PluginState.Active) {
        }
    } catch (Exception e) {
        ConsoleException(e);
    }
}

/*
public override void OnPlayerKilled(Kill kKillerVictimDetails) {
    if (!fIsEnabled) return;

    String killer = kKillerVictimDetails.Killer.SoldierName;
    String victim = kKillerVictimDetails.Victim.SoldierName;
    String weapon = kKillerVictimDetails.DamageType;
    
    bool isAdminKill = false;
    if (String.IsNullOrEmpty(killer)) {
        killer = victim;
        isAdminKill = (weapon == "Death");
    }
    
    DebugWrite("^5Got OnPlayerKilled^n: " + killer  + " -> " + victim + " (" + weapon + ")", 8);
    if (isAdminKill) DebugWrite("^9OnPlayerKilled: admin kill: ^b" + victim + "^n (" + weapon + ")", 7);

    try {

    } catch (Exception e) {
        ConsoleException(e);
    }
}
*/


public override void OnServerInfo(CServerInfo serverInfo) {
    if (!fIsEnabled || serverInfo == null) return;

    DebugWrite("^5Got OnServerInfo: Debug level = " + DebugLevel + ", " + fGameState + ", " + TotalPlayerCount() + " players", 8);

    DateTime debugTime = DateTime.Now;
    
    try {
        double elapsedTimeInSeconds = DateTime.Now.Subtract(fLastServerInfoTimestamp).TotalSeconds;

        // Update game state if just enabled (as of R38, CTF TeamScores may be null, does not mean round end)
        if (fGameState == GameState.Unknown && serverInfo.GameMode != "CaptureTheFlag0") {
            if (serverInfo.TeamScores == null || serverInfo.TeamScores.Count < 2) {
                fGameState = GameState.RoundEnding;
                DebugWrite("OnServerInfo: ^b^3Game state = " + fGameState, 6);  
            }
        }

        if (fServerInfo == null || fServerInfo.GameMode != serverInfo.GameMode || fServerInfo.Map != serverInfo.Map) {
            ConsoleDebug("ServerInfo update: " + serverInfo.Map + "/" + serverInfo.GameMode);
        }

        if (fTaskTimestamp != DateTime.MinValue) {
            double elapsedLoadTime = DateTime.Now.Subtract(fTaskTimestamp).TotalSeconds;
            DebugWrite("OnServerInfo: load level elapsed time in seconds = " + elapsedLoadTime.ToString("F1"), 6);
        }
    
        // Check for server crash
        if (fServerUptime > 0 && fServerUptime > serverInfo.ServerUptime + 2) { // +2 secs for rounding error in server!
            fServerCrashed = true;
            DebugWrite("^1^bDETECTED GAME SERVER CRASH^n (recorded uptime longer than latest serverInfo uptime)", 3);
        }
        fServerInfo = serverInfo;
        fServerUptime = serverInfo.ServerUptime;

    } catch (Exception e) {
        ConsoleException(e);
    }
}


public override void OnGlobalChat(String speaker, String message) {
    if (!fIsEnabled) return;
    if (DebugLevel >= 11) ConsoleDebug("OnGlobalChat(" + speaker + ", '" + message + "')");

    try {
    } catch (Exception e) {
        ConsoleException(e);
    }
}

public override void OnTeamChat(String speaker, String message, int teamId) {
    if (!fIsEnabled) return;
    if (DebugLevel >= 11) ConsoleDebug("OnTeamChat(" + speaker + ", '" + message + "', " +teamId + ")");

    try {
    } catch (Exception e) {
        ConsoleException(e);
    }
}

public override void OnSquadChat(String speaker, String message, int teamId, int squadId) {
    if (!fIsEnabled) return;

    try {
    } catch (Exception e) {
        ConsoleException(e);
    }
}

public override void OnRoundOverPlayers(List<CPlayerInfo> players) {
    if (!fIsEnabled) return;
    
    DebugWrite("^5Got OnRoundOverPlayers^n", 7);

    try {
        // TBD
    } catch (Exception e) {
        ConsoleException(e);
    }
}

public override void OnRoundOverTeamScores(List<TeamScore> teamScores) {
    if (!fIsEnabled) return;
    
    DebugWrite("^5Got OnRoundOverTeamScores^n", 7);

    try {
    } catch (Exception e) {
        ConsoleException(e);
    }
}

public override void OnRoundOver(int winningTeamId) {
    if (!fIsEnabled) return;
    
    DebugWrite("^5Got OnRoundOver^n: winner " + winningTeamId, 7);

    try {

        DebugWrite(":::::::::::::::::::::::::::::::::::: ^b^1Round over detected^0^n ::::::::::::::::::::::::::::::::::::", 3);
    
        if (fGameState == GameState.Playing || fGameState == GameState.Unknown || fGameState == GameState.Deploying || fGameState == GameState.RoundStarting) {
            fGameState = GameState.RoundEnding;
            DebugWrite("OnRoundOver: ^b^3Game state = " + fGameState, 6);
            StopTasks();
        } else if (fGameState == GameState.RoundEnding) {
            DebugWrite("^1OnLevelLoaded: detected another OnRoundOver event ...", 3);
        }

    } catch (Exception e) {
        ConsoleException(e);
    }
}

public override void OnLevelLoaded(String mapFileName, String Gamemode, int roundsPlayed, int roundsTotal) {
    if (!fIsEnabled) return;
    
    DebugWrite("^5Got OnLevelLoaded^n: " + mapFileName + " " + Gamemode + " " + roundsPlayed + "/" + roundsTotal, 3);

    try {
        DebugWrite(":::::::::::::::::::::::::::::::::::: ^b^1Level loaded detected^0^n ::::::::::::::::::::::::::::::::::::", 3);

        if (fGameState == GameState.RoundEnding || (fGameState == GameState.Warmup && TotalPlayerCount() >= 4) || fGameState == GameState.Unknown) {
            fGameState = GameState.RoundStarting;
            DebugWrite("OnLevelLoaded: ^b^3Game state = " + fGameState, 6);

            int totalPlayers = TotalPlayerCount();

            if (totalPlayers >= MinimumPlayers) {
                fLevelLoadTimestamp = DateTime.Now;
                StartTimerTask();
            }
        } else if (fGameState == GameState.RoundStarting) {
            DebugWrite("^1OnLevelLoaded: detected another OnLevelLoaded event ...", 3);
        }

    } catch (Exception e) {
        ConsoleException(e);
    }
}



public override void OnEndRound(int iWinningTeamID) {
    if (!fIsEnabled) return;
    
    DebugWrite("^5Got OnEndRound^n: " + iWinningTeamID, 7);
}

public override void OnRunNextLevel() {
    if (!fIsEnabled) return;
    
    DebugWrite("^5Got OnRunNextLevel^n", 3);
}





public override void OnResponseError(List<string> lstRequestWords, string strError) {
    if (!fIsEnabled) return;
    if (lstRequestWords == null || lstRequestWords.Count == 0) return;
    try {
        String msg = "Request(" + String.Join(", ", lstRequestWords.ToArray()) + "): ERROR = " + strError;

        int level = 7;
        if (lstRequestWords[0] == "player.ping") level = 8;

        DebugWrite("^5Got OnResponseError, " + msg, level);


    } catch (Exception e) {
        ConsoleException(e);
    }
}






/* ======================== SUPPORT FUNCTIONS ============================= */






private void StartTimerTask() {
    if (!String.IsNullOrEmpty(fTaskScheduled)) {
        DebugWrite("New task " + fTaskId + " replacing " + fTaskScheduled + "!", 3);
        StopTasks();
    }
    fTaskScheduled = "LST" + fTaskId;
    fTaskId += 1;
    fTaskTimestamp = DateTime.Now;
    this.ExecuteCommand("procon.protected.tasks.add", fTaskScheduled, MaximumLoadingSeconds.ToString(), "0", "0", TimeExpiredCommand);
}


private void StopTasks() {
    if (String.IsNullOrEmpty(fTaskScheduled))
        return;
    DebugWrite("Removing task " + fTaskScheduled, 8);
    try {
        this.ExecuteCommand("procon.protected.tasks.remove", fTaskScheduled);
    } catch (Exception e) {
        ConsoleException(e);
    }
    fTaskScheduled = null;
    fTaskTimestamp = DateTime.MinValue;
}


private String FormatMessage(String msg, MessageType type, int level) {
    String prefix = "^n^0[^b" + GetPluginName() + "^n]:" + level + " ";

    if (Thread.CurrentThread.Name != null) prefix += "Thread(^b^5" + Thread.CurrentThread.Name + "^0^n): ";

    if (type.Equals(MessageType.Warning))
        prefix += "^1^bWARNING^0^n: ";
    else if (type.Equals(MessageType.Error))
        prefix += "^1^bERROR^0^n: ";
    else if (type.Equals(MessageType.Exception))
        prefix += "^1^bEXCEPTION^0^n: ";
    else if (type.Equals(MessageType.Debug))
        prefix += "^5DEBUG^0^n: ";

    return prefix + msg.Replace('{','(').Replace('}',')') + "^n"; // close styling for every line with ^n
}


public void LogWrite(String msg)
{
    if (fAborted) return;
    this.ExecuteCommand("procon.protected.pluginconsole.write", msg);
}

public void ConsoleWrite(String msg, MessageType type, int level)
{
    LogWrite(FormatMessage(msg, type, level));
}

public void ConsoleWrite(String msg, int level)
{
    ConsoleWrite(msg, MessageType.Normal, level);
}

public void ConsoleWarn(String msg)
{
    ConsoleWrite(msg, MessageType.Warning, 1);
}

public void ConsoleError(String msg)
{
    ConsoleWrite(msg, MessageType.Error, 0);
}

public void ConsoleException(Exception e)
{
    if (e.GetType() == typeof(ThreadAbortException)
      || e.GetType() == typeof(ThreadInterruptedException)
      || e.GetType() == typeof(CannotUnloadAppDomainException)
    )
        return;
    if (DebugLevel >= 3) ConsoleWrite(e.ToString(), MessageType.Exception, 3);
}

public void DebugWrite(String msg, int level)
{
    if (DebugLevel >= level) ConsoleWrite(msg, MessageType.Normal, level);
}

public void ConsoleDebug(String msg)
{
    if (DebugLevel >= 6) ConsoleWrite(msg, MessageType.Debug, 6);
}



private void ServerCommand(params String[] args)
{
    if (fAborted) return;
    List<String> list = new List<String>();
    list.Add("procon.protected.send");
    list.AddRange(args);
    this.ExecuteCommand(list.ToArray());
}

private void TaskbarNotify(String title, String msg) {
    if (fAborted) return;
    this.ExecuteCommand("procon.protected.notification.write", title, msg);
}



public int TotalPlayerCount() {
    if (fServerInfo != null) {
        return fServerInfo.PlayerCount;
    }
    /*
    fPlayerCount = 0;
    if (fGameVersion == GameVersion.BF4) {
        fBF4CommanderCount = 0;
        fBF4SpectatorCount = 0;

        lock (fAllPlayers) {
            foreach (String name in fAllPlayers) {
                PlayerModel p = GetPlayer(name);
                if (p == null) continue;
                if (p.Role == ROLE_PLAYER) {
                    ++fPlayerCount;
                } else if (p.Role == ROLE_COMMANDER_PC || p.Role == ROLE_COMMANDER_MOBILE) {
                    ++fBF4CommanderCount;
                } else if (p.Role == ROLE_SPECTATOR) {
                    ++fBF4SpectatorCount;
                }
            }
        }
    } else {
        //lock (fAllPlayers) {fPlayerCount = fAllPlayers.Count;}
    }
    return fPlayerCount;
    */
    return 0;
}


private void UpdateLoadScreenDuration() {
    if (fLevelLoadTimestamp == DateTime.MinValue) return;
    double secs = DateTime.Now.Subtract(fLevelLoadTimestamp).TotalSeconds;
    fLevelLoadTimestamp = DateTime.MinValue;
    if (secs > 180) { // 3 mins
        DebugWrite("Load level greater than 180 seconds (" + secs.ToString("F0") + "), skipping", 3);
        return;
    }
    // Sum up for average
    fTotalLoadLevelSeconds += secs;
    fTotalLoadLevelRounds += 1;
    DebugWrite("Load level seconds = " + secs.ToString("F0") + ", average of " + fTotalLoadLevelRounds + " rounds = " + (fTotalLoadLevelSeconds/fTotalLoadLevelRounds).ToString("F1"), 3);
}



public static String ConvertHTMLToVBCode(String html) {
    if (String.IsNullOrEmpty(html)) return String.Empty;

    /* Normalization */

    // make all markup be lowercase
    String norm = Regex.Replace(html, @"<[^>=]+[>=]", delegate(Match match) {
        return match.Value.ToLower(); 
    });
    // make all entity refs be lowercase
    norm = Regex.Replace(norm, @"&[^;]+;", delegate(Match match) {
        return match.Value.ToLower();
    });

    StringBuilder tmp = new StringBuilder(norm);
    //tmp.Replace("\r", String.Empty);

    /* Markup deletions */

    tmp.Replace("<p>", String.Empty);
    tmp.Replace("</p>", String.Empty);

    /* Markup replacements */

    tmp.Replace("<h1>", "[SIZE=5]");
    tmp.Replace("</h1>", "[/SIZE]\n[HR][/HR]");
    tmp.Replace("<h2>", "[SIZE=4][B][COLOR=#0000FF]");
    tmp.Replace("</h2>", "[/COLOR][/B][/SIZE]\n[HR][/HR]");
    tmp.Replace("<h3>", "[SIZE=3][B]");
    tmp.Replace("</h3>", "[/B][/SIZE]");
    tmp.Replace("<h4>", "[B]");
    tmp.Replace("</h4>", "[/B]");

    tmp.Replace("<small>", "[INDENT][SIZE=2][FONT=Arial Narrow]");
    tmp.Replace("</small>", "[/FONT][/SIZE][/INDENT]");
    tmp.Replace("<font color", "[COLOR"); // TODO - be smarter about font tag
    tmp.Replace("</font>", "[/COLOR]"); // TODO - be smarter about font tag

    tmp.Replace("<ul>", "[LIST]");
    tmp.Replace("</ul>", "[/LIST]");
    tmp.Replace("<li>", "[*]");
    tmp.Replace("</li>", String.Empty);

    tmp.Replace("<table>", "[TABLE=\"class: grid\"]"); // TODO - be smarter about table tag
    tmp.Replace("<table border='0'>", "[TABLE=\"class: grid\"]");
    tmp.Replace("</table>", "[/TABLE]");
    tmp.Replace("<tr>", "[TR]\n");
    tmp.Replace("</tr>", "[/TR]");
    tmp.Replace("<td>", "[TD]");
    tmp.Replace("</td>", "[/TD]\n");

    tmp.Replace("<a href=", "[U][URL="); // TODO - be smarter about anchors
    tmp.Replace("</a>", "[/URL][/U]"); // TODO - be smarter about anchors

    tmp.Replace("<pre>", "[CODE]");
    tmp.Replace("</pre>", "[/CODE]");

    tmp.Replace("<i>", "[I]");
    tmp.Replace("</i>", "[/I]");
    tmp.Replace("<b>", "[B]");
    tmp.Replace("</b>", "[/B]");
    tmp.Replace("<hr>", "[HR]");
    tmp.Replace("</hr>", "[/HR]");
    tmp.Replace("<br>", String.Empty);
    tmp.Replace("</br>", "\n");

    // Must do this before entity ref replacement
    tmp.Replace("<", "[");
    tmp.Replace(">", "]");

    /* Entity ref replacements */

    tmp.Replace("&amp;", "&");
    tmp.Replace("&nbsp;", " ");
    tmp.Replace("&quot;", "\"");
    tmp.Replace("&apos;", "'");
    tmp.Replace("&lt;", "<");
    tmp.Replace("&gt;", ">");

    /* Done */

    return tmp.ToString();
}










public String GetPluginDescription() {
    return @"
<h1>Loading Screen Timer</h1>
";
}



}

}


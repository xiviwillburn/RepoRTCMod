using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using BepInEx;
using System.Reflection;
using System.Text.RegularExpressions;
using Photon.Pun;

namespace RepoMemeMod
{
    [BepInPlugin("conq.repomememod", "RepoMemeMod", "1.0.1")]
    public class RepostedPlugin : BaseUnityPlugin
    {
        public static RepostedPlugin instance;
        public static bool Debug = false;
        public static Dictionary<string, PlayerAvatar> playerList = new Dictionary<string, PlayerAvatar>();
        private void Awake()
        {
            instance = this;
            var harmony = new Harmony("conq.repomememod");
            harmony.PatchAll();
        }

        public void Log(string message)
        {
            if(Debug)
                Logger.LogInfo(message);
        }
    }

    [HarmonyPatch()]
    public class PlayerSpawnPatch
    {
        static MethodInfo TargetMethod()
        {
            return typeof(PlayerAvatar).GetMethod("AddToStatsManagerRPC");
        }

        [HarmonyPrefix]
        static void Patch(ref PlayerAvatar __instance, ref string _playerName, ref string _steamID)
        {
            string storedName = _playerName.ToLower();
            if (RepostedPlugin.playerList.TryGetValue(storedName, out var tmp))
            {
                RepostedPlugin.playerList[storedName] = __instance;
            }
            else
            {
                RepostedPlugin.playerList.Add(storedName, __instance);
            }
            RepostedPlugin.instance.Log($"{_playerName} -> {storedName}");
        }
    }

    [HarmonyPatch()]
    public class ChatPatch 
    {
        private class Command
        {
            public Regex regex;
            public int parameters;
            Match m;

            public Match match => m;
            public string Message => m == null ? "" : parameters == 2 ? m.Groups[2].Value : m.Groups[3].Value;
            public string Player => m == null ? "" : m.Groups[2].Value.ToLower();

            public Command(int parameters, Regex regex) 
            {
                this.parameters = parameters;
                this.regex = regex;
                m = null;
            }

            public bool Match(string message)
            {
                m = regex.Match(message);
                return m.Success && m.Groups.Count-1 == parameters;
            }

        }
        private static Dictionary<Command, Action<PlayerAvatar, Command>> commands = new Dictionary<Command, Action<PlayerAvatar, Command>>()
        {
            { new Command(3,new Regex(@"^\/(s) (.+?) (.*)")),(player,m) => Say(player,m,false) },           // Say
            { new Command(3,new Regex(@"^\/(w) (.+?) (.*)")),(player,m) => Say(player,m,true)},             // Whisper
            { new Command(3,new Regex(@"^\/(t) (.+?) (.*)")),(player,m) => TruckSay(player,m,false)},       // Player Truck Message
            { new Command(3,new Regex(@"^\/(f) (.+?) (.*)")),(player,m) => ForceImpulse(player,m)},         // Force Impulse
            { new Command(3,new Regex(@"^\/(c) (.+?) (\d+)")),(player,m) => ChangeColor(player,m)},         // Change Color
            { new Command(3,new Regex(@"^\/(l) (.+?) (\d+)")),(player,m) => FlashlightFlick(player,m)},     // Flashlight Flick
            { new Command(3,new Regex(@"^\/(h) (.+?) (\d+)")),(player,m) => Heal(player,m)},                // Heal Player
            { new Command(2,new Regex(@"^\/(q) (.+)")),(player,m) => ForceTumble(player,m)},                // Tumble
            { new Command(2,new Regex(@"^\/(t) (.+)")),(player,m) => TruckSay(player,m,true)},              // Taxman Truck Message
            { new Command(2,new Regex(@"^\/(r) (.+)")),(player,m) => Reivive(player,m)},                    // Revive
            { new Command(2,new Regex(@"^\/(g) (.+)")),(player, m) => ScreenGlitch(player,m)},              // Screen Glitch
        };

        private static bool wasExecuted = false;

        private static void Say(PlayerAvatar player, Command m, bool isWhisper)
        {
            RepostedPlugin.instance.Log("Say");
            player.photonView.RPC("ChatMessageSendRPC", Photon.Pun.RpcTarget.All, new object[] { m.Message, isWhisper });
            wasExecuted = true;
        }

        private static void ForceTumble(PlayerAvatar player, Command m)
        {
            RepostedPlugin.instance.Log("ForceTumble");
            player.tumble.TumbleRequest(true, false);
            wasExecuted = true;
        }

        private static void TruckSay(PlayerAvatar player, Command m, bool isTaxman)
        {
            RepostedPlugin.instance.Log($"TruckSay {m.Player}:{m.Message}");
            if (player == null && m.parameters == 3) return;
            TruckScreenText.instance.MessageSendCustom(isTaxman ? "" : SemiFunc.PlayerGetSteamID(player), m.Message, 0);
            wasExecuted = true;
        }

        private static void ChangeColor(PlayerAvatar player, Command m)
        {
            RepostedPlugin.instance.Log("ChangeColor");
            if (int.TryParse(m.Message, out var value))
            {
                //RepostedPlugin.instance.Log($"Color is {value}");
                if (value > 0 && value < 36)
                {
                    player.PlayerAvatarSetColor(value);
                    wasExecuted = true;
                }
            }
        }

        private static void ForceImpulse(PlayerAvatar player, Command m)
        {
            RepostedPlugin.instance.Log("ForceImpulse");
            string[] vector = m.Message.Split(',');
            if (vector.Length == 3)
            {
                Vector3 force = new Vector3(int.Parse(vector[0]), int.Parse(vector[1]), int.Parse(vector[2]));
                player.photonView.RPC("ForceImpulseRPC", Photon.Pun.RpcTarget.All, new object[] { force });
                wasExecuted = true;
            }
        }

        private static void Reivive(PlayerAvatar player, Command m)
        {
            RepostedPlugin.instance.Log("Reivive");
            //RepostedPlugin.instance.Log($"Debug current health value: {Traverse.Create(player.playerHealth).Field<int>("health").Value}");
            
            if (Traverse.Create(player.playerHealth).Field<int>("health").Value <= 0)
            {
                RepostedPlugin.instance.Log("Revive true");
                player.photonView.RPC("ReviveRPC", Photon.Pun.RpcTarget.All, new object[] { false });
                wasExecuted = true;
            }
                //player.Revive(false);
        }

        private static void Heal(PlayerAvatar player, Command m)
        {
            RepostedPlugin.instance.Log("Heal");
            if (int.TryParse(m.Message,out int value))
            {
                var final = Traverse.Create(player.playerHealth).Field<int>("health").Value;
                var max = Traverse.Create(player.playerHealth).Field<int>("maxHealth").Value;
                final += value;
                final = Mathf.Clamp(final, 0, max);
                Traverse.Create(player.playerHealth).Field<int>("health").Value = final;
                StatsManager.instance.SetPlayerHealth(SemiFunc.PlayerGetSteamID(player), final, false);
                if (GameManager.Multiplayer())
                {
                    var pv = Traverse.Create(player.playerHealth).Field<PhotonView>("photonView").Value;
                    pv.RPC("UpdateHealthRPC", RpcTarget.Others, new object[]
                    {
                        final,
                        max,
                        false
                    });
                    wasExecuted = true;
                }

            }
        }

        private static void FlashlightFlick(PlayerAvatar player, Command m)
        {
            RepostedPlugin.instance.Log("FlashlightFlick");
            if (float.TryParse(m.Message, out var value))
            {
                player.photonView.RPC("FlashlightFlickerRPC", Photon.Pun.RpcTarget.All, new object[] { value });
                wasExecuted = true;
            }
        }

        private static void ScreenGlitch(PlayerAvatar player, Command m)
        {
            RepostedPlugin.instance.Log("ScreenGlitch");
            player.photonView.RPC("PlayerGlitchShortRPC", Photon.Pun.RpcTarget.All, new object[] {  });
            wasExecuted = true;
        }


        static MethodInfo TargetMethod()
        {
            return typeof(PlayerAvatar).GetMethod("ChatMessageSend");
        }

        [HarmonyPrefix]
        static bool Patch(ref string _message, ref bool _debugMessage)
        {
            // Check if it's a command
            if (_message[0] != '/') return true;
            wasExecuted = false;
            foreach (KeyValuePair<Command,Action<PlayerAvatar, Command>> cmd in commands)
            {
                if (!cmd.Key.Match(_message)) continue;
                // Check if there's a player with that name
                if (RepostedPlugin.playerList.TryGetValue(cmd.Key.Player, out var player)){
                    cmd.Value.Invoke(player, cmd.Key);
                    if (wasExecuted) return false;
                }
                else
                {
                    cmd.Value.Invoke(null, cmd.Key);
                    if (wasExecuted) return false;
                }
            }         

            return false;
        }
    }


}

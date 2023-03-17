using System;
using Monocle;
using Celeste;
using Celeste.Mod;
using MonoMod.Utils;
using System.Collections.Generic;
using System.Reflection;
using H.Socket.IO;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace InputMania
{
    public class InputMania : EverestModule
    {
        public static string instanceNick = "unnamed-" + (new Random()).Next().ToString();
        public static int suggestedWidth = 0;
        public static int suggestedHeight = 0;
        public static bool isolateSaves = false;
        public static bool gamepadAllowed = true;
        public Game g;

        public static SocketIoClient socket;
        public static bool isRemote = false;
        public static Uri remoteURI;

        public static bool blockNativeInput = false;
        public static List<Microsoft.Xna.Framework.Input.Keys> virtualKeyPresses = new List<Microsoft.Xna.Framework.Input.Keys>();
        public override void Load()
        {
            Console.WriteLine("InputMania Load (console message)! Latest Feature: Reset virtual input remotely");
            // SetLogLevel will set the *minimum* log level that will be written for logs that use the given prefix string.
            Logger.SetLogLevel("InputManiaModule", LogLevel.Verbose);
            Logger.Log(LogLevel.Info, "InputManiaModule", "Loading epic InputMania MOD LESGO!!!");

            On.Celeste.Settings.Initialize += modInitalizeSettings;
            On.Celeste.Celeste.Initialize += modInitalize;
            On.Monocle.MInput.KeyboardData.Update += modKeyboardDataUpdate;
            On.Monocle.MInput.GamePadData.Update += modMInputGamepadUpdate;
            On.Celeste.UserIO.GetSavePath += modSavePath;
            On.Celeste.Celeste.RenderCore += modRenderCore;
            // On.Celeste.Celeste.Main += modMain;

            string[] args = Environment.GetCommandLineArgs();
            Logger.Log(LogLevel.Info, "InputManiaModule", "Passed " + args.Length + " arguments");
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--instance_nick" || args[i] == "-n")
                {
                    instanceNick = args[++i];
                }
                else if (args[i] == "--width" || args[i] == "-w")
                {
                    int.TryParse(args[++i], out suggestedWidth);
                }
                else if (args[i] == "--height" || args[i] == "-h")
                {
                    int.TryParse(args[++i], out suggestedHeight);
                }
                else if (args[i] == "--isolate-saves" || args[i] == "-i")
                {
                    isolateSaves = true;
                }
                else if (args[i] == "--remote" || args[i] == "-r")
                {
                    isRemote = true;
                    string remoteURL = args[++i];
                    Logger.Log(LogLevel.Info, "InputManiaModule", "Establishing connection to input server! URL is " + remoteURL);
                    remoteURI = new Uri(remoteURL);
                    Logger.Log(LogLevel.Info, "InputManiaModule", "Remote URI Scheme is " + remoteURI.Scheme);
                }
                else if (args[i] == "--block-native-input" || args[i] == "-b")
                {
                    blockNativeInput = true;
                }
                else if (args[i] == "--block-native-gamepad" || args[i] == "-b")
                {
                    gamepadAllowed = false;
                }
            }
            if (isolateSaves)
            {
                DynamicData uioData = new DynamicData(typeof(Celeste.UserIO));
                string basePath = (string) uioData.Invoke("GetSavePath", "");
                Logger.Log(LogLevel.Info, "InputManiaModule", "Rewrote saving to " + basePath);
               typeof(Celeste.UserIO).GetField("SavePath", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, basePath + "Saves");
                typeof(Celeste.UserIO).GetField("BackupPath", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, basePath + "Backups");
            }
            else
            {
                Logger.Log(LogLevel.Info, "InputManiaModule", "Not isolating saves!");
            }

            if (isRemote)
            {
                SocketIoClient client = new SocketIoClient();
                socket = client;
                Logger.Log(LogLevel.Info, "InputManiaModule", "Constructed socket..");
                registerRemoteListeners();
                Logger.Log(LogLevel.Info, "InputManiaModule", "Running Connect...");
                bool status = socket.ConnectAsync(remoteURI).GetAwaiter().GetResult();
                Logger.Log(LogLevel.Info, "InputManiaModule", "Connect task finished " + status);
                Logger.Log(LogLevel.Info, "InputManiaModule", "Socket ok, waiting for hello!");
            }
            Console.WriteLine("Reached InputMania end!");
        }

        private void modKeyboardDataUpdate(On.Monocle.MInput.KeyboardData.orig_Update orig, MInput.KeyboardData self) 
        {
            orig(self); // real keyboard presses
            List<Microsoft.Xna.Framework.Input.Keys> pressed = blockNativeInput ? new List<Microsoft.Xna.Framework.Input.Keys>() : new List<Microsoft.Xna.Framework.Input.Keys>(self.CurrentState.GetPressedKeys());
            // pressed.Add(Microsoft.Xna.Framework.Input.Keys.Up);
            lock (virtualKeyPresses)
            {
                virtualKeyPresses.ForEach(k => // no dup
                {
                    if (!pressed.Contains(k))
                    {
                        pressed.Add(k);
                    }
                });
            }
            self.CurrentState = new Microsoft.Xna.Framework.Input.KeyboardState(pressed.ToArray());
        }

        private void modInitalizeSettings(On.Celeste.Settings.orig_Initialize orig)
        {
            orig();
            Logger.Log(LogLevel.Info, "InputManiaModule", "Disabling fullscreen!");
            Settings.Instance.Fullscreen = false;
        }

        private void modInitalize(On.Celeste.Celeste.orig_Initialize orig, Celeste.Celeste self)
        {
            // Disable Fullscreen
            Settings.Instance.Fullscreen = false;
            // Change Target Resolution (fail cause constant also unused)
            // DynamicData celesteData = new DynamicData(self);
            // celesteData.Set("TargetWidth", 960);
            // celesteData.Set("TargetHeight", 540);
            orig(self);
            // Suggestions!
            if(suggestedHeight > 0)
            {
                Engine.Graphics.PreferredBackBufferHeight = suggestedHeight;
            }
            if(suggestedWidth > 0)
            {
                Engine.Graphics.PreferredBackBufferWidth = suggestedWidth;
            }
            if(suggestedWidth > 0 || suggestedWidth > 0)
            {
                Engine.Graphics.ApplyChanges();
            }
            Celeste.Celeste.Instance.Window.AllowUserResizing = true;
            Celeste.Celeste.Instance.Window.Title = "Celeste: Instance " + instanceNick;
            Logger.Log(LogLevel.Info, "InputManiaModule", "Changed window settings!");
        }

        private string modSavePath(On.Celeste.UserIO.orig_GetSavePath orig, string dir)
        {
            if (isolateSaves)
            {
                Logger.Log(LogLevel.Info, "InputManiaModule", "Isolated saves for " + dir);
                return orig("inst_" + instanceNick + "/" + dir);
            }
            else
            {
                return orig(dir);
            }
        }

        private void modMInputGamepadUpdate(On.Monocle.MInput.GamePadData.orig_Update orig, Monocle.MInput.GamePadData gpData)
        {
            gpData.PreviousState = gpData.CurrentState;
            gpData.CurrentState = GamePad.GetState(gpData.PlayerIndex);
            if (!gpData.Attached && gpData.CurrentState.IsConnected && gamepadAllowed)
            {
                Monocle.MInput.IsControllerFocused = true;
            }
            if (!gamepadAllowed)
            {
                Monocle.MInput.IsControllerFocused = false;
            }
            gpData.Attached = gpData.CurrentState.IsConnected && gamepadAllowed;
            DynamicData gpDynData = DynamicData.For(gpData);
            float rumbleTime = (float) gpDynData.Get("rumbleTime");
            if (rumbleTime > 0f)
            {
                rumbleTime -= Engine.DeltaTime;
                gpDynData.Set("rumbleTime", rumbleTime);
                if (rumbleTime <= 0f)
                {
                    GamePad.SetVibration(gpData.PlayerIndex, 0f, 0f);
                }
            }
        }

        private void modMain(On.Celeste.Celeste.orig_Main orig, string[] args)
        {
            // doesn't actually work since main has already been called...

            orig(args);
        }

        public override void Unload()
        {
            On.Celeste.Settings.Initialize -= modInitalizeSettings;
            On.Celeste.Celeste.Initialize -= modInitalize;
            On.Monocle.MInput.GamePadData.Update -= modMInputGamepadUpdate;
            On.Monocle.MInput.KeyboardData.Update -= modKeyboardDataUpdate;
            On.Celeste.UserIO.GetSavePath -= modSavePath;
            On.Celeste.Celeste.RenderCore -= modRenderCore;
            // On.Celeste.Celeste.Main -= modMain;
            if (isRemote)
            {
                shutdownRemote();
            }
        }

        private void forceFocus()
        {
            if (Celeste.Celeste.Instance != null)
            {
                typeof(Microsoft.Xna.Framework.Game).GetField("INTERNAL_isActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Celeste.Celeste.Instance, true);
            }
        }

        private void modRenderCore(On.Celeste.Celeste.orig_RenderCore orig, Celeste.Celeste self)
        {
            forceFocus();
            orig(self);
            forceFocus();
        }

        private void registerRemoteListeners()
        {
            socket.On("identify", response =>
            {
                Logger.Log(LogLevel.Info, "InputManiaModule", "Sending Identity");
                socket.Emit("identifier", instanceNick).GetAwaiter().GetResult(); ;
            });

            socket.On("hello", response =>
            {
                Logger.Log(LogLevel.Info, "InputManiaModule", "Recieved hello with data " + response);
            });

            socket.On<string>("toggleBorderless", response =>
            {
                Celeste.Celeste.Instance.Window.IsBorderlessEXT = !Celeste.Celeste.Instance.Window.IsBorderlessEXT;
            });

            socket.On<string>("toggleNativeInputBlock", response =>
            {
                blockNativeInput = !blockNativeInput;
            });

            socket.On<string>("toggleNativeGamepadBlock", response =>
            {
                gamepadAllowed = !gamepadAllowed;
            });

            socket.On<string>("resetInput", response =>
            {
                Console.WriteLine("Resetting Virtual Inputs");
                lock (virtualKeyPresses)
                {
                    virtualKeyPresses.Clear();
                }

            });


            socket.On<string>("key", response =>
            {
                Console.WriteLine("Key -> " + response);
                int skey = -11111;
                int.TryParse(response, out skey);
                int keyCode = Math.Abs(skey);
                bool newState = skey > 0;
                if (skey == -11111)
                {
                    return;
                }
                lock (virtualKeyPresses) { 
                    Microsoft.Xna.Framework.Input.Keys key = (Microsoft.Xna.Framework.Input.Keys)keyCode;
                    Console.WriteLine("Key abs " + keyCode + " " + newState);
                    Console.WriteLine("STATE " + virtualKeyPresses);

                    if (newState)
                    {
                        if (!virtualKeyPresses.Contains(key))
                        {
                            virtualKeyPresses.Add(key);
                        }
                    }
                    else
                    {
                        if (virtualKeyPresses.Contains(key))
                        {
                            if (!virtualKeyPresses.Remove(key))
                            {
                                Console.WriteLine("??? IMPOSSIBLE TO REMOVE ????");
                            }

                        }
                        else
                        {
                            Console.WriteLine("Odd release of already released key " + key);
                        }
                    }
                    if (virtualKeyPresses.Count() == 0)
                    {
                        Console.WriteLine("Virtual KBD Idle");
                    }
                }
            });
                
        }

        private void shutdownRemote()
        {
            Logger.Log(LogLevel.Info, "InputManiaModule", "Disconnecting remote socket!");
            Task dcTask = socket.DisconnectAsync();
            dcTask.Wait();
            Logger.Log(LogLevel.Info, "InputManiaModule", "Remote disconnect finished!");
        }
    }
}

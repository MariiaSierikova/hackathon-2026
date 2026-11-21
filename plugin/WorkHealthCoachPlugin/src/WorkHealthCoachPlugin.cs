namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;

    // This class contains the plugin-level logic of the Loupedeck plugin.

    public class WorkHealthCoachPlugin : Plugin
    {
        // Gets a value indicating whether this is an API-only plugin.
        public override Boolean UsesApplicationApiOnly => true;

        // Gets a value indicating whether this is a Universal plugin or an Application plugin.
        public override Boolean HasNoApplication => true;

        // Initializes a new instance of the plugin class.
        public WorkHealthCoachPlugin()
        {
            // Initialize the plugin log.
            PluginLog.Init(this.Log);

            // Initialize the plugin resources.
            PluginResources.Init(this.Assembly);
        }

        // This method is called when the plugin is loaded.
        private readonly LocalServer _server = new();
        private System.Timers.Timer _tick;

        public override void Load()
        {
            HapticPlayer.Init(this);

            // Haptics subscribe to triggers. Keypad widgets and the notification policy plug in here later.
            NotificationPolicy.Init();
            TriggerBus.Received += OnRaw;                              // raw trigger -> (controls) or policy
            NotificationPolicy.Decided += OnDecided;                  // policy -> outputs
            this._server.Start();

            this._tick = new System.Timers.Timer(1000) { AutoReset = true };
            this._tick.Elapsed += (_, _) => SessionState.Tick();
            this._tick.Start();
        }

        // "blink_detected" / "pace_ok" are not alerts: they end an active reminder, so they skip the policy.
        private static void OnRaw(TriggerMessage m)
        {
            switch (m.Trigger)
            {
                case "blink_detected":
                    SessionState.ClearBlinkAlert();
                    break;
                case "pace_ok":
                    SessionState.ClearPaceAlert();
                    break;
                default:
                    NotificationPolicy.Evaluate(m);
                    break;
            }
        }

        private static void OnDecided(Decision d)
        {
            if (d.Visual)
            {
                SessionState.OnTrigger(d.Message);
            }

            if (d.Overlay)
            {
                SessionState.OnOverlay(d.Message);
            }

            if (d.Haptic)
            {
                HapticPlayer.Play(d.Message.Trigger, d.HapticStrength);
            }
        }

        // This method is called when the plugin is unloaded.
        public override void Unload()
        {
            TriggerBus.Received -= OnRaw;
            NotificationPolicy.Decided -= OnDecided;
            this._tick?.Stop();
            this._server.Stop();
        }
    }
}

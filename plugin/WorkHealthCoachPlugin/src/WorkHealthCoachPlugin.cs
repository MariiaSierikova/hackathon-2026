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

        public override void Load()
        {
            HapticPlayer.Init(this);

            // Haptics subscribe to triggers. Keypad widgets and the notification policy plug in here later.
            TriggerBus.Received += OnTrigger;
            this._server.Start();
        }

        private static void OnTrigger(TriggerMessage message) => HapticPlayer.Play(message.Trigger);

        // This method is called when the plugin is unloaded.
        public override void Unload()
        {
            TriggerBus.Received -= OnTrigger;
            this._server.Stop();
        }
    }
}

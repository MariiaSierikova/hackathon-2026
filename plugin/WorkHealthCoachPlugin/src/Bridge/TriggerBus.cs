namespace Loupedeck.WorkHealthCoachPlugin
{
    using System;

    // Everything that reacts to a trigger (haptics, Keypad widgets, overlay) subscribes here.
    public record TriggerMessage(String Trigger, Double Severity);

    public static class TriggerBus
    {
        public static event Action<TriggerMessage> Received;

        public static void Publish(TriggerMessage message)
        {
            try
            {
                Received?.Invoke(message);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"TriggerBus subscriber failed for '{message.Trigger}'");
            }
        }
    }
}
